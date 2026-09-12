using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FrameForge;
public enum Tool
{
    Select,
    Arrow,
    Rectangle,
    Ellipse,
    Line,
    Pen,
    Text,
    Callout,
    Highlight,
    Step,
    Blur,
    Pixelate,
    Redact,
    Crop
}

public sealed class Mark
{
    public Tool Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string Color { get; set; } = "#FF6757EF";
    public double Width { get; set; } = 4;
    public double FontSize { get; set; } = 26;
    public bool Filled { get; set; }
    public string Text { get; set; } = "";
    public List<double[]> Points { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public Rect Bounds => new(Math.Min(X, X2), Math.Min(Y, Y2), Math.Max(1, Math.Abs(X2 - X)), Math.Max(1, Math.Abs(Y2 - Y)));

    public Mark Clone() => new()
    {
        Kind = Kind,
        X = X,
        Y = Y,
        X2 = X2,
        Y2 = Y2,
        Color = Color,
        Width = Width,
        FontSize = FontSize,
        Filled = Filled,
        Text = Text,
        Points = Points.Select(p => p.ToArray()).ToList()
    };
    public void Move(double x, double y)
    {
        X += x;
        X2 += x;
        Y += y;
        Y2 += y;
        foreach (var p in Points)
        {
            p[0] += x;
            p[1] += y;
        }
    }
}

public sealed class ProjectFile
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "Untitled";
    public string Image { get; set; } = "";
    public List<Mark> Marks { get; set; } = new();
}

public sealed class CaptureDocument
{
    public BitmapSource Image { get; private set; }
    public List<Mark> Marks { get; private set; } = new();
    public string Title { get; set; } = "Untitled capture";
    public string? ProjectPath { get; set; }
    public bool Dirty { get; set; }

    private sealed record State(BitmapSource Image, List<Mark> Marks);
    private readonly Stack<State> undo = new(), redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public CaptureDocument(BitmapSource image)
    {
        Image = Imaging.Normalize(image);
    }

    private State Snapshot() => new(Image, Marks.Select(m => m.Clone()).ToList());
    public void Checkpoint()
    {
        undo.Push(Snapshot());
        if (undo.Count > 40)
        {
            var keep = undo.Take(40).Reverse().ToArray();
            undo.Clear();
            foreach (var item in keep)
                undo.Push(item);
        }

        redo.Clear();
        Dirty = true;
    }

    public void Undo()
    {
        if (undo.Count == 0)
            return;
        redo.Push(Snapshot());
        Restore(undo.Pop());
    }

    public void Redo()
    {
        if (redo.Count == 0)
            return;
        undo.Push(Snapshot());
        Restore(redo.Pop());
    }

    private void Restore(State s)
    {
        Image = s.Image;
        Marks = s.Marks;
        Dirty = true;
    }

    public BitmapSource Render()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawDrawing(Drawing());
        var bitmap = new RenderTargetBitmap(Image.PixelWidth, Image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public DrawingGroup Drawing()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
            dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
        foreach (var mark in Marks)
        {
            BitmapSource below = Image;
            if (mark.Kind is Tool.Blur or Tool.Pixelate)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawDrawing(group);
                var raster = new RenderTargetBitmap(Image.PixelWidth, Image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                raster.Render(visual);
                raster.Freeze();
                below = raster;
            }

            using (var dc = group.Append())
                MarkRenderer.Draw(dc, mark, below);
        }

        return group;
    }

    public void Replace(BitmapSource image)
    {
        Checkpoint();
        Image = Imaging.Normalize(image);
        Marks.Clear();
    }

    public void Crop(Rect rect)
    {
        var area = Imaging.Clamp(rect, Image.PixelWidth, Image.PixelHeight);
        if (area.Width < 2 || area.Height < 2)
            return;
        Replace(new CroppedBitmap(Render(), area));
    }

    public void SaveProject(string path)
    {
        var data = new ProjectFile
        {
            Title = Title,
            Image = Convert.ToBase64String(Imaging.Png(Image)),
            Marks = Marks
        };
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data));
        File.Move(temp, path, true);
        ProjectPath = path;
        Dirty = false;
    }

    public static CaptureDocument LoadProject(string path)
    {
        if (new FileInfo(path).Length > 300_000_000)
            throw new InvalidDataException("Project exceeds the 300 MB safety limit.");
        var data = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid project.");
        if (data.Version != 1)
            throw new InvalidDataException("This project needs a newer FrameForge version.");
        var doc = new CaptureDocument(Imaging.Load(Convert.FromBase64String(data.Image)))
        {
            Title = data.Title,
            ProjectPath = path
        };
        if (data.Marks.Count > 10000)
            throw new InvalidDataException("Too many annotations.");
        foreach (var m in data.Marks)
        {
            if (!Enum.IsDefined(m.Kind) || !new[]
            {
                m.X,
                m.Y,
                m.X2,
                m.Y2,
                m.Width,
                m.FontSize
            }.All(double.IsFinite) || m.Width < 0 || m.Width > 300 || m.FontSize < 1 || m.FontSize > 1000 || m.Points.Count > 100000 || m.Points.Any(p => p.Length != 2 || !p.All(double.IsFinite)))
                throw new InvalidDataException("Invalid annotation data.");
            _ = (Color)ColorConverter.ConvertFromString(m.Color);
        }

        doc.Marks = data.Marks;
        return doc;
    }
}

