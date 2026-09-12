using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;

namespace FrameForge;
public static class ClipboardService
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("user32.dll")]
    static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")]
    static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")]
    static extern bool CloseClipboard();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalFree(IntPtr memory);
    [DllImport("kernel32.dll")]
    static extern UIntPtr GlobalSize(IntPtr memory);
    public static BitmapSource? GetImage(IntPtr owner)
    {
        uint png = RegisterClipboardFormat("PNG");
        bool hasPng = IsClipboardFormatAvailable(png), hasDib = IsClipboardFormatAvailable(8);
        if (!hasPng && !hasDib)
            return System.Windows.Clipboard.ContainsImage() ? System.Windows.Clipboard.GetImage() : null;
        bool opened = false;
        for (int i = 0; i < 12; i++)
        {
            if (OpenClipboard(owner))
            {
                opened = true;
                break;
            }

            Thread.Sleep(40);
        }

        if (!opened)
            throw new InvalidOperationException("The clipboard is busy. Please try Paste again.");
        try
        {
            var bytes = Read(hasPng ? png : 8);
            if (hasPng)
                return Imaging.Load(bytes);
            if (bytes.Length < 40)
                throw new InvalidDataException("Clipboard bitmap is incomplete.");
            int header = BitConverter.ToInt32(bytes, 0), bpp = BitConverter.ToInt16(bytes, 14), compression = BitConverter.ToInt32(bytes, 16), used = BitConverter.ToInt32(bytes, 32);
            long palette = bpp <= 8 ? (used > 0 ? used : 1 << bpp) * 4L : 0;
            long offset = 14L + header + palette + (header == 40 && compression == 3 ? 12 : 0);
            if (header < 40 || offset > bytes.Length + 14)
                throw new InvalidDataException("Unsupported clipboard bitmap.");
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write((ushort)0x4D42);
                writer.Write(bytes.Length + 14);
                writer.Write(0);
                writer.Write((int)offset);
                writer.Write(bytes);
            }

            return Imaging.Load(stream.ToArray());
        }
        finally
        {
            CloseClipboard();
        }
    }

    static byte[] Read(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero)
            throw new InvalidDataException("Clipboard data could not be read.");
        ulong length = GlobalSize(handle).ToUInt64();
        if (length < 1 || length > 400_000_000)
            throw new InvalidDataException("Clipboard image is too large or invalid.");
        var data = GlobalLock(handle);
        if (data == IntPtr.Zero)
            throw new Win32Exception();
        try
        {
            var bytes = new byte[(int)length];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    public static void SetText(string text, IntPtr owner)
    {
        bool opened = false;
        for (int i = 0; i < 12; i++)
        {
            if (OpenClipboard(owner))
            {
                opened = true;
                break;
            }

            Thread.Sleep(40);
        }

        if (!opened)
            throw new InvalidOperationException("Clipboard is busy. Please try again.");
        try
        {
            if (!EmptyClipboard())
                throw new Win32Exception();
            Put(13, System.Text.Encoding.Unicode.GetBytes(text + '\0'));
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void SetImage(BitmapSource source, IntPtr owner)
    {
        var image = Imaging.Normalize(source);
        int stride = image.PixelWidth * 4;
        var pixels = new byte[stride * image.PixelHeight];
        image.CopyPixels(pixels, stride, 0);
        // CF_DIB uses a BITMAPINFOHEADER. PNG is also supplied for applications that preserve alpha.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(40);
            writer.Write(image.PixelWidth);
            writer.Write(-image.PixelHeight);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(0);
            writer.Write(pixels.Length);
            writer.Write(3780);
            writer.Write(3780);
            writer.Write(0);
            writer.Write(0);
            writer.Write(pixels);
        }

        bool opened = false;
        for (int i = 0; i < 12; i++)
        {
            if (OpenClipboard(owner))
            {
                opened = true;
                break;
            }

            Thread.Sleep(40);
        }

        if (!opened)
            throw new InvalidOperationException("Another application is holding the clipboard. Please try Copy image again.");
        try
        {
            if (!EmptyClipboard())
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Put(8, stream.ToArray());
            uint png = RegisterClipboardFormat("PNG");
            if (png != 0)
                Put(png, Imaging.Png(image));
        }
        finally
        {
            CloseClipboard();
        }
    }

    static void Put(uint format, byte[] bytes)
    {
        IntPtr memory = GlobalAlloc(2, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero)
            throw new OutOfMemoryException();
        bool transferred = false;
        try
        {
            var address = GlobalLock(memory);
            if (address == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                Marshal.Copy(bytes, 0, address, bytes.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (SetClipboardData(format, memory) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            transferred = true;
        }
        finally
        {
            if (!transferred)
                GlobalFree(memory);
        }
    }
}
