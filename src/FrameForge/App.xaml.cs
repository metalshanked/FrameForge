using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace FrameForge;
public partial class App : Application
{
    private Mutex? mutex;
    private bool ownsMutex;
    private readonly CancellationTokenSource activationStop = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, a) =>
        {
            RegionPicker.CancelAll();
            try
            {
                Directory.CreateDirectory(Paths.Logs);
                File.AppendAllText(Path.Combine(Paths.Logs, "errors.log"), DateTime.Now + " " + a.Exception + Environment.NewLine);
            }
            catch { }
            a.Handled = true;
            if (Environment.GetEnvironmentVariable("FRAMEFORGE_TEST_OUTPUT") != null) { Shutdown(1); return; }
            MessageBox.Show(a.Exception.Message, "FrameForge", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => {
            if (sender is Window window && window.Icon == null) window.Icon = AppBrand.Image;
        }));
        if (e.Args.Contains("--self-test")) { Shutdown(await SelfTests.RunAsync(false)); return; }
        if (e.Args.Contains("--ui-test")) { Shutdown(await SelfTests.RunAsync(true)); return; }

        var path = e.Args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));
        mutex = new Mutex(true, "Local\\FrameForge.Editor", out bool isNew);
        ownsMutex = isNew;
        if (!isNew)
        {
            if (!e.Args.Contains("--background") && !await SingleInstance.NotifyAsync(path))
                MessageBox.Show("FrameForge is running but did not respond. Reopen it from the system tray.", "FrameForge");
            Shutdown();
            return;
        }
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var main = new MainWindow();
        MainWindow = main;
        SessionEnding += (_, _) => main.PrepareForSessionEnd();
        _ = SingleInstance.ListenAsync(file => Dispatcher.BeginInvoke(() =>
        {
            main.RestoreEditor();
            if (file != null && File.Exists(file)) main.OpenPath(file);
        }), activationStop.Token);
        if (e.Args.Contains("--background") && path == null) main.StartInTray();
        else { main.Show(); if (path != null) main.OpenPath(path); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        activationStop.Cancel();
        RegionPicker.CancelAll();
        if (ownsMutex) mutex?.ReleaseMutex();
        mutex?.Dispose();
        base.OnExit(e);
    }
}

public static class Paths
{
    public static string Root => Environment.GetEnvironmentVariable("FRAMEFORGE_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameForge");
    public static string Library => Path.Combine(Root, "Library");
    public static string Logs => Path.Combine(Root, "Logs");
    public static string Work => Path.Combine(Root, "Temp");
    public static string Unique(string extension) => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Guid.NewGuid().ToString("N")[..6] + extension;
}
