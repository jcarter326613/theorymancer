using System.Text.RegularExpressions;

namespace Theorymancer.GuildWars2.Desktop.Ocr;

public enum FrameMatchDecision
{
    Initial,
    Overlap,
    NoOverlap,
    Ambiguous,
}

public enum FrameMatchFeature
{
    CandidateMinimumLineEvidence,
    MatchingNumberBonus,
    DifferentNumberPenalty,
    MatchingKnownColorBonus,
    DifferentKnownColorPenalty,
    LineEvidence,
    SequenceLength,
    RowOffsetConsistency,
    CurrentPrefixCoverage,
    OverlapConfidence,
}

public sealed record FrameMatchResult(
    FrameMatchDecision Decision,
    IReadOnlyList<RecognizedCombatLogLine> LinesToEmit,
    int MatchedLineCount,
    double Confidence,
    double BestLineSimilarity);

public sealed partial class CombatLogFrameMatcher
{
    private const int MaximumHistoryLines = 200;
    private readonly List<RecognizedCombatLogLine> _history = [];

    public static IDictionary<FrameMatchFeature, double> FeatureWeights { get; } =
        new Dictionary<FrameMatchFeature, double>
        {
            [FrameMatchFeature.CandidateMinimumLineEvidence] = 0.72,
            [FrameMatchFeature.MatchingNumberBonus] = 0.12,
            [FrameMatchFeature.DifferentNumberPenalty] = -0.28,
            [FrameMatchFeature.MatchingKnownColorBonus] = 0.08,
            [FrameMatchFeature.DifferentKnownColorPenalty] = -0.36,
            [FrameMatchFeature.LineEvidence] = 0.50,
            [FrameMatchFeature.SequenceLength] = 0.20,
            [FrameMatchFeature.RowOffsetConsistency] = 0.25,
            [FrameMatchFeature.CurrentPrefixCoverage] = 0.05,
            [FrameMatchFeature.OverlapConfidence] = 0.85,
        };

    public FrameMatchResult Match(IReadOnlyList<RecognizedCombatLogLine> current)
    {
        if (_history.Count == 0)
        {
            ReplaceHistory(current);
            return new FrameMatchResult(FrameMatchDecision.Initial, current, 0, 1, 0);
        }

        var bestLineSimilarity = 0.0;
        var candidates = new List<Candidate>();
        var candidateRowOffsets = new Dictionary<int, int>();
        var candidateLineCount = 0;
        for (var historyIndex = _history.Count - 1; historyIndex >= 0; historyIndex--)
        {
            for (var currentIndex = current.Count - 1; currentIndex >= 0; currentIndex--)
            {
                var similarity = LineSimilarity(_history[historyIndex], current[currentIndex]);
                bestLineSimilarity = Math.Max(bestLineSimilarity, similarity);
                if (similarity < Weight(FrameMatchFeature.CandidateMinimumLineEvidence))
                {
                    continue;
                }

                var rowOffset = current[currentIndex].RowIndex - _history[historyIndex].RowIndex;
                candidateRowOffsets[rowOffset] = candidateRowOffsets.GetValueOrDefault(rowOffset) + 1;
                candidateLineCount++;
                candidates.Add(ExpandCandidate(current, historyIndex, currentIndex, similarity));
            }
        }

        Candidate? bestCandidate = null;
        foreach (var candidate in candidates)
        {
            var scoredCandidate = ScoreCandidate(candidate, current, candidateRowOffsets, candidateLineCount);
            if (bestCandidate is null || scoredCandidate.IsBetterThan(bestCandidate))
            {
                bestCandidate = scoredCandidate;
            }
        }

        if (bestCandidate is not null && IsConfidentOverlap(bestCandidate))
        {
            var nextCurrentIndex = bestCandidate.CurrentEnd + 1;
            var nextHistoryIndex = bestCandidate.HistoryEnd + 1;
            while (nextCurrentIndex < current.Count && nextHistoryIndex < _history.Count &&
                    AreAdjacent(_history[nextHistoryIndex - 1], _history[nextHistoryIndex]) &&
                    AreAdjacent(current[nextCurrentIndex - 1], current[nextCurrentIndex]) &&
                    LineSimilarity(_history[nextHistoryIndex], current[nextCurrentIndex]) >= Weight(FrameMatchFeature.CandidateMinimumLineEvidence))
            {
                nextCurrentIndex++;
                nextHistoryIndex++;
            }

            var newLines = current.Skip(nextCurrentIndex).ToList();
            AppendHistory(newLines);
            return new FrameMatchResult(
                FrameMatchDecision.Overlap,
                newLines,
                bestCandidate.MatchedLineCount,
                bestCandidate.Confidence,
                bestLineSimilarity);
        }

        ReplaceHistory(current);
        return new FrameMatchResult(FrameMatchDecision.NoOverlap, current, 0, 1 - bestLineSimilarity, bestLineSimilarity);
    }

