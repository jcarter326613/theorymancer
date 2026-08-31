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
    public bool IsUsable => Layout is not null && Layout.HasSkillSlots;
}

public static class SkillBarLayoutDetector
{
    private const int SkillsPerGroup = 5;

    public static SkillBarLayoutDetection Detect(CapturedFrame frame, IReadOnlyList<HudOcrWord> words)
    {
        var visualLayout = FindVisualLayout(frame);
        if (visualLayout is null)
        {
            return new SkillBarLayoutDetection(
                null,
                0,
                "Could not find two rows of five skill icons. Redraw the crop so the full skill bar is visible and unobscured.",
                new SkillBarLayoutDebugInfo(words, [], null, null, null, null, null, null));
        }

        var components = new List<SkillBarComponent>(10);
        AddComponents(components, visualLayout.Left.Bounds, SkillBarComponentKind.WeaponSkill1, frame, visualLayout.Confidence);
        AddComponents(components, visualLayout.Right.Bounds, SkillBarComponentKind.HealSkill, frame, visualLayout.Confidence);
        return new SkillBarLayoutDetection(
            new SkillBarLayout(components),
            visualLayout.Confidence,
            visualLayout.Confidence >= 0.75
                ? "Detected ten skill slots from the visual skill-bar layout. Confirm that the green boxes cover the icon interiors."
                : "Detected a possible visual skill-bar layout. Check the amber boxes before saving this layout.",
            new SkillBarLayoutDebugInfo(
                words,
                [],
                visualLayout.Left.Spacing,
                visualLayout.Confidence,
                visualLayout.Left.Size,
                visualLayout.Left.HorizontalOffset,
                visualLayout.Left.Top,
                (visualLayout.Left.Evidence + visualLayout.Right.Evidence) / 2));
    }

    private static void AddComponents(
        ICollection<SkillBarComponent> components,
        IReadOnlyList<ScreenBounds> bounds,
        SkillBarComponentKind firstKind,
        CapturedFrame frame,
        double confidence)
    {
        for (var index = 0; index < bounds.Count; index++)
        {
            components.Add(SkillBarComponent.FromPixelBounds(
                (SkillBarComponentKind)((int)firstKind + index),
                bounds[index],
                frame.Width,
                frame.Height,
                confidence));
        }
    }

    private static VisualLayoutCandidate? FindVisualLayout(CapturedFrame frame)
    {
        var groups = FindGroupCandidates(frame);
        VisualLayoutCandidate? best = null;
        foreach (var left in groups)
        {
            foreach (var right in groups)
            {
                var groupSize = Math.Max(left.Size, right.Size);
                var gap = right.Bounds[0].X - left.Bounds[^1].Right;
                var gapInIconWidths = (double)gap / groupSize;
                if (left.Bounds[0].X >= right.Bounds[0].X ||
                    gapInIconWidths is < 1.45 or > 2.65 ||
                    Math.Abs(left.Top - right.Top) > Math.Max(left.Size, right.Size) * 0.3 ||
                    Math.Abs(left.Size - right.Size) > Math.Max(left.Size, right.Size) * 0.2)
                {
                    continue;
                }

                var alignmentPenalty = (double)Math.Abs(left.Top - right.Top) / Math.Max(left.Size, right.Size);
                var sizePenalty = (double)Math.Abs(left.Size - right.Size) / Math.Max(left.Size, right.Size);
                var gapPenalty = Math.Abs(gapInIconWidths - 2.0) / 2.0;
                var rowCenter = ((left.Top + left.Size / 2.0) + (right.Top + right.Size / 2.0)) / 2 / frame.Height;
                var verticalPenalty = Math.Abs(rowCenter - 0.70);
                var barCenter = (left.Bounds[0].X + right.Bounds[^1].Right) / 2.0 / frame.Width;
                var horizontalPenalty = Math.Abs(barCenter - 0.50);
                var confidence = Math.Clamp((left.Evidence + right.Evidence) / 2 - alignmentPenalty * 0.1 - sizePenalty * 0.1 - gapPenalty * 0.1 - verticalPenalty * 0.8 - horizontalPenalty * 0.5, 0, 1);
                var candidate = new VisualLayoutCandidate(left, right, confidence);
                if (best is null || candidate.Confidence > best.Confidence)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static IReadOnlyList<IconGroupCandidate> FindGroupCandidates(CapturedFrame frame)
    {
        var sizes = new[] { 0.28, 0.31, 0.34, 0.37, 0.40 }
            .Select(fraction => Math.Max(24, (int)Math.Round(frame.Height * fraction)))
            .Distinct()
            .ToList();
        var candidates = new List<IconGroupCandidate>();
        foreach (var size in sizes)
        {
            var step = Math.Max(2, size / 14);
            for (var spacing = (int)Math.Round(size * 0.9); spacing <= (int)Math.Round(size * 1.15); spacing += step)
            {
                var groupWidth = size + (SkillsPerGroup - 1) * spacing;
                for (var top = (int)Math.Round(frame.Height * 0.35); top <= frame.Height - size; top += step)
                {
                    for (var left = 0; left <= frame.Width - groupWidth; left += step)
                    {
                        var bounds = Enumerable.Range(0, SkillsPerGroup)
                            .Select(index => new ScreenBounds(left + index * spacing, top, size, size))
                            .ToList();
                        var evidence = bounds.Average(bound => GetIconEvidence(frame, bound));
                        candidates.Add(new IconGroupCandidate(bounds, size, spacing, left, top, evidence));
                    }
                }
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Evidence)
            .Take(160)
            .ToList();
    }

    private static double GetIconEvidence(CapturedFrame frame, ScreenBounds bounds)
    {
        var minimumChannel = byte.MaxValue;
        var maximumChannel = byte.MinValue;
        foreach (var fraction in new[] { 0.2, 0.4, 0.6, 0.8 })
        {
            var x = bounds.X + (int)Math.Round((bounds.Width - 1) * fraction);
            var y = bounds.Y + (int)Math.Round((bounds.Height - 1) * fraction);
            var index = y * frame.Stride + x * 4;
            minimumChannel = Math.Min(minimumChannel, frame.BgraPixels[index]);
            minimumChannel = Math.Min(minimumChannel, frame.BgraPixels[index + 1]);
            minimumChannel = Math.Min(minimumChannel, frame.BgraPixels[index + 2]);
            maximumChannel = Math.Max(maximumChannel, frame.BgraPixels[index]);
            maximumChannel = Math.Max(maximumChannel, frame.BgraPixels[index + 1]);
            maximumChannel = Math.Max(maximumChannel, frame.BgraPixels[index + 2]);
        }

        var channelRange = (maximumChannel - minimumChannel) / 255.0;
        return Math.Clamp(GetBorderEvidence(frame, bounds) * 0.7 + channelRange * 0.3, 0, 1);
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

    private sealed record IconGroupCandidate(
        IReadOnlyList<ScreenBounds> Bounds,
        int Size,
        int Spacing,
        int HorizontalOffset,
        int Top,
        double Evidence);

    private sealed record VisualLayoutCandidate(
        IconGroupCandidate Left,
        IconGroupCandidate Right,
        double Confidence);
}
