using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using FrameForge.Core;
using SkiaSharp;
namespace FrameForge.Desktop;
public sealed partial class MainWindow
{
    async Task AutomaticScrolling()
    {
        if(recorder!=null)throw new InvalidOperationException("Stop recording before scrolling capture.");
        string source="";
        if(OperatingSystem.IsMacOS())
        {
            var data=await NativeBridge.Run("sources",Array.Empty<string>(),Token);
            var sources=data.GetProperty("items").EnumerateArray().Select(e=>new CaptureSource(e.GetProperty("id").GetString()!,e.GetProperty("name").GetString()!)).ToArray();
            var panel=new StackPanel{Spacing=12,Margin=new Thickness(24)};
            panel.Children.Add(Label("Choose the screen or window containing the scrollable page. Place it at the top before continuing."));
            var list=new ComboBox{ItemsSource=sources,SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(list);
            var dialog=Dialog("Scrolling capture",panel,620,300);string? selected=null;
            panel.Children.Add(Button("Continue",()=>{selected=(list.SelectedItem as CaptureSource)?.Id;dialog.Close();}));panel.Children.Add(Button("Cancel",()=>dialog.Close()));
            await dialog.ShowDialog(this);if(selected==null)return;source=selected;
        }
        else if(!await Choose("Scrolling capture","Choose the screen with the page in your desktop's sharing dialog and allow pointer control for this capture. FrameForge will scroll only during this session. You can stop with Escape or the Stop button.","Continue","Cancel"))return;
        string framePath=System.IO.Path.Combine(AppPaths.Temp,Guid.NewGuid().ToString("N")+".png");
        NativeBridge? bridge=null;SKBitmap? combined=null,previous=null;Window? progress=null;bool completed=false;
        try
        {
            Hide();await Task.Delay(350,Token);
            bridge=await NativeBridge.Start("scroll",OperatingSystem.IsMacOS()?new[]{"--source",source}:Array.Empty<string>(),Token);
            await bridge.Send("frame",new Dictionary<string,object>{{"path",framePath}},Token);
            using var first=Imaging.Load(framePath);
            DesktopIntegration.Show(this);
            var selection=await RegionPicker.PickImage(this,first);
            if(selection==null)return;
            previous=Imaging.Crop(first,selection.Value);combined=previous.Copy();
            var panel=new StackPanel{Spacing=10,Margin=new Thickness(14)};
            var label=Label("Scrolling capture · Escape to finish",12);panel.Children.Add(label);
            panel.Children.Add(Button("Stop & keep capture",()=>operation?.Cancel()));
            progress=new Window{Title="FrameForge · Scrolling",Content=panel,Width=290,Height=140,CanResize=false,Topmost=true,ShowInTaskbar=true,WindowStartupLocation=WindowStartupLocation.CenterScreen};
            progress.KeyDown+=(_,e)=>{if(e.Key==Avalonia.Input.Key.Escape){operation?.Cancel();e.Handled=true;}};
            progress.Closing+=(_,_)=>{if(!completed)operation?.Cancel();};
            Hide();progress.Show();progress.Activate();
            int duplicates=0;
            for(int frame=1;frame<=30;frame++)
            {
                await Task.Delay(450,Token);
                progress.Hide();
                await bridge.Send("scroll",new Dictionary<string,object>{{"x",(double)selection.Value.MidX},{"y",(double)selection.Value.MidY},{"amount",Math.Clamp(selection.Value.Height*.4,40,360)}},Token);
                // Wait beyond the preview sampling interval and for page animation to settle.
                await Task.Delay(850,Token);
                await bridge.Send("frame",new Dictionary<string,object>{{"path",framePath}},Token);
                using var screen=Imaging.Load(framePath);
                if(screen.Width!=first.Width||screen.Height!=first.Height)throw new InvalidOperationException("The shared screen changed size. The stitched frames have been kept.");
                using var next=Imaging.Crop(screen,selection.Value);
                var match=await Task.Run(()=>Stitcher.FindShift(previous,next),Token);
                progress.Show();progress.Activate();
                if(match.Duplicate){if(++duplicates>=2)break;continue;}
                duplicates=0;
                if(!match.Confident){status.Text="Stopped at an uncertain overlap; the completed frames were kept.";break;}
                var appended=Stitcher.Append(combined,next,match.Shift);combined.Dispose();combined=appended;previous.Dispose();previous=next.Copy();
                label.Text=$"Captured {frame+1} sections · Escape to finish";
            }
        }
        catch(OperationCanceledException) when(combined!=null){status.Text="Scrolling stopped; completed frames kept.";}
        finally
        {
            completed=true;progress?.Close();
            if(bridge!=null){try{await bridge.Stop();}catch(Exception e){status.Text=e.Message;}bridge.Dispose();}
            previous?.Dispose();DesktopIntegration.Show(this);
            if(combined!=null){try{SetDocument(new CaptureDocument(combined){Title="Scrolling capture"});await Copy();status.Text="Scrolling capture saved and copied.";}finally{combined.Dispose();}}
            if(File.Exists(framePath))File.Delete(framePath);
        }
    }
}
