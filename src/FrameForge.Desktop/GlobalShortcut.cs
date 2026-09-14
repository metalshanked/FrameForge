using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;
using FrameForge.Core;
using Tmds.DBus;
namespace FrameForge.Desktop;
public sealed class DesktopPreferences
{
    public bool CloseToTray { get; set; }
    public bool ShortcutEnabled { get; set; }
    public string Key { get; set; } = "Tilde";
    public bool Control { get; set; } = true;
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public string OcrLanguage { get; set; } = "eng";
    public string Trigger=>string.Join("+",new[]{Control?"CTRL":null,Alt?"ALT":null,Shift?"SHIFT":null,Key=="Tilde"?"grave":Key}.Where(s=>s!=null));
    public bool Valid=>(Control||Alt)&&!string.IsNullOrEmpty(Key)&&(Key=="Tilde"||Key.Length==1&&Key[0] is >= 'A' and <= 'Z');
    public static DesktopPreferences Load()
    {
        try{var path=Path.Combine(AppPaths.Root,"preferences.json");
            if(!File.Exists(path)||new FileInfo(path).Length>65536)return new();
            var p=JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(path));return p?.Valid==true?p:new();}
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return new();}
    }
    public void Save(){if(!Valid)throw new ArgumentException("Use Ctrl or Alt with a letter or tilde key.");AtomicFile.Write(Path.Combine(AppPaths.Root,"preferences.json"),JsonSerializer.SerializeToUtf8Bytes(this));}
}
[DBusInterface("org.freedesktop.portal.GlobalShortcuts")]
public interface IShortcutPortal : IDBusObject
{
    Task<ObjectPath> CreateSessionAsync(IDictionary<string,object> options);
    Task<ObjectPath> BindShortcutsAsync(ObjectPath session,(string Id,IDictionary<string,object> Options)[] shortcuts,string parent,IDictionary<string,object> options);
    Task<IDisposable> WatchActivatedAsync(Action<(ObjectPath Session,string Id,ulong Timestamp,IDictionary<string,object> Options)> handler,Action<Exception> error);
}
public sealed class GlobalShortcut : IDisposable
{
    Thread? thread;uint threadId;IntPtr macHotkey,macHandler;MacCallback? callback;
    Connection? bus;IDisposable? watch;
    bool disposed;
    public string Display { get; private set; } = "";
    public static async Task<GlobalShortcut> Register(DesktopPreferences p,Action capture,CancellationToken cancel)
    {
        if(!p.Valid)throw new ArgumentException("Invalid capture shortcut.");
        var result=new GlobalShortcut{Display=p.Trigger};
        Action fire=()=>Dispatcher.UIThread.Post(()=>{if(!result.disposed)capture();});
        try
        {
            if(OperatingSystem.IsWindows())
            {
                var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                result.thread=new Thread(()=>
                {
                    result.threadId=GetCurrentThreadId();PeekMessage(out _,IntPtr.Zero,0,0,0);
                    uint mods=0x4000u|(p.Control?2u:0)|(p.Alt?1u:0)|(p.Shift?4u:0);
                    if(!RegisterHotKey(IntPtr.Zero,1,mods,p.Key=="Tilde"?0xC0u:p.Key[0])){ready.TrySetException(new InvalidOperationException("That shortcut is already in use. Choose another combination."));return;}
                    ready.TrySetResult(true);
                    try{while(GetMessage(out var msg,IntPtr.Zero,0,0)>0){if(msg.Message==0x312)fire();}}
                    finally{UnregisterHotKey(IntPtr.Zero,1);}
                }){IsBackground=true,Name="FrameForge capture shortcut"};
                result.thread.Start();await ready.Task.WaitAsync(cancel);
            }
            else if(OperatingSystem.IsMacOS())
            {
                int[] letters={0,11,8,2,14,3,5,4,34,38,40,37,46,45,31,35,12,15,1,17,32,9,13,7,16,6};
                uint code=p.Key=="Tilde"?50u:(uint)letters[p.Key[0]-'A'];
                var type=new EventType{Class=0x6B657962,Kind=6};
                result.callback=(_,_,_)=>{fire();return 0;};
                if(InstallEventHandler(GetApplicationEventTarget(),result.callback,1,ref type,IntPtr.Zero,out result.macHandler)!=0)
                    throw new InvalidOperationException("Could not create the macOS capture shortcut.");
                var id=new HotkeyId{Signature=0x46724667,Id=1};
                uint mods=(p.Control?4096u:0)|(p.Alt?2048u:0)|(p.Shift?512u:0);
                if(RegisterEventHotKey(code,mods,id,GetApplicationEventTarget(),0,out result.macHotkey)!=0)
                    throw new InvalidOperationException("That shortcut is already in use. Choose another combination.");
            }
            else
            {
                result.bus=new Connection(Address.Session);var info=await result.bus.ConnectAsync();
                var portal=result.bus.CreateProxy<IShortcutPortal>("org.freedesktop.portal.Desktop","/org/freedesktop/portal/desktop");
                var sessionResult=await Request(result.bus,info.LocalName,o=>{o["session_handle_token"]="ff"+Guid.NewGuid().ToString("N");return portal.CreateSessionAsync(o);},cancel);
                if(!sessionResult.TryGetValue("session_handle",out var handle)||handle is not string sessionName)throw new InvalidOperationException("The desktop did not create a shortcut session.");
                var session=new ObjectPath(sessionName);
                result.watch=await portal.WatchActivatedAsync(e=>{if(e.Session==session&&e.Id=="capture")fire();},_=>{});
                var bound=await Request(result.bus,info.LocalName,o=>portal.BindShortcutsAsync(session,new[]{("capture",(IDictionary<string,object>)new Dictionary<string,object>{{"description","Capture a region"},{"preferred_trigger",p.Trigger}})},"",o),cancel);
                if(!bound.TryGetValue("shortcuts",out var bindings)||bindings is not (string,IDictionary<string,object>)[] list||!list.Any(x=>x.Item1=="capture"))
                    throw new InvalidOperationException("The capture shortcut was not enabled in the desktop dialog.");
                var metadata=list.First(x=>x.Item1=="capture").Item2;
                if(metadata.TryGetValue("trigger_description",out var description)&&description is string display)result.Display=display;
            }
            return result;
        }
        catch{result.Dispose();throw;}
    }
    static async Task<IDictionary<string,object>> Request(Connection bus,string name,Func<IDictionary<string,object>,Task<ObjectPath>> start,CancellationToken cancel)
    {
        var token="ff"+Guid.NewGuid().ToString("N");
        var path=new ObjectPath("/org/freedesktop/portal/desktop/request/"+name.TrimStart(':').Replace('.','_')+"/"+token);
        var request=bus.CreateProxy<IPortalRequest>("org.freedesktop.portal.Desktop",path);
        var done=new TaskCompletionSource<(uint Code,IDictionary<string,object> Results)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch=await request.WatchResponseAsync(e=>done.TrySetResult(e),e=>done.TrySetException(e));
        try
        {
            if(await start(new Dictionary<string,object>{{"handle_token",token}})!=path)throw new InvalidOperationException("Unsupported shortcut portal protocol.");
            var response=await done.Task.WaitAsync(cancel);
            if(response.Code!=0)throw new OperationCanceledException("Shortcut setup was canceled.");
            return response.Results;
        }
        finally{try{await request.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));}catch{}}
    }
    public void Dispose()
    {
        disposed=true;watch?.Dispose();bus?.Dispose();
        if(threadId!=0){PostThreadMessage(threadId,0x12,UIntPtr.Zero,IntPtr.Zero);thread?.Join(2000);}
        if(macHotkey!=IntPtr.Zero)UnregisterEventHotKey(macHotkey);
        if(macHandler!=IntPtr.Zero)RemoveEventHandler(macHandler);
        threadId=0;macHotkey=macHandler=IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)]struct MessageData{public IntPtr Hwnd;public uint Message;public UIntPtr WParam;public IntPtr LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("user32.dll")]static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll")]static extern int GetMessage(out MessageData msg,IntPtr hwnd,uint min,uint max);
    [DllImport("user32.dll")]static extern bool PeekMessage(out MessageData msg,IntPtr hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")]static extern bool PostThreadMessage(uint id,uint msg,UIntPtr wparam,IntPtr lparam);
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    const string Carbon="/System/Library/Frameworks/Carbon.framework/Carbon";
    [StructLayout(LayoutKind.Sequential)]struct EventType{public uint Class,Kind;}
    [StructLayout(LayoutKind.Sequential)]struct HotkeyId{public uint Signature,Id;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int MacCallback(IntPtr call,IntPtr ev,IntPtr data);
    [DllImport(Carbon)]static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)]static extern int InstallEventHandler(IntPtr target,MacCallback callback,uint count,ref EventType type,IntPtr data,out IntPtr handler);
    [DllImport(Carbon)]static extern int RegisterEventHotKey(uint code,uint modifiers,HotkeyId id,IntPtr target,uint options,out IntPtr reference);
    [DllImport(Carbon)]static extern int UnregisterEventHotKey(IntPtr reference);
    [DllImport(Carbon)]static extern int RemoveEventHandler(IntPtr reference);
}
