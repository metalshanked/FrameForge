using System.Diagnostics;
using System.Security;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using FrameForge.Core;
namespace FrameForge.Desktop;
public static class DesktopIntegration
{
    public static void Show(Window window)
    {
        window.Show();
        if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;
        window.Activate();
        if(OperatingSystem.IsWindows())
        {
            var handle=window.TryGetPlatformHandle()?.Handle??IntPtr.Zero;
            if(handle!=IntPtr.Zero){SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x0043);SetForegroundWindow(handle);}
        }
    }
    public static void SetStartup(bool enabled)
    {
        string exe=Environment.ProcessPath??throw new IOException("The app executable could not be located.");
        string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? path=null;string text="";
        if(OperatingSystem.IsMacOS())
        {
            path=Path.Combine(home,"Library","LaunchAgents","app.frameforge.desktop.plist");
            text="<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\"><plist version=\"1.0\"><dict><key>Label</key><string>app.frameforge.desktop</string><key>ProgramArguments</key><array><string>"+SecurityElement.Escape(exe)+"</string><string>--background</string></array><key>RunAtLoad</key><true/></dict></plist>";
        }
        else if(OperatingSystem.IsLinux())
        {
            string config=Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is string c&&Path.IsPathFullyQualified(c)?c:Path.Combine(home,".config");
            path=Path.Combine(config,"autostart","frameforge-desktop.desktop");
            string escaped=exe.Replace("\\","\\\\").Replace("\"","\\\"").Replace("$","\\$").Replace("`","\\`").Replace("%","%%");
            text="[Desktop Entry]\nType=Application\nName=FrameForge\nExec=\""+escaped+"\" --background\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        }
        else if(OperatingSystem.IsWindows())
        {
            using var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if(enabled)key.SetValue("FrameForge.Desktop","\""+exe+"\" --background");else key.DeleteValue("FrameForge.Desktop",false);
            return;
        }
        if(path==null)throw new PlatformNotSupportedException();
        if(enabled)AtomicFile.Write(path,System.Text.Encoding.UTF8.GetBytes(text));else if(File.Exists(path))File.Delete(path);
    }
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
