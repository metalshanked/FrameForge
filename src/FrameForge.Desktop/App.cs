using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
namespace FrameForge.Desktop;
public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant=ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new Style(s=>s.Is<Button>()){Setters={
            new Setter(Button.BackgroundProperty,Brushes.White),
            new Setter(Button.BorderBrushProperty,Brush.Parse("#DCDDE7")),
            new Setter(Button.BorderThicknessProperty,new Thickness(1)),
            new Setter(Button.CornerRadiusProperty,new CornerRadius(6))}});
        Styles.Add(new Style(s=>s.Is<Button>().Class(":pointerover")){Setters={
            new Setter(Button.BackgroundProperty,Brush.Parse("#EEEAFB")),
            new Setter(Button.BorderBrushProperty,Brush.Parse("#A89ADF"))}});
        Styles.Add(new Style(s=>s.Is<Button>().Class(":pressed")){Setters={
            new Setter(Button.BackgroundProperty,Brush.Parse("#D8CFF4")),
            new Setter(Button.BorderBrushProperty,Brush.Parse("#7561B8"))}});
        Styles.Add(new Style(s=>s.Is<ScrollBar>().Class(":vertical")){Setters={new Setter(ScrollBar.WidthProperty,6d),new Setter(ScrollBar.MinWidthProperty,0d)}});
        Styles.Add(new Style(s=>s.Is<ScrollBar>().Class(":horizontal")){Setters={new Setter(ScrollBar.HeightProperty,6d),new Setter(ScrollBar.MinHeightProperty,0d)}});
        Styles.Add(new Style(s=>s.Is<ToggleButton>().Class(":checked")){Setters={
            new Setter(Button.BackgroundProperty,Brush.Parse("#6757EF")),
            new Setter(Button.ForegroundProperty,Brushes.White)}});
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)desktop.MainWindow=new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
