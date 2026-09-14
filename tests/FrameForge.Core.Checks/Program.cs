using System.Reflection;
using System.Text.Json;
using FrameForge.Core;
using SkiaSharp;
if(args.FirstOrDefault()=="--echo"){Console.Write(JsonSerializer.Serialize(args.Skip(1)));return 0;}
if(args.FirstOrDefault()=="--wait"){await Task.Delay(60000);return 0;}
var root=Path.GetFullPath(args.FirstOrDefault()??Path.Combine("artifacts","desktop-checks"));
Directory.CreateDirectory(root);
int passed=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL: "+name);passed++;Console.WriteLine("PASS: "+name);}
void Throws(Action action,string name){try{action();}catch(Exception e) when(e is InvalidDataException or ArgumentException or JsonException){Check(true,name);return;}throw new Exception("FAIL: "+name);}
using var sample=new SKBitmap(180,120);
using(var c=new SKCanvas(sample)){c.Clear(SKColors.White);using var p=new SKPaint{Color=SKColors.Red};c.DrawRect(0,0,90,120,p);}
using var doc=new CaptureDocument(sample);
Check(doc.Image.GetPixel(4,4)==SKColors.Red,"document owns an exact source copy");
doc.Checkpoint();doc.Marks.Add(new(){Kind=Tool.Rectangle,X=10,Y=10,X2=70,Y2=70,Filled=true,Color="#FF0000FF"});
using(var rendered=doc.Render())Check(rendered.GetPixel(25,25)==SKColors.Blue,"filled annotation render");
doc.Undo();Check(doc.Marks.Count==0&&doc.CanRedo,"undo restores document");
doc.Redo();Check(doc.Marks.Count==1,"redo restores annotation");
doc.Checkpoint();doc.Marks.Add(new(){Kind=Tool.Redact,X=20,Y=20,X2=65,Y2=65});
using(var rendered=doc.Render())Check(rendered.GetPixel(30,30)==SKColors.Black,"opaque redaction covers earlier marks");
using(var fractional=new CaptureDocument(sample))
{
    fractional.Marks.Add(new(){Kind=Tool.Redact,X=20.6,Y=20.6,X2=21.1,Y2=21.1});
    using var pixels=fractional.Render();Check(pixels.GetPixel(20,20)==SKColors.Black&&pixels.GetPixel(21,21)==SKColors.Black,"redaction covers fractional boundary pixels");
}
var project=Path.Combine(root,"compatibility.ffg");doc.Save(project);
using(var loaded=CaptureDocument.Load(project))
{Check(loaded.Marks.Count==2&&loaded.Image.Width==180,"version-1 project round trip");Check(loaded.Image.GetPixel(30,30)==SKColors.Red,"editable project retains original pixels");}
Check(!doc.Dirty,"save clears dirty state");
doc.Crop(new SKRect(20,20,60,60));Check(doc.Image.Width==40&&doc.Image.Height==40&&doc.Marks.Count==0,"crop flattens annotations");
Check(doc.Image.GetPixel(5,5)==SKColors.Black,"crop preserves redaction");
doc.Undo();Check(doc.Image.Width==180&&doc.Marks.Count==2,"crop undo restores editable marks");
doc.Rotate();Check(doc.Image.Width==120&&doc.Image.Height==180,"clockwise rotation dimensions");
Check(doc.Image.GetPixel(119,0)==SKColors.Red,"clockwise rotation pixel orientation");
doc.Undo();doc.Resize(90,60);Check(doc.Image.Width==90&&doc.Image.Height==60,"resize dimensions");doc.Undo();
Throws(()=>doc.Resize(0,20),"zero size rejected");Throws(()=>doc.Resize(100001,1000),"oversize rejected");
using(var outside=Imaging.Crop(sample,new SKRect(-50,-50,10,10)))Check(outside.Width==10&&outside.Height==10,"crop clamps negative coordinates");
Throws(()=>Imaging.Crop(sample,new SKRect(500,500,520,520)),"empty crop rejected");
foreach(var extension in new[]{".png",".jpg",".webp"})
{
    var path=Path.Combine(root,"export"+extension);Imaging.Export(sample,path);using var decoded=Imaging.Load(path);
    Check(decoded.Width==180&&decoded.Height==120,"export and decode "+extension);
}
Throws(()=>Imaging.Export(sample,Path.Combine(root,"bad.gif")),"unsupported export format rejected");
Throws(()=>Imaging.Load(new byte[]{1,2,3,4}),"invalid image rejected");
foreach(var tool in Enum.GetValues<Tool>().Where(t=>t is not(Tool.Select or Tool.Crop)))
{
    using var d=new CaptureDocument(sample);d.Marks.Add(new(){Kind=tool,X=15,Y=15,X2=90,Y2=90,Text="1",Points=new(){new[]{15d,15d},new[]{60d,70d}}});
    using var rendered=d.Render();Check(rendered.Width==180,"renders "+tool);
}
var legacy=new ProjectFile{Title="Windows fixture",Image=Convert.ToBase64String(Imaging.Png(sample)),Marks=new(){new(){Kind=Tool.Callout,X=10,Y=10,X2=100,Y2=90,Text="Portable"}}};
var fixture=Path.Combine(root,"windows-format.ffg");AtomicFile.Write(fixture,JsonSerializer.SerializeToUtf8Bytes(legacy));
using(var loaded=CaptureDocument.Load(fixture))Check(loaded.Marks[0].Kind==Tool.Callout&&loaded.Title=="Windows fixture","Windows schema compatibility");
void Reject(ProjectFile data,string name){File.WriteAllText(fixture,JsonSerializer.Serialize(data));Throws(()=>CaptureDocument.Load(fixture).Dispose(),name);}
legacy.Version=99;Reject(legacy,"future project version rejected");legacy.Version=1;
legacy.Marks[0].Color="invalid";Reject(legacy,"invalid color rejected");legacy.Marks[0].Color="#FF000000";
legacy.Marks[0].Points=new(){new[]{2d}};Reject(legacy,"malformed points rejected");legacy.Marks[0].Points=new();
legacy.Marks[0].Kind=(Tool)999;Reject(legacy,"invalid tool rejected");
using var page=new SKBitmap(120,320);var random=new Random(17);
for(int y=0;y<page.Height;y++)for(int x=0;x<page.Width;x++)page.SetPixel(x,y,new SKColor((byte)random.Next(256),(byte)random.Next(256),(byte)random.Next(256)));
using var before=Imaging.Crop(page,new SKRect(0,0,120,200));using var after=Imaging.Crop(page,new SKRect(0,90,120,290));
var match=Stitcher.FindShift(before,after);Check(match.Confident&&match.Shift==90,"scroll overlap detects actual shift");
Check(Stitcher.FindShift(before,before).Duplicate,"duplicate scroll frame detected");
using(var stitched=Stitcher.Append(before,after,match.Shift))
{Check(stitched.Height==290,"stitch output dimensions");Check(stitched.GetPixel(40,250)==page.GetPixel(40,250),"stitch preserves bottom content");}
using var unrelated=new SKBitmap(120,200);unrelated.Erase(SKColors.Black);
Check(!Stitcher.FindShift(before,unrelated).Confident,"unrelated scrolling frame rejected");
Throws(()=>Stitcher.Append(before,after,0),"invalid stitching shift rejected");
using var tiny=new SKBitmap(2,2);Throws(()=>Stitcher.FindShift(tiny,tiny),"tiny scrolling input rejected");
var atomic=Path.Combine(root,"atomic.txt");AtomicFile.Write(atomic,new byte[]{1});AtomicFile.Write(atomic,new byte[]{2,3});
Check(File.ReadAllBytes(atomic).SequenceEqual(new byte[]{2,3}),"atomic replacement");
Check(Directory.GetFiles(root,"*.tmp").Length==0,"no temporary writes left behind");
var executable=Environment.ProcessPath!;
var prefix=Path.GetFileNameWithoutExtension(executable).Equals("dotnet",StringComparison.OrdinalIgnoreCase)?new[]{Assembly.GetExecutingAssembly().Location}:Array.Empty<string>();
var exact=new[]{"spaces here","quote\"here","$(not a command)","a;b","Unicode ✓",""};
var result=await ProcessRunner.Run(executable,prefix.Concat(new[]{"--echo"}).Concat(exact));
Check(result.ExitCode==0&&JsonSerializer.Deserialize<string[]>(result.Output)!.SequenceEqual(exact),"process arguments remain literal");
using(var cancel=new CancellationTokenSource(150))
{
    bool canceled=false;try{await ProcessRunner.Run(executable,prefix.Concat(new[]{"--wait"}),cancel.Token);}catch(OperationCanceledException){canceled=true;}
    Check(canceled,"external process cancellation");
}

