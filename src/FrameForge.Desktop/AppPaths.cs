using FrameForge.Core;
namespace FrameForge.Desktop;
public static class AppPaths
{
    public static string Root { get; } = ResolveRoot();
    public static string Library=>Ensure(Path.Combine(Root,"Library"));
    public static string Temp=>Ensure(Path.Combine(Root,"Temporary"));
    static string ResolveRoot()
    {
        var custom=Environment.GetEnvironmentVariable("FRAMEFORGE_DESKTOP_DATA");
        if(!string.IsNullOrWhiteSpace(custom))return Ensure(Path.GetFullPath(custom));
        var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Ensure(OperatingSystem.IsMacOS()?Path.Combine(home,"Library","Application Support","FrameForge Desktop"):
          OperatingSystem.IsLinux()?Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is string x&&Path.IsPathFullyQualified(x)?x:Path.Combine(home,".local","share"),"frameforge-desktop"):
          Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrameForge.Desktop"));
    }
    static string Ensure(string path){Directory.CreateDirectory(path);return path;}
    public static string NewCapture(string extension)=>Path.Combine(Library,"Capture-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+extension);
}
