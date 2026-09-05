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
    string IconPath);

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
    private const double MeasurementMaximumIconScore = 0.80;
    private const double MeasurementMinimumDarkFraction = 0.01;
    private const int MinimumCountdownGlyphPixels = 12;
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
        var bounds = component.ToPixelBounds(frame.Width, frame.Height);
        if (!Fits(frame, bounds))
        {
            return new SkillCooldownObservation(component.Kind, reference.SkillId, SkillCooldownState.Unknown, 0, null);
        }

        var iconScore = IconTemplateMatcher.MatchAt(
            frame,
            bounds,
            reference.IconPath,
            component.Kind.ToString(),
            reference.SkillId).Score;
        var template = IconTemplates.GetOrAdd(reference.IconPath, LoadIconTemplate);
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
        if (darkFraction >= CooldownDarkFractionMinimum && iconScore < CooldownMaximumIconScore)
        {
            var confidence = Math.Clamp(
                ((darkFraction - CooldownDarkFractionMinimum) / (1 - CooldownDarkFractionMinimum) +
                 (CooldownMaximumIconScore - iconScore) / CooldownMaximumIconScore) / 2,
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
            iconScore < MeasurementMaximumIconScore &&
            CountCountdownGlyphPixels(frame, bounds) >= MinimumCountdownGlyphPixels)
        {
            // Confirm the active overlay without interpreting the displayed number.
            var confidence = Math.Clamp(
                (darkFraction / CooldownDarkFractionMinimum +
                 (MeasurementMaximumIconScore - iconScore) / MeasurementMaximumIconScore) / 2,
                0,
                1);
            return new SkillCooldownObservation(
                component.Kind,
                reference.SkillId,
                SkillCooldownState.Available,
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

    private static int CountCountdownGlyphPixels(CapturedFrame frame, ScreenBounds bounds)
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
                if (GetLuminance(red, green, blue) >= 180 &&
                    Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) <= 50)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static byte GetLuminance(byte red, byte green, byte blue) =>
        (byte)((77 * red + 150 * green + 29 * blue) >> 8);

    private static IconLuminanceTemplate LoadIconTemplate(string path)
    {
        using var bitmap = new Bitmap(path);
        var luminances = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                luminances[y * bitmap.Width + x] = GetLuminance(color.R, color.G, color.B);
            }
        }

        return new IconLuminanceTemplate(bitmap.Width, bitmap.Height, luminances);
    }

    private sealed record IconLuminanceTemplate(int Width, int Height, IReadOnlyList<byte> Luminances);

}
