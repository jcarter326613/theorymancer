using System.Text.RegularExpressions;

namespace Theorymancer.GuildWars2.Desktop.Ocr;

public enum FrameMatchDecision
{
    Initial,
    Overlap,
    NoOverlap,
    Ambiguous,
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
    private const double CandidateLineSimilarity = 0.72;
    private const double ConfidentOverlapSimilarity = 0.80;
    private const double ConfidentSingleLineSimilarity = 0.96;
    private const double ConfidentNoOverlapSimilarity = 0.42;
    private readonly List<RecognizedCombatLogLine> _history = [];

    public FrameMatchResult Match(IReadOnlyList<RecognizedCombatLogLine> current)
    {
        if (_history.Count == 0)
        {
            ReplaceHistory(current);
            return new FrameMatchResult(FrameMatchDecision.Initial, current, 0, 1, 0);
        }

        var bestLineSimilarity = 0.0;
        Candidate? bestCandidate = null;
        for (var historyIndex = _history.Count - 1; historyIndex >= 0; historyIndex--)
        {
            for (var currentIndex = current.Count - 1; currentIndex >= 0; currentIndex--)
            {
                var similarity = LineSimilarity(_history[historyIndex], current[currentIndex]);
                bestLineSimilarity = Math.Max(bestLineSimilarity, similarity);
                if (similarity < CandidateLineSimilarity)
                {
                    continue;
                }

                var candidate = ExpandCandidate(current, historyIndex, currentIndex, similarity);
                if (bestCandidate is null || candidate.IsBetterThan(bestCandidate))
                {
                    bestCandidate = candidate;
                }
            }
        }

        if (bestCandidate is not null && IsConfidentOverlap(bestCandidate))
        {
            var nextCurrentIndex = bestCandidate.CurrentEnd + 1;
            var nextHistoryIndex = bestCandidate.HistoryEnd + 1;
            while (nextCurrentIndex < current.Count && nextHistoryIndex < _history.Count &&
                    AreAdjacent(_history[nextHistoryIndex - 1], _history[nextHistoryIndex]) &&
                    AreAdjacent(current[nextCurrentIndex - 1], current[nextCurrentIndex]) &&
                    LineSimilarity(_history[nextHistoryIndex], current[nextCurrentIndex]) >= CandidateLineSimilarity)
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
                bestCandidate.AverageSimilarity,
                bestLineSimilarity);
        }

        if (bestLineSimilarity < ConfidentNoOverlapSimilarity)
        {
            ReplaceHistory(current);
            return new FrameMatchResult(FrameMatchDecision.NoOverlap, current, 0, 1 - bestLineSimilarity, bestLineSimilarity);
        }

        return new FrameMatchResult(FrameMatchDecision.Ambiguous, [], 0, 0, bestLineSimilarity);
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
            if (previousSimilarity < CandidateLineSimilarity)
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
            if (nextSimilarity < CandidateLineSimilarity)
            {
                break;
            }

            historyEnd++;
            currentEnd++;
            similaritySum += nextSimilarity;
            count++;
        }

        return new Candidate(historyStart, historyEnd, currentStart, currentEnd, count, similaritySum / count);
    }

    private static bool IsConfidentOverlap(Candidate candidate) =>
        candidate.MatchedLineCount >= 2 && candidate.AverageSimilarity >= ConfidentOverlapSimilarity ||
        candidate.MatchedLineCount == 1 && candidate.AverageSimilarity >= ConfidentSingleLineSimilarity;

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
        if (!ColorsAreCompatible(left.ColorClass, right.ColorClass))
        {
            return 0;
        }

        var normalizedLeft = Normalize(left.Text);
        var normalizedRight = Normalize(right.Text);
        if (normalizedLeft == normalizedRight)
        {
            return 1;
        }

        var maximumLength = Math.Max(normalizedLeft.Length, normalizedRight.Length);
        if (maximumLength == 0)
        {
            return 1;
        }

        var editDistance = LevenshteinDistance(normalizedLeft, normalizedRight);
        var similarity = 1 - (double)editDistance / maximumLength;
        var leftNumbers = NumberTokens().Matches(normalizedLeft).Select(match => match.Value).ToList();
        var rightNumbers = NumberTokens().Matches(normalizedRight).Select(match => match.Value).ToList();
        if (leftNumbers.Count > 0 && rightNumbers.Count > 0)
        {
            similarity += leftNumbers.SequenceEqual(rightNumbers) ? 0.12 : -0.25;
        }

        return Math.Clamp(similarity, 0, 1);
    }

    private static bool ColorsAreCompatible(string left, string right) =>
        string.Equals(left, "unknown", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(right, "unknown", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

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
        double AverageSimilarity)
    {
        public bool IsBetterThan(Candidate other) =>
            MatchedLineCount > other.MatchedLineCount ||
            MatchedLineCount == other.MatchedLineCount && AverageSimilarity > other.AverageSimilarity ||
            MatchedLineCount == other.MatchedLineCount && AverageSimilarity == other.AverageSimilarity && HistoryEnd > other.HistoryEnd;
    }
}
