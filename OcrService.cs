using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace QuickOneNote;

/// <summary>Offline text recognition using the built-in Windows OCR engine (no cloud, no NuGet).</summary>
internal static class OcrService
{
    public static async Task<string> RecognizeAsync(byte[] png)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
            throw new InvalidOperationException(
                "No OCR language is installed. Add one in Windows Settings → Time & language → Language & region.");

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        if (software.PixelWidth > OcrEngine.MaxImageDimension || software.PixelHeight > OcrEngine.MaxImageDimension)
            throw new InvalidOperationException(
                $"The image is too large for OCR ({software.PixelWidth}×{software.PixelHeight}; max {OcrEngine.MaxImageDimension}). Snip a smaller area.");

        var result = await engine.RecognizeAsync(software);
        return result.Text ?? "";
    }
}
