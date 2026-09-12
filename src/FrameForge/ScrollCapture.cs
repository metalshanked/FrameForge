using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Drawing.Rectangle;

namespace FrameForge;
public static class Stitcher
{
    public sealed record Match(int Shift, double Error, bool Duplicate, bool Confident);
    public static Match FindShift(BitmapSource before, BitmapSource after)
    {
        if (before.PixelWidth != after.PixelWidth || before.PixelHeight != after.PixelHeight)
            throw new ArgumentException("Frames must have the same dimensions.");
        int w = before.PixelWidth, h = before.PixelHeight, stride = w * 4;
        var a = new byte[stride * h];
        var b = new byte[stride * h];
        before.CopyPixels(a, stride, 0);
        after.CopyPixels(b, stride, 0);
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

    public static BitmapSource Append(BitmapSource accumulated, BitmapSource next, int shift)
    {
        if (shift < 1 || shift > next.PixelHeight || accumulated.PixelWidth != next.PixelWidth)
            throw new ArgumentException("Invalid overlap.");
        int height = accumulated.PixelHeight + shift;
        if ((long)height * next.PixelWidth > 80_000_000)
            throw new InvalidOperationException("Scrolling capture reached its 80 megapixel limit. Finish this capture and start another.");
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawImage(accumulated, new Rect(0, 0, accumulated.PixelWidth, accumulated.PixelHeight));
            dc.DrawImage(new CroppedBitmap(next, new Int32Rect(0, next.PixelHeight - shift, next.PixelWidth, shift)), new Rect(0, accumulated.PixelHeight, next.PixelWidth, shift));
        }

        var result = new RenderTargetBitmap(next.PixelWidth, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(v);
        result.Freeze();
        return result;
    }
}

public sealed class ScrollSession : Window
{
    private readonly Rectangle region;
    private readonly IntPtr target;
    private BitmapSource previous, combined;
    private readonly TextBlock status;
    private readonly Button add, auto;
    private readonly CancellationTokenSource cancel = new();
    private bool busy, automatic;
    private int frames = 1;
    public BitmapSource? Result { get; private set; }
    public event Action<int>? FrameAdded;

    public ScrollSession(Rectangle rectangle)
    {
        region = rectangle;
        target = NativeCapture.WindowAt(region.X + region.Width / 2, region.Y + region.Height / 2);
        previous = combined = NativeCapture.Grab(region);
        Title = "FrameForge · Scrolling capture";
        Width = 450;
        Height = 280;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var screen = System.Windows.Forms.Screen.FromRectangle(region);
        var work = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = work.Left / dpi.DpiScaleX + 20;
        Top = work.Top / dpi.DpiScaleY + 20;
        var p = new StackPanel
        {
            Margin = new Thickness(18)
        };
        Content = p;
        p.Children.Add(Ui.Label("Scrolling capture", 19, bold: true));
        p.Children.Add(Ui.Label("Keep this panel outside the capture area. Scroll down with an overlap, then add a frame. Automatic mode scrolls for you."));
        status = Ui.Label("1 frame · ready", 12);
        p.Children.Add(status);
        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        p.Children.Add(row);
        add = Ui.Button("Add after 2s", async () =>
        {
            await Task.Delay(2000);
            await AddFrame();
        });
        auto = Ui.Button("Auto scroll", async () => await Auto());
        row.Children.Add(add);
        row.Children.Add(auto);
        row.Children.Add(Ui.Button("Finish", () =>
        {
            Result = combined;
            Close();
        }, true));
        p.Children.Add(Ui.Label("Esc cancels · " + ShortcutProfile.Load().Bindings[6].Display + " finishes", 11));
        SourceInitialized += (_, _) => NativeCapture.Exclude(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Close();
        };
        Closing += (_, _) => cancel.Cancel();
    }

    public void Finish()
    {
        Result = combined;
        Close();
    }

    public async Task<bool> AddFrame()
    {
        if (busy || cancel.IsCancellationRequested)
            return false;
        busy = true;
        add.IsEnabled = false;
        try
        {
            var next = NativeCapture.Grab(region);
            var match = await Task.Run(() => Stitcher.FindShift(previous, next));
            if (cancel.IsCancellationRequested)
                return false;
            if (match.Duplicate)
            {
                status.Text = "No new content · scroll farther down";
                return false;
            }

            if (!match.Confident)
            {
                status.Text = "Could not align. Scroll back slightly; keep 30–70% overlap.";
                return false;
            }

            combined = Stitcher.Append(combined, next, match.Shift);
            previous = next;
            frames++;
            FrameAdded?.Invoke(match.Shift);
            status.Text = $"{frames} frames · {combined.PixelWidth} × {combined.PixelHeight} px";
            return true;
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            return false;
        }
        finally
        {
            busy = false;
            add.IsEnabled = true;
        }
    }

    private async Task Auto()
    {
        if (automatic)
        {
            automatic = false;
            return;
        }

        automatic = true;
        auto.Content = "Stop scrolling";
        status.Text = "Starting in 3 seconds. Keep the content unobstructed.";
        try
        {
            await Task.Delay(3000, cancel.Token);
            int misses = 0;
            while (automatic && !cancel.IsCancellationRequested && frames < 50)
            {
                NativeCapture.SetForegroundWindow(target);
                NativeCapture.SetCursorPos(region.X + region.Width / 2, region.Y + region.Height / 2);
                NativeCapture.mouse_event(0x0800, 0, 0, -240, UIntPtr.Zero);
                await Task.Delay(900, cancel.Token);
                bool added = await AddFrame();
                misses = added ? 0 : misses + 1;
                if (misses >= 2)
                {
                    automatic = false;
                    status.Text += " · automatic capture stopped";
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            automatic = false;
            auto.Content = "Auto scroll";
        }
    }
}
