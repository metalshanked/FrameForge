using SkiaSharp;
namespace FrameForge.Core;
public static class AnnotationGeometry
{
    public static void Resize(Mark mark,Mark original,SKRect destination)
    {
        var source=original.Bounds;
        if(destination.Width<2||destination.Height<2) return;
        double sx=source.Width<.001?1:destination.Width/source.Width,sy=source.Height<.001?1:destination.Height/source.Height;
        double X(double v)=>destination.Left+(v-source.Left)*sx;
        double Y(double v)=>destination.Top+(v-source.Top)*sy;
        mark.X=X(original.X);mark.Y=Y(original.Y);mark.X2=X(original.X2);mark.Y2=Y(original.Y2);
        if(source.Width<.001) {mark.X=destination.Left;mark.X2=destination.Right;}
        if(source.Height<.001) {mark.Y=destination.Top;mark.Y2=destination.Bottom;}
        mark.Points=original.Points.Select(p=>new[]{X(p[0]),Y(p[1])}).ToList();
        if(mark.Kind is Tool.Text or Tool.Callout or Tool.Step)mark.FontSize=Math.Clamp(original.FontSize*Math.Min(sx,sy),8,500);
    }
}
