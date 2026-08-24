using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Ocr;

public sealed class WindowsCombatLogOcrEngine : ICombatLogOcrEngine
{
    private readonly OcrEngine _engine;

    private WindowsCombatLogOcrEngine(OcrEngine engine)
    {
        _engine = engine;
    }

    public static WindowsCombatLogOcrEngine CreateEnglish()
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

        return new WindowsCombatLogOcrEngine(engine);
    }

    public async Task<RecognizedCombatLogLine?> RecognizeAsync(ChangedRow row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            row.Width,
            row.Height,
            BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(row.BgraPixels));
        var result = await _engine.RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var text = result.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var words = result.Lines
            .SelectMany(line => line.Words)
            .Select(word => new RecognizedWord(
                word.Text,
                word.BoundingRect.X,
                word.BoundingRect.Y,
                word.BoundingRect.Width,
                word.BoundingRect.Height))
            .ToList();

        return new RecognizedCombatLogLine(
            row.FirstSeenQpc,
            row.RowIndex,
            row.PixelHash,
            text,
            CombatLogColorClassifier.Classify(row.BgraPixels),
            words);
    }
}
