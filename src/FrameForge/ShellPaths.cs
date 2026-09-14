using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FrameForge;

internal static class ShellPaths
{
    // A packaged launcher can virtualize LocalAppData for its child processes.
    // Explorer runs outside that context, so give it the physical path of the open handle.
    public static string ResolveExistingPath(string path)
    {
        using var handle = CreateFile(Path.GetFullPath(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(512);
        while (true)
        {
            uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length < buffer.Capacity) break;
            buffer.EnsureCapacity(checked((int)length + 1));
        }
        string resolved = buffer.ToString();
        // Explorer expects a normal drive or UNC path, not an extended-length prefix.
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + resolved[8..];
        if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) return resolved[4..];
        return resolved;
    }

    public static string OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        string resolved = ResolveExistingPath(path);
        // Ask the Windows shell to open the directory itself. Starting explorer.exe
        // only confirms that a launcher process started, not that the folder was opened.
        var start = new ProcessStartInfo(resolved)
        {
            UseShellExecute = true,
            Verb = "open",
            WindowStyle = ProcessWindowStyle.Normal
        };
        using var process = Process.Start(start);
        return resolved;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
}
