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
    public void NormalizeVisualRow_RemovesWhitespaceAroundDigitCommas()
    {
        var text = CombatLogTextNormalizer.NormalizeVisualRow("You dealt 1 , 234 damage.");

        Assert.Equal("You dealt 1,234 damage.", text);
    }
}
