using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FrameForge;

internal sealed class AppPreferences
{
    public bool CloseToTray { get; set; } = true;
    public bool TrayHintShown { get; set; }
    public static AppPreferences Load(string? path = null)
    {
        path ??= Path.Combine(Paths.Root, "preferences.json");
        try
        {
            return File.Exists(path) && new FileInfo(path).Length <= 65536
                ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string? path = null)
    {
        path ??= Path.Combine(Paths.Root, "preferences.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}

internal static class StartupRegistration
{
    internal const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string Command(string executable) => "\"" + Path.GetFullPath(executable) + "\" --background";
    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return string.Equals(key?.GetValue("FrameForge") as string, Command(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase);
        }
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled) key.SetValue("FrameForge", Command(Environment.ProcessPath!), RegistryValueKind.String);
        else key.DeleteValue("FrameForge", false);
    }
}

internal sealed class PreferencesWindow : Window
{
    public PreferencesWindow(Window owner, AppPreferences current, Action<AppPreferences> saved)
    {
        Owner = owner;
        Icon = AppBrand.Image;
        Title = "FrameForge — Preferences";
        Width = 540; Height = 460; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Dialogs.FitForm(this, panel);
        panel.Children.Add(Ui.Label("Ready whenever you need it", 23, "#252737", true));
        var close = new CheckBox { Content = "Keep running in the tray when I close the editor", IsChecked = current.CloseToTray };
        var startup = new CheckBox { Content = "Start FrameForge when I sign in to Windows" };
        bool initialStartup;
        try { initialStartup = StartupRegistration.Enabled; startup.IsChecked = initialStartup; }
        catch { initialStartup = false; startup.IsEnabled = false; }
        panel.Children.Add(close);
        panel.Children.Add(startup);
        panel.Children.Add(Ui.Label("Your capture shortcut works while the app is in the tray. Double-click the tray icon to reopen the editor. Use Exit FrameForge to quit completely.", 13));
        var status = Ui.Label("Startup opens quietly in the system tray.", 12);
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Ui.Button("Cancel", () => DialogResult = false));
        buttons.Children.Add(Ui.Button("Save preferences", () =>
        {
            var candidate = new AppPreferences { CloseToTray = close.IsChecked == true, TrayHintShown = current.TrayHintShown };
            bool startupChanged = startup.IsEnabled && (startup.IsChecked == true) != initialStartup;
            try
            {
                if (startupChanged) StartupRegistration.SetEnabled(startup.IsChecked == true);
                candidate.Save();
                saved(candidate);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                if (startupChanged) { try { StartupRegistration.SetEnabled(initialStartup); } catch { } }
                status.Text = "Could not save preferences: " + ex.Message;
            }
        }, true));
        panel.Children.Add(Ui.Button("Open-source licenses", () => AppBrand.ShowLicenses(this)));
        panel.Children.Add(Ui.Label("FrameForge " + AppBrand.Version + " · MIT licensed", 11));
        buttons.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(buttons);
    }
}
