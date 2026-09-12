using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NAudio.Wave;
using Rectangle = System.Drawing.Rectangle;

namespace FrameForge;
public static class Ffmpeg
{
    public static string Executable
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("FRAMEFORGE_FFMPEG");
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;
            foreach (var path in new[]
            {
                Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
                Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe")
            }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Select(p => Path.Combine(p.Trim('"'), "ffmpeg.exe"))))
                if (File.Exists(path))
                    return path;
            throw new FileNotFoundException("FFmpeg was not found. Install FFmpeg with winget install Gyan.FFmpeg, restart FrameForge, or put ffmpeg.exe in the app's tools folder. Screenshots and OCR work without FFmpeg.");
        }
    }

    public sealed class Job : IDisposable
    {
        public Process Process { get; }

        readonly StringBuilder log = new();
        readonly object gate = new();
        public string Log
        {
            get
            {
                lock (gate)
                    return log.ToString();
            }
        }

        public Job(IEnumerable<string> arguments)
        {
            var start = new ProcessStartInfo(Executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            foreach (var a in arguments)
                start.ArgumentList.Add(a);
            Process = new Process
            {
                StartInfo = start
            };
            Process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;
                lock (gate)
                {
                    log.AppendLine(e.Data);
                    if (log.Length > 12000)
                        log.Remove(0, log.Length - 12000);
                }
            };
            Process.Start();
            Process.BeginErrorReadLine();
        }

        public async Task Wait()
        {
            await Process.WaitForExitAsync();
            Process.WaitForExit();
            if (Process.ExitCode != 0)
                throw new InvalidOperationException("FFmpeg failed: " + Log[^Math.Min(Log.Length, 1800)..]);
        }

        public async Task Stop()
        {
            if (!Process.HasExited)
            {
                try
                {
                    await Process.StandardInput.WriteLineAsync("q");
                    await Process.StandardInput.FlushAsync();
                }
                catch (IOException)
                {
                }

                var exit = Process.WaitForExitAsync();
                if (await Task.WhenAny(exit, Task.Delay(15000)) != exit)
                {
                    Process.Kill(true);
                    await Process.WaitForExitAsync();
                    throw new TimeoutException("The recorder did not stop cleanly. The temporary recording has been retained for recovery.");
                }
            }

            await Wait();
        }

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                try
                {
                    Process.Kill(true);
                }
                catch
                {
                }
            }

            Process.Dispose();
        }
    }

    public static async Task Run(params string[] args)
    {
        using var job = new Job(args);
        await job.Wait();
    }

    public static async Task<string[]> Cameras()
    {
        using var job = new Job(new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" });
        try
        {
            await job.Wait();
        }
        catch (InvalidOperationException)
        {
        }

        return Regex.Matches(job.Log, "\"([^\"]+)\" \\(video\\)").Select(m => m.Groups[1].Value).Distinct().ToArray();
    }
}

public sealed record RecordingOptions(int Fps = 30, bool Cursor = true, bool SystemAudio = false, bool Microphone = false, string? Camera = null);
internal sealed class AudioTrack
{
    readonly IWaveIn capture;
    readonly WaveFileWriter writer;
    readonly Stopwatch clock = new();
    readonly object gate = new();
    readonly TaskCompletionSource<bool> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Path { get; }
    public Exception? Error { get; private set; }

    public AudioTrack(string path, bool loopback)
    {
        Path = path;
        capture = loopback ? new WasapiLoopbackCapture() : new WaveInEvent
        {
            WaveFormat = new WaveFormat(48000, 16, 1),
            BufferMilliseconds = 50
        };
        writer = new WaveFileWriter(path, capture.WaveFormat);
        capture.DataAvailable += (_, e) =>
        {
            lock (gate)
            {
                try
                {
                    long expected = (long)(clock.Elapsed.TotalSeconds * writer.WaveFormat.AverageBytesPerSecond) - e.BytesRecorded;
                    long gap = expected - writer.Length;
                    if (gap > writer.WaveFormat.AverageBytesPerSecond / 10)
                        Silence(gap);
                    writer.Write(e.Buffer, 0, e.BytesRecorded);
                }
                catch (Exception ex)
                {
                    Error = ex;
                }
            }
        };
        capture.RecordingStopped += (_, e) =>
        {
            Error ??= e.Exception;
            stopped.TrySetResult(true);
        };
        try
        {
            clock.Start();
            capture.StartRecording();
        }
        catch
        {
            writer.Dispose();
            capture.Dispose();
            throw;
        }
    }

