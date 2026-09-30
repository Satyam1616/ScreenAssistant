using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScreenAssistant.Services;

/// <summary>
/// On-device OCR using the built-in Windows engine (Windows.Media.Ocr).
/// No network, no external model — ships with Windows 10/11.
/// </summary>
public sealed class OcrService
{
    private readonly OcrEngine? _engine;

    public OcrService()
    {
        // Prefer the user's configured languages; fall back to en-US.
        _engine = OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
    }

    public bool IsAvailable => _engine is not null;

    /// <summary>
    /// Runs OCR over a GDI bitmap and returns text with one line per OCR line.
    /// </summary>
    public async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        if (_engine is null)
            throw new InvalidOperationException(
                "No OCR language is installed. Add one under Windows Settings → " +
                "Time & language → Language & region.");

        // GDI Bitmap → PNG bytes → WinRT SoftwareBitmap (Bgra8).
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            bitmap.Save(ms, ImageFormat.Png);
            bytes = ms.ToArray();
        }

        using var ras = new InMemoryRandomAccessStream();
        var writer = new DataWriter(ras);
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        ras.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var software = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await _engine.RecognizeAsync(software);
        return string.Join("\n", result.Lines.Select(l => l.Text));
    }
}
