using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;

public sealed record PreprocessedCombatLogFrame(CapturedFrame Frame);

public static class CombatLogImagePreprocessor
{
    public const int ScaleFactor = 3;

    public static PreprocessedCombatLogFrame Process(CapturedFrame source)
    {
        return new PreprocessedCombatLogFrame(NearestNeighborFrameScaler.Scale(source, ScaleFactor));
    }
}
