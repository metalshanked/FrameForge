using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using FrameForge.Core;
namespace FrameForge.Desktop;

public sealed record CaptureSource(string Id,string Name)
{
    public override string ToString()=>Name;
}
public sealed class NativeBridge : IDisposable
{
    readonly Process process;
    readonly Channel<JsonElement> events=Channel.CreateUnbounded<JsonElement>(new(){SingleReader=true,SingleWriter=true});
    readonly SemaphoreSlim commandLock=new(1);
    readonly Task readTask;
    readonly Task<string> errorTask;
    int nextId;
    bool disposed;
    public int Width { get; private set; }
    public int Height { get; private set; }
    NativeBridge(Process process)
    {
        this.process=process;
        errorTask=ReadError();
        readTask=ReadEvents();
    }
    static (string File,string[] Prefix) Command()
    {
        if(OperatingSystem.IsMacOS())
        {
            string path=Path.Combine(AppContext.BaseDirectory,"native","frameforge-native");
            if(!File.Exists(path))throw new FileNotFoundException("This build is missing the macOS capture helper. Install the complete Mac application bundle.");
            return(path,Array.Empty<string>());
        }
        if(OperatingSystem.IsLinux())
        {
            string python=ProcessRunner.Find("python3")??throw new InvalidOperationException("Install Python 3 and FrameForge's Linux capture dependencies.");
            return(python,new[]{"-u",Path.Combine(AppContext.BaseDirectory,"native","frameforge-linux.py")});
        }
        throw new PlatformNotSupportedException();
    }
    public static async Task<NativeBridge> Start(string mode,IEnumerable<string> arguments,CancellationToken cancel)
    {
        var command=Command();
        var process=Process.Start(ProcessRunner.StartInfo(command.File,command.Prefix.Concat(new[]{mode}).Concat(arguments),true))
          ??throw new IOException("Could not start the native capture service.");
        var bridge=new NativeBridge(process);
        try
        {
            var ready=await bridge.ReadEvent(cancel).WaitAsync(TimeSpan.FromMinutes(2),cancel);
            if(ready.GetProperty("event").GetString()!="ready")throw new IOException("The capture service did not become ready.");
            bridge.Width=ready.GetProperty("width").GetInt32();bridge.Height=ready.GetProperty("height").GetInt32();
            return bridge;
        }
        catch{bridge.Dispose();throw;}
    }
    public static async Task<JsonElement> Run(string mode,IEnumerable<string> args,CancellationToken cancel)
    {
        var command=Command();
        var result=await ProcessRunner.Run(command.File,command.Prefix.Concat(new[]{mode}).Concat(args),cancel);
        foreach(var line in result.Output.Split('\n',StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            using var parsed=JsonDocument.Parse(line);
            var value=parsed.RootElement;
            if(value.GetProperty("event").GetString()=="error")throw new InvalidOperationException(value.GetProperty("message").GetString());
            if(result.ExitCode==0)return value.Clone();
        }
        throw new IOException("The native operation failed. "+result.Error.Trim());
    }
    async Task ReadEvents()
    {
        try
        {
            while(await process.StandardOutput.ReadLineAsync() is string line)
            {
                if(line.Length>1_000_000)throw new IOException("Native capture response was too large.");
                using var parsed=JsonDocument.Parse(line);
                if(parsed.RootElement.GetProperty("event").GetString()=="error")
                    throw new InvalidOperationException(parsed.RootElement.GetProperty("message").GetString());
                await events.Writer.WriteAsync(parsed.RootElement.Clone());
            }
            await process.WaitForExitAsync();
            events.Writer.TryComplete(new IOException("The capture service stopped. "+(await errorTask).Trim()));
        }
        catch(Exception e){events.Writer.TryComplete(e);}
    }
    async Task<string> ReadError()
    {
        var text=new System.Text.StringBuilder();var chars=new char[2048];int count;
        while((count=await process.StandardError.ReadAsync(chars))>0)
        {text.Append(chars,0,count);if(text.Length>16000)text.Remove(0,text.Length-16000);}
        return text.ToString();
    }
    async Task<JsonElement> ReadEvent(CancellationToken cancel)
    {
        try{return await events.Reader.ReadAsync(cancel);}
        catch(ChannelClosedException e) when(e.InnerException!=null){throw new InvalidOperationException(e.InnerException.Message,e.InnerException);}
    }
    public async Task Send(string command,IDictionary<string,object>? values=null,CancellationToken cancel=default)
    {
        await commandLock.WaitAsync(cancel);
        try
        {
            int id=++nextId;values=values==null?new Dictionary<string,object>():new Dictionary<string,object>(values);
            values["command"]=command;values["id"]=id;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(values));
            await process.StandardInput.FlushAsync(cancel);
            var reply=await ReadEvent(cancel).WaitAsync(TimeSpan.FromSeconds(command=="stop"?60:15),cancel);
            if(!reply.TryGetProperty("id",out var actual)||actual.GetInt32()!=id)throw new IOException("The capture service returned an unexpected response.");
        }
        finally{commandLock.Release();}
    }
    public async Task Stop()
    {
        await Send("stop");
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if(process.ExitCode!=0)throw new IOException("The capture service could not finish. "+await errorTask);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try { if(!process.HasExited)process.Kill(true); } catch(InvalidOperationException) { }
        process.Dispose();
    }
}
public interface IRecording : IDisposable
{
    string Path { get; }
    bool Paused { get; }
    Task Pause();
    Task Resume();
    Task Stop();
}
public sealed class NativeRecording : IRecording
{
    readonly NativeBridge bridge;
    public string Path { get; }
    public bool Paused { get; private set; }
    NativeRecording(NativeBridge bridge,string path){this.bridge=bridge;Path=path;}
    public static async Task<NativeRecording> Start(string source,string audio,bool cursor,CancellationToken cancel)
    {
        string path=AppPaths.NewCapture(".mp4");
        var args=new List<string>{"--output",path,"--audio",audio};
        if(OperatingSystem.IsMacOS())args.AddRange(new[]{"--source",source,"--cursor",cursor?"true":"false"});
        else if(!cursor)args.Add("--no-cursor");
        return new(await NativeBridge.Start("record",args,cancel),path);
    }
    public async Task Pause(){await bridge.Send("pause");Paused=true;}
    public async Task Resume(){await bridge.Send("resume");Paused=false;}
    public Task Stop()=>bridge.Stop();
    public void Dispose()=>bridge.Dispose();
}