    private void Silence(long bytes)
    {
        bytes -= bytes % writer.WaveFormat.BlockAlign;
        var zeros = new byte[8192 - (8192 % writer.WaveFormat.BlockAlign)];
        while (bytes > 0)
        {
            int n = (int)Math.Min(bytes, zeros.Length);
            writer.Write(zeros, 0, n);
            bytes -= n;
        }
    }

    public async Task Stop()
    {
        capture.StopRecording();
        var done = await Task.WhenAny(stopped.Task, Task.Delay(5000));
        lock (gate)
        {
            long remaining = (long)(clock.Elapsed.TotalSeconds * writer.WaveFormat.AverageBytesPerSecond) - writer.Length;
            if (remaining > 0)
                Silence(remaining);
            writer.Dispose();
        }

        capture.Dispose();
        if (done != stopped.Task)
            throw new TimeoutException("Audio capture did not finish.");
        if (Error != null)
            throw new InvalidOperationException("Audio recording failed: " + Error.Message, Error);
    }
}

public sealed class Recorder
{
    readonly Rectangle area;
    readonly RecordingOptions options;
    readonly string folder;
    readonly List<string> segments = new();
    readonly List<AudioTrack> audio = new();
    Ffmpeg.Job? recording;
    string? raw;
    int index;
    public bool Active => recording != null;
    public string RecoveryFolder => folder;

