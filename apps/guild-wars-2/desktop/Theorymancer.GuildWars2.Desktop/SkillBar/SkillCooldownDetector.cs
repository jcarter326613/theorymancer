using System.Collections.Concurrent;
using System.Drawing;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public enum SkillCooldownState
{
    Unknown,
    Available,
    OnCooldown,
}

public sealed record SkillCooldownReference(
    SkillBarComponentKind Kind,
    int SkillId,
    string IconPath,
    ScreenBounds? SlotBounds = null)
{
    internal IconLuminanceTemplate? IconTemplate { get; init; }
}

internal sealed record IconLuminanceTemplate(
    int Width,
    int Height,
    IReadOnlyList<byte> Luminances,
    IReadOnlyList<byte> Rgb);

public sealed record SkillCooldownObservation(
    SkillBarComponentKind Kind,
    int SkillId,
    SkillCooldownState State,
    double Confidence,
    double? VisibleWipeFraction);

public sealed record SkillCooldownDetection(
    long QpcTimestamp,
    IReadOnlyList<SkillCooldownObservation> Observations);

public interface ISkillCooldownDetector
{
    SkillCooldownDetection Detect(
        CapturedFrame frame,
        SkillBarLayout layout,
        IReadOnlyList<SkillCooldownReference> references);
}

public sealed class SkillCooldownDetector : ISkillCooldownDetector
{
    private const int AngularSamples = 72;
    private const double CooldownDarkFractionMinimum = 0.20;
    private const double CooldownMaximumIconScore = 0.75;
    private const double MeasurementMinimumDarkFraction = 0.01;
    private const int MinimumCountdownGlyphPixels = 100;
    private static readonly double[] RadialSamples = [0.18, 0.26, 0.34, 0.42];
    private static readonly ConcurrentDictionary<string, IconLuminanceTemplate> IconTemplates = new();

    public SkillCooldownDetection Detect(
        CapturedFrame frame,
        SkillBarLayout layout,
        IReadOnlyList<SkillCooldownReference> references)
    {
        var referencesByKind = references
            .GroupBy(reference => reference.Kind)
            .ToDictionary(group => group.Key, group => group.ToList());
        var observations = layout.Components
            .OrderBy(component => component.Kind)
            .Select(component => DetectSlot(frame, component, referencesByKind))
            .ToList();
        return new SkillCooldownDetection(frame.QpcTimestamp, observations);
    }

    public static SkillCooldownReference ResolveReference(SkillCooldownReference reference) => reference with
    {
        IconTemplate = IconTemplates.GetOrAdd(reference.IconPath, LoadIconTemplate),
    };

    private SkillCooldownObservation DetectSlot(
        CapturedFrame frame,
        SkillBarComponent component,
        IReadOnlyDictionary<SkillBarComponentKind, List<SkillCooldownReference>> referencesByKind)
    {
        if (!referencesByKind.TryGetValue(component.Kind, out var references) || references.Count != 1)
        {
            return new SkillCooldownObservation(component.Kind, 0, SkillCooldownState.Unknown, 0, null);
        }

        var reference = references[0];
        var bounds = reference.SlotBounds ?? component.ToPixelBounds(frame.Width, frame.Height);
        if (!Fits(frame, bounds))
        {
            return new SkillCooldownObservation(component.Kind, reference.SkillId, SkillCooldownState.Unknown, 0, null);
        }

        var template = reference.IconTemplate;
        if (template is null)
        {
            if (reference.SlotBounds is not null)
            {
                return new SkillCooldownObservation(component.Kind, reference.SkillId, SkillCooldownState.Unknown, 0, null);
            }

            template = IconTemplates.GetOrAdd(reference.IconPath, LoadIconTemplate);
        }

        // Runtime references use their calibrated slot bounds. Keep the search fallback
        // for callers that only provide a component layout.
        double? iconScore = reference.SlotBounds is null
            ? IconTemplateMatcher.MatchAt(
                frame,
                bounds,
                reference.IconPath,
                component.Kind.ToString(),
                reference.SkillId).Score
            : GetFixedIconScore(frame, bounds, template);
        var darkSegments = 0;
        var usableSegments = 0;
        for (var segment = 0; segment < AngularSamples; segment++)
        {
            var angle = 2 * Math.PI * segment / AngularSamples;
            var usableSamples = 0;
            var darkSamples = 0;
            foreach (var radius in RadialSamples)
            {
                var x = bounds.X + (int)Math.Round(bounds.Width * (0.5 + Math.Sin(angle) * radius));
                var y = bounds.Y + (int)Math.Round(bounds.Height * (0.5 - Math.Cos(angle) * radius));
                if (GetReferenceLuminance(bounds, template, x, y) < 50)
                {
                    continue;
                }

                usableSamples++;
                if (IsDarkenedByOverlay(frame, bounds, template, x, y))
                {
                    darkSamples++;
                }
            }

            if (usableSamples == 0)
            {
                continue;
            }

            usableSegments++;
            if (darkSamples * 2 >= usableSamples)
            {
                darkSegments++;
            }
        }

        if (usableSegments < AngularSamples / 2)
        {
            return new SkillCooldownObservation(component.Kind, reference.SkillId, SkillCooldownState.Unknown, 0, null);
        }

        var darkFraction = (double)darkSegments / usableSegments;
        var visibleWipeFraction = 1 - darkFraction;
        if (darkFraction >= CooldownDarkFractionMinimum)
        {
            var iconConfidence = iconScore is null
                ? 1
                : (CooldownMaximumIconScore - iconScore.Value) / CooldownMaximumIconScore;
            var confidence = Math.Clamp(
                ((darkFraction - CooldownDarkFractionMinimum) / (1 - CooldownDarkFractionMinimum) +
                 iconConfidence) / 2,
                0,
                1);
            return new SkillCooldownObservation(
                component.Kind,
                reference.SkillId,
                SkillCooldownState.OnCooldown,
                confidence,
                visibleWipeFraction);
        }

        if (darkFraction >= MeasurementMinimumDarkFraction &&
            CountCountdownGlyphPixels(frame, bounds, template) >= MinimumCountdownGlyphPixels)
        {
            // Confirm the active overlay without interpreting the displayed number.
            var confidence = Math.Clamp(
                darkFraction / CooldownDarkFractionMinimum,
                0,
                1);
            return new SkillCooldownObservation(
                component.Kind,
                reference.SkillId,
                SkillCooldownState.OnCooldown,
                confidence,
                visibleWipeFraction);
        }

        var availableConfidence = Math.Clamp(1 - darkFraction, 0, 1);
        return new SkillCooldownObservation(component.Kind, reference.SkillId, SkillCooldownState.Available, availableConfidence, null);
    }

