using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FrameForge;

// Keep deleted capture pairs recoverable, including on network drives without a Recycle Bin.
internal static class CaptureLibrary
{
    internal sealed record DeletedCapture(string Directory, string[] OriginalFiles);
    internal static string DeletedFolder => Path.Combine(Paths.Root, "Deleted Captures");

    internal static DeletedCapture Delete(string path)
    {
        string full = Path.GetFullPath(path);
        string library = Path.GetFullPath(Paths.Library).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(full), library, StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(full).ToLowerInvariant() is not (".ffg" or ".mp4"))
            throw new InvalidOperationException("Only saved captures in this library can be deleted.");
        if (!File.Exists(full)) throw new FileNotFoundException("This capture is no longer in the library.", full);
        var files = new[] { full, Path.ChangeExtension(full, ".png") }.Where(File.Exists).ToArray();
        if (files.Any(file => (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Linked files must be managed in File Explorer.");
        string destination = Path.Combine(DeletedFolder, Paths.Unique(""));
        Directory.CreateDirectory(destination);
        var moved = new List<string>();
        try
        {
            foreach (string file in files)
            {
                File.Move(file, Path.Combine(destination, Path.GetFileName(file)));
                moved.Add(file);
            }
        }
        catch
        {
            foreach (string file in moved.AsEnumerable().Reverse())
                File.Move(Path.Combine(destination, Path.GetFileName(file)), file);
            Directory.Delete(destination);
            throw;
        }
        return new(destination, files);
    }

    internal static void Restore(DeletedCapture capture)
    {
        // Preflight every destination so Undo never overwrites a new capture or an export.
        if (capture.OriginalFiles.Any(File.Exists))
            throw new IOException("A file with this capture's name already exists. Open Deleted Captures to recover it without overwriting.");
        if (capture.OriginalFiles.Any(file => !File.Exists(Path.Combine(capture.Directory, Path.GetFileName(file)))))
            throw new IOException("A deleted capture file was moved or removed. Open Deleted Captures to locate it.");
        var restored = new List<string>();
        try
        {
            foreach (string file in capture.OriginalFiles)
            {
                File.Move(Path.Combine(capture.Directory, Path.GetFileName(file)), file);
                restored.Add(file);
            }
        }
        catch
        {
            foreach (string file in restored.AsEnumerable().Reverse())
                File.Move(file, Path.Combine(capture.Directory, Path.GetFileName(file)));
            throw;
        }
        Directory.Delete(capture.Directory);
    }
}
