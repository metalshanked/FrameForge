using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FrameForge;
public sealed class EditorSurface : FrameworkElement
{
    public CaptureDocument? Document { get; private set; }
    public Tool Tool { get; set; } = Tool.Arrow;
    public string Ink { get; set; } = "#FF6757EF";
    public double StrokeWidth { get; set; } = 4;
    public double TextSize { get; set; } = 26;
    public bool Filled { get; set; }
    public string TextValue { get; set; } = "Add your note";
    public Mark? Selected { get; private set; }

    public event Action? Changed;
    public event Action<Mark?>? SelectionChanged;
    private Mark? draft;
    private Point start, last;
    private bool moving, resizing, checkpointed;
    public EditorSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
    }

    public void SetDocument(CaptureDocument? doc)
    {
        Document = doc;
        Selected = null;
        Refresh();
    }

    public void Refresh()
    {
        Width = Document?.Image.PixelWidth ?? 1;
        Height = Document?.Image.PixelHeight ?? 1;
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Document == null)
            return;
        dc.DrawDrawing(Document.Drawing());
        if (draft != null)
        {
            if (draft.Kind == Tool.Crop)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(45, 101, 88, 245)), new System.Windows.Media.Pen(Brushes.White, 2), draft.Bounds);
            }
            else
                MarkRenderer.Draw(dc, draft, Document.Image);
        }

        if (Selected != null && Document.Marks.Contains(Selected))
        {
            var r = Selected.Bounds;
            r.Inflate(5, 5);
            var pen = new System.Windows.Media.Pen(new SolidColorBrush(Color.FromRgb(101, 88, 245)), 1)
            {
                DashStyle = DashStyles.Dash
            };
            dc.DrawRectangle(null, pen, r);
            dc.DrawRectangle(Brushes.White, new System.Windows.Media.Pen(Brushes.BlueViolet, 1), new Rect(r.Right - 5, r.Bottom - 5, 10, 10));
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left || Document == null)
            return;
        Focus();
        start = last = Clamp(e.GetPosition(this));
        checkpointed = false;
        if (Tool == Tool.Select)
        {
            resizing = Selected != null && Math.Abs(start.X - (Selected.Bounds.Right + 5)) < 12 && Math.Abs(start.Y - (Selected.Bounds.Bottom + 5)) < 12 && Selected.Kind != Tool.Pen;
            if (!resizing)
                Selected = Document.Marks.LastOrDefault(m =>
                {
                    var r = m.Bounds;
                    r.Inflate(8, 8);
                    return r.Contains(start);
                });
            moving = Selected != null && !resizing;
            if (Selected != null)
            {
                SelectionChanged?.Invoke(Selected);
                if (e.ClickCount == 2 && (Selected.Kind == Tool.Text || Selected.Kind == Tool.Callout))
                {
                    var t = Dialogs.Prompt(Window.GetWindow(this), "Edit annotation", Selected.Text, true);
                    if (t != null)
                    {
                        Document.Checkpoint();
                        Selected.Text = t;
                        SelectionChanged?.Invoke(Selected);
                        Refresh();
                    }

                    moving = resizing = false;
                    e.Handled = true;
                    return;
                }
            }
        }
        else
        {
            Selected = null;
            draft = new Mark
            {
                Kind = Tool,
                X = start.X,
                Y = start.Y,
                X2 = start.X,
                Y2 = start.Y,
                Color = Ink,
                Width = StrokeWidth,
                FontSize = TextSize,
                Filled = Filled,
                Text = TextValue
            };
            if (Tool == Tool.Pen)
                draft.Points.Add(new[] { start.X, start.Y });
            if (Tool == Tool.Step)
            {
                draft.Text = (Document.Marks.Where(m => m.Kind == Tool.Step).Select(m => int.TryParse(m.Text, out int value) ? value : 0).DefaultIfEmpty().Max() + 1).ToString();
                draft.X -= 22;
                draft.Y -= 22;
                draft.X2 = draft.X + 44;
                draft.Y2 = draft.Y + 44;
                draft.FontSize = 24;
            }
        }

        SelectionChanged?.Invoke(Selected);
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || Document == null)
            return;
        var p = Clamp(e.GetPosition(this));
        if (Selected != null && (moving || resizing))
        {
            if ((p - last).Length > 0)
            {
                if (!checkpointed)
                {
                    Document.Checkpoint();
                    checkpointed = true;
                }

                if (moving)
                    Selected.Move(p.X - last.X, p.Y - last.Y);
                else
                {
                    Selected.X2 = p.X;
                    Selected.Y2 = p.Y;
                }

                last = p;
            }
        }
        else if (draft != null && draft.Kind != Tool.Step)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && draft.Kind != Tool.Pen)
            {
                double size = Math.Max(Math.Abs(p.X - start.X), Math.Abs(p.Y - start.Y));
                p = new Point(start.X + Math.Sign(p.X - start.X) * size, start.Y + Math.Sign(p.Y - start.Y) * size);
                p = Clamp(p);
            }

            draft.X2 = p.X;
            draft.Y2 = p.Y;
            if (draft.Kind == Tool.Pen)
            {
                draft.Points.Add(new[] { p.X, p.Y });
                draft.X = draft.Points.Min(a => a[0]);
                draft.X2 = draft.Points.Max(a => a[0]);
                draft.Y = draft.Points.Min(a => a[1]);
                draft.Y2 = draft.Points.Max(a => a[1]);
            }
        }

        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!IsMouseCaptured || Document == null)
            return;
        ReleaseMouseCapture();
        moving = resizing = false;
        if (draft != null)
        {
            if (draft.Kind is Tool.Text or Tool.Callout)
            {
                if (draft.Bounds.Width < 30 || draft.Bounds.Height < 20)
                {
                    draft.X2 = Math.Min(Width, draft.X + 300);
                    draft.Y2 = Math.Min(Height, draft.Y + (draft.Kind == Tool.Callout ? 95 : 70));
                }
            }

            if (draft.Kind == Tool.Crop)
            {
                if (draft.Bounds.Width > 2 && draft.Bounds.Height > 2)
                    Document.Crop(draft.Bounds);
            }
            else if (draft.Bounds.Width > 2 || draft.Bounds.Height > 2)
            {
                Document.Checkpoint();
                Document.Marks.Add(draft);
                Selected = draft;
                SelectionChanged?.Invoke(draft);
            }

            draft = null;
        }

        Refresh();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            draft = null;
            Selected = null;
            SelectionChanged?.Invoke(null);
            moving = resizing = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }

        if (e.Key == Key.Delete)
        {
            DeleteSelection();
            e.Handled = true;
        }

        if (Selected != null && Document != null && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            Document.Checkpoint();
            double n = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            Selected.Move(e.Key == Key.Left ? -n : e.Key == Key.Right ? n : 0, e.Key == Key.Up ? -n : e.Key == Key.Down ? n : 0);
            Refresh();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    public void DeleteSelection()
    {
        if (Document == null || Selected == null)
            return;
        Document.Checkpoint();
        Document.Marks.Remove(Selected);
        Selected = null;
        Refresh();
    }

    public void DuplicateSelection()
    {
        if (Document == null || Selected == null)
            return;
        Document.Checkpoint();
        var copy = Selected.Clone();
        copy.Move(16, 16);
        Document.Marks.Add(copy);
        Selected = copy;
        Refresh();
    }

    public void ApplyStyle()
    {
        if (Document == null || Selected == null)
            return;
        Document.Checkpoint();
        Selected.Color = Ink;
        Selected.Width = StrokeWidth;
        Selected.FontSize = TextSize;
        Selected.Filled = Filled;
        if (Selected.Kind is Tool.Text or Tool.Callout)
            Selected.Text = TextValue;
        Refresh();
    }

    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, Width), Math.Clamp(p.Y, 0, Height));
}
