using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
namespace FrameForge.Desktop;
internal static class SingleInstance
{
    static Mutex? mutex;
    static bool owner;
    static CancellationTokenSource? lifetime;
    static readonly string Name="FrameForge.Desktop."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.Root)))[..20];
    internal static event Action<bool>? Requested;
    internal static bool Start(string[] args)
    {
        mutex=new Mutex(true,Name,out owner);
        if(!owner)
        {
            try{using var pipe=new NamedPipeClientStream(".",Name,PipeDirection.Out);pipe.Connect(1500);using var writer=new StreamWriter(pipe);writer.WriteLine(args.Contains("--capture")?"capture":"show");writer.Flush();}
            catch(IOException){}catch(TimeoutException){}
            mutex.Dispose();mutex=null;return false;
        }
        lifetime=new();_=Listen(lifetime.Token);return true;
    }
    static async Task Listen(CancellationToken cancel)
    {
        while(!cancel.IsCancellationRequested)
        {
            try
            {
                await using var pipe=new NamedPipeServerStream(Name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancel);using var reader=new StreamReader(pipe);var command=await reader.ReadLineAsync(cancel);
                Requested?.Invoke(command=="capture");
            }
            catch(OperationCanceledException){break;}catch(IOException){await Task.Delay(200,cancel);}
        }
    }
    internal static void Dispose()
    {
        lifetime?.Cancel();lifetime=null;
        if(owner){mutex?.ReleaseMutex();owner=false;}mutex?.Dispose();mutex=null;
    }
}