// Capture deletion is reversible and preserves companion pixels and unrelated exports.
var libraryRoot=Path.Combine(root,"capture-library-"+Guid.NewGuid().ToString("N"));
var library=new CaptureLibrary(libraryRoot);
var capturePath=Path.Combine(library.Folder,"capture.ffg");
var pngPath=Path.ChangeExtension(capturePath,".png");
var unrelatedPath=Path.Combine(library.Folder,"separate-export.jpg");
File.WriteAllBytes(capturePath,new byte[]{4,5,6});File.WriteAllBytes(pngPath,new byte[]{7,8,9});File.WriteAllBytes(unrelatedPath,new byte[]{10});
var deleted=library.Delete(capturePath);
Check(!File.Exists(capturePath)&&!File.Exists(pngPath),"delete removes both capture files from recent library");
Check(File.ReadAllBytes(Path.Combine(deleted.Folder,"capture.ffg")).SequenceEqual(new byte[]{4,5,6}),"deleted project is recoverable");
Check(File.Exists(unrelatedPath),"delete preserves unrelated exports");
library.Restore(deleted);
Check(File.ReadAllBytes(capturePath).SequenceEqual(new byte[]{4,5,6})&&File.ReadAllBytes(pngPath).SequenceEqual(new byte[]{7,8,9}),"undo restores project and image exactly");
Check(!Directory.Exists(deleted.Folder),"undo cleans the empty recovery directory");
void Refuses(Action action,string name)
{try{action();}catch(Exception e) when(e is InvalidOperationException or IOException){Check(true,name);return;}throw new Exception("FAIL: "+name);}
Refuses(()=>library.Delete(unrelatedPath),"delete refuses non-capture exports");
var outsidePath=Path.Combine(libraryRoot,"outside.ffg");File.WriteAllText(outsidePath,"outside");
Refuses(()=>library.Delete(outsidePath),"delete refuses a project outside this library");
deleted=library.Delete(capturePath);File.WriteAllText(capturePath,"new capture");
Refuses(()=>library.Restore(deleted),"undo refuses to overwrite a newer file");
Check(File.ReadAllText(capturePath)=="new capture","undo conflict preserves the newer capture");
File.Delete(capturePath);library.Restore(deleted);
var recordingPath=Path.Combine(library.Folder,"recording.mp4");File.WriteAllBytes(recordingPath,new byte[]{11,12});
var deletedVideo=library.Delete(recordingPath);library.Restore(deletedVideo);
Check(File.ReadAllBytes(recordingPath).SequenceEqual(new byte[]{11,12}),"recordings without previews support delete and undo");
Refuses(()=>library.Restore(new DeletedCapture(libraryRoot,new[]{"../outside.ffg"})),"undo rejects recovery paths outside its directory");

