using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogTextNormalizerTests
{
    [Fact]
    public void AppendFragment_JoinsColorSplitTextWithOneSpace()
    {
        var text = CombatLogTextNormalizer.AppendFragment("You inflicted", "Vampirism.");

        Assert.Equal("You inflicted Vampirism.", text);
    }

    [Fact]
    public void NormalizeCompletedLine_RemovesWhitespaceAroundDigitCommas()
    {
        var text = CombatLogTextNormalizer.NormalizeCompletedLine("You dealt 1 , 234 damage.");

        Assert.Equal("You dealt 1,234 damage.", text);
    }

    [Fact]
    public void IsCompleteLine_RejectsATrailingPartialFragment()
    {
        Assert.False(CombatLogTextNormalizer.IsCompleteLine("You dealt 1,234 damage"));
    }

    [Theory]
    [InlineData("Screenshot saved as Wars")]
    [InlineData("You critically hit Standard Kitty Golem for 1,681 using")]
    [InlineData("You critically hit Standard Kitty Golem for 1,681 using [Bleed].")]
    public void IsCompleteLine_AcceptsEveryVisualOcrRowRegardlessOfTerminalPunctuation(string visualRow)
    {
        Assert.True(CombatLogTextNormalizer.IsCompleteLine(visualRow));
    }
}
