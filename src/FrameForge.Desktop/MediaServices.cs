using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FrameForge.Core;
namespace FrameForge.Desktop;
public static class Ocr
{
    public static async Task<string> Read(byte[] png,string language,CancellationToken cancel)
    {
        if(!Regex.IsMatch(language,@"^[a-zA-Z0-9_+-]{1,80}$"))throw new ArgumentException("Choose an OCR language code, such as eng.");
        if(OperatingSystem.IsMacOS())
        {
            string image=System.IO.Path.Combine(AppPaths.Temp,Guid.NewGuid().ToString("N")+".png");
            try
            {
                await File.WriteAllBytesAsync(image,png,cancel);
                var result=await NativeBridge.Run("ocr",new[]{"--input",image,"--language",language},cancel);
                return result.GetProperty("text").GetString()??"";
            }
            finally{if(File.Exists(image))File.Delete(image);}
        }
        var tool=ProcessRunner.Find("tesseract","FRAMEFORGE_TESSERACT")??throw new InvalidOperationException("Install Tesseract and its language data to use offline OCR. You can also set FRAMEFORGE_TESSERACT to its executable.");
        var path=Path.Combine(AppPaths.Temp,Guid.NewGuid().ToString("N")+".png");
        try{await File.WriteAllBytesAsync(path,png,cancel);var r=await ProcessRunner.Run(tool,new[]{path,"stdout","-l",language},cancel);
            if(r.ExitCode!=0)throw new InvalidOperationException("OCR failed. "+r.Error.Trim());return r.Output.Trim();}
        finally{if(File.Exists(path))File.Delete(path);}
    }
}
public sealed class Recorder : IRecording
{
    readonly Process process;
    readonly Task<string> errors;
    public string Path { get; }
    public bool Paused => false;
    public Task Pause()=>throw new NotSupportedException("Use the native Windows edition for recording pause/resume.");
    public Task Resume()=>Pause();
    Recorder(Process p,string path){process=p;Path=path;errors=ReadLog(p.StandardError);}
    static async Task<string> ReadLog(StreamReader reader)
    {var text=new StringBuilder();var buffer=new char[4096];int count;while((count=await reader.ReadAsync(buffer))>0){text.Append(buffer,0,count);if(text.Length>16000)text.Remove(0,text.Length-16000);}return text.ToString();}
    public static string Ffmpeg=>ProcessRunner.Find("ffmpeg","FRAMEFORGE_FFMPEG")??throw new InvalidOperationException("Install FFmpeg for recording and video export, or set FRAMEFORGE_FFMPEG to its executable.");
    public static async Task<Recorder> Start(CancellationToken cancel)
    {
        if(PlatformCapture.Wayland)throw new NotSupportedException("Wayland recording is not available in this preview. Use an X11 session for recording; screenshots work through the desktop portal.");
        var executable=Ffmpeg;var args=new List<string>{"-hide_banner","-y"};
        if(OperatingSystem.IsWindows())args.AddRange(new[]{"-f","gdigrab","-framerate","30","-i","desktop"});
        else if(OperatingSystem.IsLinux())
        {
            var display=Environment.GetEnvironmentVariable("DISPLAY");
            if(string.IsNullOrWhiteSpace(display))throw new InvalidOperationException("An X11 desktop is required for this recorder.");
            // FFmpeg uses the screen dimensions when video_size is omitted.
            args.AddRange(new[]{"-f","x11grab","-framerate","30","-i",display});
        }
        else
        {
            var devices=await ProcessRunner.Run(executable,new[]{"-hide_banner","-f","avfoundation","-list_devices","true","-i",""},cancel);
            var match=Regex.Match(devices.Error,@"\[(\d+)\]\s+Capture screen");
            if(!match.Success)throw new InvalidOperationException("FFmpeg could not find a screen input. Enable Screen Recording for FrameForge in macOS System Settings.");
            args.AddRange(new[]{"-f","avfoundation","-framerate","30","-capture_cursor","1","-i",match.Groups[1].Value+":none"});
        }
        var output=AppPaths.NewCapture(".mp4");
        args.AddRange(new[]{"-an","-vf","pad=ceil(iw/2)*2:ceil(ih/2)*2","-c:v","libx264","-preset","veryfast","-crf","20","-pix_fmt","yuv420p","-movflags","+faststart",output});
        var start=ProcessRunner.StartInfo(executable,args,true);start.RedirectStandardOutput=false;
        var process=Process.Start(start)??throw new InvalidOperationException("Could not start FFmpeg.");
        var recorder=new Recorder(process,output);
        try{await Task.Delay(800,cancel);if(process.HasExited)throw new InvalidOperationException("Recording failed. "+await recorder.errors);return recorder;}
        catch{recorder.Dispose();throw;}
    }
    public async Task Stop()
    {
        if(!process.HasExited){try{await process.StandardInput.WriteLineAsync("q");await process.StandardInput.FlushAsync();}catch(IOException){}
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));}
            catch(TimeoutException){process.Kill(true);await process.WaitForExitAsync();throw new IOException("Recording could not finish. The partial file was kept for recovery.");}}
        var log=await errors;
        if(process.ExitCode!=0)throw new InvalidOperationException("Recording failed. "+log);
    }
    public void Dispose(){try{if(!process.HasExited)process.Kill(true);}finally{process.Dispose();}}
    public static async Task Convert(string source,string destination,double start,double seconds,bool gif,CancellationToken cancel)
    {
        if(!double.IsFinite(start)||start<0||!double.IsFinite(seconds)||seconds<=0)throw new ArgumentException("Enter a valid start time and duration.");
        string temporary=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(destination))!,".frameforge-"+Guid.NewGuid().ToString("N")+(gif?".gif":".mp4"));
        try
        {
            if(OperatingSystem.IsMacOS())
            {
                await NativeBridge.Run("convert",new[]{"--input",source,"--output",temporary,"--start",start.ToString(System.Globalization.CultureInfo.InvariantCulture),"--duration",seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),"--gif",gif?"true":"false"},cancel);
            }
            else
            {
                var args=new List<string>{"-hide_banner","-y","-ss",start.ToString(System.Globalization.CultureInfo.InvariantCulture),"-i",source,"-t",seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                args.AddRange(gif?new[]{"-filter_complex","fps=12,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse"}:new[]{"-c:v","libx264","-crf","20","-c:a","aac","-movflags","+faststart"});
                args.Add(temporary);var r=await ProcessRunner.Run(Ffmpeg,args,cancel);if(r.ExitCode!=0)throw new InvalidOperationException("Video export failed. "+r.Error);
            }
            cancel.ThrowIfCancellationRequested();File.Move(temporary,destination,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
