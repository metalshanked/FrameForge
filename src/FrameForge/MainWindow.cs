using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

namespace FrameForge;
public sealed class MainWindow : Window
{
    public EditorSurface Editor { get; } = new();

    readonly TextBlock status = Ui.Label("Ready", 12), dimensions = Ui.Label("", 12), documentTitle = Ui.Label("Your next idea starts with a capture", 18, bold: true);
    readonly TextBlock shortcutHint = Ui.Label("", 12);
    ShortcutProfile shortcuts = ShortcutProfile.Load();
    bool configuringShortcuts;
    readonly StackPanel library = new()
    {
        Orientation = Orientation.Horizontal
    };
    readonly TextBox librarySearch = new()
    {
        Width = double.NaN,
        ToolTip = "Search captures by title",
        Text = "",
        VerticalContentAlignment = VerticalAlignment.Center
    };
    readonly ScrollViewer viewport = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Ui.Brush("#EDEFF5")
    };
    readonly Border imageBorder = new()
    {
        Background = Brushes.White,
        Margin = new Thickness(36),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    readonly StackPanel welcome = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        MaxWidth = 520
    };
    readonly ComboBox delay = Ui.Combo(new[] { "No delay", "3 seconds", "5 seconds", "10 seconds" });
    readonly CheckBox cursor = new()
    {
        Content = "Include cursor",
        IsChecked = false
    };
    readonly ComboBox stroke = Ui.Combo(new[] { "2", "4", "6", "10", "16" }, 1), font = Ui.Combo(new[] { "16", "20", "26", "32", "42", "56" }, 2);
    readonly TextBox note = new()
    {
        Text = "Add your note",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 105,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    readonly CheckBox filled = new()
    {
        Content = "Filled shape"
    };
    readonly TextBlock toolName = Ui.Label("Arrow", 22, bold: true);
    readonly Dictionary<Tool, Button> toolButtons = new();
    readonly List<(Button button, Func<bool> enabled)> commandStates = new();
    readonly DispatcherTimer autosave = new()
    {
        Interval = TimeSpan.FromSeconds(1.2)
    };
    Forms.NotifyIcon? tray;
    HwndSource? source;
    IntPtr handle;
    bool busy, loading;
    double zoom = 1;
    string? libraryProject;
    RecordingPanel? recordingPanel;
    ScrollSession? scrollSession;
    RegionPicker? regionPicker;
    CancellationTokenSource? captureCancellation;
    bool exitRequested, sessionEnding;
    AppPreferences preferences = AppPreferences.Load();
    System.Drawing.Icon? trayIcon;
    internal bool IsTrayVisible => tray?.Visible == true;
    public MainWindow()
    {
        Title = "FrameForge — Capture & explain";
        Icon = AppBrand.Image;
        Width = 1420;
        Height = 900;
        MinWidth = 1050;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        documentTitle.TextWrapping = TextWrapping.NoWrap;
        documentTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        documentTitle.VerticalAlignment = VerticalAlignment.Center;
        viewport.Style = (Style)FindResource("QuietScrollViewer");
        System.Windows.Automation.AutomationProperties.SetName(librarySearch, "Search recent captures");
        System.Windows.Automation.AutomationProperties.SetName(delay, "Capture delay");
        System.Windows.Automation.AutomationProperties.SetName(stroke, "Stroke width");
        System.Windows.Automation.AutomationProperties.SetName(font, "Text size");
        System.Windows.Automation.AutomationProperties.SetName(note, "Annotation text");
        delay.ToolTip = "Wait before starting a capture";
        var root = new Grid { Background = Background };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Content = root;
        var top = new DockPanel
        {
            Background = Brushes.White,
            Margin = new Thickness(0)
        };
        Grid.SetRow(top, 0);
        root.Children.Add(top);
        top.Children.Add(Header());
        var body = new Grid();
        body.ColumnDefinitions.Add(new() { Width = new GridLength(232) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(244) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        body.Children.Add(sidebar);
        var center = new Grid();
        center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        center.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        center.RowDefinitions.Add(new() { Height = new GridLength(168) });
        Grid.SetColumn(center, 1);
        body.Children.Add(center);
        var docbar = new DockPanel
        {
            Margin = new Thickness(16, 8, 16, 8)
        };
        center.Children.Add(docbar);
        var zoomPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(zoomPanel, Dock.Right);
        docbar.Children.Add(zoomPanel);
        zoomPanel.Children.Add(DocumentButton("−", () => SetZoom(zoom / 1.2), "Zoom out"));
        zoomPanel.Children.Add(DocumentButton("Fit", Fit, "Fit the image inside the editor"));
        zoomPanel.Children.Add(DocumentButton("1:1", () => SetZoom(1), "Show at 100% zoom"));
        zoomPanel.Children.Add(DocumentButton("+", () => SetZoom(zoom * 1.2), "Zoom in"));
        docbar.Children.Add(documentTitle);
        var stage = new Grid();
        Grid.SetRow(stage, 1);
        center.Children.Add(stage);
        stage.Children.Add(viewport);
        imageBorder.Child = Editor;
        viewport.Content = imageBorder;
        stage.Children.Add(welcome);
        BuildWelcome();
        var lib = new DockPanel
        {
            Background = Brushes.White,
            Margin = new Thickness(0, 1, 0, 0)
        };
        Grid.SetRow(lib, 2);
        center.Children.Add(lib);
        var libhead = new DockPanel
        {
            Margin = new Thickness(12, 3, 12, 0)
        };
        DockPanel.SetDock(libhead, Dock.Top);
        lib.Children.Add(libhead);
        var search = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        DockPanel.SetDock(search, Dock.Right);
        libhead.Children.Add(search);
        var searchBox = new Grid { Width = 185, VerticalAlignment = VerticalAlignment.Center };
        librarySearch.Padding = new Thickness(10, 8, 34, 8);
        searchBox.Children.Add(librarySearch);
        var placeholder = new TextBlock {
            Text = "Search captures…", Foreground = Ui.Brush("#737B8D"),
            Margin = new Thickness(15, 0, 34, 0), VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        searchBox.Children.Add(placeholder);
        var clearSearch = Ui.Button("×", () => { librarySearch.Clear(); librarySearch.Focus(); }, tooltip: "Clear capture search");
        clearSearch.MinHeight = 0;
        clearSearch.Width = 28;
        clearSearch.Height = 28;
        clearSearch.VerticalAlignment = VerticalAlignment.Center;
        clearSearch.Padding = new Thickness(0);
        clearSearch.Margin = new Thickness(0, 0, 8, 0);
        clearSearch.HorizontalAlignment = HorizontalAlignment.Right;
        clearSearch.Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(clearSearch, "Clear capture search");
        searchBox.Children.Add(clearSearch);
        search.Children.Add(searchBox);
        const string folderHelp = "Open saved screenshots, editable projects, and recordings in Windows File Explorer.";
        var captureFolderButton = Ui.Button("Open capture folder", OpenCaptureFolder, tooltip: folderHelp);
        captureFolderButton.ToolTipOpening += (_, _) => {
            try { captureFolderButton.ToolTip = folderHelp + "\n" + ShellPaths.ResolveExistingPath(Paths.Library); }
            catch { captureFolderButton.ToolTip = folderHelp; }
        };
        search.Children.Add(captureFolderButton);
        var libraryHeading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        libraryHeading.Children.Add(new TextBlock { Text = "RECENT CAPTURES", FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 3, 4, 0) });
        libraryHeading.Children.Add(new TextBlock { Text = "Saved on this PC", FontSize = 10, Foreground = Ui.Brush("#737B8D"), Margin = new Thickness(4, 1, 4, 3) });
        libhead.Children.Add(libraryHeading);
        lib.Children.Add(new ScrollViewer {
            Content = library, Style = (Style)FindResource("QuietScrollViewer"),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // Keep the hover scrollbar below the cards, with space reserved in both hover states.
            Padding = new Thickness(0, 0, 0, 16),
            PanningMode = PanningMode.HorizontalOnly
        });
        librarySearch.TextChanged += (_, _) => {
            bool empty = librarySearch.Text.Length == 0;
            placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            clearSearch.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            RefreshLibrary();
        };
        var inspector = BuildInspector();
        Grid.SetColumn(inspector, 2);
        body.Children.Add(inspector);
        var bottom = new DockPanel
        {
            Background = Brushes.White,
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetRow(bottom, 2);
        root.Children.Add(bottom);
        DockPanel.SetDock(dimensions, Dock.Right);
        bottom.Children.Add(dimensions);
        bottom.Children.Add(status);
        Editor.Changed += () =>
        {
            RefreshCommandStates();
            dimensions.Text = Editor.Document is { } d ? $"{d.Image.PixelWidth:N0} × {d.Image.PixelHeight:N0} px  ·  {zoom:P0}" : "";
            if (!loading && Editor.Document?.Dirty == true)
            {
                autosave.Stop();
                autosave.Start();
            }
        };
        Editor.SelectionChanged += m =>
        {
            RefreshCommandStates();
            if (m == null) return;
            Editor.Ink = m.Color;
            var width = m.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var size = m.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!stroke.Items.Contains(width))
                stroke.Items.Add(width);
            if (!font.Items.Contains(size))
                font.Items.Add(size);
            stroke.SelectedItem = width;
            font.SelectedItem = size;
            filled.IsChecked = m.Filled;
            if (m.Kind is Tool.Text or Tool.Callout)
                note.Text = m.Text;
        };
        autosave.Tick += (_, _) =>
        {
            autosave.Stop();
            Persist();
        };
        PreviewKeyDown += KeyHandler;
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop)is string[] files && files.Length > 0)
                OpenPath(files[0]);
        };
        SourceInitialized += (_, _) => SetupHotkeys();
        Loaded += (_, _) => RefreshLibrary();
        Closing += (_, e) =>
        {
            if (sessionEnding) { Persist(); return; }
            if (!exitRequested && preferences.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            exitRequested = true;
            if (busy)
            {
                e.Cancel = true;
                exitRequested = true;
                captureCancellation?.Cancel();
                regionPicker?.Cancel();
                scrollSession?.Close();
                if (recordingPanel != null) _ = recordingPanel.Stop();
                status.Text = "Cancelling capture and closing…";
                return;
            }

            Persist();
        };
        Closed += (_, _) =>
        {
            autosave.Stop();
            regionPicker?.Dispose();
            for (int i = 1; i <= 6; i++)
                NativeCapture.UnregisterHotKey(handle, i);
            source?.RemoveHook(Hook);
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            trayIcon?.Dispose();
        };
        SelectTool(Tool.Arrow);
        RefreshCommandStates();
    }

    private UIElement Header()
    {
        var grid = new Grid
        {
            Margin = new Thickness(18, 12, 18, 12)
        };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        brand.Children.Add(new Image { Source = AppBrand.Image, Width = 40, Height = 40 });
        brand.Children.Add(Ui.Label("FrameForge", 22, "#252737", true));
        grid.Children.Add(brand);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        actions.Children.Add(Ui.Button("Open", Open, tooltip: "Open an image, editable .ffg project, or video · Ctrl+O"));
        actions.Children.Add(Ui.Button("Paste", Paste, tooltip: "Open an image from the clipboard · Ctrl+V"));
        actions.Children.Add(DocumentButton("Save project", SaveProject, "Save an editable .ffg copy with original pixels and annotation layers · Ctrl+S"));
        actions.Children.Add(DocumentButton("Copy image", Copy, "Copy the image with its current annotations · Ctrl+C"));
        actions.Children.Add(DocumentButton("Export image", Export, "Save a flattened PNG, JPEG, BMP, or TIFF for sharing · Ctrl+E", true));
        return grid;
    }

    private UIElement BuildSidebar()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(12)
        };
        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Style = (Style)FindResource("QuietScrollViewer"),
            Background = Brushes.White
        };
        panel.Children.Add(Ui.Label("CAPTURE", 10, bold: true));
        panel.Children.Add(Ui.Button("＋  Capture region", async () => await Capture("region"), true));
        var row = new UniformGrid2();
        row.Children.Add(Ui.Button("Window", async () => await Capture("window"), tooltip: "Choose a visible window to capture"));
        row.Children.Add(Ui.Button("All screens", async () => await Capture("screen"), tooltip: "Capture the entire desktop across all connected monitors"));
        panel.Children.Add(row);
        panel.Children.Add(Ui.Button("↕  Scrolling capture", async () => await Capture("scroll")));
        panel.Children.Add(Ui.Button("●  Record screen", async () => await Capture("video")));
        panel.Children.Add(delay);
        panel.Children.Add(cursor);
        panel.Children.Add(new Separator());
        panel.Children.Add(Ui.Label("ANNOTATE", 10, bold: true));
        var tools = new System.Windows.Controls.Primitives.UniformGrid
        {
            Columns = 2
        };
        var labels = new Dictionary<Tool, string>
        {
            {
                Tool.Select,
                "↖  Select"
            },
            {
                Tool.Arrow,
                "↗  Arrow"
            },
            {
                Tool.Rectangle,
                "□  Box"
            },
            {
                Tool.Ellipse,
                "○  Ellipse"
            },
            {
                Tool.Line,
                "╱  Line"
            },
            {
                Tool.Pen,
                "✎  Pen"
            },
            {
                Tool.Text,
                "T  Text"
            },
            {
                Tool.Callout,
                "▱  Callout"
            },
            {
                Tool.Highlight,
                "▰  Highlight"
            },
            {
                Tool.Step,
                "①  Step"
            },
            {
                Tool.Blur,
                "◌  Blur"
            },
            {
                Tool.Pixelate,
                "▦  Pixelate"
            },
            {
                Tool.Redact,
                "■  Redact"
            },
            {
                Tool.Crop,
                "⌗  Crop"
            }
        };
        foreach (var(tool, label)in labels)
        {
            var b = Ui.Button(label, () => SelectTool(tool));
            b.Padding = new Thickness(8);
            b.FontSize = 12;
            var content = new Grid();
            content.ColumnDefinitions.Add(new() { Width = new GridLength(20) });
            content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            int separator = label.IndexOf("  ", StringComparison.Ordinal);
            content.Children.Add(new TextBlock { Text = label[..separator], TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            var caption = new TextBlock { Text = label[(separator + 2)..], Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(caption, 1);
            content.Children.Add(caption);
            b.Content = content;
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(b, label);
            b.ToolTip = tool.ToString();
            toolButtons[tool] = b;
            tools.Children.Add(b);
        }

        panel.Children.Add(tools);
        panel.Children.Add(new Separator());
        panel.Children.Add(DocumentButton("Extract text · OCR", async () => await Ocr(), "Recognize text in the current image using Windows OCR"));
        var sidebar = new DockPanel { Background = Brushes.White };
        var footer = new StackPanel { Margin = new Thickness(12, 8, 12, 12) };
        var settings = new UniformGrid2();
        settings.Children.Add(Ui.Button("Shortcuts…", ShowShortcuts, tooltip: "Choose your global capture keyboard shortcuts"));
        settings.Children.Add(Ui.Button("Preferences…", ShowPreferences, tooltip: "Tray behavior, startup at sign-in, and licenses"));
        var utilities = new UniformGrid2();
        utilities.Children.Add(Ui.Button("Help", Help, tooltip: "Capture and editor keyboard reference"));
        utilities.Children.Add(Ui.Button("Hide to tray", HideToTray, tooltip: "Keep capture shortcuts active and hide the editor"));
        foreach (var button in settings.Children.Cast<Button>().Concat(utilities.Children.Cast<Button>())) {
            button.FontSize = 12;
            button.Padding = new Thickness(8);
        }
        footer.Children.Add(settings);
        footer.Children.Add(utilities);
        var exit = Ui.Button("Exit FrameForge", RequestExit, tooltip: "Save the current image and quit completely; capture shortcuts will stop");
        exit.Padding = new Thickness(8);
        footer.Children.Add(exit);
        var footerBorder = new Border { Child = footer, BorderBrush = Ui.Brush("#EEF0F5"), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        sidebar.Children.Add(footerBorder);
        sidebar.Children.Add(scroll);
        return sidebar;
    }

    private UIElement BuildInspector()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(12)
        };
        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Style = (Style)FindResource("QuietScrollViewer"),
            Background = Brushes.White
        };
        panel.Children.Add(Ui.Label("TOOL PROPERTIES", 10, bold: true));
        panel.Children.Add(toolName);
        panel.Children.Add(Ui.Label("Color", 12, bold: true));
        var colors = new System.Windows.Controls.Primitives.UniformGrid
        {
            Columns = 6
        };
        string[] colorNames = { "Purple", "Coral", "Orange", "Yellow", "Green", "Blue", "Ink", "White", "Violet", "Teal", "Brown", "Gray" };
        int colorIndex = 0;
        foreach (var hex in new[]
        {
            "#FF6757EF",
            "#FFF25464",
            "#FFFFAA39",
            "#FFFFD449",
            "#FF23AD83",
            "#FF298DEF",
            "#FF222637",
            "#FFFFFFFF",
            "#FF9B59B6",
            "#FF16B9BE",
            "#FF854B32",
            "#FF94A1B2"
        }

        )
        {
            var b = Ui.Button("", () =>
            {
                Editor.Ink = hex;
                status.Text = "Color selected. Apply style to update a selected annotation.";
            });
            b.Background = Ui.Brush(hex);
            b.MinHeight = 0;
            b.Width = 28;
            b.Height = 28;
            b.Padding = new Thickness(0);
            string colorName = colorNames[colorIndex++];
            b.ToolTip = colorName + " · " + hex;
            System.Windows.Automation.AutomationProperties.SetName(b, colorName);
            colors.Children.Add(b);
        }

        panel.Children.Add(colors);
        panel.Children.Add(Ui.Button("Custom color…", () =>
        {
            using var d = new Forms.ColorDialog
            {
                FullOpen = true
            };
            if (d.ShowDialog() == Forms.DialogResult.OK)
                Editor.Ink = $"#FF{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}";
        }));
        panel.Children.Add(Ui.Label("Stroke width", 12, bold: true));
        panel.Children.Add(stroke);
        stroke.SelectionChanged += (_, _) => Editor.StrokeWidth = double.Parse(stroke.SelectedItem?.ToString() ?? "4");
        panel.Children.Add(filled);
        filled.Checked += (_, _) => Editor.Filled = true;
        filled.Unchecked += (_, _) => Editor.Filled = false;
        panel.Children.Add(Ui.Label("Text size", 12, bold: true));
        panel.Children.Add(font);
        font.SelectionChanged += (_, _) => Editor.TextSize = double.Parse(font.SelectedItem?.ToString() ?? "26");
        panel.Children.Add(Ui.Label("Annotation text", 12, bold: true));
        panel.Children.Add(note);
        note.TextChanged += (_, _) => Editor.TextValue = note.Text;
        panel.Children.Add(DocumentButton("Apply style to selected", () => Editor.ApplyStyle(), "Update the selected annotation with these properties", enabled: HasSelection));
        var edit = new UniformGrid2();
        edit.Children.Add(DocumentButton("Duplicate", () => Editor.DuplicateSelection(), "Duplicate the selected annotation · Ctrl+D", enabled: HasSelection));
        edit.Children.Add(DocumentButton("Delete", () => Editor.DeleteSelection(), "Delete the selected annotation · Delete", enabled: HasSelection));
        panel.Children.Add(edit);
        panel.Children.Add(new Separator());
        var history = new UniformGrid2();
        history.Children.Add(DocumentButton("↶ Undo", Undo, "Undo the last image edit · Ctrl+Z", enabled: () => Editor.Document?.CanUndo == true));
        history.Children.Add(DocumentButton("↷ Redo", Redo, "Redo the last undone image edit · Ctrl+Y", enabled: () => Editor.Document?.CanRedo == true));
        panel.Children.Add(history);
        panel.Children.Add(DocumentButton("Resize image…", ResizeImage, "Change image dimensions in pixels; annotations are flattened"));
        panel.Children.Add(DocumentButton("Rotate 90°", Rotate, "Rotate the image clockwise; annotations are flattened"));
        panel.Children.Add(DocumentButton("Print image…", Print, "Print the image with its annotations"));
        panel.Children.Add(Ui.Label("Drag to draw. Use Select to move or resize. Double-click text to edit. Hold Shift for square shapes.", 11));
        return scroll;
    }

    private void BuildWelcome()
    {
        welcome.Children.Add(Ui.Label("MAKE YOUR POINT.", 12, "#6558F5", true));
        welcome.Children.Add(Ui.Label("Capture something.\nMake it clear.", 40, "#252737", true));
        welcome.Children.Add(Ui.Label("Grab a region, a window, or an entire screen. Add the context that turns a screenshot into an explanation.", 16));
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        row.Children.Add(Ui.Button("Capture a region", async () => await Capture("region"), true));
        row.Children.Add(Ui.Button("Try the editor", () => LoadDocument(new CaptureDocument(DemoImage()) { Title = "Welcome to FrameForge" })));
        welcome.Children.Add(row);
        shortcutHint.Text = shortcuts.Bindings[1].Display + " to capture · Ctrl+V to paste · Drop an image to open";
        welcome.Children.Add(shortcutHint);
    }

    public static BitmapSource DemoImage()
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(Ui.Brush("#FCFCFF"), null, new Rect(0, 0, 1000, 600));
            dc.DrawRoundedRectangle(Ui.Brush("#EBE8FF"), null, new Rect(60, 55, 880, 490), 22, 22);
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(105, 110, 790, 380), 18, 18);
            void Text(string text, double x, double y, double size, Brush color)
            {
                dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, 1), new Point(x, y));
            }

            Text("FRAMEFORGE  /  SAMPLE IMAGE", 150, 150, 16, Ui.Brush("#6558F5"));
            Text("Capture. Explain. Share.", 150, 215, 42, Ui.Brush("#252737"));
            Text("Draw an arrow. Highlight what matters.", 150, 292, 23, Ui.Brush("#697086"));
            Text("Every good explanation starts with a clear picture.", 150, 336, 22, Ui.Brush("#697086"));
            dc.DrawRoundedRectangle(Ui.Brush("#6558F5"), null, new Rect(150, 404, 220, 44), 8, 8);
            Text("Try your first annotation", 168, 413, 17, Brushes.White);
        }

        var b = new RenderTargetBitmap(1000, 600, 96, 96, PixelFormats.Pbgra32);
        b.Render(v);
        b.Freeze();
        return b;
    }

    public void LoadDocument(CaptureDocument document)
    {
        Persist();
        loading = true;
        Editor.SetDocument(document);
        documentTitle.Text = document.Title;
        documentTitle.ToolTip = document.Title;
        welcome.Visibility = Visibility.Collapsed;
        libraryProject = document.ProjectPath != null && Path.GetFullPath(document.ProjectPath).StartsWith(Path.GetFullPath(Paths.Library) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? document.ProjectPath : Path.Combine(Paths.Library, Paths.Unique(".ffg"));
        document.Dirty = true;
        loading = false;
        Persist();
        Dispatcher.BeginInvoke(Fit, DispatcherPriority.Loaded);
    }

    public void OpenPath(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".mp4" or ".mkv" or ".mov" or ".webm")
            {
                new VideoWindow(this, path).Show();
                return;
            }

            LoadDocument(ext == ".ffg" ? CaptureDocument.LoadProject(path) : new CaptureDocument(Imaging.Load(path)) { Title = Path.GetFileNameWithoutExtension(path) });
            status.Text = "Opened " + Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private void Persist()
    {
        if (Editor.Document is not { } doc || !doc.Dirty || libraryProject == null)
            return;
        try
        {
            Directory.CreateDirectory(Paths.Library);
            var external = doc.ProjectPath;
            doc.SaveProject(libraryProject);
            doc.ProjectPath = external ?? libraryProject;
            Imaging.Save(doc.Render(), Path.ChangeExtension(libraryProject, ".png"));
            RefreshLibrary();
            status.Text = "Saved to your local capture library";
        }
        catch (Exception ex)
        {
            status.Text = "Autosave failed: " + ex.Message;
        }
    }

    private Button DocumentButton(string text, Action action, string tooltip, bool accent = false, Func<bool>? enabled = null)
    {
        var button = Ui.Button(text, action, accent, tooltip);
        commandStates.Add((button, enabled ?? (() => Editor.Document != null)));
        return button;
    }

    private bool HasSelection() => Editor.Selected is { } selected && Editor.Document?.Marks.Contains(selected) == true;

    private void RefreshCommandStates()
    {
        foreach (var (button, enabled) in commandStates) button.IsEnabled = enabled();
    }

    private void OpenCaptureFolder()
    {
        try {
            Persist();
            string folder = ShellPaths.OpenFolder(Paths.Library);
            status.Text = "Capture folder: " + folder;
        }
        catch (Exception ex) { Error(ex); }
    }

    private void RefreshLibrary()
    {
        Directory.CreateDirectory(Paths.Library);
        library.Children.Clear();
        var files = Directory.EnumerateFiles(Paths.Library).Where(p => Path.GetExtension(p)is ".ffg" or ".mp4").OrderByDescending(File.GetLastWriteTimeUtc).Take(100).ToArray();
        foreach (var path in files)
        {
            string title = Path.GetFileNameWithoutExtension(path);
            if (path.EndsWith(".ffg"))
            {
                try
                {
                    title = ReadLibraryTitle(path) ?? title;
                }
                catch
                {
                }
            }

            if (!title.Contains(librarySearch.Text, StringComparison.OrdinalIgnoreCase))
                continue;
            var content = new StackPanel
            {
                Width = 138
            };
            var thumb = Path.ChangeExtension(path, ".png");
            if (File.Exists(thumb))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 140;
                bmp.UriSource = new Uri(thumb);
                bmp.EndInit();
                content.Children.Add(new Image { Source = bmp, Height = 60, Stretch = Stretch.Uniform });
            }
            else
                content.Children.Add(Ui.Label("▶  SCREEN RECORDING", 10, "#6558F5", true));
            content.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Margin = new Thickness(2, 4, 2, 0) });
            var button = Ui.Button("", () => OpenPath(path));
            button.Content = content;
            button.ToolTip = title + "\n" + File.GetLastWriteTime(path).ToString("g");
            button.Padding = new Thickness(6);
            library.Children.Add(button);
        }

        if (library.Children.Count == 0) {
            var empty = Ui.Label(librarySearch.Text.Length > 0
                ? "No captures match your search. Clear the search to see recent captures."
                : "Your screenshots and recordings are saved here automatically.", 13);
            empty.MaxWidth = 420;
            empty.Margin = new Thickness(16, 14, 16, 6);
            library.Children.Add(empty);
        }
    }

    private static string? ReadLibraryTitle(string path)
    {
        // Title precedes the image payload in .ffg files. Avoid reading large base64 images just to list captures.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var prefix = new byte[8192];
        int length = stream.Read(prefix, 0, prefix.Length);
        var reader = new System.Text.Json.Utf8JsonReader(prefix.AsSpan(0, length), false, default);
        while (reader.Read())
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.PropertyName && reader.ValueTextEquals("Title") && reader.Read())
                return reader.GetString();
        }

        return null;
    }

    private void SelectTool(Tool tool)
    {
        Editor.Tool = tool;
        toolName.Text = tool.ToString();
        foreach (var(t, b)in toolButtons)
        {
            b.Background = Ui.Brush(t == tool ? "#EBE8FF" : "#F8F9FC");
            b.Foreground = Ui.Brush(t == tool ? "#6558F5" : "#36384C");
            b.BorderBrush = Ui.Brush(t == tool ? "#6558F5" : "#E4E7EF");
            b.FontWeight = t == tool ? FontWeights.SemiBold : FontWeights.Normal;
        }

        Editor.Cursor = tool == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        status.Text = tool switch
        {
            Tool.Crop => "Drag over the area to keep. Cropping flattens annotations; Undo restores them.",
            Tool.Redact => "Drag to cover sensitive content with an opaque black box. Export an image to share.",
            Tool.Blur => "Blur is a visual effect. Use Redact for sensitive information.",
            Tool.Text or Tool.Callout => "Enter your annotation text in the right panel, then click or drag on the image.",
            _ => $"{tool} selected"};
    }

    private void SetZoom(double value)
    {
        zoom = Math.Clamp(value, .05, 4);
        Editor.LayoutTransform = new ScaleTransform(zoom, zoom);
        Editor.Refresh();
    }

    private void Fit()
    {
        if (Editor.Document == null)
            return;
        SetZoom(Math.Min(1, Math.Min(Math.Max(100, viewport.ActualWidth - 90) / Editor.Width, Math.Max(100, viewport.ActualHeight - 90) / Editor.Height)));
    }

    private bool HasDocument()
    {
        if (Editor.Document != null)
            return true;
        status.Text = "Capture, open, or paste an image first.";
        return false;
    }

    private void Open()
    {
        var d = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Images, projects, and video|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ffg;*.mp4;*.mkv;*.mov;*.webm|All files|*.*"
        };
        if (d.ShowDialog(this) == true)
            OpenPath(d.FileName);
    }

    private void Paste()
    {
        try
        {
            var image = ClipboardService.GetImage(handle);
            if (image != null)
                LoadDocument(new CaptureDocument(image) { Title = "Pasted image" });
            else if (System.Windows.Clipboard.ContainsFileDropList() && System.Windows.Clipboard.GetFileDropList().Count > 0)
                OpenPath(System.Windows.Clipboard.GetFileDropList()[0]!);
            else
                status.Text = "The clipboard does not contain an image.";
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private void Copy()
    {
        if (!HasDocument())
            return;
        try
        {
            ClipboardService.SetImage(Editor.Document!.Render(), handle);
            status.Text = "Image copied to clipboard";
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private void Export()
    {
        if (!HasDocument())
            return;
        var d = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap image|*.bmp|TIFF image|*.tiff",
            FileName = SafeName(Editor.Document!.Title) + ".png"
        };
        if (d.ShowDialog(this) == true)
        {
            try
            {
                Imaging.Save(Editor.Document.Render(), d.FileName);
                status.Text = "Exported " + Path.GetFileName(d.FileName);
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }
    }

    private void SaveProject()
    {
        if (!HasDocument())
            return;
        var d = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "FrameForge editable project|*.ffg",
            FileName = SafeName(Editor.Document!.Title) + ".ffg"
        };
        if (d.ShowDialog(this) == true)
        {
            try
            {
                Persist();
                Editor.Document.SaveProject(d.FileName);
                status.Text = "Editable project saved";
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }
    }

    private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    private void Undo()
    {
        Editor.Document?.Undo();
        Editor.SetDocument(Editor.Document);
    }

    private void Redo()
    {
        Editor.Document?.Redo();
        Editor.SetDocument(Editor.Document);
    }

    private void ResizeImage()
    {
        if (!HasDocument())
            return;
        var doc = Editor.Document!;
        var size = Dialogs.Size(this, doc.Image.PixelWidth, doc.Image.PixelHeight);
        if (size.HasValue)
        {
            doc.Replace(Imaging.Resize(doc.Render(), size.Value.width, size.Value.height));
            Editor.SetDocument(doc);
            Fit();
        }
    }

    private void Rotate()
    {
        if (!HasDocument())
            return;
        Editor.Document!.Replace(new TransformedBitmap(Editor.Document.Render(), new RotateTransform(90)));
        Editor.SetDocument(Editor.Document);
        Fit();
    }

    private void Print()
    {
        if (!HasDocument())
            return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
        {
            var image = new Image
            {
                Source = Editor.Document!.Render(),
                Stretch = Stretch.Uniform,
                Width = dialog.PrintableAreaWidth,
                Height = dialog.PrintableAreaHeight
            };
            image.Measure(new Size(image.Width, image.Height));
            image.Arrange(new Rect(0, 0, image.Width, image.Height));
            dialog.PrintVisual(image, Editor.Document.Title);
        }
    }

    private async System.Threading.Tasks.Task Ocr()
    {
        if (!HasDocument())
            return;
        try
        {
            status.Text = "Recognizing text with Windows OCR…";
            var text = await OcrService.RecognizeAsync(Editor.Document!.Render());
            status.Text = string.IsNullOrWhiteSpace(text) ? "No text recognized in this image." : "Text recognition complete";
            if (!string.IsNullOrWhiteSpace(text))
                Dialogs.TextResult(this, text);
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    public async System.Threading.Tasks.Task Capture(string mode)
    {
        if (busy)
            return;
        busy = true;
        captureCancellation = new CancellationTokenSource();
        var token = captureCancellation.Token;
        Persist();
        try
        {
            NativeCapture.WindowInfo? window = null;
            RecordingOptions? opts = null;
            if (mode == "window")
            {
                window = Dialogs.Pick(this, "Choose a window to capture", NativeCapture.Windows(handle));
                if (window == null)
                    return;
            }

            if (mode == "video")
            {
                _ = Ffmpeg.Executable;
                opts = await VideoWindow.Options(this);
                if (opts == null)
                    return;
            }

            if (mode == "scroll")
                MessageBox.Show(this, "Select only the scrolling content. Leave fixed headers, footers, and scrollbars outside the region. You can add frames manually or let FrameForge scroll vertically.", "Scrolling capture", MessageBoxButton.OK, MessageBoxImage.Information);
            Hide();
            await Task.Delay(250, token);
            if (window != null)
                NativeCapture.SetForegroundWindow(window.Handle);
            int seconds = delay.SelectedIndex switch
            {
                1 => 3,
                2 => 5,
                3 => 10,
                _ => 0
            };
            if (seconds > 0)
                await Countdown(seconds, captureCancellation);
            System.Drawing.Rectangle? area;
            if (mode == "screen") area = NativeCapture.Desktop;
            else if (window != null) area = NativeCapture.Bounds(window.Handle);
            else
            {
                regionPicker = new RegionPicker();
                area = await regionPicker.PickAsync(token);
                regionPicker.Dispose();
                regionPicker = null;
            }
            if (area == null)
                return;
            await Task.Delay(150, token);
            if (mode == "video")
            {
                await Countdown(3, captureCancellation);
                var recorder = new Recorder(area.Value, opts!);
                recordingPanel = new RecordingPanel(recorder);
                recordingPanel.ShowDialog();
                var output = recordingPanel.Output;
                recordingPanel = null;
                if (output != null)
                {
                    Show();
                    new VideoWindow(this, output).Show();
                    status.Text = "Recording saved to your library";
                    RefreshLibrary();
                }
            }
            else if (mode == "scroll")
            {
                scrollSession = new ScrollSession(area.Value);
                scrollSession.ShowDialog();
                var result = scrollSession.Result;
                scrollSession = null;
                if (result != null)
                    OpenCapture(result, "Scrolling capture " + DateTime.Now.ToString("HH.mm.ss"));
            }
            else
            {
                var image = NativeCapture.Grab(area.Value, cursor.IsChecked == true);
                OpenCapture(image, (window?.Title ?? (mode == "screen" ? "Screen capture" : "Region capture")) + " " + DateTime.Now.ToString("HH.mm.ss"));
            }
        }
        catch (OperationCanceledException)
        {
            status.Text = "Capture cancelled";
        }
        catch (Exception ex)
        {
            regionPicker?.Cancel();
            Show();
            Error(ex);
        }
        finally
        {
            regionPicker?.Dispose();
            regionPicker = null;
            captureCancellation.Dispose();
            captureCancellation = null;
            busy = false;
            if (exitRequested) Close();
            else
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Fit();
            }
        }
    }

    internal void OpenCapture(BitmapSource image, string title)
    {
        LoadDocument(new CaptureDocument(image) { Title = title });
        try
        {
            ClipboardService.SetImage(image, handle);
            status.Text = "Capture copied to clipboard · Ctrl+V to paste";
        }
        catch (Exception ex)
        {
            // Keep the captured image available even when another app owns the clipboard.
            status.Text = "Capture opened, but auto-copy failed. Use Copy image to retry. " + ex.Message;
        }
    }

    private static async Task Countdown(int seconds, CancellationTokenSource cancellation)
    {
        var w = Dialogs.Shell(null, "Capture countdown", 360, 200);
        w.Topmost = true;
        w.ResizeMode = ResizeMode.NoResize;
        var text = Ui.Label("", 28, "#6558F5", true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(text);
        panel.Children.Add(Ui.Button("Cancel capture · Esc", cancellation.Cancel));
        w.Content = panel;
        bool completed = false;
        w.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; cancellation.Cancel(); } };
        w.Closing += (_, _) => { if (!completed) cancellation.Cancel(); };
        w.Show();
        NativeCapture.Exclude(w);
        try
        {
            for (int i = seconds; i > 0; i--)
            {
                text.Text = $"Capturing in {i}";
                await Task.Delay(1000, cancellation.Token);
            }
        }
        finally
        {
            completed = true;
            w.Close();
        }

        await Task.Delay(150, cancellation.Token);
    }

    private void SetupHotkeys()
    {
        handle = new WindowInteropHelper(this).Handle;
        source = HwndSource.FromHwnd(handle);
        source.AddHook(Hook);
        string? shortcutError = RegisterShortcuts(shortcuts);
        status.Text = shortcutError ?? ("Ready · " + shortcuts.Bindings[1].Display + " captures a region");
        trayIcon = AppBrand.TrayIcon();
        tray = new Forms.NotifyIcon
        {
            Text = "FrameForge · " + shortcuts.Bindings[1].Display,
            Icon = trayIcon,
            Visible = true
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open editor", null, (_, _) => Dispatcher.Invoke(RestoreEditor));
        menu.Items.Add("Capture region", null, async (_, _) => await Dispatcher.InvokeAsync(async () => await Capture("region")));
        menu.Items.Add("Keyboard shortcuts…", null, (_, _) => Dispatcher.Invoke(() => { RestoreEditor(); ShowShortcuts(); }));
        menu.Items.Add("Preferences…", null, (_, _) => Dispatcher.Invoke(() => { RestoreEditor(); ShowPreferences(); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit FrameForge", null, (_, _) => Dispatcher.Invoke(RequestExit));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreEditor);
    }

    public void RestoreEditor()
    {
        if (recordingPanel != null) { recordingPanel.Activate(); return; }
        if (scrollSession != null) { scrollSession.Activate(); return; }
        regionPicker?.Cancel();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        var dialog = OwnedWindows.Cast<Window>().FirstOrDefault(window => window.IsVisible);
        if (dialog != null) dialog.Activate();
    }

    public void StartInTray()
    {
        new WindowInteropHelper(this).EnsureHandle();
        Hide();
    }

    public void HideToTray()
    {
        Persist();
        Hide();
        if (!preferences.TrayHintShown && tray != null)
        {
            preferences.TrayHintShown = true;
            try { preferences.Save(); } catch { }
            tray.ShowBalloonTip(3500, "FrameForge is still running",
                shortcuts.Bindings[1].Display + " captures a region. Double-click the tray icon to reopen. Right-click it and choose Exit FrameForge to quit.", Forms.ToolTipIcon.Info);
        }
    }

    public void RequestExit()
    {
        exitRequested = true;
        Close();
    }

    public void PrepareForSessionEnd() => sessionEnding = true;

    private void ShowPreferences()
    {
        if (busy || configuringShortcuts) return;
        new PreferencesWindow(this, preferences, saved => {
            preferences = saved;
            status.Text = "Preferences saved";
        }).ShowDialog();
    }

    private void UnregisterShortcuts()
    {
        foreach (int id in ShortcutProfile.Actions.Keys) NativeCapture.UnregisterHotKey(handle, id);
    }

    private string? RegisterShortcuts(ShortcutProfile profile)
    {
        var failed = new List<string>();
        foreach (var pair in profile.Bindings)
            if (!NativeCapture.RegisterHotKey(handle, pair.Key, pair.Value.Modifiers | 0x4000, pair.Value.VirtualKey))
                failed.Add(pair.Value.Display + " (" + ShortcutProfile.Actions[pair.Key] + ")");
        return failed.Count == 0 ? null : "Already in use or unavailable: " + string.Join(", ", failed) + ". Choose another combination in Keyboard shortcuts.";
    }

    private void ShowShortcuts()
    {
        if (busy || configuringShortcuts) return;
        configuringShortcuts = true;
        UnregisterShortcuts();
        bool saved = false;
        try
        {
            var dialog = new ShortcutsWindow(this, shortcuts, candidate =>
            {
                UnregisterShortcuts();
                string? problem = candidate.Validate() ?? RegisterShortcuts(candidate);
                if (problem != null) { UnregisterShortcuts(); return problem; }
                try { candidate.Save(); }
                catch (Exception ex) { UnregisterShortcuts(); return "Could not save shortcuts: " + ex.Message; }
                shortcuts = candidate.Clone();
                return null;
            });
            saved = dialog.ShowDialog() == true;
        }
        finally
        {
            if (!saved) { UnregisterShortcuts(); status.Text = RegisterShortcuts(shortcuts) ?? "Shortcut changes cancelled"; }
            else status.Text = "Shortcuts saved · " + shortcuts.Bindings[1].Display + " captures a region";
            shortcutHint.Text = shortcuts.Bindings[1].Display + " to capture · Ctrl+V to paste · Drop an image to open";
            if (tray != null) tray.Text = "FrameForge · " + shortcuts.Bindings[1].Display;
            configuringShortcuts = false;
        }
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == 0x0312)
        {
            handled = true;
            if (configuringShortcuts) return IntPtr.Zero;
            int id = wparam.ToInt32();
            if (id == 6)
            {
                if (recordingPanel != null)
                    _ = recordingPanel.Stop();
                else if (scrollSession != null) scrollSession.Finish();
                else
                {
                    captureCancellation?.Cancel();
                    regionPicker?.Cancel();
                }
            }
            else if (!busy)
                _ = Capture(id switch
                {
                    1 => "region",
                    2 => "screen",
                    3 => "window",
                    4 => "video",
                    5 => "scroll",
                    _ => "region"
                });
        }

        return IntPtr.Zero;
    }

    private void KeyHandler(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O:
                    Open();
                    break;
                case Key.V:
                    if (Keyboard.FocusedElement is TextBox)
                        return;
                    Paste();
                    break;
                case Key.C:
                    if (Keyboard.FocusedElement is TextBox)
                        return;
                    Copy();
                    break;
                case Key.S:
                    SaveProject();
                    break;
                case Key.E:
                    Export();
                    break;
                case Key.Z:
                    if (Keyboard.FocusedElement is TextBox)
                        return;
                    Undo();
                    break;
                case Key.Y:
                    if (Keyboard.FocusedElement is TextBox)
                        return;
                    Redo();
                    break;
                case Key.D:
                    Editor.DuplicateSelection();
                    break;
                default:
                    return;
            }

            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBox || Keyboard.Modifiers != ModifierKeys.None)
            return;
        var tool = e.Key switch
        {
            Key.V => Tool.Select,
            Key.A => Tool.Arrow,
            Key.R => Tool.Rectangle,
            Key.E => Tool.Ellipse,
            Key.L => Tool.Line,
            Key.P => Tool.Pen,
            Key.T => Tool.Text,
            Key.C => Tool.Callout,
            Key.H => Tool.Highlight,
            Key.S => Tool.Step,
            Key.B => Tool.Blur,
            Key.X => Tool.Redact,
            Key.K => Tool.Crop,
            _ => (Tool? )null
        };
        if (tool.HasValue)
        {
            SelectTool(tool.Value);
            e.Handled = true;
        }
    }

    private void Help() => MessageBox.Show(this, "CAPTURE SHORTCUTS (while FrameForge is running)\n" + string.Join("\n", ShortcutProfile.Actions.Select(action => shortcuts.Bindings[action.Key].Display + "  —  " + action.Value)) + "\n\nUse Keyboard shortcuts to change these combinations. Closing the editor keeps FrameForge in the tray by default. Use Preferences for startup and close behavior; Exit FrameForge quits completely. During region selection, Escape, right-click, the Cancel button, or Alt+F4 cancels. Switching away also cancels; a two-minute timeout clears idle overlays.\n\nEDITOR\nCtrl+O open · Ctrl+V paste · Ctrl+C copy image\nCtrl+S save editable project · Ctrl+E export\nCtrl+Z / Ctrl+Y undo / redo · Ctrl+D duplicate\nDelete removes selected annotation · arrows nudge\nV select · A arrow · R rectangle · T text · K crop\n\nYour library is saved locally at:\n" + Paths.Library + "\n\nProjects preserve original pixels and editable annotations. Share exported PNG/JPEG files when redacting sensitive information. Blur is not secure redaction.\n\nFrameForge " + AppBrand.Version + " · MIT licensed.", "FrameForge help");
    public static void OpenShell(string path)
    {
        Directory.CreateDirectory(Paths.Library);
        Process.Start(new ProcessStartInfo(ShellPaths.ResolveExistingPath(path)) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
    }

    private void Error(Exception ex)
    {
        status.Text = ex.Message;
        if (Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_OUTPUT") != null)
            throw new InvalidOperationException("UI action failed", ex);
        MessageBox.Show(this, ex.Message, "FrameForge", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

internal sealed class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid
{
    public UniformGrid2()
    {
        Columns = 2;
    }
}
