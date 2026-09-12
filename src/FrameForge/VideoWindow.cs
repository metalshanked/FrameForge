using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FrameForge;
public sealed class VideoWindow : Window
{
    readonly MediaElement player = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Close,
        Stretch = Stretch.Uniform
    };
    readonly Slider seek = new()
    {
        Minimum = 0,
        Margin = new Thickness(12, 6, 12, 6)
    };
    readonly TextBlock info = Ui.Label("Loading video…");
    readonly DispatcherTimer timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };
    readonly string path;
    bool scrubbing, playing = true;
    public VideoWindow(Window owner, string path)
    {
        this.path = path;
        Owner = owner;
        Title = "FrameForge · " + Path.GetFileName(path);
        Width = 1000;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel
        {
            Margin = new Thickness(14)
        };
        Content = root;
        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        bottom.Children.Add(seek);
        bottom.Children.Add(info);
        var actions = new WrapPanel();
        bottom.Children.Add(actions);
        actions.Children.Add(Ui.Button("Play / pause", () =>
        {
            if (playing)
                player.Pause();
            else
                player.Play();
            playing = !playing;
        }));
        actions.Children.Add(Ui.Button("Save video as…", SaveAs));
        actions.Children.Add(Ui.Button("Trim / export GIF…", async () => await Export(), true));
        actions.Children.Add(Ui.Button("Open in player", () => MainWindow.OpenShell(path)));
        root.Children.Add(new Border { Background = Ui.Brush("#151827"), Child = player });
        player.Source = new Uri(path);
        player.MediaOpened += (_, _) =>
        {
            if (player.NaturalDuration.HasTimeSpan)
            {
                seek.Maximum = player.NaturalDuration.TimeSpan.TotalSeconds;
                info.Text = player.NaturalVideoWidth + " × " + player.NaturalVideoHeight + " · " + player.NaturalDuration.TimeSpan.ToString(@"hh\:mm\:ss");
            }
        };
        player.MediaFailed += (_, e) =>
        {
            info.Text = "Windows cannot preview this video. Use Open in player, or export it to MP4.";
        };
        player.MediaEnded += (_, _) =>
        {
            player.Position = TimeSpan.Zero;
            player.Pause();
            playing = false;
        };
        seek.PreviewMouseLeftButtonDown += (_, _) => scrubbing = true;
        seek.PreviewMouseLeftButtonUp += (_, _) =>
        {
            player.Position = TimeSpan.FromSeconds(seek.Value);
            scrubbing = false;
        };
        timer.Tick += (_, _) =>
        {
            if (!scrubbing)
                seek.Value = player.Position.TotalSeconds;
        };
        Loaded += (_, _) =>
        {
            player.Play();
            timer.Start();
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            player.Close();
        };
    }

    private void SaveAs()
    {
        string extension = Path.GetExtension(path);
        var d = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Original video format|*" + extension,
            DefaultExt = extension,
            FileName = Path.GetFileName(path)
        };
        if (d.ShowDialog(this) == true)
        {
            if (Path.GetFullPath(path).Equals(Path.GetFullPath(d.FileName), StringComparison.OrdinalIgnoreCase))
                return;
            File.Copy(path, d.FileName, true);
            info.Text = "Saved " + d.FileName;
        }
    }

    private async Task Export()
    {
        var w = Dialogs.Shell(this, "Trim video or create a GIF", 440, 340);
        var p = new StackPanel
        {
            Margin = new Thickness(20)
        };
        Dialogs.FitForm(w, p);
        p.Children.Add(Ui.Label("Choose the section to export", 19, bold: true));
        p.Children.Add(Ui.Label("Start time (seconds)"));
        var start = new TextBox
        {
            Text = "0"
        };
        p.Children.Add(start);
        p.Children.Add(Ui.Label("Duration (seconds)"));
        var duration = new TextBox
        {
            Text = (player.NaturalDuration.HasTimeSpan ? Math.Round(player.NaturalDuration.TimeSpan.TotalSeconds, 2) : 10).ToString()
        };
        p.Children.Add(duration);
        var type = Ui.Combo(new[] { "MP4 video", "Animated GIF (12 fps, up to 960 px wide)" });
        p.Children.Add(type);
        double s = 0, d = 0;
        p.Children.Add(Ui.Button("Choose export file…", () =>
        {
            if (double.TryParse(start.Text, out s) && double.TryParse(duration.Text, out d) && s >= 0 && d > 0 && double.IsFinite(s) && double.IsFinite(d))
            {
                if (player.NaturalDuration.HasTimeSpan && s >= player.NaturalDuration.TimeSpan.TotalSeconds)
                {
                    MessageBox.Show(w, "Start must be before the end of the video.");
                    return;
                }

                w.DialogResult = true;
            }
            else
                MessageBox.Show(w, "Enter a nonnegative start and a positive duration.");
        }, true));
        if (w.ShowDialog() != true)
            return;
        bool gif = type.SelectedIndex == 1;
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Filter = gif ? "Animated GIF|*.gif" : "MP4 video|*.mp4",
            FileName = Path.GetFileNameWithoutExtension(path) + "-clip" + (gif ? ".gif" : ".mp4")
        };
        if (save.ShowDialog(this) != true)
            return;
        if (Path.GetFullPath(save.FileName).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Choose a new filename to keep the source video intact.");
            return;
        }

        try
        {
            info.Text = "Exporting…";
            IsEnabled = false;
            await Recorder.ExportVideo(path, save.FileName, s, d, gif);
            info.Text = "Exported " + Path.GetFileName(save.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed");
        }
        finally
        {
            IsEnabled = true;
        }
    }

    public static async Task<RecordingOptions?> Options(Window owner)
    {
        var w = Dialogs.Shell(owner, "Screen recording settings", 520, 455);
        var p = new StackPanel
        {
            Margin = new Thickness(20)
        };
        Dialogs.FitForm(w, p);
        p.Children.Add(Ui.Label("Record a quick explanation", 21, bold: true));
        p.Children.Add(Ui.Label("Choose your options, select an area, and recording starts after a three-second countdown."));
        var fps = Ui.Combo(new[] { "15 fps", "30 fps", "60 fps" }, 1);
        p.Children.Add(fps);
        var cursor = new CheckBox
        {
            Content = "Show cursor",
            IsChecked = true
        };
        var system = new CheckBox
        {
            Content = "Record system audio (default speakers)"
        };
        var mic = new CheckBox
        {
            Content = "Record microphone (default input)"
        };
        p.Children.Add(cursor);
        p.Children.Add(system);
        p.Children.Add(mic);
        p.Children.Add(Ui.Label("Webcam picture-in-picture"));
        var camera = Ui.Combo(new[] { "No webcam" });
        p.Children.Add(camera);
        var scan = Ui.Button("Find cameras", async () =>
        {
            try
            {
                var cameras = await Ffmpeg.Cameras();
                camera.Items.Clear();
                camera.Items.Add("No webcam");
                foreach (var c in cameras)
                    camera.Items.Add(c);
                camera.SelectedIndex = 0;
                if (cameras.Length == 0)
                    MessageBox.Show(w, "No DirectShow cameras were found.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(w, ex.Message);
            }
        });
        p.Children.Add(scan);
        p.Children.Add(Ui.Button("Select recording area", () => w.DialogResult = true, true));
        await Task.CompletedTask;
        if (w.ShowDialog() != true)
            return null;
        return new RecordingOptions(fps.SelectedIndex switch
        {
            0 => 15,
            2 => 60,
            _ => 30
        }, cursor.IsChecked == true, system.IsChecked == true, mic.IsChecked == true, camera.SelectedIndex > 0 ? camera.SelectedItem.ToString() : null);
    }
}
