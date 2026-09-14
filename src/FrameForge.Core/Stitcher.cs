using SkiaSharp;
using System.Runtime.InteropServices;
namespace FrameForge.Core;
public static class Stitcher
{
    public sealed record Match(int Shift, double Error, bool Duplicate, bool Confident);
    public static Match FindShift(SKBitmap before, SKBitmap after)
    {
        if (before.Width != after.Width || before.Height != after.Height)
            throw new ArgumentException("Frames must have the same dimensions.");
        if (before.Height < 20 || before.Width < 20) throw new ArgumentException("Scrolling frames must be at least 20 by 20 pixels.");
        int w = before.Width, h = before.Height, stride = w * 4;
        var a = new byte[stride * h];
        var b = new byte[stride * h];
        using var normalizedA = before.Copy(SKColorType.Bgra8888); Marshal.Copy(normalizedA.GetPixels(), a, 0, a.Length);
        using var normalizedB = after.Copy(SKColorType.Bgra8888); Marshal.Copy(normalizedB.GetPixels(), b, 0, b.Length);
        double Error(int shift, bool detailed)
        {
            double sum = 0;
            long weights = 0;
            int top = 6, bottom = h - shift - 6;
            int xstep = detailed ? 2 : Math.Max(4, w / 100);
            int ystep = detailed ? 1 : Math.Max(3, h / 180);
            for (int y = top; y < bottom; y += ystep)
                for (int x = Math.Max(2, w / 30) + (detailed ? 0 : (y * 17) % xstep); x < w - w / 30; x += xstep)
                {
                    int p = (y + shift) * stride + x * 4, q = y * stride + x * 4;
                    // Dense edge weighting keeps small identifiers significant on pages with repeated rows.
                    int contrast = Math.Abs(a[p] - a[p - 4]) + Math.Abs(b[q] - b[q - 4]);
                    int weight = detailed && contrast > 24 ? 8 : 1;
                    for (int c = 0; c < 3; c++)
                    {
                        sum += Math.Abs(a[p + c] - b[q + c]) * weight;
                        weights += weight;
                    }
                }
            return weights > 0 ? sum / weights : 255;
        }

        double zero = Error(0, true);
        if (zero < 0.05)
            return new(0, zero, true, true);
        var candidates = Enumerable.Range(1, Math.Max(1, (int)(h * .85)))
            .Select(shift => (Shift: shift, Error: Error(shift, false)))
            .OrderBy(item => item.Error).Take(16)
            .Select(item => (item.Shift, Error: Error(item.Shift, true)))
            .OrderBy(item => item.Error).ThenBy(item => item.Shift).ToArray();
        var best = candidates[0];
        bool ambiguous = candidates.Skip(1).Any(other => Math.Abs(other.Shift - best.Shift) > 3 && other.Error <= best.Error * 1.12 + .015);
        return new(best.Shift, best.Error, false, !ambiguous && best.Error < 10 && best.Error < zero * .65);
    }


    public static SKBitmap Append(SKBitmap accumulated,SKBitmap next,int shift)
    {
        if(shift<1||shift>next.Height||accumulated.Width!=next.Width)throw new ArgumentException("Invalid overlap.");
        var height=accumulated.Height+shift;
        if((long)height*next.Width>80_000_000)throw new InvalidOperationException("Scrolling capture reached 80 megapixels.");
        var output=new SKBitmap(next.Width,height);
        using(var c=new SKCanvas(output))
        {c.DrawBitmap(accumulated,0,0);c.DrawBitmap(next,new SKRect(0,next.Height-shift,next.Width,next.Height),new SKRect(0,accumulated.Height,next.Width,height));}
        return output;
    }
}
