using System;
using System.Windows;
using System.Windows.Interop;

namespace FrameForge;

internal static class WindowActivation
{
    // Called in direct response to capture completion or an explicit Open Editor command.
    internal static void Show(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        IntPtr handle = new WindowInteropHelper(window).EnsureHandle();
        NativeCapture.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0043); // TOP, NOMOVE, NOSIZE, SHOWWINDOW
        window.Activate();
        if (!NativeCapture.SetForegroundWindow(handle))
        {
            // Raise the editor without leaving it permanently above other applications.
            bool wasTopmost = window.Topmost;
            try { window.Topmost = true; }
            finally { window.Topmost = wasTopmost; }
            window.Activate();
            NativeCapture.SetForegroundWindow(handle);
        }
    }
}
