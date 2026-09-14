using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FrameForge.Core;
using SkiaSharp;
namespace FrameForge.Desktop;
public sealed class RegionPicker : Window
{
    readonly TaskCompletionSource<SKRect?> result=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Bitmap screenshot;
    readonly Selector selector;
    readonly DispatcherTimer timeout;
    bool active;
    RegionPicker(SKBitmap image,PixelRect screen,double scale)
    {
        Title="FrameForge — Select a region"; WindowDecorations=Avalonia.Controls.WindowDecorations.None;ShowInTaskbar=true;Topmost=true;CanResize=false;
        Position=screen.Position;Width=screen.Width/scale;Height=screen.Height/scale;WindowStartupLocation=WindowStartupLocation.Manual;
        screenshot=new Bitmap(new MemoryStream(Imaging.Png(image)));
        var grid=new Grid();grid.Children.Add(new Image{Source=screenshot,Stretch=Stretch.Fill});
        selector=new Selector(image.Width,image.Height,r=>{result.TrySetResult(r);Close();});grid.Children.Add(selector);
        var cancel=new Button{Content="Cancel · Escape",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Top,Margin=new Thickness(16),Padding=new Thickness(16,8)};
        cancel.Click+=(_,_)=>Cancel();grid.Children.Add(cancel);Content=grid;
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Cancel();}};
        Opened+=(_,_)=>{active=true;Activate();selector.Focus();};
        Deactivated+=(_,_)=>{if(active)Cancel();};
        Closed+=(_,_)=>{result.TrySetResult(null);screenshot.Dispose();};
        timeout=new DispatcherTimer{Interval=TimeSpan.FromMinutes(2)};timeout.Tick+=(_,_)=>Cancel();timeout.Start();
    }
    void Cancel(){result.TrySetResult(null);Close();}
    public static async Task<SKRect?> Pick(SKBitmap image,PixelRect screen,double scale,CancellationToken cancel)
    {
        var picker=new RegionPicker(image,screen,scale);
        using var registration=cancel.Register(()=>Dispatcher.UIThread.Post(picker.Cancel));
        try{cancel.ThrowIfCancellationRequested();picker.Show();CaptureTrace.Write("region window shown");return await picker.result.Task;}
        finally{picker.timeout.Stop();picker.active=false;picker.Close();}
    }
    sealed class Selector : Control
    {
        readonly int width,height;readonly Action<SKRect?> complete;Point start,current;bool selecting;
        public Selector(int w,int h,Action<SKRect?> callback){width=w;height=h;complete=callback;Focusable=true;Cursor=new Cursor(StandardCursorType.Cross);}
        Rect Area=>new(Math.Min(start.X,current.X),Math.Min(start.Y,current.Y),Math.Abs(current.X-start.X),Math.Abs(current.Y-start.Y));
        public override void Render(DrawingContext c)
        {
            var r=Area;var shade=new SolidColorBrush(Color.FromArgb(90,0,0,0));
            if(!selecting){c.DrawRectangle(shade,null,Bounds.WithX(0).WithY(0));return;}
            c.DrawRectangle(shade,null,new Rect(0,0,Bounds.Width,r.Top));c.DrawRectangle(shade,null,new Rect(0,r.Bottom,Bounds.Width,Math.Max(0,Bounds.Height-r.Bottom)));
            c.DrawRectangle(shade,null,new Rect(0,r.Top,r.Left,r.Height));c.DrawRectangle(shade,null,new Rect(r.Right,r.Top,Math.Max(0,Bounds.Width-r.Right),r.Height));
            c.DrawRectangle(null,new Pen(Brushes.White,3),r);c.DrawRectangle(null,new Pen(Brushes.BlueViolet,1),r);
            var text=new FormattedText($"{Math.Round(r.Width*width/Bounds.Width)} × {Math.Round(r.Height*height/Bounds.Height)} px",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Typeface.Default,14,Brushes.White);
            var x=Math.Clamp(r.Left,0,Math.Max(0,Bounds.Width-text.Width-20));var y=Math.Max(48,r.Top-32);
            c.DrawRectangle(Brushes.Black,null,new Rect(x,y,text.Width+16,26));c.DrawText(text,new Point(x+8,y+3));
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {if(e.GetCurrentPoint(this).Properties.IsRightButtonPressed){complete(null);return;}if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;start=current=e.GetPosition(this);selecting=true;e.Pointer.Capture(this);InvalidateVisual();}
        protected override void OnPointerMoved(PointerEventArgs e){if(!selecting)return;var p=e.GetPosition(this);current=new(Math.Clamp(p.X,0,Bounds.Width),Math.Clamp(p.Y,0,Bounds.Height));InvalidateVisual();}
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {if(!selecting)return;var r=Area;selecting=false;e.Pointer.Capture(null);complete(r.Width<2||r.Height<2?null:new SKRect((float)(r.Left*width/Bounds.Width),(float)(r.Top*height/Bounds.Height),(float)(r.Right*width/Bounds.Width),(float)(r.Bottom*height/Bounds.Height)));}
    }
}