    private static bool Fits(CapturedFrame frame, ScreenBounds bounds) =>
        bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= frame.Width && bounds.Bottom <= frame.Height;

    private static bool IsDarkenedByOverlay(
        CapturedFrame frame,
        ScreenBounds bounds,
        IconLuminanceTemplate template,
        int x,
        int y)
    {
        var index = y * frame.Stride + x * 4;
        var blue = frame.BgraPixels[index];
        var green = frame.BgraPixels[index + 1];
        var red = frame.BgraPixels[index + 2];
        var referenceLuminance = GetReferenceLuminance(bounds, template, x, y);
        var luminance = GetLuminance(red, green, blue);
        return referenceLuminance >= 50 && luminance <= referenceLuminance * 0.25 && luminance < 50;
    }

    private static byte GetReferenceLuminance(
        ScreenBounds bounds,
        IconLuminanceTemplate template,
        int x,
        int y)
    {
        var referenceX = Math.Clamp(
            (int)Math.Round((x - bounds.X) * (template.Width - 1) / (double)(bounds.Width - 1)),
            0,
            template.Width - 1);
        var referenceY = Math.Clamp(
            (int)Math.Round((y - bounds.Y) * (template.Height - 1) / (double)(bounds.Height - 1)),
            0,
            template.Height - 1);
        return template.Luminances[referenceY * template.Width + referenceX];
    }

    private static int CountCountdownGlyphPixels(
        CapturedFrame frame,
        ScreenBounds bounds,
        IconLuminanceTemplate template)
    {
        var count = 0;
        var left = bounds.X + (int)Math.Round(bounds.Width * 0.2);
        var right = bounds.X + (int)Math.Round(bounds.Width * 0.8);
        var top = bounds.Y + (int)Math.Round(bounds.Height * 0.2);
        var bottom = bounds.Y + (int)Math.Round(bounds.Height * 0.7);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = y * frame.Stride + x * 4;
                var blue = frame.BgraPixels[index];
                var green = frame.BgraPixels[index + 1];
                var red = frame.BgraPixels[index + 2];
                var luminance = GetLuminance(red, green, blue);
                var saturation = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
                var referenceIndex = GetReferenceRgbIndex(bounds, template, x, y);
                var referenceRed = template.Rgb[referenceIndex];
                var referenceGreen = template.Rgb[referenceIndex + 1];
                var referenceBlue = template.Rgb[referenceIndex + 2];
                var referenceLuminance = GetLuminance(referenceRed, referenceGreen, referenceBlue);
                var referenceSaturation = Math.Max(referenceRed, Math.Max(referenceGreen, referenceBlue)) -
                    Math.Min(referenceRed, Math.Min(referenceGreen, referenceBlue));
                var isReferenceGlyphLike = referenceLuminance >= 160 && referenceSaturation <= 60;
                var isNewGlyphPixel = luminance >= referenceLuminance + 35;
                if (luminance >= 180 && saturation <= 50 && !isReferenceGlyphLike && isNewGlyphPixel)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static byte GetLuminance(byte red, byte green, byte blue) =>
        (byte)((77 * red + 150 * green + 29 * blue) >> 8);

    private static int GetReferenceRgbIndex(
        ScreenBounds bounds,
        IconLuminanceTemplate template,
        int x,
        int y)
    {
        var referenceX = Math.Clamp(
            (int)Math.Round((x - bounds.X) * (template.Width - 1) / (double)(bounds.Width - 1)),
            0,
            template.Width - 1);
        var referenceY = Math.Clamp(
            (int)Math.Round((y - bounds.Y) * (template.Height - 1) / (double)(bounds.Height - 1)),
            0,
            template.Height - 1);
        return (referenceY * template.Width + referenceX) * 3;
    }

    private static double GetFixedIconScore(
        CapturedFrame frame,
        ScreenBounds bounds,
        IconLuminanceTemplate template)
    {
        Span<double> frameSums = stackalloc double[3];
        Span<double> templateSums = stackalloc double[3];
        var sampleCount = bounds.Width * bounds.Height;
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                var frameIndex = (bounds.Y + y) * frame.Stride + (bounds.X + x) * 4;
                var templateIndex = (Math.Min(template.Height - 1, (int)((long)y * template.Height / bounds.Height)) * template.Width +
                    Math.Min(template.Width - 1, (int)((long)x * template.Width / bounds.Width))) * 3;
                frameSums[0] += frame.BgraPixels[frameIndex + 2];
                frameSums[1] += frame.BgraPixels[frameIndex + 1];
                frameSums[2] += frame.BgraPixels[frameIndex];
                templateSums[0] += template.Rgb[templateIndex];
                templateSums[1] += template.Rgb[templateIndex + 1];
                templateSums[2] += template.Rgb[templateIndex + 2];
            }
        }

