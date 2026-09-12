using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FrameForge;

public sealed record Shortcut(uint Modifiers, uint VirtualKey)
{
    public string Display
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & 2) != 0) parts.Add("Ctrl");
            if ((Modifiers & 1) != 0) parts.Add("Alt");
            if ((Modifiers & 4) != 0) parts.Add("Shift");
            string key = VirtualKey switch
            {
                0xC0 => "Tilde (`/~)",
                >= 0x30 and <= 0x39 => ((char)VirtualKey).ToString(),
                >= 0x41 and <= 0x5A => ((char)VirtualKey).ToString(),
                _ => KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString()
            };
            parts.Add(key);
            return string.Join(" + ", parts);
        }
    }

    public bool Valid => (Modifiers & ~7u) == 0 && (Modifiers & 3) != 0 && VirtualKey is >= 0x08 and <= 0xFE
        && VirtualKey is not (0x10 or 0x11 or 0x12 or 0x1B or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5);

    public static Shortcut? FromKey(Key key, ModifierKeys modifiers)
    {
        uint flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= 2;
        if (modifiers.HasFlag(ModifierKeys.Alt)) flags |= 1;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= 4;
        if (modifiers.HasFlag(ModifierKeys.Windows)) return null;
        var shortcut = new Shortcut(flags, (uint)KeyInterop.VirtualKeyFromKey(key));
        return shortcut.Valid ? shortcut : null;
    }
}

public sealed class ShortcutProfile
{
    public static readonly IReadOnlyDictionary<int, string> Actions = new Dictionary<int, string>
    {
        [1] = "Capture region", [2] = "Capture all screens", [3] = "Choose a window",
        [4] = "Record a region", [5] = "Scrolling capture", [6] = "Cancel capture / stop recording"
    };
    public Dictionary<int, Shortcut> Bindings { get; set; } = new()
    {
        [1] = new(2, 0xC0), [2] = new(6, 0x33), [3] = new(6, 0x34),
        [4] = new(6, 0x35), [5] = new(6, 0x36), [6] = new(6, 0x79)
    };
    public ShortcutProfile Clone() => new() { Bindings = new(Bindings) };
    public string? Validate()
    {
        if (Bindings == null || Bindings.Count != Actions.Count || Actions.Keys.Any(id => !Bindings.TryGetValue(id, out var shortcut) || shortcut == null || !shortcut.Valid))
            return "Use Ctrl or Alt plus a non-modifier key for every action. Escape is reserved for cancellation.";
        foreach (var group in Bindings.GroupBy(pair => pair.Value).Where(group => group.Count() > 1))
            return $"{group.Key.Display} is assigned twice. Choose a different shortcut for {string.Join(" and ", group.Select(pair => Actions[pair.Key]))}.";
        return null;
    }
    public void Save(string? path = null)
    {
        var error = Validate();
        if (error != null) throw new InvalidDataException(error);
        path ??= Path.Combine(Paths.Root, "shortcuts.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
    public static ShortcutProfile Load(string? path = null)
    {
        path ??= Path.Combine(Paths.Root, "shortcuts.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return new();
            var profile = JsonSerializer.Deserialize<ShortcutProfile>(File.ReadAllText(path));
            return profile != null && profile.Validate() == null ? profile : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return new(); }
    }
}

internal sealed class ShortcutInput : TextBox
{
    public Shortcut Binding { get; private set; }
    private Shortcut beforeFocus;
    public ShortcutInput(Shortcut binding)
    {
        Style = (Style)Application.Current.FindResource(typeof(TextBox));
        Binding = beforeFocus = binding;
        Text = binding.Display;
        IsReadOnly = true;
        MinWidth = 210;
        FontSize = 14;
        ToolTip = "Click here, then press your preferred key combination.";
        GotKeyboardFocus += (_, _) => { beforeFocus = Binding; Text = "Press your shortcut…"; SelectAll(); };
        LostKeyboardFocus += (_, _) => Text = Binding.Display;
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
            e.Handled = true;
            if (key == Key.Escape) { Binding = beforeFocus; Text = Binding.Display; Keyboard.ClearFocus(); return; }
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift) return;
            var shortcut = Shortcut.FromKey(key, Keyboard.Modifiers);
            if (shortcut == null) { Text = "Use Ctrl or Alt + a key"; return; }
            Binding = shortcut;
            Text = Binding.Display;
        };
    }
    public void Reset(Shortcut binding) { Binding = beforeFocus = binding; Text = binding.Display; }
}

internal sealed class ShortcutsWindow : Window
{
    public ShortcutsWindow(Window owner, ShortcutProfile current, Func<ShortcutProfile, string?> save)
    {
        Owner = owner;
        Title = "FrameForge — Keyboard shortcuts";
        Width = 620;
        Height = 600;
        MinWidth = 540;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel();
        Content = layout;
        var panel = new StackPanel { Margin = new Thickness(24, 24, 24, 8) };
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Style = (Style)FindResource("QuietScrollViewer") });
        panel.Children.Add(Ui.Label("Make capture your shortcut", 23, "#252737", true));
        panel.Children.Add(Ui.Label("Click a shortcut and press the combination you want. The tilde key is the physical ` / ~ key; Shift is optional and recorded separately.", 13));
        var inputs = new Dictionary<int, ShortcutInput>();
        foreach (var pair in ShortcutProfile.Actions)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(230) });
            var label = Ui.Label(pair.Value, 13, "#36384C", true);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            var input = new ShortcutInput(current.Bindings[pair.Key]) { TabIndex = pair.Key - 1 };
            System.Windows.Automation.AutomationProperties.SetName(input, pair.Value + " shortcut");
            Grid.SetColumn(input, 1);
            inputs[pair.Key] = input;
            row.Children.Add(input);
            panel.Children.Add(row);
        }
        var error = Ui.Label("Shortcuts are paused while this panel is open. Save applies them immediately.", 12);
        error.MinHeight = 45;
        panel.Children.Add(error);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Ui.Button("Restore defaults", () => { var defaults = new ShortcutProfile(); foreach (var pair in inputs) pair.Value.Reset(defaults.Bindings[pair.Key]); }));
        buttons.Children.Add(Ui.Button("Cancel", () => DialogResult = false));
        buttons.Children.Add(Ui.Button("Save shortcuts", () =>
        {
            var candidate = new ShortcutProfile { Bindings = inputs.ToDictionary(pair => pair.Key, pair => pair.Value.Binding) };
            string? problem = candidate.Validate() ?? save(candidate);
            if (problem != null) { error.Text = problem; error.Foreground = Ui.Brush("#BC304A"); return; }
            DialogResult = true;
        }, true));
        int actionTabIndex = 100;
        foreach (Button action in buttons.Children) action.TabIndex = actionTabIndex++;
        buttons.Margin = new Thickness(24, 8, 24, 20);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Insert(0, buttons);
    }
}
