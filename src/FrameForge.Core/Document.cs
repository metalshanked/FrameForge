using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace FrameForge.Core;

// Numeric values preserve the version-1 .ffg format used by the Windows edition.
public enum Tool { Select, Arrow, Rectangle, Ellipse, Line, Pen, Text, Callout, Highlight, Step, Blur, Pixelate, Redact, Crop }
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
    [JsonIgnore] public SKRect Bounds => new((float)Math.Min(X,X2),(float)Math.Min(Y,Y2),(float)Math.Max(X,X2),(float)Math.Max(Y,Y2));
    public Mark Clone() => new() { Kind=Kind,X=X,Y=Y,X2=X2,Y2=Y2,Color=Color,Width=Width,FontSize=FontSize,Filled=Filled,Text=Text,Points=Points.Select(p=>p.ToArray()).ToList() };
    public void Move(double x,double y) { X+=x;Y+=y;X2+=x;Y2+=y; foreach(var p in Points){p[0]+=x;p[1]+=y;} }
}
public sealed class ProjectFile
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "Capture";
    public string Image { get; set; } = "";
    public List<Mark> Marks { get; set; } = new();
}
public sealed class CaptureDocument : IDisposable
{
    SKBitmap original;
    readonly List<(byte[] Image,List<Mark> Marks)> undo = new(), redo = new();
    public SKBitmap Image => original;
    public List<Mark> Marks { get; private set; } = new();
    public string Title { get; set; } = "Capture";
    public bool Dirty { get; private set; }
    public CaptureDocument(SKBitmap image) { original=image.Copy(); }
    public bool CanUndo => undo.Count>0;
    public bool CanRedo => redo.Count>0;
    public void Checkpoint()
    {
        undo.Add((Imaging.Png(original),Marks.Select(m=>m.Clone()).ToList()));
        while(undo.Count>20 || (undo.Count>1 && undo.Sum(s=>(long)s.Image.Length)>200_000_000)) undo.RemoveAt(0);
        redo.Clear(); Dirty=true;
    }
    void Restore(List<(byte[] Image,List<Mark> Marks)> from,List<(byte[] Image,List<Mark> Marks)> to)
    {
        if(from.Count==0)return;
        to.Add((Imaging.Png(original),Marks.Select(m=>m.Clone()).ToList()));
        var state=from[^1];from.RemoveAt(from.Count-1);
        original.Dispose();original=Imaging.Load(state.Image);Marks=state.Marks;Dirty=true;
    }
    public void Undo()=>Restore(undo,redo);
    public void Redo()=>Restore(redo,undo);
    public SKBitmap Render(Mark? draft=null)
    {
        var result=original.Copy();
        using(var c=new SKCanvas(result)) foreach(var m in Marks) MarkRenderer.Draw(c,result,m);
        if(draft!=null) using(var c=new SKCanvas(result)) MarkRenderer.Draw(c,result,draft);
        return result;
    }
    public void Replace(SKBitmap bitmap)
    {
        Checkpoint();original.Dispose();original=bitmap.Copy();Marks.Clear();
    }
    public void Crop(SKRect rect) { using var rendered=Render();using var crop=Imaging.Crop(rendered,rect);Replace(crop); }
    public void Resize(int width,int height) { using var rendered=Render();using var resized=Imaging.Resize(rendered,width,height);Replace(resized); }
    public void Rotate()
    {
        using var source=Render(); using var result=new SKBitmap(source.Height,source.Width);
        using(var c=new SKCanvas(result)){c.Translate(result.Width,0);c.RotateDegrees(90);c.DrawBitmap(source,0,0);}
        Replace(result);
    }
    public void Save(string path)
    {
        var data=new ProjectFile { Title=Title,Image=Convert.ToBase64String(Imaging.Png(original)),Marks=Marks };
        AtomicFile.Write(path,JsonSerializer.SerializeToUtf8Bytes(data));Dirty=false;
    }
    public static CaptureDocument Load(string path)
    {
        if(new FileInfo(path).Length>300_000_000)throw new InvalidDataException("Project exceeds 300 MB.");
        var data=JsonSerializer.Deserialize<ProjectFile>(File.ReadAllBytes(path))??throw new InvalidDataException("Invalid project.");
        if(data.Version!=1)throw new InvalidDataException("This project requires a newer FrameForge version.");
        if(data.Marks==null||data.Marks.Count>10000)throw new InvalidDataException("Too many annotations.");
        foreach(var m in data.Marks)
        {
            if(m==null || !Enum.IsDefined(m.Kind)||!new[]{m.X,m.Y,m.X2,m.Y2,m.Width,m.FontSize}.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1_000_000)
              ||m.Width<0||m.Width>300||m.FontSize<1||m.FontSize>1000||m.Points==null||m.Points.Count>100000
              ||m.Points.Any(p=>p==null||p.Length!=2||!p.All(v=>double.IsFinite(v)&&Math.Abs(v)<=1_000_000))
              ||!SKColor.TryParse(m.Color,out _)||m.Text==null||m.Text.Length>100000)
                throw new InvalidDataException("Invalid annotation data.");
        }
        using var bitmap=Imaging.Load(Convert.FromBase64String(data.Image));
        return new CaptureDocument(bitmap){Title=data.Title,Marks=data.Marks};
    }
    public void Dispose(){original.Dispose();undo.Clear();redo.Clear();}
}
public static class AtomicFile
{
    public static void Write(string path,byte[] bytes)
    {
        path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
public static class Imaging
{
    public const long MaxPixels=100_000_000;
    public static void ValidateSize(int width,int height)
    {if(width<1||height<1||(long)width*height>MaxPixels)throw new InvalidDataException("Images must be between 1 pixel and 100 megapixels.");}
    public static SKBitmap Load(byte[] bytes)
    {
        using var codec=SKCodec.Create(new SKMemoryStream(bytes))??throw new InvalidDataException("Unsupported image.");
        ValidateSize(codec.Info.Width,codec.Info.Height);
        var b=new SKBitmap(new SKImageInfo(codec.Info.Width,codec.Info.Height,SKColorType.Bgra8888,SKAlphaType.Premul));
        if(codec.GetPixels(b.Info,b.GetPixels())!=SKCodecResult.Success){b.Dispose();throw new InvalidDataException("Incomplete image.");}
        return b;
    }
    public static SKBitmap Load(string path)
    {if(new FileInfo(path).Length>300_000_000)throw new InvalidDataException("Image file exceeds 300 MB.");return Load(File.ReadAllBytes(path));}
    public static byte[] Png(SKBitmap image){using var i=SKImage.FromBitmap(image);using var d=i.Encode(SKEncodedImageFormat.Png,100);return d.ToArray();}
    public static void Export(SKBitmap image,string path)
    {
        var extension=Path.GetExtension(path).ToLowerInvariant();
        var format=extension switch{".png"=>SKEncodedImageFormat.Png,".jpg" or ".jpeg"=>SKEncodedImageFormat.Jpeg,".webp"=>SKEncodedImageFormat.Webp,_=>throw new InvalidDataException("Choose PNG, JPEG, or WebP.")};
        using var flattened=new SKBitmap(image.Width,image.Height);
        using(var c=new SKCanvas(flattened)){c.Clear(format==SKEncodedImageFormat.Jpeg?SKColors.White:SKColors.Transparent);c.DrawBitmap(image,0,0);}
        using var i=SKImage.FromBitmap(flattened);using var data=i.Encode(format,95);AtomicFile.Write(path,data.ToArray());
    }
    public static SKRectI Clamp(SKRect r,int w,int h)=>new(
        (int)Math.Clamp(Math.Floor(r.Left),0,w),(int)Math.Clamp(Math.Floor(r.Top),0,h),
        (int)Math.Clamp(Math.Ceiling(r.Right),0,w),(int)Math.Clamp(Math.Ceiling(r.Bottom),0,h));
    public static SKBitmap Crop(SKBitmap image,SKRect rect)
    {
        var r=Clamp(rect,image.Width,image.Height);ValidateSize(r.Width,r.Height);
        var b=new SKBitmap(r.Width,r.Height);using(var c=new SKCanvas(b))c.DrawBitmap(image,r,new SKRect(0,0,r.Width,r.Height));return b;
    }
    public static SKBitmap Resize(SKBitmap image,int width,int height)
    {
        ValidateSize(width,height);var b=new SKBitmap(width,height);
        using(var c=new SKCanvas(b))using(var i=SKImage.FromBitmap(image))c.DrawImage(i,new SKRect(0,0,width,height),new SKSamplingOptions(SKFilterMode.Linear));
        return b;
    }
}
public static class MarkRenderer
{
    public static void Draw(SKCanvas c,SKBitmap below,Mark m)
    {
        var r=m.Bounds;var color=SKColor.Parse(m.Color);
        using var paint=new SKPaint{Color=color,IsAntialias=true,Style=m.Filled?SKPaintStyle.Fill:SKPaintStyle.Stroke,StrokeWidth=(float)m.Width,StrokeCap=SKStrokeCap.Round,StrokeJoin=SKStrokeJoin.Round};
        var a=new SKPoint((float)m.X,(float)m.Y);var b=new SKPoint((float)m.X2,(float)m.Y2);
        switch(m.Kind)
        {
            case Tool.Rectangle:c.DrawRect(r,paint);break;
            case Tool.Ellipse:c.DrawOval(r,paint);break;
            case Tool.Line:case Tool.Arrow:
                paint.Style=SKPaintStyle.Stroke;c.DrawLine(a,b,paint);
                if(m.Kind==Tool.Arrow){var angle=Math.Atan2(b.Y-a.Y,b.X-a.X);var size=Math.Max(16,m.Width*4);
                    c.DrawLine(b,new SKPoint(b.X-(float)(size*Math.Cos(angle-.48)),b.Y-(float)(size*Math.Sin(angle-.48))),paint);
                    c.DrawLine(b,new SKPoint(b.X-(float)(size*Math.Cos(angle+.48)),b.Y-(float)(size*Math.Sin(angle+.48))),paint);}break;
            case Tool.Pen:
                if(m.Points.Count==0)break;paint.Style=SKPaintStyle.Stroke;using(var path=new SKPath()){
                    path.MoveTo((float)m.Points[0][0],(float)m.Points[0][1]);foreach(var p in m.Points.Skip(1))path.LineTo((float)p[0],(float)p[1]);c.DrawPath(path,paint);}break;
            case Tool.Highlight:paint.Style=SKPaintStyle.Fill;paint.Color=color.WithAlpha(80);c.DrawRect(r,paint);break;
            case Tool.Redact:paint.Style=SKPaintStyle.Fill;paint.Color=SKColors.Black;paint.IsAntialias=false;c.DrawRect(Imaging.Clamp(r,below.Width,below.Height),paint);break;
            case Tool.Blur:case Tool.Pixelate:
                var area=Imaging.Clamp(r,below.Width,below.Height);if(area.Width<1||area.Height<1)break;
                // Read the already-composited pixels, so filters include preceding annotations.
                using(var crop=Imaging.Crop(below,area)){
                    using var tiny=Imaging.Resize(crop,Math.Max(1,area.Width/14),Math.Max(1,area.Height/14));
                    using var img=SKImage.FromBitmap(tiny);
                    c.DrawImage(img,area,new SKSamplingOptions(m.Kind==Tool.Blur?SKFilterMode.Linear:SKFilterMode.Nearest));}
                break;
            case Tool.Text:case Tool.Callout:case Tool.Step:
                paint.Style=SKPaintStyle.Fill;
                if(m.Kind==Tool.Callout){c.DrawRoundRect(r,8,8,paint);using var tail=new SKPath();tail.MoveTo(r.Left+15,r.Bottom);tail.LineTo(r.Left+8,r.Bottom+18);tail.LineTo(r.Left+38,r.Bottom);tail.Close();c.DrawPath(tail,paint);paint.Color=SKColors.White;}
                if(m.Kind==Tool.Step){c.DrawOval(r,paint);paint.Color=SKColors.White;}
                using(var typeface=SKTypeface.FromFamilyName("Arial"))using(var font=new SKFont(typeface,(float)m.FontSize))
                {
                    float x=m.Kind==Tool.Step?r.MidX:r.Left+(m.Kind==Tool.Callout?10:0);
                    float y=m.Kind==Tool.Step?r.MidY+(font.Size*.35f):r.Top+font.Size+(m.Kind==Tool.Callout?5:0);
                    foreach(var line in m.Text.Split('\n')){c.DrawText(line,x,y,m.Kind==Tool.Step?SKTextAlign.Center:SKTextAlign.Left,font,paint);y+=font.Size*1.25f;}
                }break;
        }
    }
}
