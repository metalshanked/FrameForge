namespace FrameForge.Desktop;
internal static class CaptureTrace
{
    public static void Write(string message)
    {
        if(Environment.GetEnvironmentVariable("FRAMEFORGE_CAPTURE_TRACE")!="1")return;
        try{File.AppendAllText(Path.Combine(AppPaths.Root,"capture-diagnostics.log"),DateTimeOffset.UtcNow.ToString("O")+" "+message+Environment.NewLine);}catch(IOException){}
    }
}
