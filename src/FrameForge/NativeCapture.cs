using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Rect = System.Drawing.Rectangle;

namespace FrameForge;
public static class NativeCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public Rect Rectangle => Rect.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CursorInfo
    {
        public int Size, Flags;
        public IntPtr Cursor;
        public NativePoint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IconInfo
    {
        public bool Icon;
        public int XHotspot, YHotspot;
        public IntPtr Mask, Color;
    }

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")]
    internal static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindow callback, IntPtr param);
    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect rect, int size);
    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    public static IntPtr WindowAt(int x, int y) => GetAncestor(WindowFromPoint(new NativePoint { X = x, Y = y }), 2);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint x, uint y, int data, UIntPtr extra);
    [DllImport("user32.dll")]
    static extern bool GetCursorInfo(ref CursorInfo ci);
    [DllImport("user32.dll")]
    static extern bool GetIconInfo(IntPtr icon, out IconInfo ii);
    [DllImport("user32.dll")]
    static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, uint step, IntPtr brush, uint flags);
    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]
    public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private delegate bool EnumWindow(IntPtr hwnd, IntPtr param);
    public sealed record WindowInfo(IntPtr Handle, string Title, Rect Bounds)
    {
        public override string ToString() => Title;
    }

    public static Rect Desktop => Forms.SystemInformation.VirtualScreen;

    public static List<WindowInfo> Windows(IntPtr except = default)
    {
        var list = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            if (h == except || !IsWindowVisible(h) || IsIconic(h))
                return true;
            var text = new StringBuilder(1024);
            GetWindowText(h, text, text.Capacity);
            if (text.Length == 0)
                return true;
            DwmGetWindowAttribute(h, 14, out int cloaked, 4);
            if (cloaked != 0)
                return true;
            var r = Bounds(h);
            if (r.Width > 10 && r.Height > 10)
                list.Add(new(h, text.ToString(), r));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static Rect Bounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, 9, out NativeRect r, Marshal.SizeOf<NativeRect>()) != 0)
            GetWindowRect(hwnd, out r);
        return r.Rectangle;
    }

    public static BitmapSource Grab(Rect rect, bool cursor = false)
    {
        rect = Rect.Intersect(rect, Desktop);
        if (rect.Width < 1 || rect.Height < 1)
            throw new ArgumentException("The capture area is outside the desktop.");
        using var bmp = new Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rect.Location, System.Drawing.Point.Empty, rect.Size, CopyPixelOperation.SourceCopy);
            if (cursor)
            {
                var ci = new CursorInfo
                {
                    Size = Marshal.SizeOf<CursorInfo>()
                };
                if (GetCursorInfo(ref ci) && ci.Flags == 1 && GetIconInfo(ci.Cursor, out var ii))
                {
                    var dc = g.GetHdc();
                    try
                    {
                        DrawIconEx(dc, ci.Position.X - rect.X - ii.XHotspot, ci.Position.Y - rect.Y - ii.YHotspot, ci.Cursor, 0, 0, 0, IntPtr.Zero, 3);
                    }
                    finally
                    {
                        g.ReleaseHdc(dc);
                        if (ii.Mask != IntPtr.Zero)
                            DeleteObject(ii.Mask);
                        if (ii.Color != IntPtr.Zero)
                            DeleteObject(ii.Color);
                    }
                }
            }
        }

        var handle = bmp.GetHbitmap();
        try
        {
            var result = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return Imaging.Normalize(result);
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    public static void Exclude(Window window)
    {
        SetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, 0x11);
    }
}

public sealed class RegionPicker : IDisposable
{
    private static readonly HashSet<RegionPicker> active = new();
    private readonly List<SelectionWindow> windows = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly TaskCompletionSource<Rect?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly DispatcherTimer timeout;
    private CancellationTokenRegistration cancellation;
    private System.Drawing.Point? start, current;
    private bool started, finished;
    public Rect? Selection { get; private set; }
    internal IReadOnlyList<SelectionWindow> Windows => windows;

    public RegionPicker(TimeSpan? maximumDuration = null)
    {
        timeout = new DispatcherTimer { Interval = maximumDuration ?? TimeSpan.FromMinutes(2) };
        timeout.Tick += (_, _) => Cancel();
    }

