using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public sealed record HudOcrWord(string Text, double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;
}

public interface IHudOcrEngine
{
    Task<IReadOnlyList<HudOcrWord>> RecognizeWordsAsync(CapturedFrame frame, CancellationToken cancellationToken);
}

public sealed class WindowsHudOcrEngine : IHudOcrEngine
{
    private const int ScaleFactor = 3;
    private readonly OcrEngine _engine;

    private WindowsHudOcrEngine(OcrEngine engine)
    {
        _engine = engine;
    }

    public static WindowsHudOcrEngine CreateEnglish()
    {
        var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(candidate =>
            candidate.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (language is null)
        {
            throw new InvalidOperationException(
                "An English Windows OCR language pack is required. Install it in Windows Settings, then restart the collector.");
        }

        var engine = OcrEngine.TryCreateFromLanguage(new Language(language.LanguageTag));
        if (engine is null)
        {
            throw new InvalidOperationException("Windows could not start its English OCR engine.");
        }

        return new WindowsHudOcrEngine(engine);
    }

    public async Task<IReadOnlyList<HudOcrWord>> RecognizeWordsAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scaledFrame = NearestNeighborFrameScaler.Scale(frame, ScaleFactor);
        using var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            scaledFrame.Width,
            scaledFrame.Height,
            BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(scaledFrame.BgraPixels));
        var result = await _engine.RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        return result.Lines
            .SelectMany(line => line.Words)
            .Select(word => new HudOcrWord(
                word.Text,
                word.BoundingRect.X / ScaleFactor,
                word.BoundingRect.Y / ScaleFactor,
                word.BoundingRect.Width / ScaleFactor,
                word.BoundingRect.Height / ScaleFactor))
            .ToList();
    }
}
