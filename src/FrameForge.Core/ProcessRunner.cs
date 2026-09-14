using System.Diagnostics;
using System.Text;
namespace FrameForge.Core;
public sealed record ProcessResult(int ExitCode,string Output,string Error);
public static class ProcessRunner
{
    public static string? Find(string name,string? variable=null)
    {
        var configured=variable==null?null:Environment.GetEnvironmentVariable(variable);
        if(!string.IsNullOrWhiteSpace(configured)&&File.Exists(configured))return Path.GetFullPath(configured);
        var file=name+(OperatingSystem.IsWindows()?".exe":"");
        foreach(var dir in new[]{Path.Combine(AppContext.BaseDirectory,"tools"),AppContext.BaseDirectory}
          .Concat((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator))
          .Concat(OperatingSystem.IsMacOS()?new[]{"/opt/homebrew/bin","/usr/local/bin","/usr/bin"}:Array.Empty<string>()))
        {if(string.IsNullOrWhiteSpace(dir))continue;try{var p=Path.Combine(dir.Trim('"'),file);if(File.Exists(p))return p;}catch(ArgumentException){}}
        return null;
    }
    public static ProcessStartInfo StartInfo(string executable,IEnumerable<string> args,bool input=false)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=input};
        foreach(var arg in args)start.ArgumentList.Add(arg);return start;
    }
    static async Task<string> ReadBounded(StreamReader reader)
    {
        var result=new StringBuilder();var chunk=new char[4096];int n;
        while((n=await reader.ReadAsync(chunk))>0){result.Append(chunk,0,n);if(result.Length>1_000_000)result.Remove(0,result.Length-1_000_000);}
        return result.ToString();
    }
    public static async Task<ProcessResult> Run(string executable,IEnumerable<string> args,CancellationToken cancel=default)
    {
        using var process=new Process{StartInfo=StartInfo(executable,args)};process.Start();
        var output=ReadBounded(process.StandardOutput);var error=ReadBounded(process.StandardError);
        try{await process.WaitForExitAsync(cancel);}
        catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw;}
        return new(process.ExitCode,await output,await error);
    }
    public static Task OpenFile(string path)=>OpenPath(path,false);
    public static Task OpenFolder(string path)=>OpenPath(path,true);
    static async Task OpenPath(string path,bool folder)
    {
        path=Path.GetFullPath(path);
        if(folder&&!Directory.Exists(path))throw new DirectoryNotFoundException("The capture folder could not be found.");
        if(!folder&&!File.Exists(path))throw new FileNotFoundException("This capture no longer exists.",path);
        if(OperatingSystem.IsWindows())
        {
            if(folder)global::FrameForge.ShellPaths.OpenFolder(path);
            else using(Process.Start(new ProcessStartInfo(path){UseShellExecute=true})){ }
            return;
        }
        var executable=OperatingSystem.IsMacOS()?"/usr/bin/open":Find("xdg-open");
        if(executable==null)throw new InvalidOperationException("A Linux file opener is unavailable. Install xdg-utils and a file manager. Capture location: "+path);
        var process=Process.Start(StartInfo(executable,new[]{path}))??throw new IOException("The file manager could not start.");
        // Some desktop openers remain attached to their viewer. Observe immediate errors,
        // then leave a successfully launched viewer running independently of FrameForge.
        async Task<ProcessResult> Observe()
        {
            using(process)
            {
                var output=ReadBounded(process.StandardOutput);var error=ReadBounded(process.StandardError);
                await process.WaitForExitAsync();return new(process.ExitCode,await output,await error);
            }
        }
        var completion=Observe();
        if(await Task.WhenAny(completion,Task.Delay(1500))!=completion)
        {
            _=completion.ContinueWith(t=>{_ = t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            return;
        }
        var result=await completion;
        if(result.ExitCode!=0)throw new InvalidOperationException("Your desktop could not open "+(folder?"the capture folder":"this capture")+". Check that a file manager or viewer is installed. Location: "+path);
    }
}
