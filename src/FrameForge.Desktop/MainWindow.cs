using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FrameForge.Core;
using SkiaSharp;
namespace FrameForge.Desktop;

public sealed class MainWindow : Window
{
    readonly EditorSurface editor=new();
    readonly TextBlock status=new(){Text="Open an image or capture your screen.",TextWrapping=TextWrapping.Wrap};
    readonly TextBlock dimensions=new(){FontSize=12,Foreground=Brushes.DimGray};
    readonly StackPanel recent=new(){Orientation=Orientation.Horizontal,Spacing=10};
    readonly List<Bitmap> thumbs=new();
    readonly Button stop;
    readonly Button cancel;
    readonly TextBox annotationText=new(){Text="Add your note",PlaceholderText="Annotation text",AcceptsReturn=true,MinHeight=70};
    readonly TextBox color=new(){Text="#FF6757EF",PlaceholderText="#AARRGGBB"};
    readonly NumericUpDown stroke=new(){Value=4,Minimum=1,Maximum=100,Increment=1};
    readonly NumericUpDown font=new(){Value=26,Minimum=8,Maximum=150,Increment=1};
    readonly CheckBox fill=new(){Content="Fill shape"};
    readonly Slider zoom=new(){Minimum=.15,Maximum=2.5,Value=1,Width=130};
    readonly LayoutTransformControl zoomHost;
    DesktopPreferences preferences=DesktopPreferences.Load();
    CaptureDocument? document;
    string? projectPath;
    CancellationTokenSource? operation;
    Recorder? recorder;
    GlobalShortcut? shortcut;
    TrayIcon? tray;
    bool busy,quitting;
    public MainWindow()
    {
        Title="FrameForge — Desktop preview";Width=1300;Height=850;MinWidth=1000;MinHeight=700;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brush.Parse("#F5F6FA");
        Icon=new WindowIcon(AssetLoader.Open(new Uri("avares://FrameForge.Desktop/Assets/FrameForge.ico")));
        var root=new DockPanel{LastChildFill=true,Margin=new Thickness(16)};
        var header=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(0,0,0,14)};
        var title=new StackPanel{Spacing=3};title.Children.Add(Label("FrameForge",25,true));title.Children.Add(Label("Desktop preview 0.3.0 · "+PlatformCapture.Description,12));header.Children.Add(title);
        var topActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};
        topActions.Children.Add(Button("Open…",()=>Run(Open)));topActions.Children.Add(Button("Paste",()=>Run(Paste)));topActions.Children.Add(Button("Copy image",()=>Run(Copy)));topActions.Children.Add(Button("Export…",()=>Run(Export)));
        Grid.SetColumn(topActions,1);header.Children.Add(topActions);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);

        var footer=new StackPanel{Spacing=8,Margin=new Thickness(0,12,0,0)};
        var statusRow=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};
        statusRow.Children.Add(status);cancel=Button("Cancel operation",()=>operation?.Cancel());cancel.IsVisible=false;Grid.SetColumn(cancel,1);statusRow.Children.Add(cancel);footer.Children.Add(statusRow);
        footer.Children.Add(Label("Recent captures",12,true));
        footer.Children.Add(new ScrollViewer{Content=recent,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,0,16),Height=108});
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);

        var sidebar=new DockPanel{Width=218,Margin=new Thickness(0,0,16,0)};
        var settings=new StackPanel{Spacing=8,Margin=new Thickness(0,14,0,0)};
        settings.Children.Add(Button("Preferences & shortcut…",()=>Run(Preferences)));
        settings.Children.Add(Button("Open capture folder",()=>Run(()=>{ProcessRunner.OpenFolder(AppPaths.Library);return Task.CompletedTask;})));
        settings.Children.Add(Button("Exit FrameForge",()=>Run(Exit)));
        DockPanel.SetDock(settings,Dock.Bottom);sidebar.Children.Add(settings);
        var actions=new StackPanel{Spacing=8};
        actions.Children.Add(Label("CAPTURE",11,true));
        actions.Children.Add(Button(OperatingSystem.IsLinux()?"Capture screen / region":"Capture region",()=>Run(()=>Capture(true))));
        actions.Children.Add(Button(OperatingSystem.IsLinux()?"Choose in desktop dialog":"Capture screen",()=>Run(()=>Capture(false))));
        actions.Children.Add(Button("Scrolling capture…",()=>Run(Scrolling)));
        actions.Children.Add(Button("Stitch image files…",()=>Run(StitchFiles)));
        actions.Children.Add(Label("RECORD & EXTRACT",11,true));
        actions.Children.Add(Button("Record screen",()=>Run(StartRecording)));
        stop=Button("Stop recording",()=>Run(StopRecording));stop.IsVisible=false;actions.Children.Add(stop);
        actions.Children.Add(Button("Trim video / export GIF…",()=>Run(VideoExport)));
        actions.Children.Add(Button("Extract text (OCR)",()=>Run(ExtractText)));
        actions.Children.Add(Label("Recording is video-only in this preview. FFmpeg and Tesseract are installed separately.",12));
        actions.Children.Add(Label("PROJECT",11,true));
        actions.Children.Add(Button("Save editable project…",()=>Run(SaveProject)));
        actions.Children.Add(Label("Projects keep the original image. Use Redact and export a flat image when hiding private content.",12));
        sidebar.Children.Add(new ScrollViewer{Content=actions,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});DockPanel.SetDock(sidebar,Dock.Left);root.Children.Add(sidebar);

        var inspector=new StackPanel{Spacing=8,Width=172,Margin=new Thickness(16,0,0,0)};
        inspector.Children.Add(Label("ANNOTATION",11,true));
        inspector.Children.Add(Label("Color",12));inspector.Children.Add(color);
        inspector.Children.Add(Label("Stroke width",12));inspector.Children.Add(stroke);
        inspector.Children.Add(Label("Text size",12));inspector.Children.Add(font);
        inspector.Children.Add(fill);inspector.Children.Add(Label("Text / callout",12));inspector.Children.Add(annotationText);
        inspector.Children.Add(Button("Resize image…",()=>Run(Resize)));inspector.Children.Add(Button("Rotate clockwise",()=>Run(()=>{RequireDocument().Rotate();editor.Refresh();return Task.CompletedTask;})));
        inspector.Children.Add(Label("Select a mark to move it. Delete removes it. Shift draws equal sides. Escape clears selection.",12));
        DockPanel.SetDock(inspector,Dock.Right);root.Children.Add(inspector);

        var workspace=new DockPanel();
        var tools=new WrapPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};
        foreach(var tool in Enum.GetValues<Tool>())
        {
            var button=new ToggleButton{Content=tool.ToString(),Tag=tool,Margin=new Thickness(0,0,6,6),Padding=new Thickness(10,7),MinHeight=34,IsChecked=tool==Tool.Arrow};
            button.Click+=(_,_)=>{foreach(var other in tools.Children.OfType<ToggleButton>())other.IsChecked=ReferenceEquals(other,button);editor.Tool=tool;};
            tools.Children.Add(button);
        }
        DockPanel.SetDock(tools,Dock.Top);workspace.Children.Add(tools);
        var editActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,Margin=new Thickness(0,0,0,10)};
        editActions.Children.Add(Button("Undo",()=>{document?.Undo();editor.Refresh();}));editActions.Children.Add(Button("Redo",()=>{document?.Redo();editor.Refresh();}));
        editActions.Children.Add(Label("Zoom",12));editActions.Children.Add(zoom);editActions.Children.Add(dimensions);
        DockPanel.SetDock(editActions,Dock.Top);workspace.Children.Add(editActions);
        zoomHost=new LayoutTransformControl{Child=editor,LayoutTransform=new ScaleTransform(1,1),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
        workspace.Children.Add(new Border{Background=Brush.Parse("#E4E6EE"),CornerRadius=new CornerRadius(8),Padding=new Thickness(20),Child=new ScrollViewer{Content=zoomHost,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
        root.Children.Add(workspace);Content=root;
        zoom.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty)zoomHost.LayoutTransform=new ScaleTransform(zoom.Value,zoom.Value);};
        editor.Changed+=()=>{if(document!=null)dimensions.Text=$"{document.Image.Width} × {document.Image.Height} px";};
        annotationText.TextChanged+=(_,_)=>editor.TextValue=annotationText.Text??"";
        color.TextChanged+=(_,_)=>{if(SKColor.TryParse(color.Text,out _))editor.Ink=color.Text!;};
        stroke.ValueChanged+=(_,_)=>editor.Stroke=(double)(stroke.Value??4);font.ValueChanged+=(_,_)=>editor.FontSize=(double)(font.Value??26);
        fill.IsCheckedChanged+=(_,_)=>editor.Filled=fill.IsChecked==true;
        Closing+=(_,e)=>{if(quitting)return;e.Cancel=true;if(busy){operation?.Cancel();status.Text="Canceling the current operation. Close again when it finishes.";return;}if(preferences.CloseToTray&&tray!=null){Run(()=>{SaveCurrent();Hide();return Task.CompletedTask;});}else Run(Exit);};
        Opened+=(_,_)=>{SetupTray();LoadRecent();if(preferences.ShortcutEnabled)Run(ApplyShortcut);};
        KeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape&&busy){operation?.Cancel();e.Handled=true;}
            if(e.Source is TextBox||e.Source is NumericUpDown)return;
            bool command=e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS()?KeyModifiers.Meta:KeyModifiers.Control);
            if(!command)return;
            if(e.Key==Key.Z){document?.Undo();editor.Refresh();e.Handled=true;}
            if(e.Key==Key.Y){document?.Redo();editor.Refresh();e.Handled=true;}
            if(e.Key==Key.S){Run(SaveProject);e.Handled=true;}
            if(e.Key==Key.C){Run(Copy);e.Handled=true;}
        };
    }
    static TextBlock Label(string text,double size=14,bool bold=false)=>new(){Text=text,FontSize=size,FontWeight=bold?FontWeight.SemiBold:FontWeight.Normal,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    static Button Button(string text,Action action){var b=new Button{Content=text,MinHeight=36,Padding=new Thickness(12,8),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center};b.Click+=(_,_)=>action();return b;}
    async void Run(Func<Task> action)
    {
        if(busy)return;busy=true;operation=new CancellationTokenSource(TimeSpan.FromMinutes(2));cancel.IsVisible=true;
        try{await action();}
        catch(OperationCanceledException){status.Text="Canceled. Your existing capture is unchanged.";}
        catch(Exception e){status.Text=e.Message;Show();Activate();}
        finally{operation.Dispose();operation=null;busy=false;cancel.IsVisible=false;}
    }
    CancellationToken Token=>operation?.Token??CancellationToken.None;
    CaptureDocument RequireDocument()=>document??throw new InvalidOperationException("Capture or open an image first.");
    void SaveCurrent()
    {
        if(document==null)return;
        if(projectPath==null)projectPath=AppPaths.NewCapture(".ffg");
        if(document.Dirty||!File.Exists(projectPath))document.Save(projectPath);
    }
    void SetDocument(CaptureDocument next,string? path=null)
    {
        try{SaveCurrent();}catch{next.Dispose();throw;}
        document?.Dispose();document=next;projectPath=path;editor.SetDocument(next);
        zoom.Value=Math.Min(1,720.0/next.Image.Width);SaveCurrent();LoadRecent();
    }
    async Task Capture(bool region)
    {
        if(recorder!=null)throw new InvalidOperationException("Stop recording before starting a screenshot.");
        var screen=Screens.ScreenFromWindow(this)??Screens.Primary??throw new InvalidOperationException("No screen is available.");
        Hide();CaptureTrace.Write("editor hidden; capture starting");
        try
        {
            await Task.Delay(300,Token);
            using var captured=await PlatformCapture.Capture(region,screen.Bounds,Token);
            CaptureTrace.Write("platform capture returned");if(captured==null){status.Text="Capture canceled.";return;}
            if(region&&OperatingSystem.IsWindows())
            {
                var rectangle=await RegionPicker.Pick(captured,screen.Bounds,screen.Scaling,Token);if(rectangle==null){status.Text="Capture canceled.";return;}
                using var crop=Imaging.Crop(captured,rectangle.Value);SetDocument(new CaptureDocument(crop));
            }
            else SetDocument(new CaptureDocument(captured));
            Show();Activate();await Copy();status.Text="Captured and copied to the clipboard.";
        }
        finally{CaptureTrace.Write("capture finished; editor restored");Show();Activate();}
    }
    async Task Open()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Open image or FrameForge project",AllowMultiple=false,FileTypeFilter=new[]{new FilePickerFileType("Images and projects"){Patterns=new[]{"*.png","*.jpg","*.jpeg","*.webp","*.bmp","*.ffg"}}}});
        var path=files.FirstOrDefault()?.TryGetLocalPath();if(path==null)return;
        if(Path.GetExtension(path).Equals(".ffg",StringComparison.OrdinalIgnoreCase))SetDocument(CaptureDocument.Load(path));
        else {using var bitmap=Imaging.Load(path);SetDocument(new CaptureDocument(bitmap){Title=System.IO.Path.GetFileNameWithoutExtension(path)});}
        status.Text="Opened "+System.IO.Path.GetFileName(path);
    }
    async Task Copy()
    {
        var clipboard=Clipboard??throw new InvalidOperationException("The clipboard is unavailable.");
        using var rendered=RequireDocument().Render();
        var bitmap=new Bitmap(new MemoryStream(Imaging.Png(rendered)));
        await clipboard.SetBitmapAsync(bitmap);await clipboard.FlushAsync();status.Text="Image copied.";
    }
    async Task Paste()
    {
        using var image=Clipboard==null?null:await Clipboard.TryGetBitmapAsync();
        if(image==null)throw new InvalidOperationException("There is no image on the clipboard.");
        using var stream=new MemoryStream();image.Save(stream,PngBitmapEncoderOptions.Default);using var decoded=Imaging.Load(stream.ToArray());SetDocument(new CaptureDocument(decoded));status.Text="Pasted image.";
    }
    async Task Export()
    {
        RequireDocument();
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Export image",SuggestedFileName="Capture.png",DefaultExtension="png",ShowOverwritePrompt=true,FileTypeChoices=new[]{new FilePickerFileType("PNG"){Patterns=new[]{"*.png"}},new FilePickerFileType("JPEG"){Patterns=new[]{"*.jpg"}},new FilePickerFileType("WebP"){Patterns=new[]{"*.webp"}}}});
        var path=file?.TryGetLocalPath();if(path==null)return;
        using var rendered=document!.Render();Imaging.Export(rendered,path);status.Text="Exported "+System.IO.Path.GetFileName(path);
    }
    async Task SaveProject()
    {
        RequireDocument();
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Save editable project",SuggestedFileName="Capture.ffg",DefaultExtension="ffg",ShowOverwritePrompt=true,FileTypeChoices=new[]{new FilePickerFileType("FrameForge project"){Patterns=new[]{"*.ffg"}}}});
        var path=file?.TryGetLocalPath();if(path==null)return;document!.Save(path);status.Text="Saved editable project.";
    }
    void LoadRecent()
    {
        recent.Children.Clear();foreach(var t in thumbs)t.Dispose();thumbs.Clear();
        foreach(var file in new DirectoryInfo(AppPaths.Library).EnumerateFiles("*.ffg").OrderByDescending(f=>f.LastWriteTimeUtc).Take(10))
        {
            var button=Button(file.LastWriteTime.ToString("MMM d · HH:mm"),()=>Run(()=>{SaveCurrent();SetDocument(CaptureDocument.Load(file.FullName),file.FullName);return Task.CompletedTask;}));
            button.Width=150;button.Height=64;ToolTip.SetTip(button,file.Name);recent.Children.Add(button);
        }
    }
    async Task Resize()
    {
        var d=RequireDocument();var value=await Prompt("Resize image","Width × height in pixels",d.Image.Width+" x "+d.Image.Height);
        if(value==null)return;var parts=value.ToLowerInvariant().Split('x','×',',');
        if(parts.Length!=2||!int.TryParse(parts[0].Trim(),out int w)||!int.TryParse(parts[1].Trim(),out int h))throw new ArgumentException("Enter width x height, such as 1280 x 720.");
        d.Resize(w,h);editor.Refresh();status.Text="Image resized.";
    }
    async Task ExtractText()
    {
        using var image=RequireDocument().Render();status.Text="Reading text locally…";
        var text=await Ocr.Read(Imaging.Png(image),preferences.OcrLanguage,Token);
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(20)};
        var output=new TextBox{Text=text.Length==0?"No text found.":text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,IsReadOnly=true,Height=300};
        panel.Children.Add(output);var dialog=Dialog("Extracted text",panel,650,430);
        panel.Children.Add(Button("Copy text",()=>{if(Clipboard!=null)_=Clipboard.SetTextAsync(text);}));
        panel.Children.Add(Button("Close",()=>dialog.Close()));await dialog.ShowDialog(this);status.Text="OCR complete.";
    }
    async Task StartRecording()
    {
        if(recorder!=null)return;status.Text="Starting video-only recording…";
        Hide();try{await Task.Delay(400,Token);recorder=await Recorder.Start(Token);}finally{Show();Activate();}
        stop.IsVisible=true;status.Text="Recording screen · video only. Use Stop recording to save.";
    }
    async Task StopRecording()
    {
        if(recorder==null)return;
        var active=recorder;recorder=null;
        try{await active.Stop();status.Text="Recording saved: "+System.IO.Path.GetFileName(active.Path);}
        finally{active.Dispose();stop.IsVisible=false;}
    }
    async Task VideoExport()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Choose video",AllowMultiple=false});
        var source=files.FirstOrDefault()?.TryGetLocalPath();if(source==null)return;
        var times=await Prompt("Trim or export GIF","Start seconds, duration seconds, format (mp4 or gif)","0, 10, mp4");if(times==null)return;
        var p=times.Split(',');if(p.Length!=3||!double.TryParse(p[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var start)||!double.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var duration)||p[2].Trim() is not("mp4" or "gif"))throw new ArgumentException("Enter values such as 0, 10, mp4.");
        bool gif=p[2].Trim()=="gif";
        var file=await StorageProvider.SaveFilePickerAsync(new(){Title="Export video",SuggestedFileName="Capture."+(gif?"gif":"mp4"),DefaultExtension=gif?"gif":"mp4",ShowOverwritePrompt=true});
        var target=file?.TryGetLocalPath();if(target==null)return;
        if(string.Equals(System.IO.Path.GetFullPath(source),System.IO.Path.GetFullPath(target),OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))throw new InvalidOperationException("Export to a different file to keep your original video.");
        status.Text="Exporting video…";await Recorder.Convert(source,target,start,duration,gif,Token);status.Text="Video exported.";
    }
    async Task StitchFiles()
    {
        var files=await StorageProvider.OpenFilePickerAsync(new(){Title="Select overlapping captures (ordered by filename)",AllowMultiple=true,FileTypeFilter=new[]{new FilePickerFileType("Images"){Patterns=new[]{"*.png","*.jpg","*.webp"}}}});
        var paths=files.Select(f=>f.TryGetLocalPath()).Where(p=>p!=null).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        if(paths.Length<2)return;
        SKBitmap combined=Imaging.Load(paths[0]!);SKBitmap previous=combined.Copy();
        try
        {
            foreach(var path in paths.Skip(1))
            {
                Token.ThrowIfCancellationRequested();using var next=Imaging.Load(path!);var match=await Task.Run(()=>Stitcher.FindShift(previous,next),Token);
                if(match.Duplicate)continue;if(!match.Confident)throw new InvalidOperationException("These images do not have a reliable downward overlap. Keep at least 20% of the preceding frame visible.");
                var appended=Stitcher.Append(combined,next,match.Shift);combined.Dispose();combined=appended;previous.Dispose();previous=next.Copy();
            }
            SetDocument(new CaptureDocument(combined){Title="Scrolling capture"});await Copy();status.Text="Stitched capture copied to the clipboard.";
        }
        finally{combined.Dispose();previous.Dispose();}
    }
    async Task Scrolling()
    {
        await Message("Scrolling capture","Capture overlapping sections from top to bottom. Choose the same rectangle for each frame; scroll about half a page between captures. The desktop permission picker will appear for each frame on Linux. Press Finish when done.");
        var initialProject=projectPath;await Capture(true);if(document==null||projectPath==initialProject)return;
        using var first=document.Image.Copy();SKBitmap previous=first.Copy(),combined=first.Copy();
        try
        {
            while(true)
            {
                var choice=await Choose("Scrolling capture","Scroll the target page down, keeping about half of the preceding content visible. Then add another frame.","Add frame","Finish");
                if(!choice)break;
                var existing=projectPath;await Capture(true);if(projectPath==existing)break;
                using var next=RequireDocument().Image.Copy();var match=await Task.Run(()=>Stitcher.FindShift(previous,next),Token);
                if(match.Duplicate){status.Text="Duplicate frame skipped.";continue;}
                if(!match.Confident)throw new InvalidOperationException("No reliable overlap found. Individual frames are saved in the capture folder; use Stitch image files after correcting the selection.");
                var appended=Stitcher.Append(combined,next,match.Shift);combined.Dispose();combined=appended;previous.Dispose();previous=next.Copy();
            }
            SetDocument(new CaptureDocument(combined){Title="Scrolling capture"});await Copy();status.Text="Scrolling capture finished and copied.";
        }
        finally{combined.Dispose();previous.Dispose();}
    }
    async Task ApplyShortcut()
    {
        shortcut?.Dispose();shortcut=null;
        if(preferences.ShortcutEnabled){shortcut=await GlobalShortcut.Register(preferences,()=>Run(recorder==null?()=>Capture(true):StopRecording),Token);status.Text="Capture shortcut: "+shortcut.Display;}
    }
    async Task Preferences()
    {
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(22)};
        var enable=new CheckBox{Content="Enable global capture shortcut",IsChecked=preferences.ShortcutEnabled};
        var close=new CheckBox{Content="Keep running in the tray when closing",IsChecked=preferences.CloseToTray};
        var ctrl=new CheckBox{Content="Ctrl",IsChecked=preferences.Control};var alt=new CheckBox{Content="Alt / Option",IsChecked=preferences.Alt};var shift=new CheckBox{Content="Shift",IsChecked=preferences.Shift};
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=14};row.Children.Add(ctrl);row.Children.Add(alt);row.Children.Add(shift);
        var keys=new ComboBox{ItemsSource=new[]{"Tilde"}.Concat(Enumerable.Range('A',26).Select(c=>((char)c).ToString())).ToArray(),SelectedItem=preferences.Key,HorizontalAlignment=HorizontalAlignment.Stretch};
        var language=new TextBox{Text=preferences.OcrLanguage,PlaceholderText="eng"};
        panel.Children.Add(enable);panel.Children.Add(row);panel.Children.Add(keys);panel.Children.Add(close);
        panel.Children.Add(Label("On Linux, your desktop may ask you to choose the shortcut. Some desktops need a tray extension; enable close-to-tray only when the FrameForge icon is visible.",12));
        panel.Children.Add(Label("OCR language (installed Tesseract language code)",12));panel.Children.Add(language);
        var dialog=Dialog("Preferences",panel,570,530);DesktopPreferences? candidate=null;
        panel.Children.Add(Button("Save preferences",()=>{candidate=new(){ShortcutEnabled=enable.IsChecked==true,CloseToTray=close.IsChecked==true,Control=ctrl.IsChecked==true,Alt=alt.IsChecked==true,Shift=shift.IsChecked==true,Key=keys.SelectedItem as string??"Tilde",OcrLanguage=language.Text??"eng"};dialog.Close();}));
        panel.Children.Add(Button("Cancel",()=>dialog.Close()));await dialog.ShowDialog(this);
        if(candidate!=null){if(!candidate.Valid)throw new ArgumentException("Use Ctrl or Alt with your capture key.");preferences=candidate;preferences.Save();await ApplyShortcut();}
    }
    void SetupTray()
    {
        try
        {
            var menu=new NativeMenu();var show=new NativeMenuItem("Open FrameForge");show.Click+=(_,_)=>{Show();Activate();};menu.Items.Add(show);
            var capture=new NativeMenuItem("Capture region / stop recording");capture.Click+=(_,_)=>Run(recorder==null?()=>Capture(true):StopRecording);menu.Items.Add(capture);
            var exit=new NativeMenuItem("Exit FrameForge");exit.Click+=(_,_)=>Run(Exit);menu.Items.Add(exit);
            tray=new TrayIcon{Icon=Icon,ToolTipText="FrameForge desktop preview",Menu=menu,IsVisible=true};tray.Clicked+=(_,_)=>{Show();Activate();};
            TrayIcon.SetIcons(Application.Current!,new TrayIcons{tray});
        }
        catch{tray=null;preferences.CloseToTray=false;}
    }
    async Task Exit()
    {
        if(recorder!=null)await StopRecording();SaveCurrent();quitting=true;
        shortcut?.Dispose();tray?.Dispose();document?.Dispose();editor.ReleaseResources();
        if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown();
    }
    static Window Dialog(string title,Control content,double width,double height)=>new(){Title="FrameForge · "+title,Content=new ScrollViewer{Content=content},Width=width,Height=height,MinWidth=380,MinHeight=250,WindowStartupLocation=WindowStartupLocation.CenterOwner};
    async Task<string?> Prompt(string title,string instructions,string initial)
    {
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(24)};panel.Children.Add(Label(instructions));var input=new TextBox{Text=initial};panel.Children.Add(input);
        var window=Dialog(title,panel,560,270);string? value=null;panel.Children.Add(Button("Apply",()=>{value=input.Text;window.Close();}));panel.Children.Add(Button("Cancel",()=>window.Close()));await window.ShowDialog(this);return value;
    }
    async Task Message(string title,string text)
    {var panel=new StackPanel{Spacing=16,Margin=new Thickness(24)};panel.Children.Add(Label(text));var window=Dialog(title,panel,560,330);panel.Children.Add(Button("Continue",()=>window.Close()));await window.ShowDialog(this);}
    async Task<bool> Choose(string title,string text,string yes,string no)
    {var panel=new StackPanel{Spacing=16,Margin=new Thickness(24)};panel.Children.Add(Label(text));var window=Dialog(title,panel,560,330);bool result=false;panel.Children.Add(Button(yes,()=>{result=true;window.Close();}));panel.Children.Add(Button(no,()=>window.Close()));await window.ShowDialog(this);return result;}
}
