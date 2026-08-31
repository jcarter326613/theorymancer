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
        var maximumSize = Math.Min(256, Math.Min(frame.Width, frame.Height));
        if (maximumSize < 20)
        {
            return null;
        }

        return FindBestMatchInRegion(
            frame,
            new ScreenBounds(0, 0, frame.Width, frame.Height),
            20,
            maximumSize,
            referenceIconPath,
            name,
            skillId);
    }

    public static IconTemplateMatch? FindBestMatchInRegion(
        CapturedFrame frame,
        ScreenBounds searchRegion,
        int minimumSize,
        int maximumSize,
        string referenceIconPath,
        string name,
        int skillId)
    {
        var template = LoadTemplate(referenceIconPath);
        minimumSize = Math.Max(20, minimumSize);
        maximumSize = Math.Min(maximumSize, Math.Min(searchRegion.Width, searchRegion.Height));
        if (minimumSize > maximumSize || !Fits(frame, searchRegion))
        {
            return null;
        }

        var coarseFrame = CreateCoarseFrame(frame, out var scaleX, out var scaleY);
        var minimumScale = Math.Min(scaleX, scaleY);
        var coarseRegion = ScaleBounds(searchRegion, scaleX, scaleY);
        var coarseMinimumSize = Math.Max(20, (int)Math.Round(minimumSize * minimumScale));
        var coarseMaximumSize = Math.Max(coarseMinimumSize, (int)Math.Round(maximumSize * minimumScale));
        var coarseSizeStep = Math.Max(4, Math.Min(12, coarseMaximumSize / 20));
        var coarsePositionStep = Math.Max(4, Math.Min(12, coarseMaximumSize / 20));
        var coarse = FindBestCandidateInRegion(
            coarseFrame,
            template,
            coarseRegion,
            coarseMinimumSize,
            coarseMaximumSize,
            coarseSizeStep,
            coarsePositionStep,
            GetNormalizedCorrelation);
        coarse = new Candidate(
            new ScreenBounds(
                (int)Math.Round(coarse.Bounds.X / scaleX),
                (int)Math.Round(coarse.Bounds.Y / scaleY),
                Math.Max(1, (int)Math.Round(coarse.Bounds.Width / minimumScale)),
                Math.Max(1, (int)Math.Round(coarse.Bounds.Height / minimumScale))),
            coarse.Score);
        var refined = RefineWithFullResolutionPixels(
            frame,
            template,
            searchRegion,
            coarse,
            minimumSize,
            maximumSize,
            Math.Max(4, (int)Math.Ceiling((coarsePositionStep / 2.0 + 1) / minimumScale)));
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
        var minimumDimension = Math.Min(bounds.Width, bounds.Height);
        var minimumSize = Math.Max(20, (int)Math.Round(minimumDimension * 0.85));
        var maximumSize = Math.Min(
            Math.Min(frame.Width, frame.Height),
            (int)Math.Round(minimumDimension * 1.05));
        var padding = Math.Max(2, (int)Math.Round(minimumDimension * 0.1));
        var sizeStep = Math.Max(1, minimumSize / 16);
        var positionStep = Math.Max(1, minimumSize / 12);
        var best = FindBestCandidateWithinBounds(
            frame,
            template,
            minimumSize,
            maximumSize,
            sizeStep,
            positionStep,
            bounds,
            padding);
        return new IconTemplateMatch(name, skillId, best.Bounds, best.Score);
    }

    private static Candidate FindBestCandidateInRegion(
        CapturedFrame frame,
        ReferenceTemplate template,
        ScreenBounds searchRegion,
        int minimumSize,
        int maximumSize,
        int sizeStep,
        int positionStep,
        Func<CapturedFrame, ScreenBounds, ReferenceTemplate, double> scoreCandidate)
    {
        Candidate? best = null;
        for (var size = minimumSize; size <= maximumSize; size += sizeStep)
        {
            var maximumX = searchRegion.Right - size;
            var maximumY = searchRegion.Bottom - size;
            for (var y = searchRegion.Y; y <= maximumY; y += positionStep)
            {
                for (var x = searchRegion.X; x <= maximumX; x += positionStep)
                {
                    var bounds = new ScreenBounds(x, y, size, size);
                    var score = scoreCandidate(frame, bounds, template);
                    if (best is null || score > best.Score)
                    {
                        best = new Candidate(bounds, score);
                    }
                }
            }
        }

        return best ?? throw new InvalidOperationException("The search range must contain at least one icon candidate.");
    }

    private static Candidate RefineWithFullResolutionPixels(
        CapturedFrame frame,
        ReferenceTemplate template,
        ScreenBounds searchRegion,
        Candidate coarse,
        int minimumSize,
        int maximumSize,
        int radius)
    {
        var refinedPosition = FindBestCandidateInRegion(
            frame,
            template,
            Intersect(
                searchRegion,
                new ScreenBounds(
                    coarse.Bounds.X - radius,
                    coarse.Bounds.Y - radius,
                    coarse.Bounds.Width + radius * 2,
                    coarse.Bounds.Height + radius * 2)),
            coarse.Bounds.Width,
            coarse.Bounds.Width,
            1,
            1,
            GetFullResolutionScore);
        var refinedSize = FindBestCandidateAtPosition(
            frame,
            template,
            searchRegion,
            refinedPosition.Bounds.X,
            refinedPosition.Bounds.Y,
            Math.Max(minimumSize, refinedPosition.Bounds.Width - radius),
            Math.Min(maximumSize, refinedPosition.Bounds.Width + radius));
        return FindBestCandidateInRegion(
            frame,
            template,
            Intersect(
                searchRegion,
                new ScreenBounds(
                    refinedSize.Bounds.X - 2,
                    refinedSize.Bounds.Y - 2,
                    refinedSize.Bounds.Width + 4,
                    refinedSize.Bounds.Height + 4)),
            refinedSize.Bounds.Width,
            refinedSize.Bounds.Width,
            1,
            1,
            GetFullResolutionScore);
    }

    private static Candidate FindBestCandidateAtPosition(
        CapturedFrame frame,
        ReferenceTemplate template,
        ScreenBounds searchRegion,
        int x,
        int y,
        int minimumSize,
        int maximumSize)
    {
        Candidate? best = null;
        for (var size = minimumSize; size <= maximumSize; size++)
        {
            var bounds = new ScreenBounds(x, y, size, size);
            if (!Contains(searchRegion, bounds))
            {
                continue;
            }

            var score = GetFullResolutionScore(frame, bounds, template);
            if (best is null || score > best.Score)
            {
                best = new Candidate(bounds, score);
            }
        }

        return best ?? throw new InvalidOperationException("The search range must contain at least one icon candidate.");
    }

    private static Candidate FindBestCandidateWithinBounds(
        CapturedFrame frame,
        ReferenceTemplate template,
        int minimumSize,
        int maximumSize,
        int sizeStep,
        int positionStep,
        ScreenBounds bounds,
        int padding)
    {
        Candidate? best = null;
        for (var size = minimumSize; size <= maximumSize; size += sizeStep)
        {
            var minimumX = Math.Max(0, bounds.X - padding);
            var maximumX = Math.Min(frame.Width - size, bounds.Right - size + padding);
            var minimumY = Math.Max(0, bounds.Y - padding);
            var maximumY = Math.Min(frame.Height - size, bounds.Bottom - size + padding);
            for (var y = minimumY; y <= maximumY; y += positionStep)
            {
                for (var x = minimumX; x <= maximumX; x += positionStep)
                {
                    var candidateBounds = new ScreenBounds(x, y, size, size);
                    var score = GetFullResolutionScore(frame, candidateBounds, template);
                    if (best is null || score > best.Score)
                    {
                        best = new Candidate(candidateBounds, score);
                    }
                }
            }
        }

        return best ?? throw new InvalidOperationException("The slot search range must contain at least one icon candidate.");
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

    private static double GetFullResolutionScore(CapturedFrame frame, ScreenBounds bounds, ReferenceTemplate template)
    {
        // Keep every screenshot pixel; only the canonical template is resampled to the tested bounds.
        var width = bounds.Width;
        var height = bounds.Height;
        Span<double> candidateSums = stackalloc double[ChannelCount];
        Span<double> templateSums = stackalloc double[ChannelCount];
        var sampleCount = width * height;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var frameIndex = (bounds.Y + y) * frame.Stride + (bounds.X + x) * 4;
                var templateIndex = GetTemplatePixelIndex(template, x, y, width, height);
                candidateSums[0] += frame.BgraPixels[frameIndex + 2];
                candidateSums[1] += frame.BgraPixels[frameIndex + 1];
                candidateSums[2] += frame.BgraPixels[frameIndex];
                templateSums[0] += template.RawRgb[templateIndex];
                templateSums[1] += template.RawRgb[templateIndex + 1];
                templateSums[2] += template.RawRgb[templateIndex + 2];
            }
        }

        var covariance = 0.0;
        var candidateEnergy = 0.0;
        var templateEnergy = 0.0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var frameIndex = (bounds.Y + y) * frame.Stride + (bounds.X + x) * 4;
                var templateIndex = GetTemplatePixelIndex(template, x, y, width, height);
                for (var channel = 0; channel < ChannelCount; channel++)
                {
                    var candidate = frame.BgraPixels[frameIndex + 2 - channel] - candidateSums[channel] / sampleCount;
                    var reference = template.RawRgb[templateIndex + channel] - templateSums[channel] / sampleCount;
                    covariance += candidate * reference;
                    candidateEnergy += candidate * candidate;
                    templateEnergy += reference * reference;
                }
            }
        }

        if (candidateEnergy < double.Epsilon || templateEnergy < double.Epsilon)
        {
            return 0;
        }

        // For unit-length centered vectors, squared error is 2 - 2 * correlation.
        var squaredError = 2 - 2 * covariance / Math.Sqrt(candidateEnergy * templateEnergy);
        return Math.Clamp(1 - squaredError / 4, 0, 1);
    }

    private static ReferenceTemplate LoadTemplate(string path)
    {
        using var bitmap = new Bitmap(path);
        var bounds = TrimBlackBorder(bitmap);
        var rawRgb = new byte[bounds.Width * bounds.Height * ChannelCount];
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                var color = bitmap.GetPixel(bounds.X + x, bounds.Y + y);
                var rawIndex = (y * bounds.Width + x) * ChannelCount;
                rawRgb[rawIndex] = color.R;
                rawRgb[rawIndex + 1] = color.G;
                rawRgb[rawIndex + 2] = color.B;
            }
        }
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

        return new ReferenceTemplate(values, squaredDifference, bounds.Width, bounds.Height, rawRgb);
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

    private static int GetTemplatePixelIndex(ReferenceTemplate template, int x, int y, int width, int height)
    {
        var templateX = Math.Min(template.Width - 1, (int)((long)x * template.Width / width));
        var templateY = Math.Min(template.Height - 1, (int)((long)y * template.Height / height));
        return (templateY * template.Width + templateX) * ChannelCount;
    }

    private static bool Fits(CapturedFrame frame, ScreenBounds bounds) =>
        bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= frame.Width && bounds.Bottom <= frame.Height;

    private static CapturedFrame CreateCoarseFrame(CapturedFrame source, out double scaleX, out double scaleY)
    {
        const int MaximumCoarseDimension = 224;
        var scale = Math.Min(1, (double)MaximumCoarseDimension / Math.Min(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        scaleX = (double)width / source.Width;
        scaleY = (double)height / source.Height;
        if (width == source.Width && height == source.Height)
        {
            return source;
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)(y / scaleY));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)(x / scaleX));
                var sourceIndex = sourceY * source.Stride + sourceX * 4;
                var targetIndex = y * stride + x * 4;
                pixels[targetIndex] = source.BgraPixels[sourceIndex];
                pixels[targetIndex + 1] = source.BgraPixels[sourceIndex + 1];
                pixels[targetIndex + 2] = source.BgraPixels[sourceIndex + 2];
                pixels[targetIndex + 3] = source.BgraPixels[sourceIndex + 3];
            }
        }

        return new CapturedFrame(source.QpcTimestamp, width, height, stride, pixels);
    }

    private static ScreenBounds ScaleBounds(ScreenBounds bounds, double scaleX, double scaleY) => new(
        (int)Math.Round(bounds.X * scaleX),
        (int)Math.Round(bounds.Y * scaleY),
        Math.Max(1, (int)Math.Round(bounds.Width * scaleX)),
        Math.Max(1, (int)Math.Round(bounds.Height * scaleY)));

    private static bool Contains(ScreenBounds outer, ScreenBounds inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static ScreenBounds Intersect(ScreenBounds left, ScreenBounds right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottomEdge = Math.Min(left.Bottom, right.Bottom);
        return new ScreenBounds(x, y, Math.Max(0, rightEdge - x), Math.Max(0, bottomEdge - y));
    }

    private sealed record ReferenceTemplate(
        IReadOnlyList<double> CenteredValues,
        double SquaredDifference,
        int Width,
        int Height,
        IReadOnlyList<byte> RawRgb);

    private sealed record Candidate(ScreenBounds Bounds, double Score);
}
