using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FrameForge;
public static class Ui
{
    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    public static Button Button(string text, Action action, bool accent = false, string? tooltip = null)
    {
        var b = new Button
        {
            Content = text,
            ToolTip = tooltip
        };
        if (accent)
        {
            b.Background = Brush("#6558F5");
            b.Foreground = Brushes.White;
            b.BorderBrush = b.Background;
        }

        b.Click += (_, _) => action();
        return b;
    }

    public static TextBlock Label(string text, double size = 13, string color = "#697086", bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brush(color),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, 6, 4, 6)
    };
    public static ComboBox Combo(IEnumerable<string> values, int selected = 0)
    {
        var c = new ComboBox();
        foreach (var s in values)
            c.Items.Add(s);
        c.SelectedIndex = selected;
        return c;
    }
}

public static class Dialogs
{
    public static Window Shell(Window? owner, string title, double width = 440, double height = 300)
    {
        var w = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = 320,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip
        };
        if (owner != null && owner.IsVisible)
            w.Owner = owner;
        return w;
    }

    public static void FitForm(Window window, UIElement form)
    {
        window.SizeToContent = SizeToContent.Height;
        window.MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 48);
        window.Content = new ScrollViewer {
            Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Style = (Style)Application.Current.FindResource("QuietScrollViewer")
        };
    }

    public static string? Prompt(Window? owner, string title, string value, bool multiline = false)
    {
        var w = Shell(owner, title, 500, multiline ? 360 : 205);
        var panel = new DockPanel
        {
            Margin = new Thickness(20)
        };
        w.Content = panel;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        var input = new TextBox
        {
            Text = value,
            AcceptsReturn = multiline,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        buttons.Children.Add(Ui.Button("Cancel", () => w.DialogResult = false));
        var ok = Ui.Button("Apply", () => w.DialogResult = true, true);
        ok.IsDefault = !multiline;
        buttons.Children.Add(ok);
        panel.Children.Add(input);
        w.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return w.ShowDialog() == true ? input.Text : null;
    }

    public static T? Pick<T>(Window owner, string title, IEnumerable<T> options)
        where T : class
    {
        var w = Shell(owner, title, 620, 450);
        var panel = new DockPanel
        {
            Margin = new Thickness(18)
        };
        w.Content = panel;
        var list = new ListBox
        {
            Margin = new Thickness(4),
            DisplayMemberPath = ""
        };
        foreach (var item in options)
            list.Items.Add(item);
        if (list.Items.Count > 0)
            list.SelectedIndex = 0;
        var choose = Ui.Button("Select window", () =>
        {
            if (list.SelectedItem != null)
                w.DialogResult = true;
        }, true);
        DockPanel.SetDock(choose, Dock.Bottom);
        panel.Children.Add(choose);
        panel.Children.Add(list);
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem != null)
                w.DialogResult = true;
        };
        return w.ShowDialog() == true ? list.SelectedItem as T : null;
    }

    public static void TextResult(Window owner, string text)
    {
        var w = Shell(owner, "Text recognized on this image", 700, 500);
        var p = new DockPanel
        {
            Margin = new Thickness(20)
        };
        w.Content = p;
        var input = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        p.Children.Add(buttons);
        buttons.Children.Add(Ui.Button("Copy text", () =>
        {
            ClipboardService.SetText(input.Text, new System.Windows.Interop.WindowInteropHelper(w).Handle);
        }, true));
        buttons.Children.Add(Ui.Button("Save .txt", () =>
        {
            var d = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text file|*.txt",
                FileName = "Recognized text.txt"
            };
            if (d.ShowDialog(w) == true)
                System.IO.File.WriteAllText(d.FileName, input.Text);
        }));
        p.Children.Add(input);
        w.ShowDialog();
    }

    public static (int width, int height)? Size(Window owner, int width, int height)
    {
        var w = Shell(owner, "Resize image", 400, 265);
        var p = new StackPanel
        {
            Margin = new Thickness(20)
        };
        FitForm(w, p);
        p.Children.Add(Ui.Label("Image dimensions in pixels", 18, bold: true));
        var x = new TextBox
        {
            Text = width.ToString()
        };
        var y = new TextBox
        {
            Text = height.ToString()
        };
        var ratio = new CheckBox
        {
            Content = "Keep aspect ratio",
            IsChecked = true
        };
        var dimensions = new UniformGrid2();
        var widthColumn = new StackPanel();
        widthColumn.Children.Add(Ui.Label("Width (px)"));
        widthColumn.Children.Add(x);
        var heightColumn = new StackPanel();
        heightColumn.Children.Add(Ui.Label("Height (px)"));
        heightColumn.Children.Add(y);
        dimensions.Children.Add(widthColumn);
        dimensions.Children.Add(heightColumn);
        System.Windows.Automation.AutomationProperties.SetName(x, "Image width in pixels");
        System.Windows.Automation.AutomationProperties.SetName(y, "Image height in pixels");
        p.Children.Add(dimensions);
        p.Children.Add(ratio);
        x.TextChanged += (_, _) =>
        {
            if (ratio.IsChecked == true && int.TryParse(x.Text, out int a))
                y.Text = Math.Max(1, (int)((double)a * height / width)).ToString();
        };
        (int, int)? result = null;
        p.Children.Add(Ui.Button("Resize", () =>
        {
            if (int.TryParse(x.Text, out int a) && int.TryParse(y.Text, out int b) && a > 0 && b > 0 && (long)a * b <= 100_000_000)
            {
                result = (a, b);
                w.DialogResult = true;
            }
            else
                MessageBox.Show(w, "Enter positive dimensions up to 100 megapixels.");
        }, true));
        w.ShowDialog();
        return result;
    }
}
