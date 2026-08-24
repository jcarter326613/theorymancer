using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogColorClassifierTests
{
    [Theory]
    [InlineData(20, 20, 220, "red")]
    [InlineData(20, 220, 20, "green")]
    [InlineData(220, 20, 20, "blue")]
    [InlineData(20, 220, 220, "yellow")]
    public void Classify_UsesTheDominantTextColor(byte blue, byte green, byte red, string expected)
    {
        var pixels = Enumerable.Range(0, 64)
            .SelectMany(_ => new byte[] { blue, green, red, 255 })
            .ToArray();

        Assert.Equal(expected, CombatLogColorClassifier.Classify(pixels));
    }

    [Fact]
    public void Classify_ReturnsUnknownForLowContrastPixels()
    {
        var pixels = Enumerable.Repeat((byte)120, 64 * 4).ToArray();

        Assert.Equal("unknown", CombatLogColorClassifier.Classify(pixels));
    }
}
