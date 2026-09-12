using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FrameForge;
public static class SelfTests
{
    static readonly List<string> results = new();
    static int failures;
    static string folder = "";
    private static void Check(string name, bool success, string detail = "")
    {
        results.Add((success ? "PASS" : "FAIL") + " | " + name + (detail.Length > 0 ? " | " + detail : ""));
        if (!success)
            failures++;
        File.WriteAllLines(Path.Combine(folder, "progress.txt"), results);
    }

    public static async Task<int> RunAsync(bool ui)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        folder = Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_OUTPUT") ?? Path.Combine(Paths.Root, "TestResults");
        Directory.CreateDirectory(folder);
        try
        {
            var sample = MainWindow.DemoImage();
            ShellPathTests();
            ShortcutTests();
            DesktopPreferenceTests();
            OutlineTest();
            var doc = new CaptureDocument(sample);
            Check("Image normalization", doc.Image.DpiX == 96 && doc.Image.Format == PixelFormats.Bgra32);
            doc.Checkpoint();
            doc.Marks.Add(new Mark { Kind = Tool.Rectangle, X = 100, Y = 100, X2 = 400, Y2 = 250, Color = "#FFFF0000", Width = 6 });
            doc.Undo();
            Check("Undo annotation", doc.Marks.Count == 0 && doc.CanRedo);
            doc.Redo();
            Check("Redo annotation", doc.Marks.Count == 1);
            foreach (var tool in Enum.GetValues<Tool>().Where(t => t is not Tool.Select and not Tool.Crop))
                doc.Marks.Add(new Mark { Kind = tool, X = 100, Y = 300, X2 = 300, Y2 = 420, Color = "#FF6757EF", Text = "Test 123", Points = new() { new[] { 100.0, 300.0 }, new[] { 200.0, 340.0 }, new[] { 300.0, 420.0 } } });
            var rendered = doc.Render();
            Imaging.Save(rendered, Path.Combine(folder, "all-tools.png"));
            Check("Render all annotation types", rendered.PixelWidth == 1000 && rendered.PixelHeight == 600);
            string project = Path.Combine(folder, "roundtrip.ffg");
            doc.Title = "Round trip ✓";
            doc.SaveProject(project);
            var loaded = CaptureDocument.LoadProject(project);
            Check("Project round trip", loaded.Title == doc.Title && loaded.Marks.Count == doc.Marks.Count && Imaging.Png(loaded.Render()).SequenceEqual(Imaging.Png(doc.Render())));
            loaded.Crop(new Rect(-5, -5, 305, 205));
            Check("Crop clips to image bounds", loaded.Image.PixelWidth == 300 && loaded.Image.PixelHeight == 200 && loaded.Marks.Count == 0);
            loaded.Undo();
            Check("Undo restores crop and layers", loaded.Image.PixelWidth == 1000 && loaded.Marks.Count == doc.Marks.Count);
            loaded.Replace(Imaging.Resize(loaded.Render(), 500, 300));
            Check("Resize", loaded.Image.PixelWidth == 500 && loaded.Image.PixelHeight == 300);
            loaded.Undo();
            Check("Undo resize", loaded.Image.PixelWidth == 1000);
            var redact = new CaptureDocument(sample);
            redact.Marks.Add(new Mark { Kind = Tool.Redact, X = 100, Y = 100, X2 = 250, Y2 = 200 });
            var red = Imaging.Normalize(redact.Render());
            byte[] pixel = new byte[4];
            red.CopyPixels(new Int32Rect(150, 150, 1, 1), pixel, 4, 0);
            Check("Opaque redaction in exported pixels", pixel[0] == 0 && pixel[1] == 0 && pixel[2] == 0 && pixel[3] == 255);
            redact.Marks.Add(new Mark { Kind = Tool.Blur, X = 120, Y = 120, X2 = 220, Y2 = 180 });
            var blurred = Imaging.Normalize(redact.Render());
            blurred.CopyPixels(new Int32Rect(150, 150, 1, 1), pixel, 4, 0);
            Check("Blur does not reveal pixels beneath redaction", pixel[0] == 0 && pixel[1] == 0 && pixel[2] == 0 && pixel[3] == 255);
            foreach (var ext in new[]
            {
                "png",
                "jpg",
                "bmp",
                "tiff"
            }

            )
            {
                var path = Path.Combine(folder, "export." + ext);
                Imaging.Save(sample, path);
                var roundtrip = Imaging.Load(path);
                Check("Export and reopen " + ext, roundtrip.PixelWidth == 1000 && roundtrip.PixelHeight == 600);
            }

            var tall = Pattern(640, 1500, 42);
            var frame1 = new CroppedBitmap(tall, new Int32Rect(0, 0, 640, 600));
            frame1.Freeze();
            var frame2 = new CroppedBitmap(tall, new Int32Rect(0, 240, 640, 600));
            frame2.Freeze();
            var match = Stitcher.FindShift(frame1, frame2);
            Check("Scrolling overlap finds exact offset", match.Confident && match.Shift == 240, $"shift={match.Shift}, error={match.Error:F3}");
            var joined = Stitcher.Append(frame1, frame2, match.Shift);
            var expected = Imaging.Normalize(new CroppedBitmap(tall, new Int32Rect(0, 0, 640, 840)));
            Check("Scrolling stitch pixel equality", Imaging.Png(Imaging.Normalize(joined)).SequenceEqual(Imaging.Png(expected)));
            Check("Scrolling duplicate detection", Stitcher.FindShift(frame1, frame1).Duplicate);
            Check("Scrolling rejects unrelated content", !Stitcher.FindShift(frame1, Pattern(640, 600, 733)).Confident);
            Imaging.Save(joined, Path.Combine(folder, "stitched.png"));
            string invalid = Path.Combine(folder, "invalid.ffg");
            File.WriteAllText(invalid, "{\"Version\":999}");
            try
            {
                CaptureDocument.LoadProject(invalid);
                Check("Unsupported project rejected", false);
            }
            catch (InvalidDataException)
            {
                Check("Unsupported project rejected", true);
            }

            try
            {
                var text = await OcrService.RecognizeAsync(sample);
                File.WriteAllText(Path.Combine(folder, "ocr.txt"), text);
                Check("Windows OCR recognizes sample", text.Contains("Capture", StringComparison.OrdinalIgnoreCase) && text.Contains("Explain", StringComparison.OrdinalIgnoreCase), text.Replace(Environment.NewLine, " / "));
            }
            catch (Exception ex)
            {
                Check("Windows OCR", false, ex.Message);
            }

            try
            {
                var synthetic = Path.Combine(folder, "synthetic.mp4");
                await Ffmpeg.Run("-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=15", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "2", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", synthetic);
                Check("FFmpeg H.264/AAC encode", new FileInfo(synthetic).Length > 1000);
                var trim = Path.Combine(folder, "trim.mp4");
                await Recorder.ExportVideo(synthetic, trim, .5, 1, false);
                await Ffmpeg.Run("-v", "error", "-i", trim, "-f", "null", "-");
                Check("Trimmed MP4 decodes", new FileInfo(trim).Length > 1000);
                var gif = Path.Combine(folder, "clip.gif");
                await Recorder.ExportVideo(synthetic, gif, 0, 1, true);
                using var stream = File.OpenRead(gif);
                var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Check("Animated GIF has multiple frames", decoder.Frames.Count >= 10, $"frames={decoder.Frames.Count}");
            }
            catch (Exception ex)
            {
                Check("Video pipeline", false, ex.ToString());
            }

            if (ui)
                await UiTests();
        }
        catch (Exception ex)
        {
            Check("Unhandled test exception", false, ex.ToString());
        }

        results.Add($"RESULT | {results.Count(r => r.StartsWith("PASS"))} passed | {failures} failed");
        File.WriteAllLines(Path.Combine(folder, ui ? "ui-test-results.txt" : "test-results.txt"), results);
        return failures == 0 ? 0 : 1;
    }

