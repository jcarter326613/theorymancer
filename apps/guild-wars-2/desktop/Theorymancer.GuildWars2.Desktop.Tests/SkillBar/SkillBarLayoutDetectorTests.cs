using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class SkillBarLayoutDetectorTests
{
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Detect_FindsEvenlySpacedFixtureSlots(double scaleFactor)
    {
        var fixture = ReaperGreatswordFixture.Load();
        var frame = fixture.LoadScaledScreenshot(scaleFactor);

        var detection = SkillBarLayoutDetector.Detect(frame, fixture.CreateTemplates());

        Assert.True(detection.IsUsable, detection.Message);
        var layout = Assert.IsType<SkillBarLayout>(detection.Layout);
        AssertEvenlySpaced(layout, frame, [
            SkillBarComponentKind.WeaponSkill1,
            SkillBarComponentKind.WeaponSkill2,
            SkillBarComponentKind.WeaponSkill3,
            SkillBarComponentKind.WeaponSkill4,
            SkillBarComponentKind.WeaponSkill5,
        ]);
        AssertEvenlySpaced(layout, frame, [
            SkillBarComponentKind.HealSkill,
            SkillBarComponentKind.UtilitySkill1,
            SkillBarComponentKind.UtilitySkill2,
            SkillBarComponentKind.UtilitySkill3,
            SkillBarComponentKind.EliteSkill,
        ]);
        foreach (var expected in fixture.Slots)
        {
            var component = Assert.Single(layout.Components, component => component.Kind == expected.ComponentKind);
            var actual = component.ToPixelBounds(frame.Width, frame.Height);
            var expectedBounds = expected.ToBounds(scaleFactor);
            var tolerance = Math.Max(2, (int)Math.Ceiling(8 * scaleFactor));
            Assert.InRange(actual.X, expectedBounds.X - tolerance, expectedBounds.X + tolerance);
            Assert.InRange(actual.Y, expectedBounds.Y - tolerance, expectedBounds.Y + tolerance);
            Assert.InRange(actual.Width, expectedBounds.Width - tolerance, expectedBounds.Width + tolerance);
            Assert.InRange(actual.Height, expectedBounds.Height - tolerance, expectedBounds.Height + tolerance);
        }
    }

    private static void AssertEvenlySpaced(
        SkillBarLayout layout,
        CapturedFrame frame,
        IReadOnlyList<SkillBarComponentKind> kinds)
    {
        var centers = kinds.Select(kind => Assert.Single(layout.Components, component => component.Kind == kind)
            .ToPixelBounds(frame.Width, frame.Height))
            .Select(bounds => bounds.X + bounds.Width / 2.0)
            .ToList();
        var gaps = centers.Zip(centers.Skip(1), (left, right) => right - left).ToList();
        Assert.InRange(gaps.Max() - gaps.Min(), 0, 1);
    }
}
