using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class SkillBarLayoutDetectorTests
{
    [Fact]
    public void Detect_FindsFiveEvenlySpacedHotkeyLabels()
    {
        var frame = new CapturedFrame(123, 360, 100, 360 * 4, new byte[360 * 100 * 4]);
        var words = Enumerable.Range(1, 5)
            .Select(index => new HudOcrWord(index.ToString(), 30 + (index - 1) * 60, 62, 8, 10))
            .ToList();

        var detection = SkillBarLayoutDetector.Detect(frame, words);

        Assert.True(detection.IsUsable);
        Assert.NotNull(detection.Layout);
        Assert.Equal(
            Enum.GetValues<SkillBarComponentKind>(),
            detection.Layout.Components.Select(component => component.Kind));
        Assert.All(detection.Layout.Components, component =>
        {
            Assert.InRange(component.X, 0, 1);
            Assert.InRange(component.Y, 0, 1);
            Assert.InRange(component.Width, 0, 1);
            Assert.InRange(component.Height, 0, 1);
        });
    }

    [Fact]
    public void Detect_RejectsAnIncompleteHotkeySequence()
    {
        var frame = new CapturedFrame(123, 360, 100, 360 * 4, new byte[360 * 100 * 4]);
        var words = Enumerable.Range(1, 4)
            .Select(index => new HudOcrWord(index.ToString(), 30 + (index - 1) * 60, 62, 8, 10))
            .ToList();

        var detection = SkillBarLayoutDetector.Detect(frame, words);

        Assert.False(detection.IsUsable);
        Assert.Null(detection.Layout);
    }
}
