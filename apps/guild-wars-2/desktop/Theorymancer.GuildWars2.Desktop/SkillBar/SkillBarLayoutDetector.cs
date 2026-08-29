using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public sealed record SkillBarLayoutDebugInfo(
    IReadOnlyList<HudOcrWord> RecognizedWords,
    IReadOnlyList<HudOcrWord> SelectedLabels,
    double? LabelSpacing,
    double? LabelConfidence,
    int? SquareSize,
    int? HorizontalOffset,
    int? SquareTop,
    double? BorderEvidence);

public sealed record SkillBarLayoutDetection(
    SkillBarLayout? Layout,
    double Confidence,
    string Message,
    SkillBarLayoutDebugInfo DebugInfo)
{
    public bool IsUsable => Layout is not null && Layout.HasWeaponSkillSlots;
}

public static class SkillBarLayoutDetector
{
    private const int WeaponSkillCount = 5;

    public static SkillBarLayoutDetection Detect(CapturedFrame frame, IReadOnlyList<HudOcrWord> words)
    {
        var recognizedWords = words
            .Where(word => word.CenterX >= 0 && word.CenterX <= frame.Width && word.CenterY >= 0 && word.CenterY <= frame.Height)
            .ToList();
        var labels = recognizedWords.Where(IsPotentialHotkey).ToList();
        var cluster = FindBestCluster(labels);
        if (cluster is null)
        {
            return new SkillBarLayoutDetection(
                null,
                0,
                "Could not find five evenly spaced skill labels. Redraw the crop so the weapon skills are clear and unobscured.",
                new SkillBarLayoutDebugInfo(recognizedWords, [], null, null, null, null, null, null));
        }

        var grid = FindBestGrid(frame, cluster);
        var components = grid.Bounds
            .Select((bounds, index) => SkillBarComponent.FromPixelBounds(
                (SkillBarComponentKind)index,
                bounds,
                frame.Width,
                frame.Height,
                grid.Confidence))
            .ToList();
        var confidence = Math.Clamp((cluster.Confidence + grid.Confidence) / 2, 0, 1);
        return new SkillBarLayoutDetection(
            new SkillBarLayout(components),
            confidence,
            confidence >= 0.75
                ? "Detected five weapon skill slots. Confirm that the green boxes cover the icon interiors."
                : "Detected a possible weapon skill row. Check the amber boxes before saving this layout.",
            new SkillBarLayoutDebugInfo(
                recognizedWords,
                cluster.Labels,
                cluster.Spacing,
                cluster.Confidence,
                grid.Size,
                grid.HorizontalOffset,
                grid.Top,
                grid.BorderEvidence));
    }

    private static bool IsPotentialHotkey(HudOcrWord word) =>
        !string.IsNullOrWhiteSpace(word.Text) &&
        word.Text.Length <= 12 &&
        word.Width > 0 &&
        word.Height > 0 &&
        word.Text.Any(char.IsLetterOrDigit);

    private static LabelCluster? FindBestCluster(IReadOnlyList<HudOcrWord> labels)
    {
        LabelCluster? best = null;
        foreach (var anchor in labels)
        {
            var sameRow = labels
                .Where(candidate => Math.Abs(candidate.CenterY - anchor.CenterY) <= Math.Max(candidate.Height, anchor.Height) * 1.25)
                .OrderBy(candidate => candidate.CenterX)
                .ToList();
            for (var start = 0; start <= sameRow.Count - WeaponSkillCount; start++)
            {
                var candidates = sameRow.Skip(start).Take(WeaponSkillCount).ToList();
                var spacings = candidates.Zip(candidates.Skip(1), (left, right) => right.CenterX - left.CenterX).ToList();
                var spacing = spacings.Average();
                if (spacing < Math.Max(12, candidates.Max(candidate => candidate.Width) * 1.5))
                {
                    continue;
                }

                var spacingDeviation = StandardDeviation(spacings) / spacing;
                var verticalDeviation = StandardDeviation(candidates.Select(candidate => candidate.CenterY)) /
                    Math.Max(1, candidates.Average(candidate => candidate.Height));
                var sizeDeviation = StandardDeviation(candidates.Select(candidate => candidate.Height)) /
                    Math.Max(1, candidates.Average(candidate => candidate.Height));
                var numberBonus = candidates.Select(candidate => candidate.Text.Trim()).SequenceEqual(["1", "2", "3", "4", "5"])
                    ? 0.2
                    : 0;
                var confidence = Math.Clamp(1 - spacingDeviation * 2 - verticalDeviation * 0.2 - sizeDeviation * 0.2 + numberBonus, 0, 1);
                var cluster = new LabelCluster(candidates, spacing, confidence);
                if (best is null || cluster.Confidence > best.Confidence)
                {
                    best = cluster;
                }
            }
        }

        return best is { Confidence: >= 0.45 } ? best : null;
    }

