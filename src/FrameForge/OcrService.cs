using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace FrameForge;
public static class OcrService
{
    public static async Task<string> RecognizeAsync(BitmapSource image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage).FirstOrDefault(x => x != null);
        if (engine == null)
            throw new InvalidOperationException("No Windows OCR language is installed. Add a language in Windows Settings → Time & language → Language & region, then try again.");
        int max = (int)OcrEngine.MaxImageDimension;
        if (image.PixelWidth > max || image.PixelHeight > max)
        {
            double scale = (double)max / Math.Max(image.PixelWidth, image.PixelHeight);
            image = Imaging.Resize(image, Math.Max(1, (int)(image.PixelWidth * scale)), Math.Max(1, (int)(image.PixelHeight * scale)));
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(Imaging.Png(image));
            await writer.StoreAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }
}
