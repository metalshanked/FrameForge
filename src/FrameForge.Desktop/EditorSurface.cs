using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FrameForge.Core;
using SkiaSharp;
namespace FrameForge.Desktop;
public sealed class EditorSurface : Control
{
    public CaptureDocument? Document { get; private set; }
    public Tool Tool { get; set; } = Tool.Arrow;
    public string Ink { get; set; } = "#FF6757EF";
    public double Stroke { get; set; } = 4;
    public double FontSize { get; set; } = 26;
    public string TextValue { get; set; } = "Add your note";
    public bool Filled { get; set; }
    public event Action? Changed;
    Bitmap? preview;
    Mark? draft,selected;
    Point start,last;
    bool dragging,moving,checkpointed;
    public EditorSurface(){Focusable=true;ClipToBounds=true;Cursor=new Cursor(StandardCursorType.Cross);}
    public void SetDocument(CaptureDocument doc){Document=doc;selected=draft=null;Refresh();}
    public void Refresh()
    {
        preview?.Dispose();preview=null;
        if(Document!=null){using var rendered=Document.Render(draft?.Kind==Tool.Crop?null:draft);preview=new Bitmap(new MemoryStream(Imaging.Png(rendered)));Width=rendered.Width;Height=rendered.Height;}
        InvalidateVisual();Changed?.Invoke();
    }
    public override void Render(DrawingContext context)
    {
        if(preview==null)return;
        context.DrawImage(preview,new Rect(0,0,preview.PixelSize.Width,preview.PixelSize.Height),new Rect(0,0,Width,Height));
        var mark=draft?.Kind==Tool.Crop?draft:selected;
        if(mark!=null){var r=mark.Bounds;context.DrawRectangle(null,new Pen(Brushes.BlueViolet,2),new Rect(r.Left,r.Top,Math.Max(1,r.Width),Math.Max(1,r.Height)));}
    }
    Point Clamp(Point p)=>new(Math.Clamp(p.X,0,Document!.Image.Width),Math.Clamp(p.Y,0,Document.Image.Height));
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if(Document==null||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        Focus();start=last=Clamp(e.GetPosition(this));dragging=true;checkpointed=false;
        if(Tool==Tool.Select)
        {
            selected=Document.Marks.LastOrDefault(m=>{var b=m.Bounds;b.Inflate(8,8);return b.Contains((float)start.X,(float)start.Y);});
            moving=selected!=null;
        }
        else
        {
            selected=null;
            draft=new Mark{Kind=Tool,X=start.X,Y=start.Y,X2=start.X,Y2=start.Y,Color=Ink,Width=Stroke,FontSize=FontSize,Text=TextValue,Filled=Filled};
            if(Tool==Tool.Pen)draft.Points.Add(new[]{start.X,start.Y});
            if(Tool==Tool.Step){draft.X-=22;draft.Y-=22;draft.X2=draft.X+44;draft.Y2=draft.Y+44;draft.FontSize=24;draft.Text=(Document.Marks.Where(m=>m.Kind==Tool.Step).Select(m=>int.TryParse(m.Text,out int n)?n:0).DefaultIfEmpty().Max()+1).ToString();}
            if(Tool==Tool.Text){draft.X2+=Math.Max(100,TextValue.Length*FontSize*.55);draft.Y2+=FontSize*1.4;}
        }
        e.Pointer.Capture(this);e.Handled=true;Refresh();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);if(!dragging||Document==null)return;
        var p=Clamp(e.GetPosition(this));
        if(moving&&selected!=null)
        {
            if(!checkpointed){Document.Checkpoint();checkpointed=true;}
            selected.Move(p.X-last.X,p.Y-last.Y);last=p;
        }
        else if(draft!=null&&draft.Kind is not(Tool.Step or Tool.Text))
        {
            if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)&&draft.Kind!=Tool.Pen)
            {double length=Math.Max(Math.Abs(p.X-start.X),Math.Abs(p.Y-start.Y));p=new(start.X+Math.Sign(p.X-start.X)*length,start.Y+Math.Sign(p.Y-start.Y)*length);}
            draft.X2=p.X;draft.Y2=p.Y;
            if(draft.Kind==Tool.Pen){draft.Points.Add(new[]{p.X,p.Y});draft.X=draft.Points.Min(p=>p[0]);draft.Y=draft.Points.Min(p=>p[1]);draft.X2=draft.Points.Max(p=>p[0]);draft.Y2=draft.Points.Max(p=>p[1]);}
        }
        Refresh();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);if(!dragging||Document==null)return;
        dragging=moving=false;
        if(draft!=null)
        {
            if(draft.Kind==Tool.Crop){if(draft.Bounds.Width>=2&&draft.Bounds.Height>=2)Document.Crop(draft.Bounds);}
            else if(draft.Kind is Tool.Text or Tool.Step || draft.Bounds.Width>=2 || draft.Bounds.Height>=2){Document.Checkpoint();Document.Marks.Add(draft);}
            draft=null;
        }
        e.Pointer.Capture(null);Refresh();e.Handled=true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);dragging=moving=false;draft=null;Refresh();}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key==Key.Escape){draft=selected=null;dragging=moving=false;Refresh();e.Handled=true;}
        if(e.Key==Key.Delete&&selected!=null&&Document!=null){Document.Checkpoint();Document.Marks.Remove(selected);selected=null;Refresh();e.Handled=true;}
    }
    public void ReleaseResources(){preview?.Dispose();}
}