    private static GridCandidate FindBestGrid(CapturedFrame frame, LabelCluster cluster)
    {
        GridCandidate? best = null;
        var labelY = cluster.Labels.Average(label => label.CenterY);
        foreach (var sizeFactor in new[] { 0.65, 0.72, 0.79, 0.86, 0.93 })
        {
            var size = Math.Max(12, (int)Math.Round(cluster.Spacing * sizeFactor));
            for (var xOffset = -size / 4; xOffset <= size / 4; xOffset += Math.Max(1, size / 8))
            {
                var minimumTop = Math.Max(0, (int)Math.Round(labelY - size));
                var maximumTop = Math.Min(frame.Height - size, (int)Math.Round(labelY));
                for (var top = minimumTop; top <= maximumTop; top += Math.Max(1, size / 8))
                {
                    var bounds = cluster.Labels
                        .Select(label => new ScreenBounds(
                            (int)Math.Round(label.CenterX + xOffset - size / 2),
                            top,
                            size,
                            size))
                        .ToList();
                    if (bounds.Any(bound => bound.X < 0 || bound.Y < 0 || bound.Right > frame.Width || bound.Bottom > frame.Height))
                    {
                        continue;
                    }

                    var borderEvidence = bounds.Average(bound => GetBorderEvidence(frame, bound));
                    var candidate = new GridCandidate(
                        bounds,
                        Math.Clamp(0.55 + borderEvidence * 0.45, 0, 1),
                        size,
                        xOffset,
                        top,
                        borderEvidence);
                    if (best is null || candidate.Confidence > best.Confidence)
                    {
                        best = candidate;
                    }
                }
            }
        }

        return best ?? throw new InvalidOperationException("A valid weapon-skill grid should fit inside the skill-bar crop.");
    }

    private static double GetBorderEvidence(CapturedFrame frame, ScreenBounds bounds)
    {
        var samples = 0;
        var difference = 0L;
        foreach (var fraction in new[] { 0.15, 0.35, 0.5, 0.65, 0.85 })
        {
            var x = bounds.X + (int)Math.Round((bounds.Width - 1) * fraction);
            var y = bounds.Y + (int)Math.Round((bounds.Height - 1) * fraction);
            difference += PixelDifference(frame, x, bounds.Y, x, Math.Min(bounds.Bottom - 1, bounds.Y + 2));
            difference += PixelDifference(frame, x, bounds.Bottom - 1, x, Math.Max(bounds.Y, bounds.Bottom - 3));
            difference += PixelDifference(frame, bounds.X, y, Math.Min(bounds.Right - 1, bounds.X + 2), y);
            difference += PixelDifference(frame, bounds.Right - 1, y, Math.Max(bounds.X, bounds.Right - 3), y);
            samples += 4;
        }

        return Math.Clamp((double)difference / (samples * 765), 0, 1);
    }

    private static int PixelDifference(CapturedFrame frame, int leftX, int leftY, int rightX, int rightY)
    {
        var leftIndex = leftY * frame.Stride + leftX * 4;
        var rightIndex = rightY * frame.Stride + rightX * 4;
        return Math.Abs(frame.BgraPixels[leftIndex] - frame.BgraPixels[rightIndex]) +
            Math.Abs(frame.BgraPixels[leftIndex + 1] - frame.BgraPixels[rightIndex + 1]) +
            Math.Abs(frame.BgraPixels[leftIndex + 2] - frame.BgraPixels[rightIndex + 2]);
    }

    private static double StandardDeviation(IEnumerable<double> values)
    {
        var items = values.ToList();
        var mean = items.Average();
        return Math.Sqrt(items.Average(value => Math.Pow(value - mean, 2)));
    }

    private sealed record LabelCluster(IReadOnlyList<HudOcrWord> Labels, double Spacing, double Confidence);

    private sealed record GridCandidate(
        IReadOnlyList<ScreenBounds> Bounds,
        double Confidence,
        int Size,
        int HorizontalOffset,
        int Top,
        double BorderEvidence);
}