    public Task<Rect?> PickAsync(CancellationToken token = default)
    {
        if (started) throw new InvalidOperationException("A capture picker can only be opened once.");
        started = true;
        if (token.IsCancellationRequested) { Cancel(); return completion.Task; }
        active.Add(this);
        try
        {
            // Take all snapshots before showing any overlays. No hidden modal window: ShowDialog
            // would disable the overlays on this UI thread and prevent mouse and Escape input.
            var frames = Forms.Screen.AllScreens.Select(screen => (screen.Bounds, Image: NativeCapture.Grab(screen.Bounds))).ToArray();
            foreach (var frame in frames)
            {
                var win = new SelectionWindow(this, frame.Bounds, frame.Image, windows.Count == 0);
                windows.Add(win);
                win.Show();
            }
            cancellation = token.Register(() => dispatcher.BeginInvoke(Cancel));
            timeout.Start();
            var pointer = Forms.Cursor.Position;
            var target = windows.FirstOrDefault(w => w.ScreenBounds.Contains(pointer)) ?? windows.First();
            target.Activate();
            target.Surface.Focus();
            Keyboard.Focus(target.Surface);
        }
        catch (Exception ex) { Complete(null, ex); }
        return completion.Task;
    }

    internal void Down(System.Drawing.Point p)
    {
        start = current = p;
        Redraw();
    }

    internal void Move(System.Drawing.Point p)
    {
        current = p;
        Redraw();
    }

    internal void Up(System.Drawing.Point p)
    {
        current = p;
        if (start.HasValue)
        {
            var a = start.Value;
            var r = Rect.FromLTRB(Math.Min(a.X, p.X), Math.Min(a.Y, p.Y), Math.Max(a.X, p.X), Math.Max(a.Y, p.Y));
            if (r.Width > 2 && r.Height > 2)
                Selection = r;
        }

        Complete(Selection);
    }

    internal Rect? Current => start.HasValue && current.HasValue ? Rect.FromLTRB(Math.Min(start.Value.X, current.Value.X), Math.Min(start.Value.Y, current.Value.Y), Math.Max(start.Value.X, current.Value.X), Math.Max(start.Value.Y, current.Value.Y)) : null;

    internal void Redraw()
    {
        foreach (var w in windows)
            w.Surface.InvalidateVisual();
    }

    public void Cancel() => Complete(null);

    public static void CancelAll()
    {
        foreach (var picker in active.ToArray()) picker.Cancel();
    }

    internal void WindowClosed(SelectionWindow window)
    {
        windows.Remove(window);
        Cancel();
    }

    internal void CheckActivation()
    {
        dispatcher.BeginInvoke(() => {
            if (!finished && !windows.Any(w => new WindowInteropHelper(w).Handle == NativeCapture.GetForegroundWindow()))
                Cancel();
        }, DispatcherPriority.ContextIdle);
    }

    private void Complete(Rect? selection, Exception? error = null)
    {
        if (finished) return;
        finished = true;
        Selection = selection;
        timeout.Stop();
        cancellation.Dispose();
        active.Remove(this);
        try
        {
            // Hide every surface first so an exception during WPF window teardown cannot cover the desktop.
            foreach (var window in windows.ToArray()) window.Hide();
            foreach (var window in windows.ToArray())
            {
                window.Surface.ReleaseMouseCapture();
                window.Close();
            }
        }
        finally
        {
            windows.Clear();
            if (error != null) completion.TrySetException(error);
            else completion.TrySetResult(selection);
        }
    }

    public void Dispose() => Cancel();
}

internal sealed class SelectionWindow : Window
{
    internal readonly SelectionSurface Surface;
    internal Rect ScreenBounds { get; }
    public SelectionWindow(RegionPicker owner, Rect bounds, BitmapSource image, bool showInTaskbar)
    {
        ScreenBounds = bounds;
        Title = "FrameForge — Select region · Esc to cancel";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = showInTaskbar;
        Background = System.Windows.Media.Brushes.Black;
        Cursor = System.Windows.Input.Cursors.Cross;
        Surface = new SelectionSurface(owner, bounds, image);
        var layout = new Grid();
        layout.Children.Add(Surface);
        var cancel = Ui.Button("Cancel capture · Esc", owner.Cancel);
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        cancel.VerticalAlignment = VerticalAlignment.Top;
        cancel.Margin = new Thickness(24);
        cancel.Cursor = Cursors.Arrow;
        layout.Children.Add(cancel);
        Content = layout;
        SourceInitialized += (_, _) => NativeCapture.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; owner.Cancel(); }
        };
        PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; owner.Cancel(); };
        Deactivated += (_, _) => owner.CheckActivation();
        Closed += (_, _) => owner.WindowClosed(this);
    }
}

