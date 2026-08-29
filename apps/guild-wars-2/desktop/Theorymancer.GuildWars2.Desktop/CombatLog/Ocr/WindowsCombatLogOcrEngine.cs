using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;

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

    public async Task<IReadOnlyList<RecognizedCombatLogLine>> RecognizeAsync(
        CapturedFrame sourceFrame,
        CapturedFrame ocrFrame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = new SoftwareBitmap(
            BitmapPixelFormat.Bgra8,
            ocrFrame.Width,
            ocrFrame.Height,
            BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(CryptographicBuffer.CreateFromByteArray(ocrFrame.BgraPixels));
        var result = await _engine.RecognizeAsync(bitmap);
        cancellationToken.ThrowIfCancellationRequested();

        var frameHash = FrameHasher.Fnv1a64(sourceFrame.BgraPixels);
        var visualRows = result.Lines.Select(line => new OcrVisualRow(
            line.Text,
            line.Words.Select(word => new RecognizedWord(
                word.Text,
                word.BoundingRect.X / CombatLogImagePreprocessor.ScaleFactor,
                word.BoundingRect.Y / CombatLogImagePreprocessor.ScaleFactor,
                word.BoundingRect.Width / CombatLogImagePreprocessor.ScaleFactor,
                word.BoundingRect.Height / CombatLogImagePreprocessor.ScaleFactor)).ToList())).ToList();
        return CombatLogVisualRowAssembler.Assemble(
            sourceFrame.QpcTimestamp,
            frameHash,
            visualRows,
            words => CombatLogColorClassifier.Classify(sourceFrame, words));
    }
}