public static class Imaging
{
    public static BitmapSource Normalize(BitmapSource src)
    {
        var converted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(src.PixelWidth, src.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    public static BitmapSource Load(string path) => Load(File.ReadAllBytes(path));
    public static BitmapSource Load(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var b = decoder.Frames[0];
        if ((long)b.PixelWidth * b.PixelHeight > 100_000_000)
            throw new InvalidDataException("Image exceeds 100 megapixels.");
        return Normalize(b);
    }

    public static byte[] Png(BitmapSource image)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static void Save(BitmapSource image, string path)
    {
        BitmapEncoder enc = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder
            {
                QualityLevel = 95
            },
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()};
        enc.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        enc.Save(stream);
    }

    public static Int32Rect Clamp(Rect rect, int width, int height)
    {
        int x = Math.Clamp((int)Math.Floor(rect.X), 0, width), y = Math.Clamp((int)Math.Floor(rect.Y), 0, height);
        return new(x, y, Math.Max(0, Math.Min(width, (int)Math.Ceiling(rect.Right)) - x), Math.Max(0, Math.Min(height, (int)Math.Ceiling(rect.Bottom)) - y));
    }

    public static BitmapSource Resize(BitmapSource image, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 100_000_000)
            throw new ArgumentException("Size must be between 1 pixel and 100 megapixels.");
        var v = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(v, BitmapScalingMode.HighQuality);
        using (var dc = v.RenderOpen())
            dc.DrawImage(image, new Rect(0, 0, width, height));
        var b = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        b.Render(v);
        b.Freeze();
        return b;
    }

    public static BitmapSource Pixelate(BitmapSource source, Rect rect, int size, bool smooth)
    {
        var r = Clamp(rect, source.PixelWidth, source.PixelHeight);
        if (r.Width < 1 || r.Height < 1)
            return source;
        var crop = new CroppedBitmap(source, r);
        int w = Math.Max(1, r.Width / size), h = Math.Max(1, r.Height / size);
        var tiny = Resize(crop, w, h);
        var v = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(v, smooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        using (var dc = v.RenderOpen())
            dc.DrawImage(tiny, new Rect(0, 0, r.Width, r.Height));
        var b = new RenderTargetBitmap(r.Width, r.Height, 96, 96, PixelFormats.Pbgra32);
        b.Render(v);
        b.Freeze();
        return b;
    }
}

public static class MarkRenderer
{
    public static void Draw(DrawingContext dc, Mark m, BitmapSource image)
    {
        var color = (Color)ColorConverter.ConvertFromString(m.Color);
        var brush = new SolidColorBrush(color);
        var pen = new System.Windows.Media.Pen(brush, m.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        var a = new Point(m.X, m.Y);
        var b = new Point(m.X2, m.Y2);
        var r = m.Bounds;
        switch (m.Kind)
        {
            case Tool.Arrow:
            case Tool.Line:
                dc.DrawLine(pen, a, b);
                if (m.Kind == Tool.Arrow)
                {
                    var d = a - b;
                    if (d.Length < 1)
                        break;
                    d.Normalize();
                    var n = new Vector(-d.Y, d.X);
                    double len = Math.Max(14, m.Width * 4);
                    var geo = new StreamGeometry();
                    using (var c = geo.Open())
                    {
                        c.BeginFigure(b, true, true);
                        c.LineTo(b + d * len + n * len * .43, true, false);
                        c.LineTo(b + d * len - n * len * .43, true, false);
                    }

                    dc.DrawGeometry(brush, null, geo);
                }

                break;
            case Tool.Rectangle:
                dc.DrawRoundedRectangle(m.Filled ? brush : null, pen, r, 3, 3);
                break;
            case Tool.Ellipse:
                dc.DrawEllipse(m.Filled ? brush : null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                break;
            case Tool.Highlight:
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(85, color.R, color.G, color.B)), null, r);
                break;
            case Tool.Redact:
                dc.DrawRectangle(Brushes.Black, null, r);
                break;
            case Tool.Blur:
            case Tool.Pixelate:
                var rr = Imaging.Clamp(r, image.PixelWidth, image.PixelHeight);
                if (rr.Width > 0 && rr.Height > 0)
                    dc.DrawImage(Imaging.Pixelate(image, r, m.Kind == Tool.Blur ? 24 : 12, m.Kind == Tool.Blur), new Rect(rr.X, rr.Y, rr.Width, rr.Height));
                break;
            case Tool.Pen:
                if (m.Points.Count > 1)
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(m.Points[0][0], m.Points[0][1]), false, false);
                        c.PolyLineTo(m.Points.Skip(1).Select(p => new Point(p[0], p[1])).ToArray(), true, false);
                    }

                    dc.DrawGeometry(null, pen, g);
                }

                break;
            case Tool.Step:
                dc.DrawEllipse(brush, new System.Windows.Media.Pen(Brushes.White, 2), new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                var num = Text(m.Text, m.FontSize, Brushes.White);
                dc.DrawText(num, new Point(r.X + (r.Width - num.Width) / 2, r.Y + (r.Height - num.Height) / 2));
                break;
            case Tool.Text:
            case Tool.Callout:
                if (m.Kind == Tool.Callout)
                {
                    dc.DrawRoundedRectangle(brush, null, r, 8, 8);
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(r.X + 16, r.Bottom - 1), true, true);
                        c.LineTo(new Point(r.X + 16, r.Bottom + 18), true, false);
                        c.LineTo(new Point(r.X + 38, r.Bottom - 1), true, false);
                    }

                    dc.DrawGeometry(brush, null, g);
                }

                var text = Text(m.Text, m.FontSize, m.Kind == Tool.Callout ? Brushes.White : brush);
                text.MaxTextWidth = Math.Max(1, r.Width - 16);
                text.MaxTextHeight = Math.Max(1, r.Height - 8);
                text.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(text, new Point(r.X + 8, r.Y + 4));
                break;
        }
    }

    private static FormattedText Text(string value, double size, Brush color) => new(value, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, 1);
}
