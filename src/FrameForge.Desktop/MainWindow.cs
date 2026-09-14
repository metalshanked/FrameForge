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

public sealed partial class MainWindow : Window
{
    readonly EditorSurface editor=new();
    readonly TextBlock status=new(){Text="Open an image or capture your screen.",TextWrapping=TextWrapping.Wrap};
    readonly TextBlock dimensions=new(){FontSize=12,Foreground=Brushes.DimGray};
    readonly StackPanel recent=new(){Orientation=Orientation.Horizontal,Spacing=10};
    readonly List<Bitmap> thumbs=new();
    readonly Button stop;
    readonly Button pause;
    readonly Button undoDelete;
    readonly TextBox search=new(){PlaceholderText="Search captures…",Width=190};
    readonly CaptureLibrary library=new(AppPaths.Root);
    readonly Stack<(DeletedCapture Capture,bool WasOpen)> deleted=new();
    readonly DispatcherTimer autosave=new(){Interval=TimeSpan.FromSeconds(2)};
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
    IRecording? recorder;
    GlobalShortcut? shortcut;
    TrayIcon? tray;
    bool busy,quitting;
    public MainWindow()
    {
        Title="FrameForge — Capture & explain";Width=1300;Height=850;MinWidth=1000;MinHeight=700;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brush.Parse("#F5F6FA");
        Icon=new WindowIcon(AssetLoader.Open(new Uri("avares://FrameForge.Desktop/Assets/FrameForge.ico")));
        var root=new DockPanel{LastChildFill=true,Margin=new Thickness(16)};
        var header=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),Margin=new Thickness(0,0,0,14)};
        var title=new StackPanel{Spacing=3};title.Children.Add(Label("FrameForge",25,true));title.Children.Add(Label("Capture. Explain. Share.",12));header.Children.Add(title);
        var topActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,VerticalAlignment=VerticalAlignment.Center};
        topActions.Children.Add(Button("Open…",()=>Run(Open)));topActions.Children.Add(Button("Paste",()=>Run(Paste)));topActions.Children.Add(Button("Copy image",()=>Run(Copy)));topActions.Children.Add(Button("Export…",()=>Run(Export)));
        Grid.SetColumn(topActions,1);header.Children.Add(topActions);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);

        var footer=new StackPanel{Spacing=8,Margin=new Thickness(0,12,0,0)};
        var statusRow=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto")};
        undoDelete=Button("Undo delete",()=>Run(UndoDelete));undoDelete.IsVisible=false;undoDelete.Margin=new Thickness(8,0);Grid.SetColumn(undoDelete,1);statusRow.Children.Add(undoDelete);
        statusRow.Children.Add(status);cancel=Button("Cancel operation",()=>operation?.Cancel());cancel.IsVisible=false;Grid.SetColumn(cancel,2);statusRow.Children.Add(cancel);footer.Children.Add(statusRow);
        var libraryHeader=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};
        libraryHeader.Children.Add(Label("Recent captures",12,true));Grid.SetColumn(search,1);libraryHeader.Children.Add(search);search.TextChanged+=(_,_)=>LoadRecent();footer.Children.Add(libraryHeader);
        footer.Children.Add(new ScrollViewer{Content=recent,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,0,18),Height=126});
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);

        var sidebar=new DockPanel{Width=218,Margin=new Thickness(0,0,16,0)};
        var settings=new StackPanel{Spacing=8,Margin=new Thickness(0,14,0,0)};
        settings.Children.Add(Button("Preferences & shortcut…",()=>Run(Preferences)));
        var openFolder=Button("Open capture folder",()=>Run(()=>{SaveCurrent();ProcessRunner.OpenFolder(AppPaths.Library);return Task.CompletedTask;}));
        ToolTip.SetTip(openFolder,"Open saved projects, screenshots, and recordings in your file manager.");
        var deletedFolder=new MenuItem{Header="Open deleted captures"};deletedFolder.Click+=(_,_)=>Run(()=>{Directory.CreateDirectory(library.DeletedFolder);ProcessRunner.OpenFolder(library.DeletedFolder);return Task.CompletedTask;});
        openFolder.ContextMenu=new ContextMenu{ItemsSource=new[]{deletedFolder}};settings.Children.Add(openFolder);
        settings.Children.Add(Button("Exit FrameForge",()=>Run(Exit)));
        DockPanel.SetDock(settings,Dock.Bottom);sidebar.Children.Add(settings);
        var actions=new StackPanel{Spacing=8};
        actions.Children.Add(Label("CAPTURE",11,true));
        actions.Children.Add(Button(OperatingSystem.IsLinux()?"Capture screen / region":"Capture region",()=>Run(()=>Capture(true))));
        actions.Children.Add(Button(OperatingSystem.IsLinux()?"Choose in desktop dialog":"Capture screen",()=>Run(()=>Capture(false))));
        if(OperatingSystem.IsMacOS())actions.Children.Add(Button("Capture window",()=>Run(CaptureWindow)));
        actions.Children.Add(Button("Scrolling capture…",()=>Run(OperatingSystem.IsWindows()?Scrolling:AutomaticScrolling)));
        actions.Children.Add(Button("Stitch image files…",()=>Run(StitchFiles)));
        actions.Children.Add(Label("RECORD & EXTRACT",11,true));
        actions.Children.Add(Button("Record screen",()=>Run(StartRecording)));
        var recordingActions=new Grid{ColumnDefinitions=new ColumnDefinitions("*,*")};
        pause=Button("Pause",()=>Run(PauseRecording));pause.IsVisible=false;pause.Margin=new Thickness(0,0,4,0);recordingActions.Children.Add(pause);
        stop=Button("Stop",()=>Run(StopRecording));stop.IsVisible=false;stop.Margin=new Thickness(4,0,0,0);Grid.SetColumn(stop,1);recordingActions.Children.Add(stop);actions.Children.Add(recordingActions);
        actions.Children.Add(Button("Trim video / export GIF…",()=>Run(VideoExport)));
        actions.Children.Add(Button("Extract text (OCR)",()=>Run(ExtractText)));
        actions.Children.Add(Button("Setup & permissions…",()=>Run(SetupHelp)));
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
        inspector.Children.Add(Button("Apply style to selected",()=>editor.ApplyStyle()));
        var selectionActions=new Grid{ColumnDefinitions=new ColumnDefinitions("*,*")};
        var duplicate=Button("Duplicate",()=>editor.DuplicateSelected());duplicate.Margin=new Thickness(0,0,4,0);selectionActions.Children.Add(duplicate);
        var deleteMark=Button("Delete",()=>editor.DeleteSelected());deleteMark.Margin=new Thickness(4,0,0,0);Grid.SetColumn(deleteMark,1);selectionActions.Children.Add(deleteMark);inspector.Children.Add(selectionActions);
        inspector.Children.Add(Button("Resize image…",()=>Run(Resize)));inspector.Children.Add(Button("Rotate clockwise",()=>Run(()=>{RequireDocument().Rotate();editor.Refresh();return Task.CompletedTask;})));
        inspector.Children.Add(Label("Select a mark to move or resize it. Double-click text to edit. Shift draws equal sides. Escape cancels.",12));
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
        zoom.PropertyChanged+=(_,e)=>{if(e.Property==RangeBase.ValueProperty){zoomHost.LayoutTransform=new ScaleTransform(zoom.Value,zoom.Value);editor.ViewZoom=zoom.Value;editor.InvalidateVisual();}};
        editor.Changed+=()=>{dimensions.Text=document==null?"":$"{document.Image.Width} × {document.Image.Height} px";};
        editor.EditText+=mark=>Run(async()=>{var text=await Prompt("Edit text","Text / callout",mark.Text);if(text!=null&&document!=null&&document.Marks.Contains(mark)){document.Checkpoint();mark.Text=text;editor.Refresh();}});
        autosave.Tick+=(_,_)=>{if(!busy&&!editor.IsDrawing&&document?.Dirty==true){try{SaveCurrent();}catch(Exception error){status.Text="Could not save the capture: "+error.Message;}}};autosave.Start();
        annotationText.TextChanged+=(_,_)=>editor.TextValue=annotationText.Text??"";
        color.TextChanged+=(_,_)=>{if(SKColor.TryParse(color.Text,out _))editor.Ink=color.Text!;};
        stroke.ValueChanged+=(_,_)=>editor.Stroke=(double)(stroke.Value??4);font.ValueChanged+=(_,_)=>editor.FontSize=(double)(font.Value??26);
        fill.IsCheckedChanged+=(_,_)=>editor.Filled=fill.IsChecked==true;
        Closing+=(_,e)=>{if(quitting)return;e.Cancel=true;if(busy){operation?.Cancel();status.Text="Canceling the current operation. Close again when it finishes.";return;}if(preferences.CloseToTray&&tray!=null){Run(()=>{SaveCurrent();Hide();return Task.CompletedTask;});}else Run(Exit);};
        Opened+=(_,_)=>{SetupTray();LoadRecent();if(preferences.ShortcutEnabled)Run(ApplyShortcut);if(Environment.GetCommandLineArgs().Contains("--background")&&tray!=null)Hide();};
        SingleInstance.Requested+=capture=>Dispatcher.UIThread.Post(()=>{DesktopIntegration.Show(this);if(capture)Run(()=>Capture(true));});
        KeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape&&busy){operation?.Cancel();e.Handled=true;}
            if(e.Source is TextBox||e.Source is NumericUpDown)return;
            bool command=e.KeyModifiers.HasFlag(OperatingSystem.IsMacOS()?KeyModifiers.Meta:KeyModifiers.Control);
            if(!command)return;
            if(e.Key==Key.Z&&!e.KeyModifiers.HasFlag(KeyModifiers.Shift)){document?.Undo();editor.Refresh();e.Handled=true;}
            if(e.Key==Key.Y||(e.Key==Key.Z&&e.KeyModifiers.HasFlag(KeyModifiers.Shift))){document?.Redo();editor.Refresh();e.Handled=true;}
            if(e.Key==Key.O){Run(Open);e.Handled=true;}
            if(e.Key==Key.V){Run(Paste);e.Handled=true;}
            if(e.Key==Key.E){Run(Export);e.Handled=true;}
            if(e.Key==Key.D){editor.DuplicateSelected();e.Handled=true;}
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
        catch(Exception e){status.Text=e.Message;DesktopIntegration.Show(this);}
        finally{operation.Dispose();operation=null;busy=false;cancel.IsVisible=false;}
    }
    CancellationToken Token=>operation?.Token??CancellationToken.None;
    CaptureDocument RequireDocument()=>document??throw new InvalidOperationException("Capture or open an image first.");
    void SaveCurrent()
    {
        if(document==null)return;
        if(projectPath==null)projectPath=AppPaths.NewCapture(".ffg");
        if(document.Dirty||!File.Exists(projectPath))
        {
            document.Save(projectPath);
            using var rendered=document.Render();AtomicFile.Write(System.IO.Path.ChangeExtension(projectPath,".png"),Imaging.Png(rendered));
        }
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
            DesktopIntegration.Show(this);await Copy();status.Text="Captured and copied to the clipboard.";
        }
        finally{CaptureTrace.Write("capture finished; editor restored");DesktopIntegration.Show(this);editor.Focus();}
    }
    async Task CaptureWindow()
    {
        if(recorder!=null)throw new InvalidOperationException("Stop recording before capturing a window.");
        string path=System.IO.Path.Combine(AppPaths.Temp,Guid.NewGuid().ToString("N")+".png");
        Hide();
        try
        {
            await Task.Delay(300,Token);var result=await ProcessRunner.Run("/usr/sbin/screencapture",new[]{"-x","-i","-w","-t","png",path},Token);
            if(!File.Exists(path)){if(result.ExitCode!=0&&!string.IsNullOrWhiteSpace(result.Error))throw new InvalidOperationException(result.Error.Trim());return;}
            using var image=Imaging.Load(path);SetDocument(new CaptureDocument(image){Title="Window capture"});
            DesktopIntegration.Show(this);await Copy();status.Text="Window captured and copied.";
        }
        finally{if(File.Exists(path))File.Delete(path);DesktopIntegration.Show(this);}
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
        recent.Children.Clear();foreach(var thumbnail in thumbs)thumbnail.Dispose();thumbs.Clear();
        var filter=search.Text?.Trim()??"";
        foreach(var file in new DirectoryInfo(AppPaths.Library).EnumerateFiles().Where(f=>f.Extension is ".ffg" or ".mp4" or ".webm").Where(f=>filter.Length==0||f.Name.Contains(filter,StringComparison.OrdinalIgnoreCase)||f.LastWriteTime.ToString("MMM d HH:mm").Contains(filter,StringComparison.OrdinalIgnoreCase)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(100))
        {
            string path=file.FullName;bool video=file.Extension is ".mp4" or ".webm";
            var card=new Grid{Width=154,Height=90};
            var content=new StackPanel{Spacing=5};
            var imagePath=System.IO.Path.ChangeExtension(path,".png");
            if(File.Exists(imagePath)){try{using var thumbnailStream=File.OpenRead(imagePath);var thumbnail=Bitmap.DecodeToWidth(thumbnailStream,144);thumbs.Add(thumbnail);content.Children.Add(new Image{Source=thumbnail,Height=48,Stretch=Stretch.Uniform});}catch{}}
            else content.Children.Add(Label(video?"▶ Recording":"Editable capture",12));
            content.Children.Add(Label(file.LastWriteTime.ToString("MMM d · HH:mm"),11));
            var open=Button("",()=>Run(()=>OpenRecent(path)));open.Content=content;open.Padding=new Thickness(8);ToolTip.SetTip(open,file.Name);
            Avalonia.Automation.AutomationProperties.SetName(open,"Open capture "+file.LastWriteTime.ToString("MMM d HH:mm"));card.Children.Add(open);
            var remove=Button("×",()=>Run(()=>DeleteCapture(path)));remove.HorizontalAlignment=HorizontalAlignment.Right;remove.VerticalAlignment=VerticalAlignment.Top;remove.Width=26;remove.MinHeight=26;remove.Height=26;remove.Padding=new Thickness(0);remove.Margin=new Thickness(2);
            ToolTip.SetTip(remove,"Delete capture · Undo available");Avalonia.Automation.AutomationProperties.SetName(remove,"Delete capture "+file.LastWriteTime.ToString("MMM d HH:mm"));card.Children.Add(remove);
            var openMenu=new MenuItem{Header="Open capture"};openMenu.Click+=(_,_)=>Run(()=>OpenRecent(path));
            var deleteMenu=new MenuItem{Header="Delete capture"};deleteMenu.Click+=(_,_)=>Run(()=>DeleteCapture(path));
            card.ContextMenu=new ContextMenu{ItemsSource=new[]{openMenu,deleteMenu}};recent.Children.Add(card);
        }
    }
    Task OpenRecent(string path)
    {
        if(System.IO.Path.GetExtension(path)==".ffg"){SaveCurrent();SetDocument(CaptureDocument.Load(path),path);}
        else ProcessRunner.OpenFile(path);
        return Task.CompletedTask;
    }
    Task DeleteCapture(string path)
    {
        if(recorder!=null&&CaptureLibrary.SamePath(path,recorder.Path))throw new InvalidOperationException("Stop this recording before deleting it.");
        bool open=projectPath!=null&&CaptureLibrary.SamePath(projectPath,path);
        if(open)SaveCurrent();
        var capture=library.Delete(path);
        if(open){document?.Dispose();document=null;projectPath=null;editor.SetDocument(null);}
        deleted.Push((capture,open));undoDelete.IsVisible=true;LoadRecent();status.Text="Capture moved to Deleted Captures. Undo delete restores it.";return Task.CompletedTask;
    }
    Task UndoDelete()
    {
        if(deleted.Count==0)return Task.CompletedTask;
        var item=deleted.Peek();library.Restore(item.Capture);deleted.Pop();undoDelete.IsVisible=deleted.Count>0;
        var path=System.IO.Path.Combine(AppPaths.Library,item.Capture.Names[0]);if(item.WasOpen&&document==null)SetDocument(CaptureDocument.Load(path),path);
        LoadRecent();status.Text="Capture restored.";return Task.CompletedTask;
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
        if(recorder!=null)return;
        if(OperatingSystem.IsWindows())
        {
            Hide();try{await Task.Delay(400,Token);recorder=await Recorder.Start(Token);}finally{DesktopIntegration.Show(this);}
        }
        else
        {
            var options=await RecordingOptions();if(options==null)return;
            Hide();try{await Task.Delay(350,Token);recorder=await NativeRecording.Start(options.Value.Source,options.Value.Audio,options.Value.Cursor,Token);}finally{DesktopIntegration.Show(this);}
        }
        stop.IsVisible=true;pause.IsVisible=!OperatingSystem.IsWindows();pause.Content="Pause";status.Text="Recording. Pause or Stop from here; your capture shortcut also stops recording.";
    }
    async Task PauseRecording()
    {
        if(recorder==null)return;
        if(recorder.Paused){await recorder.Resume();pause.Content="Pause";status.Text="Recording resumed.";}
        else{await recorder.Pause();pause.Content="Resume";status.Text="Recording paused.";}
    }
    async Task StopRecording()
    {
        if(recorder==null)return;
        var active=recorder;
        try{await active.Stop();status.Text="Recording saved: "+System.IO.Path.GetFileName(active.Path);}
        finally{active.Dispose();recorder=null;stop.IsVisible=pause.IsVisible=false;LoadRecent();DesktopIntegration.Show(this);}
    }
    async Task<(string Source,string Audio,bool Cursor)?> RecordingOptions()
    {
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(24)};
        var sources=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};
        if(OperatingSystem.IsMacOS())
        {
            var data=await NativeBridge.Run("sources",Array.Empty<string>(),Token);
            sources.ItemsSource=data.GetProperty("items").EnumerateArray().Select(e=>new CaptureSource(e.GetProperty("id").GetString()!,e.GetProperty("name").GetString()!)).ToArray();sources.SelectedIndex=0;
            panel.Children.Add(Label("Screen or window",12));panel.Children.Add(sources);
        }
        else panel.Children.Add(Label("Your desktop will ask which screen or window to share.",13));
        var audio=new ComboBox{ItemsSource=new[]{"No audio","Microphone","System audio","Microphone + system audio"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        panel.Children.Add(Label("Sound",12));panel.Children.Add(audio);
        var cursor=new CheckBox{Content="Include pointer",IsChecked=true};panel.Children.Add(cursor);
        var dialog=Dialog("Record screen",panel,620,390);(string,string,bool)? result=null;
        panel.Children.Add(Button("Start recording",()=>{result=((sources.SelectedItem as CaptureSource)?.Id??"",new[]{"none","microphone","system","both"}[audio.SelectedIndex],cursor.IsChecked==true);dialog.Close();}));
        panel.Children.Add(Button("Cancel",()=>dialog.Close()));await dialog.ShowDialog(this);return result;
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
        if(preferences.ShortcutEnabled){shortcut=await GlobalShortcut.Register(preferences,()=>{if(busy)operation?.Cancel();else Run(recorder==null?()=>Capture(true):StopRecording);},Token);status.Text="Capture shortcut: "+shortcut.Display;}
    }
    async Task Preferences()
    {
        var panel=new StackPanel{Spacing=12,Margin=new Thickness(22)};
        var enable=new CheckBox{Content="Enable global capture shortcut",IsChecked=preferences.ShortcutEnabled};
        var close=new CheckBox{Content="Keep running in the tray when closing",IsChecked=preferences.CloseToTray};
        var startup=new CheckBox{Content="Start FrameForge when I sign in",IsChecked=preferences.StartAtLogin};
        var ctrl=new CheckBox{Content="Ctrl",IsChecked=preferences.Control};var alt=new CheckBox{Content="Alt / Option",IsChecked=preferences.Alt};var shift=new CheckBox{Content="Shift",IsChecked=preferences.Shift};
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=14};row.Children.Add(ctrl);row.Children.Add(alt);row.Children.Add(shift);
        var keys=new ComboBox{ItemsSource=new[]{"Tilde"}.Concat(Enumerable.Range('A',26).Select(c=>((char)c).ToString())).ToArray(),SelectedItem=preferences.Key,HorizontalAlignment=HorizontalAlignment.Stretch};
        var language=new TextBox{Text=preferences.OcrLanguage,PlaceholderText="eng"};
        panel.Children.Add(enable);panel.Children.Add(row);panel.Children.Add(keys);panel.Children.Add(close);panel.Children.Add(startup);
        panel.Children.Add(Label("On Linux, your desktop may ask you to choose the shortcut. Some desktops need a tray extension; enable close-to-tray only when the FrameForge icon is visible.",12));
        panel.Children.Add(Label("OCR language (eng or auto on Mac; installed Tesseract code on Linux)",12));panel.Children.Add(language);
        var dialog=Dialog("Preferences",panel,570,590);DesktopPreferences? candidate=null;
        panel.Children.Add(Button("Save preferences",()=>{candidate=new(){ShortcutEnabled=enable.IsChecked==true,CloseToTray=close.IsChecked==true,StartAtLogin=startup.IsChecked==true,Control=ctrl.IsChecked==true,Alt=alt.IsChecked==true,Shift=shift.IsChecked==true,Key=keys.SelectedItem as string??"Tilde",OcrLanguage=language.Text??"eng"};dialog.Close();}));
        panel.Children.Add(Button("Cancel",()=>dialog.Close()));await dialog.ShowDialog(this);
        if(candidate!=null)
        {
            if(!candidate.Valid)throw new ArgumentException("Use Ctrl or Alt with your capture key.");
            var prior=preferences;preferences=candidate;
            try{await ApplyShortcut();if(prior.StartAtLogin!=candidate.StartAtLogin)DesktopIntegration.SetStartup(candidate.StartAtLogin);preferences.Save();}
            catch{preferences=prior;try{await ApplyShortcut();}catch{}throw;}
        }
    }
    void SetupTray()
    {
        try
        {
            var menu=new NativeMenu();var show=new NativeMenuItem("Open FrameForge");show.Click+=(_,_)=>{DesktopIntegration.Show(this);};menu.Items.Add(show);
            var capture=new NativeMenuItem("Capture region / stop recording");capture.Click+=(_,_)=>{if(busy)operation?.Cancel();else Run(recorder==null?()=>Capture(true):StopRecording);};menu.Items.Add(capture);
            var exit=new NativeMenuItem("Exit FrameForge");exit.Click+=(_,_)=>Run(Exit);menu.Items.Add(exit);
            tray=new TrayIcon{Icon=Icon,ToolTipText="FrameForge",Menu=menu,IsVisible=true};tray.Clicked+=(_,_)=>{DesktopIntegration.Show(this);};
            TrayIcon.SetIcons(Application.Current!,new TrayIcons{tray});
        }
        catch{tray=null;preferences.CloseToTray=false;}
    }
    async Task Exit()
    {
        if(recorder!=null)await StopRecording();SaveCurrent();quitting=true;autosave.Stop();SingleInstance.Dispose();
        shortcut?.Dispose();tray?.Dispose();document?.Dispose();editor.ReleaseResources();
        if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown();
    }
    Task SetupHelp()
    {
        string info=OperatingSystem.IsMacOS()
            ?"Screenshots, OCR, and recording use macOS services. Allow Screen Recording when asked; microphone recording also needs Microphone permission. Automatic scrolling needs Accessibility access. These choices live in System Settings → Privacy & Security. Video trimming and GIF export also run locally using macOS services."
            :"Install the Debian package with its dependencies for recording and OCR. Screen and window sharing use your desktop's permission dialog on both X11 and Wayland. Automatic scrolling additionally asks for pointer control for that session. Global shortcuts and tray icons depend on desktop support. Details are in the included README-FIRST guide.";
        return Message("Setup & permissions",info);
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
