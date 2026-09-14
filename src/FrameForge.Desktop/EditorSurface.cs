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
    public Tool Tool { get; set; }=Tool.Arrow;
    public string Ink { get; set; }="#FF6757EF";
    public double Stroke { get; set; }=4;
    public double FontSize { get; set; }=26;
    public string TextValue { get; set; }="Add your note";
    public bool Filled { get; set; }
    public double ViewZoom { get; set; }=1;
    public bool IsDrawing=>dragging;
    public Mark? Selection=>selected;
    public event Action? Changed;
    public event Action<Mark>? EditText;
    Bitmap? preview;
    Mark? draft,selected,original;
    Point start,last;
    int resizeHandle=-1;
    bool dragging,moving,checkpointed;
    public EditorSurface(){Focusable=true;ClipToBounds=true;Cursor=new Cursor(StandardCursorType.Cross);}
    public void SetDocument(CaptureDocument? doc){Document=doc;selected=draft=null;dragging=moving=false;Refresh();}
    public void Refresh()
    {
        if(selected!=null&&Document?.Marks.Contains(selected)!=true)selected=null;
        preview?.Dispose();preview=null;
        if(Document!=null){using var rendered=Document.Render(draft?.Kind==Tool.Crop?null:draft);preview=new Bitmap(new MemoryStream(Imaging.Png(rendered)));Width=rendered.Width;Height=rendered.Height;}
        else{Width=Height=0;}
        InvalidateVisual();Changed?.Invoke();
    }
    static Point[] Handles(SKRect r)=>new[]{new Point(r.Left,r.Top),new Point(r.MidX,r.Top),new Point(r.Right,r.Top),new Point(r.Right,r.MidY),new Point(r.Right,r.Bottom),new Point(r.MidX,r.Bottom),new Point(r.Left,r.Bottom),new Point(r.Left,r.MidY)};
    double HandleSize=>8/Math.Max(.1,ViewZoom);
    public override void Render(DrawingContext context)
    {
        if(preview==null)return;
        context.DrawImage(preview,new Rect(0,0,preview.PixelSize.Width,preview.PixelSize.Height),new Rect(0,0,Width,Height));
        var mark=draft?.Kind==Tool.Crop?draft:selected;
        if(mark==null)return;
        var r=mark.Bounds;
        context.DrawRectangle(null,new Pen(Brushes.BlueViolet,1.5/Math.Max(.1,ViewZoom)),new Rect(r.Left,r.Top,Math.Max(1,r.Width),Math.Max(1,r.Height)));
        if(ReferenceEquals(mark,selected))foreach(var point in Handles(r))
            context.DrawRectangle(Brushes.White,new Pen(Brushes.BlueViolet,1/Math.Max(.1,ViewZoom)),new Rect(point.X-HandleSize/2,point.Y-HandleSize/2,HandleSize,HandleSize));
    }
    Point Clamp(Point p)=>new(Math.Clamp(p.X,0,Document!.Image.Width),Math.Clamp(p.Y,0,Document.Image.Height));
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if(Document==null||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        Focus();start=last=Clamp(e.GetPosition(this));checkpointed=false;resizeHandle=-1;original=null;dragging=true;
        if(Tool==Tool.Select)
        {
            if(selected!=null)
            {
                var handles=Handles(selected.Bounds);
                resizeHandle=Array.FindIndex(handles,p=>Math.Abs(p.X-start.X)<=HandleSize&&Math.Abs(p.Y-start.Y)<=HandleSize);
            }
            if(resizeHandle>=0)original=selected!.Clone();
            else
            {
                selected=Document.Marks.LastOrDefault(m=>{var b=m.Bounds;b.Inflate((float)HandleSize,(float)HandleSize);return b.Contains((float)start.X,(float)start.Y);});
                if(e.ClickCount==2&&selected?.Kind is Tool.Text or Tool.Callout or Tool.Step){dragging=false;EditText?.Invoke(selected);e.Handled=true;Refresh();return;}
                moving=selected!=null;
            }
        }
        else
        {
            moving=false;selected=null;
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
        if(selected!=null&&(moving||resizeHandle>=0))
        {
            if(Math.Abs(p.X-start.X)+Math.Abs(p.Y-start.Y)<.5&&!checkpointed)return;
            if(!checkpointed){Document.Checkpoint();checkpointed=true;}
            if(resizeHandle>=0&&original!=null)
            {
                var r=original.Bounds;
                float left=r.Left,top=r.Top,right=r.Right,bottom=r.Bottom;
                if(resizeHandle is 0 or 6 or 7)left=Math.Min((float)p.X,right-2);
                if(resizeHandle is 0 or 1 or 2)top=Math.Min((float)p.Y,bottom-2);
                if(resizeHandle is 2 or 3 or 4)right=Math.Max((float)p.X,left+2);
                if(resizeHandle is 4 or 5 or 6)bottom=Math.Max((float)p.Y,top+2);
                AnnotationGeometry.Resize(selected,original,new SKRect(left,top,right,bottom));
            }
            else {selected.Move(p.X-last.X,p.Y-last.Y);last=p;}
        }
        else if(draft!=null&&draft.Kind is not(Tool.Step or Tool.Text))
        {
            if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)&&draft.Kind!=Tool.Pen)
            {double length=Math.Max(Math.Abs(p.X-start.X),Math.Abs(p.Y-start.Y));p=Clamp(new(start.X+(p.X>=start.X?1:-1)*length,start.Y+(p.Y>=start.Y?1:-1)*length));}
            draft.X2=p.X;draft.Y2=p.Y;
            if(draft.Kind==Tool.Pen){draft.Points.Add(new[]{p.X,p.Y});draft.X=draft.Points.Min(p=>p[0]);draft.Y=draft.Points.Min(p=>p[1]);draft.X2=draft.Points.Max(p=>p[0]);draft.Y2=draft.Points.Max(p=>p[1]);}
        }
        Refresh();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);if(!dragging||Document==null)return;
        dragging=moving=false;resizeHandle=-1;
        if(draft!=null)
        {
            if(draft.Kind==Tool.Crop){if(draft.Bounds.Width>=2&&draft.Bounds.Height>=2)Document.Crop(draft.Bounds);}
            else if(draft.Kind is Tool.Text or Tool.Step||draft.Bounds.Width>=2||draft.Bounds.Height>=2){Document.Checkpoint();Document.Marks.Add(draft);}
            draft=null;
        }
        e.Pointer.Capture(null);Refresh();e.Handled=true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);dragging=moving=false;resizeHandle=-1;draft=null;Refresh();}
    public void DeleteSelected(){if(selected==null||Document==null)return;Document.Checkpoint();Document.Marks.Remove(selected);selected=null;Refresh();}
    public void DuplicateSelected(){if(selected==null||Document==null)return;Document.Checkpoint();var copy=selected.Clone();copy.Move(16,16);Document.Marks.Add(copy);selected=copy;Refresh();}
    public void ApplyStyle()
    {
        if(selected==null||Document==null)return;Document.Checkpoint();
        selected.Color=Ink;selected.Width=Stroke;selected.FontSize=FontSize;selected.Filled=Filled;
        if(selected.Kind is Tool.Text or Tool.Callout)selected.Text=TextValue;
        Refresh();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key==Key.Escape){draft=selected=null;dragging=moving=false;resizeHandle=-1;Refresh();e.Handled=true;}
        if(e.Key is Key.Delete or Key.Back&&selected!=null){DeleteSelected();e.Handled=true;}
    }
    public void ReleaseResources(){preview?.Dispose();}
}
