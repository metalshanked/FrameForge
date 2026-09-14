using System.Runtime.InteropServices;
using Avalonia;
using FrameForge.Core;
using SkiaSharp;
using Tmds.DBus;
namespace FrameForge.Desktop;

[DBusInterface("org.freedesktop.portal.Screenshot")]
public interface IScreenshotPortal : IDBusObject
{
    Task<ObjectPath> ScreenshotAsync(string parentWindow,IDictionary<string,object> options);
}
[DBusInterface("org.freedesktop.portal.Request")]
public interface IPortalRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint Code,IDictionary<string,object> Results)> handler,Action<Exception> onError);
    Task CloseAsync();
}
public static class PlatformCapture
{
    public static bool Wayland=>OperatingSystem.IsLinux()&&!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    public static string Description=>OperatingSystem.IsWindows()?"Windows desktop":OperatingSystem.IsMacOS()?"macOS screen capture":Wayland?"Linux · Wayland portal":"Linux · desktop portal";
    public static async Task<SKBitmap?> Capture(bool region,PixelRect screen,CancellationToken cancel)
    {
        if(OperatingSystem.IsWindows())
        {
            var capture=Task.Run(()=>GrabWindows(screen));
            try{return await capture.WaitAsync(cancel);}
            catch(OperationCanceledException)
            {
                _=capture.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion)t.Result.Dispose();},TaskScheduler.Default);
                throw;
            }
        }
        if(OperatingSystem.IsLinux())return await Portal(cancel);
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        var temp=Path.Combine(AppPaths.Temp,Guid.NewGuid().ToString("N")+".png");
        try
        {
            var args=region?new[]{"-x","-i","-t","png",temp}:new[]{"-x","-m","-t","png",temp};
            var result=await ProcessRunner.Run("/usr/sbin/screencapture",args,cancel);
            if(!File.Exists(temp))
            {
                if(result.ExitCode==0 || region&&string.IsNullOrWhiteSpace(result.Error))return null;
                throw new InvalidOperationException("Allow Screen Recording for FrameForge in System Settings → Privacy & Security, then reopen the app. "+result.Error.Trim());
            }
            return Imaging.Load(temp);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static async Task<SKBitmap?> Portal(CancellationToken cancel)
    {
        using var connection=new Connection(Address.Session);
        var info=await connection.ConnectAsync();
        await PortalSupport.Require(connection,"Screenshot","screenshot capture",cancel);
        var token="frameforge"+Guid.NewGuid().ToString("N");
        var path=new ObjectPath("/org/freedesktop/portal/desktop/request/"+info.LocalName.TrimStart(':').Replace('.','_')+"/"+token);
        var request=connection.CreateProxy<IPortalRequest>("org.freedesktop.portal.Desktop",path);
        var completed=new TaskCompletionSource<(uint Code,IDictionary<string,object> Results)>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Subscribe before invoking Screenshot; a quick response must not be lost.
        using var watch=await request.WatchResponseAsync(r=>completed.TrySetResult(r),e=>completed.TrySetException(e));
        try
        {
            var portal=connection.CreateProxy<IScreenshotPortal>("org.freedesktop.portal.Desktop","/org/freedesktop/portal/desktop");
            var returned=await portal.ScreenshotAsync("",new Dictionary<string,object>{{"handle_token",token},{"interactive",true},{"modal",true}});
            if(returned!=path)throw new InvalidOperationException("This desktop portal uses an unsupported request protocol.");
            var result=await completed.Task.WaitAsync(cancel);
            if(result.Code==1)return null;
            if(result.Code!=0||!result.Results.TryGetValue("uri",out var value)||value is not string uri)
                throw new InvalidOperationException("The desktop portal could not capture the screen. Check your desktop screenshot permissions.");
            if(!Uri.TryCreate(uri,UriKind.Absolute,out var file)||!file.IsFile||file.IsUnc)
                throw new InvalidDataException("The screenshot portal did not return a local image.");
            return Imaging.Load(file.LocalPath);
        }
        finally{try{await request.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));}catch{}}
    }
    [StructLayout(LayoutKind.Sequential)]struct BitmapInfo
    {
        public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,SizeImage;public int XPels,YPels;public uint Used,Important;
    }
    [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int w,int h);
    [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool BitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr source,int sx,int sy,uint operation);
    [DllImport("gdi32.dll")]static extern int GetDIBits(IntPtr dc,IntPtr bmp,uint start,uint lines,IntPtr bits,ref BitmapInfo info,uint usage);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
    static SKBitmap GrabWindows(PixelRect r)
    {
        Imaging.ValidateSize(r.Width,r.Height);
        var dc=GetDC(IntPtr.Zero);var memory=IntPtr.Zero;var bitmap=IntPtr.Zero;var old=IntPtr.Zero;
        try
        {
            if(dc==IntPtr.Zero)throw new InvalidOperationException("The desktop is unavailable.");
            memory=CreateCompatibleDC(dc);bitmap=CreateCompatibleBitmap(dc,r.Width,r.Height);
            if(memory==IntPtr.Zero||bitmap==IntPtr.Zero)throw new InvalidOperationException("Could not allocate a screenshot.");
            old=SelectObject(memory,bitmap);
            if(!BitBlt(memory,0,0,r.Width,r.Height,dc,r.X,r.Y,0x40CC0020))throw new InvalidOperationException("Screen capture failed.");
            SelectObject(memory,old);old=IntPtr.Zero;
            var pixels=new SKBitmap(new SKImageInfo(r.Width,r.Height,SKColorType.Bgra8888,SKAlphaType.Opaque));
            var header=new BitmapInfo{Size=(uint)Marshal.SizeOf<BitmapInfo>(),Width=r.Width,Height=-r.Height,Planes=1,BitCount=32};
            if(GetDIBits(dc,bitmap,0,(uint)r.Height,pixels.GetPixels(),ref header,0)==0){pixels.Dispose();throw new InvalidOperationException("Could not read the screenshot.");}
            // GDI's unused alpha channel is zero; normalize before encoding PNG.
            var data=new byte[pixels.ByteCount];Marshal.Copy(pixels.GetPixels(),data,0,data.Length);
            for(var i=3;i<data.Length;i+=4)data[i]=255;Marshal.Copy(data,0,pixels.GetPixels(),data.Length);return pixels;
        }
        finally
        {
            if(old!=IntPtr.Zero)SelectObject(memory,old);
            if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(memory!=IntPtr.Zero)DeleteDC(memory);if(dc!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,dc);
        }
    }
}
