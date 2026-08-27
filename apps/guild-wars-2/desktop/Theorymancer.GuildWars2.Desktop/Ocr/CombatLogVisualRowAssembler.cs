namespace Theorymancer.GuildWars2.Desktop.Ocr;

public sealed record OcrVisualRow(string Text, IReadOnlyList<RecognizedWord> Words);

public static class CombatLogVisualRowAssembler
{
    public static IReadOnlyList<RecognizedCombatLogLine> Assemble(
        long firstSeenQpc,
        ulong pixelHash,
        IEnumerable<OcrVisualRow> visualRows,
        Func<IReadOnlyList<RecognizedWord>, string> classifyColor)
    {
        var recognizedLines = new List<RecognizedCombatLogLine>();
        var physicalRow = new List<OcrVisualRow>();
        foreach (var visualRow in visualRows)
        {
            var text = visualRow.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var fragment = new OcrVisualRow(text, visualRow.Words);
            if (fragment.Words.Count == 0)
            {
                AddPhysicalRow(recognizedLines, firstSeenQpc, pixelHash, physicalRow, classifyColor);
                physicalRow.Clear();
                AddPhysicalRow(recognizedLines, firstSeenQpc, pixelHash, [fragment], classifyColor);
                continue;
            }

            if (physicalRow.Count > 0 && !AreNeighbors(physicalRow[^1], fragment))
            {
                AddPhysicalRow(recognizedLines, firstSeenQpc, pixelHash, physicalRow, classifyColor);
                physicalRow.Clear();
            }

            physicalRow.Add(fragment);
        }

        AddPhysicalRow(recognizedLines, firstSeenQpc, pixelHash, physicalRow, classifyColor);
        return recognizedLines;
    }

    private static bool AreNeighbors(OcrVisualRow first, OcrVisualRow second)
    {
        var firstHeight = CharacterHeight(first);
        var secondHeight = CharacterHeight(second);
        var maximumBaselineDistance = Math.Max(firstHeight, secondHeight) / 2;
        return Math.Abs(Baseline(first) - Baseline(second)) <= maximumBaselineDistance;
    }

    private static double Baseline(OcrVisualRow row) => row.Words.Average(word => word.Y + word.Height);

    private static double CharacterHeight(OcrVisualRow row) => row.Words.Average(word => word.Height);

    private static void AddPhysicalRow(
        ICollection<RecognizedCombatLogLine> recognizedLines,
        long firstSeenQpc,
        ulong pixelHash,
        IEnumerable<OcrVisualRow> fragments,
        Func<IReadOnlyList<RecognizedWord>, string> classifyColor)
    {
        var orderedFragments = fragments
            .OrderBy(fragment => fragment.Words.Count == 0 ? double.MinValue : fragment.Words.Min(word => word.X))
            .ToList();
        if (orderedFragments.Count == 0)
        {
            return;
        }

        var text = orderedFragments.Aggregate(
            string.Empty,
            (combined, fragment) => CombatLogTextNormalizer.AppendFragment(combined, fragment.Text));
        var words = orderedFragments
            .SelectMany(fragment => fragment.Words)
            .OrderBy(word => word.X)
            .ThenBy(word => word.Y)
            .ToList();
        recognizedLines.Add(new RecognizedCombatLogLine(
            firstSeenQpc,
            recognizedLines.Count,
            pixelHash,
            CombatLogTextNormalizer.NormalizeVisualRow(text),
            classifyColor(words),
            words));
    }
}
