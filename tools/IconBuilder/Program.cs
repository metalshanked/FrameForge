using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "assets");
Directory.CreateDirectory(output);
var frames = new List<(int Size, byte[] Data)>();
foreach (int size in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
{
    using var bitmap = Draw(size);
    using var stream = new MemoryStream();
    if (size >= 128) bitmap.Save(stream, ImageFormat.Png);
    else
    {
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            using var encoded = new MemoryStream();
            icon.Save(encoded);
            byte[] bytes = encoded.ToArray();
            using var reader = new BinaryReader(new MemoryStream(bytes));
            reader.BaseStream.Position = 14;
            int length = reader.ReadInt32(), offset = reader.ReadInt32();
            stream.Write(bytes, offset, length);
        }
        finally { Native.DestroyIcon(handle); }
    }
    frames.Add((size, stream.ToArray()));
    if (size == 256) bitmap.Save(Path.Combine(output, "FrameForge.png"), ImageFormat.Png);
}
using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "FrameForge.ico"))))
{
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
    int offset = 6 + 16 * frames.Count;
    foreach (var frame in frames)
    {
        writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size)); writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(frame.Data.Length); writer.Write(offset); offset += frame.Data.Length;
    }
    foreach (var frame in frames) writer.Write(frame.Data);
}
Console.WriteLine("Created FrameForge.ico (9 sizes) and FrameForge.png.");

static Bitmap Draw(int size)
{
    var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bitmap);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.ScaleTransform(size / 256f, size / 256f);
    using var path = new GraphicsPath();
    path.AddArc(8, 8, 72, 72, 180, 90); path.AddArc(176, 8, 72, 72, 270, 90);
    path.AddArc(176, 176, 72, 72, 0, 90); path.AddArc(8, 176, 72, 72, 90, 90); path.CloseFigure();
    using var gradient = new LinearGradientBrush(new Point(16, 8), new Point(240, 256), Color.FromArgb(116, 96, 255), Color.FromArgb(72, 52, 201));
    g.FillPath(gradient, path);
    using var white = new SolidBrush(Color.White);
    // Geometric F stays legible at tray size; the cyan corner marks the capture frame.
    g.FillRectangle(white, 77, 57, 29, 147); g.FillRectangle(white, 77, 57, 110, 29); g.FillRectangle(white, 77, 113, 87, 27);
    using var accent = new Pen(Color.FromArgb(78, 236, 233), 12) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    g.DrawLines(accent, new[] { new Point(170, 201), new Point(205, 201), new Point(205, 166) });
    return bitmap;
}
static class Native { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon); }
