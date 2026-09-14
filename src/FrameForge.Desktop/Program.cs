using Avalonia;
namespace FrameForge.Desktop;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) { if(!SingleInstance.Start(args))return; try{BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);}finally{SingleInstance.Dispose();} }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