        var covariance = 0.0;
        var frameEnergy = 0.0;
        var templateEnergy = 0.0;
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                var frameIndex = (bounds.Y + y) * frame.Stride + (bounds.X + x) * 4;
                var templateIndex = (Math.Min(template.Height - 1, (int)((long)y * template.Height / bounds.Height)) * template.Width +
                    Math.Min(template.Width - 1, (int)((long)x * template.Width / bounds.Width))) * 3;
                for (var channel = 0; channel < 3; channel++)
                {
                    var frameValue = frame.BgraPixels[frameIndex + 2 - channel] - frameSums[channel] / sampleCount;
                    var templateValue = template.Rgb[templateIndex + channel] - templateSums[channel] / sampleCount;
                    covariance += frameValue * templateValue;
                    frameEnergy += frameValue * frameValue;
                    templateEnergy += templateValue * templateValue;
                }
            }
        }

        if (frameEnergy < double.Epsilon || templateEnergy < double.Epsilon)
        {
            return 0;
        }

        return Math.Clamp(0.5 + covariance / Math.Sqrt(frameEnergy * templateEnergy) / 2, 0, 1);
    }

    private static IconLuminanceTemplate LoadIconTemplate(string path)
    {
        using var bitmap = new Bitmap(path);
        var bounds = TrimBlackBorder(bitmap);
        var luminances = new byte[bounds.Width * bounds.Height];
        var rgb = new byte[bounds.Width * bounds.Height * 3];
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                var color = bitmap.GetPixel(bounds.X + x, bounds.Y + y);
                luminances[y * bounds.Width + x] = GetLuminance(color.R, color.G, color.B);
                var rgbIndex = (y * bounds.Width + x) * 3;
                rgb[rgbIndex] = color.R;
                rgb[rgbIndex + 1] = color.G;
                rgb[rgbIndex + 2] = color.B;
            }
        }

        return new IconLuminanceTemplate(bounds.Width, bounds.Height, luminances, rgb);
    }

    private static Rectangle TrimBlackBorder(Bitmap bitmap)
    {
        var left = 0;
        var right = bitmap.Width - 1;
        var top = 0;
        var bottom = bitmap.Height - 1;
        while (left < right && IsMostlyBlackColumn(bitmap, left, top, bottom)) left++;
        while (right > left && IsMostlyBlackColumn(bitmap, right, top, bottom)) right--;
        while (top < bottom && IsMostlyBlackRow(bitmap, top, left, right)) top++;
        while (bottom > top && IsMostlyBlackRow(bitmap, bottom, left, right)) bottom--;
        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static bool IsMostlyBlackColumn(Bitmap bitmap, int x, int top, int bottom) =>
        Enumerable.Range(top, bottom - top + 1).Count(y => IsBlack(bitmap.GetPixel(x, y))) >= (bottom - top + 1) * 0.9;

    private static bool IsMostlyBlackRow(Bitmap bitmap, int y, int left, int right) =>
        Enumerable.Range(left, right - left + 1).Count(x => IsBlack(bitmap.GetPixel(x, y))) >= (right - left + 1) * 0.9;

    private static bool IsBlack(Color color) => color.R <= 12 && color.G <= 12 && color.B <= 12;

}