internal sealed class SelectionSurface : FrameworkElement
{
    readonly RegionPicker picker;
    readonly Rect screen;
    readonly BitmapSource image;
    public SelectionSurface(RegionPicker p, Rect b, BitmapSource i)
    {
        picker = p;
        screen = b;
        image = i;
        Focusable = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawImage(image, new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(110, 12, 15, 30)), null, new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
        if (picker.Current is Rect selected)
        {
            var clipped = Rect.Intersect(selected, screen);
            if (clipped.Width > 0 && clipped.Height > 0)
            {
                double sx = ActualWidth / screen.Width, sy = ActualHeight / screen.Height;
                var r = new System.Windows.Rect((clipped.X - screen.X) * sx, (clipped.Y - screen.Y) * sy, clipped.Width * sx, clipped.Height * sy);
                dc.PushClip(new RectangleGeometry(r));
                dc.DrawImage(image, new System.Windows.Rect(0, 0, ActualWidth, ActualHeight));
                dc.Pop();
                // Outline the true selection, even when it crosses a monitor boundary.
                var outline = new System.Windows.Rect((selected.X - screen.X) * sx, (selected.Y - screen.Y) * sy, selected.Width * sx, selected.Height * sy);
                SelectionOutline.Draw(dc, outline, selected.Width, selected.Height, new System.Windows.Size(ActualWidth, ActualHeight));
            }
        }

        string label = picker.Current is Rect r2 ? $"{r2.Width} × {r2.Height} px  ·  Release to capture  ·  Esc to cancel" : "Drag to select an area  ·  Esc or right-click to cancel";
        var text = new FormattedText(label, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, System.Windows.Media.Brushes.White, 1);
        dc.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 26, 43)), null, new System.Windows.Rect(24, 24, text.Width + 36, 52), 10, 10);
        dc.DrawText(text, new System.Windows.Point(42, 38));
    }

    protected override void OnMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left)
            return;
        Focus();
        if (!CaptureMouse()) return;
        picker.Down(EventPosition(e));
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (IsMouseCaptured)
            picker.Move(EventPosition(e));
    }

    protected override void OnMouseUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !IsMouseCaptured)
            return;
        ReleaseMouseCapture();
        picker.Up(EventPosition(e));
        e.Handled = true;
    }

    private System.Drawing.Point EventPosition(MouseEventArgs e)
    {
        // Keep each queued input event's position. Polling the system cursor can
        // see the end of a fast drag before WPF has processed its mouse-down.
        var point = PointToScreen(e.GetPosition(this));
        return new System.Drawing.Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }
}

internal static class SelectionOutline
{
    public static void Draw(DrawingContext dc, System.Windows.Rect rectangle, int pixelWidth, int pixelHeight, System.Windows.Size viewport)
    {
        var accent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 210, 255));
        dc.DrawRectangle(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 6), rectangle);
        dc.DrawRectangle(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 4), rectangle);
        dc.DrawRectangle(null, new System.Windows.Media.Pen(accent, 2) { DashStyle = DashStyles.Dash }, rectangle);
        foreach (var corner in new[] { rectangle.TopLeft, rectangle.TopRight, rectangle.BottomLeft, rectangle.BottomRight })
            dc.DrawRectangle(accent, new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 2), new System.Windows.Rect(corner.X - 4, corner.Y - 4, 8, 8));
        var size = new FormattedText($"{pixelWidth:N0} × {pixelHeight:N0} px", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 15, System.Windows.Media.Brushes.White, 1);
        double width = size.Width + 24;
        double x = Math.Clamp(rectangle.Left, 8, Math.Max(8, viewport.Width - width - 8));
        double y = rectangle.Bottom + 12;
        if (y + 36 > viewport.Height) y = Math.Max(8, rectangle.Bottom - 48);
        dc.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(18, 23, 37)), new System.Windows.Media.Pen(accent, 1), new System.Windows.Rect(x, y, width, 36), 6, 6);
        dc.DrawText(size, new System.Windows.Point(x + 12, y + 8));
    }
}
