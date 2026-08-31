using System.Drawing;
using System.IO;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public sealed record IconTemplateMatch(string Name, int SkillId, ScreenBounds Bounds, double Score);

public static class IconTemplateMatcher
{
    private const int SampleSize = 12;
    private const int ChannelCount = 3;
    private const double InteriorInset = 0.08;

    public static IconTemplateMatch? FindBestMatch(
        CapturedFrame frame,
        string referenceIconPath,
        string name,
        int skillId)
    {
        var template = LoadTemplate(referenceIconPath);
        var maximumSize = Math.Min(128, Math.Min(frame.Width, frame.Height));
        if (maximumSize < 20)
        {
            return null;
        }

        var coarse = FindBestCandidate(frame, template, 20, maximumSize, 4, 4, 4);
        var refined = FindBestCandidate(
            frame,
            template,
            Math.Max(20, coarse.Bounds.Width - 4),
            Math.Min(maximumSize, coarse.Bounds.Width + 4),
            1,
            1,
            1,
            coarse.Bounds);
        return new IconTemplateMatch(name, skillId, refined.Bounds, refined.Score);
    }

    public static IconTemplateMatch MatchAt(
        CapturedFrame frame,
        ScreenBounds bounds,
        string referenceIconPath,
        string name,
        int skillId)
    {
        var template = LoadTemplate(referenceIconPath);
        return new IconTemplateMatch(name, skillId, bounds, GetNormalizedCorrelation(frame, bounds, template));
    }

    private static Candidate FindBestCandidate(
        CapturedFrame frame,
        ReferenceTemplate template,
        int minimumSize,
        int maximumSize,
        int sizeStep,
        int horizontalStep,
        int verticalStep,
        ScreenBounds? center = null)
    {
        Candidate? best = null;
        for (var size = minimumSize; size <= maximumSize; size += sizeStep)
        {
            var minimumX = center is null ? 0 : Math.Max(0, center.Value.X - 6);
            var maximumX = center is null ? frame.Width - size : Math.Min(frame.Width - size, center.Value.X + 6);
            var minimumY = center is null ? 0 : Math.Max(0, center.Value.Y - 6);
            var maximumY = center is null ? frame.Height - size : Math.Min(frame.Height - size, center.Value.Y + 6);
            for (var y = minimumY; y <= maximumY; y += verticalStep)
            {
                for (var x = minimumX; x <= maximumX; x += horizontalStep)
                {
                    var bounds = new ScreenBounds(x, y, size, size);
                    var score = GetNormalizedCorrelation(frame, bounds, template);
                    if (best is null || score > best.Score)
                    {
                        best = new Candidate(bounds, score);
                    }
                }
            }
        }

        return best ?? throw new InvalidOperationException("The search range must contain at least one icon candidate.");
    }

    private static double GetNormalizedCorrelation(CapturedFrame frame, ScreenBounds bounds, ReferenceTemplate template)
    {
        Span<double> candidate = stackalloc double[SampleSize * SampleSize * ChannelCount];
        Span<double> sums = stackalloc double[ChannelCount];
        for (var y = 0; y < SampleSize; y++)
        {
            for (var x = 0; x < SampleSize; x++)
            {
                var sourceX = bounds.X + (int)Math.Round((InteriorInset + (x + 0.5) / SampleSize * (1 - InteriorInset * 2)) * (bounds.Width - 1));
                var sourceY = bounds.Y + (int)Math.Round((InteriorInset + (y + 0.5) / SampleSize * (1 - InteriorInset * 2)) * (bounds.Height - 1));
                var index = (y * SampleSize + x) * ChannelCount;
                candidate[index] = frame.BgraPixels[sourceY * frame.Stride + sourceX * 4 + 2];
                candidate[index + 1] = frame.BgraPixels[sourceY * frame.Stride + sourceX * 4 + 1];
                candidate[index + 2] = frame.BgraPixels[sourceY * frame.Stride + sourceX * 4];
                sums[0] += candidate[index];
                sums[1] += candidate[index + 1];
                sums[2] += candidate[index + 2];
            }
        }

        var covariance = 0.0;
        var squaredDifference = 0.0;
        for (var index = 0; index < candidate.Length; index++)
        {
            var centered = candidate[index] - sums[index % ChannelCount] / (SampleSize * SampleSize);
            covariance += centered * template.CenteredValues[index];
            squaredDifference += centered * centered;
        }

        if (squaredDifference < double.Epsilon || template.SquaredDifference < double.Epsilon)
        {
            return 0;
        }

        var correlation = covariance / Math.Sqrt(squaredDifference * template.SquaredDifference);
        return Math.Clamp((correlation + 1) / 2, 0, 1);
    }

    private static ReferenceTemplate LoadTemplate(string path)
    {
        using var bitmap = new Bitmap(path);
        var bounds = TrimBlackBorder(bitmap);
        var values = new double[SampleSize * SampleSize * ChannelCount];
        var sums = new double[ChannelCount];
        for (var y = 0; y < SampleSize; y++)
        {
            for (var x = 0; x < SampleSize; x++)
            {
                var sourceX = bounds.X + (int)Math.Round((x + 0.5) / SampleSize * (bounds.Width - 1));
                var sourceY = bounds.Y + (int)Math.Round((y + 0.5) / SampleSize * (bounds.Height - 1));
                var color = bitmap.GetPixel(sourceX, sourceY);
                var index = (y * SampleSize + x) * ChannelCount;
                values[index] = color.R;
                values[index + 1] = color.G;
                values[index + 2] = color.B;
                sums[0] += values[index];
                sums[1] += values[index + 1];
                sums[2] += values[index + 2];
            }
        }

        var squaredDifference = 0.0;
        for (var index = 0; index < values.Length; index++)
        {
            values[index] -= sums[index % ChannelCount] / (SampleSize * SampleSize);
            squaredDifference += values[index] * values[index];
        }

        return new ReferenceTemplate(values, squaredDifference);
    }

    private static Rectangle TrimBlackBorder(Bitmap bitmap)
    {
        var left = 0;
        var right = bitmap.Width - 1;
        var top = 0;
        var bottom = bitmap.Height - 1;
        while (left < right && IsMostlyBlackColumn(bitmap, left, top, bottom))
        {
            left++;
        }

        while (right > left && IsMostlyBlackColumn(bitmap, right, top, bottom))
        {
            right--;
        }

        while (top < bottom && IsMostlyBlackRow(bitmap, top, left, right))
        {
            top++;
        }

        while (bottom > top && IsMostlyBlackRow(bitmap, bottom, left, right))
        {
            bottom--;
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static bool IsMostlyBlackColumn(Bitmap bitmap, int x, int top, int bottom) =>
        Enumerable.Range(top, bottom - top + 1).Count(y => IsBlack(bitmap.GetPixel(x, y))) >= (bottom - top + 1) * 0.9;

    private static bool IsMostlyBlackRow(Bitmap bitmap, int y, int left, int right) =>
        Enumerable.Range(left, right - left + 1).Count(x => IsBlack(bitmap.GetPixel(x, y))) >= (right - left + 1) * 0.9;

    private static bool IsBlack(Color color) => color.R <= 12 && color.G <= 12 && color.B <= 12;

    private sealed record ReferenceTemplate(IReadOnlyList<double> CenteredValues, double SquaredDifference);

    private sealed record Candidate(ScreenBounds Bounds, double Score);
}
