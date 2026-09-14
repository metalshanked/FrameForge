using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FrameForge;

internal static class AppBrand
{
    public const string Version = "0.2.6";
    private static readonly Uri IconUri = new("pack://application:,,,/FrameForge;component/Assets/FrameForge.ico");
    public static BitmapFrame Image => BitmapDecoder.Create(IconUri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.OrderByDescending(frame => frame.PixelWidth).First();
    public static void ShowLicenses(Window owner)
    {
        var assembly = typeof(AppBrand).Assembly;
        var parts = assembly.GetManifestResourceNames().Where(name => name.StartsWith("Licenses/")).OrderBy(name => name).Select(name => {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            return name + Environment.NewLine + reader.ReadToEnd();
        });
        var window = Dialogs.Shell(owner, "FrameForge — Open-source licenses", 760, 560);
        window.Icon = Image;
        window.Content = new TextBox {
            Text = string.Join(Environment.NewLine + Environment.NewLine, parts),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16)
        };
        window.ShowDialog();
    }
    public static System.Drawing.Icon TrayIcon()
    {
        using var stream = Application.GetResourceStream(IconUri).Stream;
        using var icon = new System.Drawing.Icon(stream);
        return new System.Drawing.Icon(icon, System.Windows.Forms.SystemInformation.SmallIconSize);
    }
}