    private Candidate ExpandCandidate(IReadOnlyList<RecognizedCombatLogLine> current, int historyIndex, int currentIndex, double similarity)
    {
        var historyStart = historyIndex;
        var currentStart = currentIndex;
        var historyEnd = historyIndex;
        var currentEnd = currentIndex;
        var similaritySum = similarity;
        var count = 1;

        while (historyStart > 0 && currentStart > 0)
        {
            if (!AreAdjacent(_history[historyStart - 1], _history[historyStart]) ||
                !AreAdjacent(current[currentStart - 1], current[currentStart]))
            {
                break;
            }

            var previousSimilarity = LineSimilarity(_history[historyStart - 1], current[currentStart - 1]);
            if (previousSimilarity < Weight(FrameMatchFeature.CandidateMinimumLineEvidence))
            {
                break;
            }

            historyStart--;
            currentStart--;
            similaritySum += previousSimilarity;
            count++;
        }

        while (historyEnd + 1 < _history.Count && currentEnd + 1 < current.Count)
        {
            if (!AreAdjacent(_history[historyEnd], _history[historyEnd + 1]) ||
                !AreAdjacent(current[currentEnd], current[currentEnd + 1]))
            {
                break;
            }

            var nextSimilarity = LineSimilarity(_history[historyEnd + 1], current[currentEnd + 1]);
            if (nextSimilarity < Weight(FrameMatchFeature.CandidateMinimumLineEvidence))
            {
                break;
            }

            historyEnd++;
            currentEnd++;
            similaritySum += nextSimilarity;
            count++;
        }

        return new Candidate(
            historyStart,
            historyEnd,
            currentStart,
            currentEnd,
            count,
            similaritySum / count,
            current[currentStart].RowIndex - _history[historyStart].RowIndex,
            0);
    }

    private static Candidate ScoreCandidate(
        Candidate candidate,
        IReadOnlyList<RecognizedCombatLogLine> current,
        IReadOnlyDictionary<int, int> candidateRowOffsets,
        int candidateLineCount)
    {
        var rowOffsetConsistency = candidate.MatchedLineCount == 1
            ? (double)candidateRowOffsets.GetValueOrDefault(candidate.RowOffset) / candidateLineCount
            : 1;
        var sequenceEvidence = 1 - Math.Exp(-candidate.MatchedLineCount);
        var currentPrefixCoverage = 1 - (double)candidate.CurrentStart / current.Count;
        var confidence =
            candidate.AverageSimilarity * Weight(FrameMatchFeature.LineEvidence) +
            sequenceEvidence * Weight(FrameMatchFeature.SequenceLength) +
            rowOffsetConsistency * Weight(FrameMatchFeature.RowOffsetConsistency) +
            currentPrefixCoverage * Weight(FrameMatchFeature.CurrentPrefixCoverage);

        return candidate with { Confidence = Math.Clamp(confidence, 0, 1) };
    }

    private static bool IsConfidentOverlap(Candidate candidate) =>
        candidate.Confidence >= Weight(FrameMatchFeature.OverlapConfidence);

    private static double Weight(FrameMatchFeature feature) => FeatureWeights[feature];

    private void AppendHistory(IEnumerable<RecognizedCombatLogLine> lines)
    {
        _history.AddRange(lines);
        if (_history.Count > MaximumHistoryLines)
        {
            _history.RemoveRange(0, _history.Count - MaximumHistoryLines);
        }
    }

    private void ReplaceHistory(IEnumerable<RecognizedCombatLogLine> lines)
    {
        _history.Clear();
        AppendHistory(lines);
    }

    private static bool AreAdjacent(RecognizedCombatLogLine first, RecognizedCombatLogLine second) =>
        second.RowIndex == first.RowIndex + 1;

    private static double LineSimilarity(RecognizedCombatLogLine left, RecognizedCombatLogLine right)
    {
        var normalizedLeft = Normalize(left.Text);
        var normalizedRight = Normalize(right.Text);
        double similarity;
        if (normalizedLeft == normalizedRight)
        {
            similarity = 1;
        }
        else
        {
            var maximumLength = Math.Max(normalizedLeft.Length, normalizedRight.Length);
            if (maximumLength == 0)
            {
                similarity = 1;
            }
            else
            {
                var editDistance = LevenshteinDistance(normalizedLeft, normalizedRight);
                similarity = 1 - (double)editDistance / maximumLength;
            }
        }

        var leftNumbers = NumberTokens().Matches(normalizedLeft).Select(match => match.Value).ToList();
        var rightNumbers = NumberTokens().Matches(normalizedRight).Select(match => match.Value).ToList();
        if (leftNumbers.Count > 0 && rightNumbers.Count > 0)
        {
            similarity += leftNumbers.SequenceEqual(rightNumbers)
                ? Weight(FrameMatchFeature.MatchingNumberBonus)
                : Weight(FrameMatchFeature.DifferentNumberPenalty);
        }

        similarity += ColorEvidence(left.ColorClass, right.ColorClass);
        return Math.Clamp(similarity, 0, 1);
    }

    private static double ColorEvidence(string left, string right)
    {
        if (string.Equals(left, "unknown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(right, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? Weight(FrameMatchFeature.MatchingKnownColorBonus)
            : Weight(FrameMatchFeature.DifferentKnownColorPenalty);
    }

    private static string Normalize(string text) => Whitespace().Replace(text.Trim().ToLowerInvariant(), " ");

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    [GeneratedRegex(@"\d+(?:,\d+)*")]
    private static partial Regex NumberTokens();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record Candidate(
        int HistoryStart,
        int HistoryEnd,
        int CurrentStart,
        int CurrentEnd,
        int MatchedLineCount,
        double AverageSimilarity,
        int RowOffset,
        double Confidence)
    {
        public bool IsBetterThan(Candidate other) =>
            Confidence > other.Confidence ||
            Confidence == other.Confidence && MatchedLineCount > other.MatchedLineCount ||
            Confidence == other.Confidence && MatchedLineCount == other.MatchedLineCount && AverageSimilarity > other.AverageSimilarity ||
            Confidence == other.Confidence && MatchedLineCount == other.MatchedLineCount && AverageSimilarity == other.AverageSimilarity && CurrentStart < other.CurrentStart;
    }
}