var reversed=new Mark{Kind=Tool.Arrow,X=80,Y=90,X2=20,Y2=30};
var resized=reversed.Clone();AnnotationGeometry.Resize(resized,reversed,new SKRect(10,20,130,140));
Check(resized.X==130&&resized.Y==140&&resized.X2==10&&resized.Y2==20,"resize preserves reversed arrow direction");
var pen=new Mark{Kind=Tool.Pen,X=10,Y=10,X2=30,Y2=30,Points=new(){new[]{10d,10d},new[]{20d,25d},new[]{30d,30d}}};
var resizedPen=pen.Clone();AnnotationGeometry.Resize(resizedPen,pen,new SKRect(0,0,40,60));
Check(resizedPen.Points[1][0]==20&&resizedPen.Points[1][1]==45,"resize scales freehand points with their bounds");
Check(pen.Points[1][0]==20&&pen.Points[1][1]==25,"resize keeps the gesture's source geometry unchanged");
var textMark=new Mark{Kind=Tool.Text,X=0,Y=0,X2=100,Y2=50,FontSize=20};
var biggerText=textMark.Clone();AnnotationGeometry.Resize(biggerText,textMark,new SKRect(0,0,200,100));
Check(biggerText.FontSize==40,"text size follows annotation resize");
AnnotationGeometry.Resize(biggerText,textMark,new SKRect(0,0,1,1));
Check(biggerText.FontSize==40,"collapsed resize does not corrupt the annotation");
Console.WriteLine($"{passed} checks passed.");
File.WriteAllText(Path.Combine(root,"results.txt"),$"{passed} checks passed on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}).\n");
return 0;
