using System.Text.Json;
namespace FrameForge.Core;

public sealed record DeletedCapture(string Folder, string[] Names);
public sealed class CaptureLibrary
{
    public string Folder { get; }
    public string DeletedFolder { get; }
    public CaptureLibrary(string root) { Folder=Path.GetFullPath(Path.Combine(root,"Library")); DeletedFolder=Path.GetFullPath(Path.Combine(root,"Deleted Captures")); Directory.CreateDirectory(Folder); }
    public DeletedCapture Delete(string path)
    {
        path=Path.GetFullPath(path);
        if(!SamePath(Path.GetDirectoryName(path)!,Folder)||Path.GetExtension(path).ToLowerInvariant() is not (".ffg" or ".mp4" or ".webm"))
            throw new InvalidOperationException("Only saved captures in this library can be deleted.");
        if(!File.Exists(path))throw new FileNotFoundException("This capture is no longer in the library.",path);
        string[] files=new[]{path,Path.ChangeExtension(path,".png")}.Where(File.Exists).Distinct().ToArray();
        if(files.Any(f=>(File.GetAttributes(f)&FileAttributes.ReparsePoint)!=0))throw new IOException("Manage linked capture files in your file manager.");
        string destination=Path.Combine(DeletedFolder,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var moved=new List<string>();
        try { foreach(var file in files){File.Move(file,Path.Combine(destination,Path.GetFileName(file)));moved.Add(file);} }
        catch { foreach(var file in moved.AsEnumerable().Reverse())File.Move(Path.Combine(destination,Path.GetFileName(file)),file);Directory.Delete(destination);throw; }
        return new(destination,files.Select(Path.GetFileName).Cast<string>().ToArray());
    }
    public void Restore(DeletedCapture capture)
    {
        if(!SamePath(Path.GetDirectoryName(Path.GetFullPath(capture.Folder))!,DeletedFolder)
           ||capture.Names.Length is <1 or >2||capture.Names.Any(n=>n!=Path.GetFileName(n)||n is "." or ".."))
            throw new InvalidOperationException("Invalid deleted capture.");
        if(capture.Names.Any(n=>File.Exists(Path.Combine(Folder,n))))throw new IOException("A capture with this name already exists. Open Deleted Captures to recover it without overwriting.");
        if(capture.Names.Any(n=>!File.Exists(Path.Combine(capture.Folder,n))))throw new IOException("A deleted file has moved. Open Deleted Captures to locate it.");
        var restored=new List<string>();
        try { foreach(var name in capture.Names){File.Move(Path.Combine(capture.Folder,name),Path.Combine(Folder,name));restored.Add(name);} }
        catch { foreach(var name in restored.AsEnumerable().Reverse())File.Move(Path.Combine(Folder,name),Path.Combine(capture.Folder,name));throw; }
        Directory.Delete(capture.Folder);
    }
    public static bool SamePath(string a,string b)=>string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
}
