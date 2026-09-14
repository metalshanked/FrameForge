using FrameForge.Desktop;
internal static class Program
{
    static int Main(string[] args)
    {
        var message=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SingleInstance.Requested+=capture=>message.TrySetResult(capture);
        bool first=SingleInstance.Start(args);
        try
        {
            if(args.Contains("--capture")){Console.WriteLine(first?"unexpected-primary":"secondary");return first?1:0;}
            if(!first)return 2;
            Console.WriteLine("ready");
            var capture=message.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
            Console.WriteLine(capture?"capture-received":"wrong-command");
            return capture?0:3;
        }
        finally{SingleInstance.Dispose();}
    }
}
namespace FrameForge.Desktop
{
    internal static class AppPaths
    {
        internal static string Root=>Environment.GetEnvironmentVariable("FRAMEFORGE_DESKTOP_DATA")??throw new InvalidOperationException("An isolated test root is required.");
    }
}
