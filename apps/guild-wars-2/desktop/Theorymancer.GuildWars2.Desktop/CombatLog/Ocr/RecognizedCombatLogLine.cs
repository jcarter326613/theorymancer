using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;

public sealed record RecognizedCombatLogLine(
    long FirstSeenQpc,
    int RowIndex,
    ulong PixelHash,
    string Text,
    string ColorClass,
    IReadOnlyList<RecognizedWord> Words);

public sealed record RecognizedWord(string Text, double X, double Y, double Width, double Height);

public interface ICombatLogOcrEngine
{
    Task<IReadOnlyList<RecognizedCombatLogLine>> RecognizeAsync(
        CapturedFrame sourceFrame,
        CapturedFrame ocrFrame,
        CancellationToken cancellationToken);
}