    public Recorder(Rectangle area, RecordingOptions options)
    {
        this.area = area;
        this.options = options;
        folder = Path.Combine(Paths.Work, "record-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
    }

    public async Task StartSegment()
    {
        if (recording != null)
            return;
        index++;
        raw = Path.Combine(folder, $"raw-{index}.mp4");
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel",
            "warning",
            "-thread_queue_size",
            "512",
            "-f",
            "gdigrab",
            "-framerate",
            options.Fps.ToString(),
            "-draw_mouse",
            options.Cursor ? "1" : "0",
            "-offset_x",
            area.X.ToString(),
            "-offset_y",
            area.Y.ToString(),
            "-video_size",
            $"{area.Width}x{area.Height}",
            "-i",
            "desktop"
        };
        if (options.Camera != null)
        {
            args.AddRange(new[] { "-thread_queue_size", "512", "-f", "dshow", "-i", "video=" + options.Camera, "-filter_complex", "[1:v]scale=320:-2[cam];[0:v][cam]overlay=W-w-20:H-h-20:shortest=1,pad=ceil(iw/2)*2:ceil(ih/2)*2[v]", "-map", "[v]" });
        }
        else
            args.AddRange(new[] { "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2" });
        args.AddRange(new[] { "-an", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", raw });
        try
        {
            recording = new Ffmpeg.Job(args);
            if (options.SystemAudio)
                audio.Add(new AudioTrack(Path.Combine(folder, $"system-{index}.wav"), true));
            if (options.Microphone)
                audio.Add(new AudioTrack(Path.Combine(folder, $"mic-{index}.wav"), false));
            await Task.Delay(900);
            if (recording.Process.HasExited)
                await recording.Wait();
        }
        catch
        {
            foreach (var a in audio)
            {
                try
                {
                    await a.Stop();
                }
                catch
                {
                }
            }

            audio.Clear();
            recording?.Dispose();
            recording = null;
            throw;
        }
    }

    public async Task EndSegment()
    {
        if (recording == null)
            return;
        var job = recording;
        recording = null;
        Exception? failure = null;
        try
        {
            await Task.WhenAll(audio.Select(a => a.Stop()).Append(job.Stop()));
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            job.Dispose();
        }

        if (failure != null)
        {
            audio.Clear();
            throw failure;
        }

        var segment = Path.Combine(folder, $"segment-{index}.mp4");
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            raw!
        };
        foreach (var a in audio)
            args.AddRange(new[] { "-i", a.Path });
        if (audio.Count == 0)
            File.Copy(raw!, segment, true);
        else
        {
            string filter = audio.Count == 1 ? "[1:a]apad[a]" : "[1:a][2:a]amix=inputs=2:duration=longest:normalize=0,alimiter=limit=0.95,apad[a]";
            args.AddRange(new[] { "-filter_complex", filter, "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-shortest", "-movflags", "+faststart", segment });
            await Ffmpeg.Run(args.ToArray());
        }

        audio.Clear();
        segments.Add(segment);
    }

    public async Task<string> Finish()
    {
        await EndSegment();
        if (segments.Count == 0)
            throw new InvalidOperationException("No recording to save.");
        Directory.CreateDirectory(Paths.Library);
        var output = Path.Combine(Paths.Library, Paths.Unique(".mp4"));
        if (segments.Count == 1)
            File.Copy(segments[0], output);
        else
        {
            var list = Path.Combine(folder, "segments.txt");
            File.WriteAllLines(list, segments.Select(p => "file '" + p.Replace("\\", "/").Replace("'", "'\\''") + "'"), new UTF8Encoding(false));
            await Ffmpeg.Run("-y", "-hide_banner", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", list, "-c", "copy", "-movflags", "+faststart", output);
        }

        // Remove only the uniquely created session directory, and only after a successful final export.
        if (File.Exists(output) && new FileInfo(output).Length > 0 && Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Paths.Work) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(folder, true);
        return output;
    }

    public static async Task ExportVideo(string input, string output, double start, double duration, bool gif)
    {
        if (start < 0 || duration <= 0 || !double.IsFinite(start) || !double.IsFinite(duration))
            throw new ArgumentException("Enter a valid start time and duration.");
        var args = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-ss",
            start.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-i",
            input,
            "-t",
            duration.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (gif)
            args.AddRange(new[] { "-filter_complex", "fps=12,scale='min(960,iw)':-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse", "-loop", "0" });
        else
            args.AddRange(new[] { "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-c:a", "aac", "-movflags", "+faststart" });
        args.Add(output);
        await Ffmpeg.Run(args.ToArray());
    }
}

public sealed class RecordingPanel : Window
{
    readonly Recorder recorder;
    readonly TextBlock status;
    readonly Button pause, stop;
    readonly Stopwatch elapsed = new();
    readonly DispatcherTimer timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    bool paused, busy, finished, stopRequested;
    public string? Output { get; private set; }

    public RecordingPanel(Recorder recorder)
    {
        this.recorder = recorder;
        Title = "FrameForge · Recording";
        Width = 370;
        Height = 172;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var p = new StackPanel
        {
            Margin = new Thickness(16)
        };
        Content = p;
        status = Ui.Label("Preparing recording…", 18, bold: true);
        p.Children.Add(status);
        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        p.Children.Add(row);
        pause = Ui.Button("Pause", async () => await Toggle());
        stop = Ui.Button("Stop & save", async () => await Stop(), true);
        row.Children.Add(pause);
        row.Children.Add(stop);
        SourceInitialized += (_, _) => NativeCapture.Exclude(this);
        timer.Tick += (_, _) =>
        {
            if (!busy)
                status.Text = (paused ? "Paused  " : "● Recording  ") + elapsed.Elapsed.ToString(@"hh\:mm\:ss");
        };
        Loaded += async (_, _) =>
        {
            busy = true;
            pause.IsEnabled = stop.IsEnabled = false;
            try
            {
                await recorder.StartSegment();
                elapsed.Start();
                timer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Recording could not start");
                finished = true;
                Close();
            }
            finally
            {
                busy = false;
                pause.IsEnabled = stop.IsEnabled = true;
                if (stopRequested && !finished) await Stop();
            }
        };
        Closing += async (_, e) =>
        {
            if (!finished)
            {
                e.Cancel = true;
                await Stop();
            }
        };
        Closed += (_, _) => timer.Stop();
    }

    private async Task Toggle()
    {
        if (busy)
            return;
        busy = true;
        pause.IsEnabled = stop.IsEnabled = false;
        try
        {
            if (paused)
            {
                await recorder.StartSegment();
                elapsed.Start();
            }
            else
            {
                elapsed.Stop();
                status.Text = "Finishing segment…";
                await recorder.EndSegment();
            }

            paused = !paused;
            pause.Content = paused ? "Resume" : "Pause";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message + "\nRecovery files: " + recorder.RecoveryFolder);
            finished = true;
            Close();
        }
        finally
        {
            busy = false;
            pause.IsEnabled = stop.IsEnabled = true;
            if (stopRequested && !finished) await Stop();
        }
    }

    public async Task Stop()
    {
        stopRequested = true;
        if (busy || finished) return;
        busy = true;
        pause.IsEnabled = stop.IsEnabled = false;
        elapsed.Stop();
        status.Text = "Saving recording…";
        try
        {
            Output = await recorder.Finish();
            finished = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message + "\nRecovery files: " + recorder.RecoveryFolder, "Recording error");
            finished = true;
            Close();
        }
        finally
        {
            busy = false;
        }
    }
}
