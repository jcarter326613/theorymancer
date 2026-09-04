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
    private const double CooldownDarkFractionMinimum = 0.50;
    private const double CooldownMaximumIconScore = 0.75;
    private static readonly double[] RadialSamples = [0.18, 0.26, 0.34, 0.42];

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
        var darkSegments = 0;
        var usableSegments = 0;
        for (var segment = 0; segment < AngularSamples; segment++)
        {
            var angle = 2 * Math.PI * segment / AngularSamples;
            var usableSamples = 0;
            var blackOverlaySamples = 0;
            foreach (var radius in RadialSamples)
            {
                var x = bounds.X + (int)Math.Round(bounds.Width * (0.5 + Math.Sin(angle) * radius));
                var y = bounds.Y + (int)Math.Round(bounds.Height * (0.5 - Math.Cos(angle) * radius));
                usableSamples++;
                if (IsBlackOverlayPixel(frame, x, y))
                {
                    blackOverlaySamples++;
                }
            }

            if (usableSamples == 0)
            {
                continue;
            }

            usableSegments++;
            if (blackOverlaySamples * 2 >= usableSamples)
            {
                darkSegments++;
            }
        }

        if (usableSegments < AngularSamples / 2)
        {
            return new SkillCooldownObservation(component.Kind, reference.SkillId, SkillCooldownState.Unknown, 0, null);
        }

        var darkFraction = (double)darkSegments / usableSegments;
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
                1 - darkFraction);
        }

        var availableConfidence = Math.Clamp(1 - darkFraction, 0, 1);
        return new SkillCooldownObservation(component.Kind, reference.SkillId, SkillCooldownState.Available, availableConfidence, null);
    }

    private static bool Fits(CapturedFrame frame, ScreenBounds bounds) =>
        bounds.X >= 0 && bounds.Y >= 0 && bounds.Right <= frame.Width && bounds.Bottom <= frame.Height;

    private static bool IsBlackOverlayPixel(CapturedFrame frame, int x, int y)
    {
        var index = y * frame.Stride + x * 4;
        var blue = frame.BgraPixels[index];
        var green = frame.BgraPixels[index + 1];
        var red = frame.BgraPixels[index + 2];
        return GetLuminance(red, green, blue) < 70 &&
            Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) < 45;
    }

    private static byte GetLuminance(byte red, byte green, byte blue) =>
        (byte)((77 * red + 150 * green + 29 * blue) >> 8);

}