    static void ShellPathTests()
    {
        string directory = Path.Combine(folder, "Shell path with spaces — ü");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "capture proof.txt");
        File.WriteAllText(file, "FrameForge shell path regression");
        string resolvedDirectory = ShellPaths.ResolveExistingPath(directory);
        string resolvedFile = ShellPaths.ResolveExistingPath(file);
        Check("Shell directory path reaches the same file with spaces and Unicode",
            File.ReadAllText(Path.Combine(resolvedDirectory, Path.GetFileName(file))) == "FrameForge shell path regression");
        Check("Shell file path preserves file identity",
            File.ReadAllText(resolvedFile) == File.ReadAllText(file));
        string missing = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        try
        {
            ShellPaths.ResolveExistingPath(missing);
            Check("Missing shell destination reports failure", false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Check("Missing shell destination reports failure", ex.NativeErrorCode is 2 or 3 && !File.Exists(missing) && !Directory.Exists(missing));
        }
    }

    static BitmapSource Pattern(int w, int h, int seed)
    {
        var bytes = new byte[w * h * 4];
        var random = new Random(seed);
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = (byte)random.Next(256);
            bytes[i + 1] = (byte)random.Next(256);
            bytes[i + 2] = (byte)random.Next(256);
            bytes[i + 3] = 255;
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bytes, w * 4);
        result.Freeze();
        return result;
    }

    static IEnumerable<T> Descendants<T>(DependencyObject item)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
        {
            var child = VisualTreeHelper.GetChild(item, i);
            if (child is T t)
                yield return t;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    static Button Button(Window w, string text) => Descendants<Button>(w).First(b => b.Content?.ToString() == text);
    static void Click(Window w, string text) => Button(w, text).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    static void RenderWindow(Window w, string filename)
    {
        w.UpdateLayout();
        var content = (FrameworkElement)w.Content;
        var b = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        b.Render(content);
        Imaging.Save(b, Path.Combine(folder, filename));
    }

    static async Task UiTests()
    {
        var w = new MainWindow();
        w.Show();
        // Background launches may not receive Windows foreground permission.
        // Wait for the test window to be activated before exercising focus-sensitive overlays.
        w.Activate();
        var testHandle = new WindowInteropHelper(w).Handle;
        for (int attempt = 0; attempt < 240 && NativeCapture.GetForegroundWindow() != testHandle; attempt++)
            await Task.Delay(125);
        if (NativeCapture.GetForegroundWindow() != testHandle)
            throw new InvalidOperationException("Activate the FrameForge test window, then rerun the UI tests. Windows did not grant the background test process foreground focus.");
        await Task.Delay(500);
        RenderWindow(w, "ui-welcome.png");
        bool imageCommandsInitiallyDisabled = new[] { "Save project", "Copy image", "Export image" }.All(label => !Button(w, label).IsEnabled);
        var originalSize = new Size(w.Width, w.Height);
        w.Width = w.MinWidth; w.Height = w.MinHeight;
        await Task.Delay(150);
        RenderWindow(w, "ui-compact.png");
        bool InsideWindow(FrameworkElement element) {
            var origin = element.TranslatePoint(new Point(), w);
            return element.IsVisible && element.ActualWidth > 0 && origin.X >= 0 && origin.Y >= 0
                && origin.X + element.ActualWidth <= w.ActualWidth
                && origin.Y + element.ActualHeight <= w.ActualHeight;
        }
        var captureSearch = Descendants<TextBox>(w).First(box => System.Windows.Automation.AutomationProperties.GetName(box) == "Search recent captures");
        Check("Compact layout keeps capture folder, search, preferences, and Exit visible",
            InsideWindow(Button(w, "Open capture folder")) && InsideWindow(captureSearch)
            && InsideWindow(Button(w, "Preferences…")) && InsideWindow(Button(w, "Exit FrameForge")));
        w.Width = originalSize.Width; w.Height = originalSize.Height;
        await Task.Delay(150);
        Click(w, "Try the editor");
        await Task.Delay(300);
        Check("Welcome button opens editable document", w.Editor.Document != null);
        Check("Image commands enable only after an image is open", imageCommandsInitiallyDisabled
            && new[] { "Save project", "Copy image", "Export image" }.All(label => Button(w, label).IsEnabled));
        var d = w.Editor.Document!;
        d.Checkpoint();
        d.Marks.Add(new Mark { Kind = Tool.Arrow, X = 600, Y = 435, X2 = 400, Y2 = 430, Color = "#FFFFAA39", Width = 6 });
        w.Editor.Refresh();
        Click(w, "↶ Undo");
        Check("Undo button works", w.Editor.Document!.Marks.Count == 0);
        Click(w, "↷ Redo");
        Check("Redo button works", w.Editor.Document.Marks.Count == 1);
        Click(w, "Copy image");
        var clipboard = ClipboardService.GetImage(new WindowInteropHelper(w).Handle);
        Check("Native clipboard image round trip", clipboard != null && clipboard.PixelWidth == 1000);
        Click(w, "Paste");
        Check("Paste button opens clipboard image", w.Editor.Document!.Title == "Pasted image" && w.Editor.Document.Image.PixelWidth == 1000);
        RenderWindow(w, "ui-editor.png");
        var handle = new WindowInteropHelper(w).Handle;
        var bounds = NativeCapture.Bounds(handle);
        var image = NativeCapture.Grab(bounds);
        Imaging.Save(image, Path.Combine(folder, "native-capture.png"));
        Check("Native GDI window-area capture", image.PixelWidth == bounds.Width && image.PixelHeight == bounds.Height);
        Check("Window enumeration finds app", NativeCapture.Windows().Any(a => a.Handle == handle));
        await PickerRegressionTests(w, bounds);
        await CaptureClipboardTests(w, handle);
        await Task.Delay(200);
        try
        {
            var area = new System.Drawing.Rectangle(bounds.X + 20, bounds.Y + 60, 640, 360);
            var recorder = new Recorder(area, new RecordingOptions(15));
            await recorder.StartSegment();
            await Task.Delay(1500);
            await recorder.EndSegment();
            await recorder.StartSegment();
            await Task.Delay(1100);
            string path = await recorder.Finish();
            File.Copy(path, Path.Combine(folder, "screen-recording.mp4"), true);
            await Ffmpeg.Run("-v", "error", "-i", path, "-f", "null", "-");
            Check("Real screen recording with pause/resume decodes", new FileInfo(path).Length > 1000);
        }
        catch (Exception ex)
        {
            Check("Real screen recording", false, ex.ToString());
        }

        try
        {
            var area = new System.Drawing.Rectangle(bounds.X + 20, bounds.Y + 60, 640, 360);
            var recorder = new Recorder(area, new RecordingOptions(15, SystemAudio: true));
            await recorder.StartSegment();
            await Task.Delay(1500);
            string path = await recorder.Finish();
            File.Copy(path, Path.Combine(folder, "system-audio-recording.mp4"), true);
            await Ffmpeg.Run("-v", "error", "-i", path, "-map", "0:a", "-f", "null", "-");
            Check("System loopback recording produces decodable audio", true);
        }
        catch (Exception ex)
        {
            Check("System audio recording", false, ex.ToString());
        }

        await ScrollUiTest();
        var openDocument = w.Editor.Document;
        w.Close();
        await Task.Delay(150);
        Check("Closing editor keeps tray and capture process available", !w.IsVisible && w.IsTrayVisible);
        w.RestoreEditor();
        await Task.Delay(150);
        Check("Restoring from tray preserves the open document", w.IsVisible && ReferenceEquals(openDocument, w.Editor.Document));
        w.RequestExit();
        Check("Explicit Exit removes the tray icon", !w.IsTrayVisible);
    }

    static async Task CaptureClipboardTests(MainWindow window, IntPtr handle)
    {
        var captured = Pattern(320, 180, 97);
        window.OpenCapture(captured, "Auto-copy test");
        var copied = ClipboardService.GetImage(handle);
        Check("Completed screenshot opens in editor and auto-copies identical pixels", copied != null
            && window.Editor.Document?.Title == "Auto-copy test"
            && Imaging.Png(Imaging.Normalize(copied)).SequenceEqual(Imaging.Png(Imaging.Normalize(captured))));

        var captureTask = window.Capture("region");
        for (int attempt = 0; attempt < 40 && !Application.Current.Windows.OfType<SelectionWindow>().Any(); attempt++)
            await Task.Delay(50);
        bool selectorOpened = Application.Current.Windows.OfType<SelectionWindow>().Any();
        RegionPicker.CancelAll();
        await captureTask.WaitAsync(TimeSpan.FromSeconds(3));
        var afterCancel = ClipboardService.GetImage(handle);
        Check("Cancelled capture preserves clipboard and the open image", selectorOpened && afterCancel != null
            && window.Editor.Document?.Title == "Auto-copy test"
            && Imaging.Png(Imaging.Normalize(afterCancel)).SequenceEqual(Imaging.Png(Imaging.Normalize(captured))));
    }

    static void DesktopPreferenceTests()
    {
        var path = Path.Combine(folder, "preferences-roundtrip.json");
        var preferences = new AppPreferences { CloseToTray = false, TrayHintShown = true };
        preferences.Save(path);
        var loaded = AppPreferences.Load(path);
        Check("Tray preferences persist across reload", !loaded.CloseToTray && loaded.TrayHintShown);
        Check("Startup command quotes executable paths and starts quietly",
            StartupRegistration.Command(@"C:\Program Files\FrameForge\FrameForge.exe") == "\"C:\\Program Files\\FrameForge\\FrameForge.exe\" --background");
    }
    static void ShortcutTests()
    {
        var preferred = new Shortcut(2, 0xC0);
        Check("Default region shortcut is Ctrl plus the tilde key", new ShortcutProfile().Bindings[1] == preferred);
        Check("Shortcut recorder recognizes the physical tilde key", Shortcut.FromKey(System.Windows.Input.Key.Oem3, System.Windows.Input.ModifierKeys.Control) == preferred);
        Check("Shortcut recorder preserves Shift separately", Shortcut.FromKey(System.Windows.Input.Key.Oem3, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift) == new Shortcut(6, 0xC0));
        Check("Unmodified shortcuts and Escape are rejected", Shortcut.FromKey(System.Windows.Input.Key.A, System.Windows.Input.ModifierKeys.None) == null && Shortcut.FromKey(System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.Control) == null);
        var profile = new ShortcutProfile();
        profile.Bindings[2] = preferred;
        Check("Duplicate shortcut assignments are rejected", profile.Validate() != null);
        profile.Bindings[2] = new Shortcut(3, 0x4B);
        string path = Path.Combine(folder, "shortcuts-roundtrip.json");
        profile.Save(path);
        var reloaded = ShortcutProfile.Load(path);
        Check("Custom shortcuts persist across reload", reloaded.Bindings.SequenceEqual(profile.Bindings));
        File.WriteAllText(path, "invalid json");
        Check("Invalid settings safely restore shortcut defaults", ShortcutProfile.Load(path).Bindings[1] == preferred);
    }

    static void OutlineTest()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Ui.Brush("#DDE1ED"), null, new Rect(0, 0, 640, 360));
            dc.DrawRectangle(Brushes.White, null, new Rect(64, 48, 448, 212));
            SelectionOutline.Draw(dc, new Rect(64, 48, 448, 212), 448, 212, new Size(640, 360));
        }
        var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Imaging.Save(bitmap, Path.Combine(folder, "selection-outline.png"));
        var pixels = new byte[640 * 360 * 4];
        bitmap.CopyPixels(pixels, 640 * 4, 0);
        int cyan = 0, dark = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 220 && pixels[i+1] > 170 && pixels[i+2] < 30) cyan++;
            if (pixels[i] < 50 && pixels[i+1] < 50 && pixels[i+2] < 50) dark++;
        }
        Check("Selection outline remains visible on a white capture", cyan > 500 && dark > 1000);
    }

    static async Task PickerRegressionTests(Window owner, System.Drawing.Rectangle bounds)
    {
        using (var picker = new RegionPicker())
        {
            var task = picker.PickAsync();
            await Task.Delay(150);
            var windows = picker.Windows.ToArray();
            Check("Capture overlays remain enabled for native input", !task.IsCompleted && windows.Length > 0 && windows.All(window => NativeCapture.IsWindowEnabled(new WindowInteropHelper(window).Handle)),
                $"open={windows.Length}, completed={task.IsCompleted}, foreground={Application.Current.Windows.Cast<Window>().FirstOrDefault(window => new WindowInteropHelper(window).Handle == NativeCapture.GetForegroundWindow())?.GetType().Name ?? "outside test process"}");
            picker.Down(new System.Drawing.Point(bounds.X + 20, bounds.Y + 20));
            picker.Up(new System.Drawing.Point(bounds.X + 320, bounds.Y + 220));
            var picked = await task.WaitAsync(TimeSpan.FromSeconds(2));
            Check("Selection geometry returns physical pixel rectangle", picked.HasValue && picked.Value.Width == 300 && picked.Value.Height == 200);
            Check("Successful selection removes every overlay", windows.All(window => !window.IsVisible) && picker.Windows.Count == 0);
        }
        using (var picker = new RegionPicker())
        {
            var task = picker.PickAsync();
            await Task.Delay(100);
            var window = picker.Windows.First();
            window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            var result = await task.WaitAsync(TimeSpan.FromSeconds(2));
            Check("Escape handler cancels and clears overlays", result == null && picker.Windows.Count == 0);
        }
        using (var cancellation = new System.Threading.CancellationTokenSource())
        using (var picker = new RegionPicker())
        {
            var task = picker.PickAsync(cancellation.Token);
            cancellation.Cancel();
            Check("Cancellation token releases capture", await task.WaitAsync(TimeSpan.FromSeconds(2)) == null && picker.Windows.Count == 0);
        }
        using (var picker = new RegionPicker())
        {
            var task = picker.PickAsync();
            await Task.Delay(100);
            picker.Windows.First().Close();
            Check("Closing a selection window cancels the whole session", await task.WaitAsync(TimeSpan.FromSeconds(2)) == null && picker.Windows.Count == 0);
        }
        using (var picker = new RegionPicker(TimeSpan.FromMilliseconds(250)))
        {
            var result = await picker.PickAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check("Safety timeout clears capture overlays", result == null && picker.Windows.Count == 0);
        }
        using (var picker = new RegionPicker())
        {
            var task = picker.PickAsync();
            owner.Activate();
            Check("Switching away cancels capture", await task.WaitAsync(TimeSpan.FromSeconds(2)) == null && picker.Windows.Count == 0);
        }
        bool repeated = true;
        for (int i = 0; i < 3; i++)
        {
            using var picker = new RegionPicker();
            var task = picker.PickAsync();
            picker.Cancel();
            repeated &= await task.WaitAsync(TimeSpan.FromSeconds(2)) == null && picker.Windows.Count == 0;
        }
        Check("Repeated capture cancellation leaves no overlays", repeated);
    }

    static async Task ScrollUiTest()
    {
        var host = new Window
        {
            Title = "FrameForge scrolling test fixture",
            Width = 760,
            Height = 610,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true
        };
        var stack = new StackPanel();
        for (int i = 0; i < 55; i++)
        {
            var row = new Border
            {
                Height = 80,
                Background = Ui.Brush(i % 2 == 0 ? "#EEEAFD" : "#FFFFFF"),
                Padding = new Thickness(15),
                Child = Ui.Label($"Section {i:00} — capture overlap test\nThe quick brown fox explores a scrolling page. ID {i * 1729}", 16, "#252737", true)
            };
            stack.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            CanContentScroll = false
        };
        host.Content = scroll;
        host.Show();
        await Task.Delay(500);
        var origin = scroll.PointToScreen(new Point(15, 15));
        var dpi = VisualTreeHelper.GetDpi(scroll);
        var area = new System.Drawing.Rectangle((int)origin.X, (int)origin.Y, (int)((scroll.ActualWidth - 30) * dpi.DpiScaleX), (int)((scroll.ActualHeight - 30) * dpi.DpiScaleY));
        var session = new ScrollSession(area);
        double previousOffset = scroll.VerticalOffset * dpi.DpiScaleY;
        var offsetErrors = new List<string>();
        int matchedFrames = 0;
        session.FrameAdded += shift => {
            double offset = scroll.VerticalOffset * dpi.DpiScaleY;
            double expected = offset - previousOffset;
            if (Math.Abs(shift - expected) > 2) offsetErrors.Add($"expected {expected:F1}, matched {shift}");
            previousOffset = offset;
            matchedFrames++;
        };
        session.Show();
        session.Left = 0;
        session.Top = 0;
        Click(session, "Auto scroll");
        await Task.Delay(8000);
        session.Finish();
        Check("Automatic scrolling captures real scrolling window", session.Result != null && session.Result.PixelHeight > area.Height, $"original={area.Height}, stitched={session.Result?.PixelHeight}");
        Check("Live stitch offsets match actual scroll movement", matchedFrames > 0 && offsetErrors.Count == 0, offsetErrors.Count > 0 ? string.Join("; ",offsetErrors) : $"{matchedFrames} frames aligned within 2 pixels");
        if (session.Result != null)
            Imaging.Save(session.Result, Path.Combine(folder, "scrolling-live.png"));
        host.Close();
    }
}
