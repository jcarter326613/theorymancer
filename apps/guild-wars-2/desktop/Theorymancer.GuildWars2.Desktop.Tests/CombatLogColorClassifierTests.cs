using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogColorClassifierTests
{
    [Theory]
    [InlineData(49, 49, 218, "red")]
    [InlineData(207, 81, 206, "blue")]
    [InlineData(2, 118, 203, "green")]
    [InlineData(49, 49, 178, "red")]
    public void Classify_UsesTheCalibratedTextColor(byte blue, byte green, byte red, string expected)
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

    [Fact]
    public void Classify_ReturnsUnknownWhenColorExceedsCalibrationDistance()
    {
        var pixels = Enumerable.Range(0, 64)
            .SelectMany(_ => new byte[] { 49, 49, 177, 255 })
            .ToArray();

        Assert.Equal("unknown", CombatLogColorClassifier.Classify(pixels));
    }

    [Fact]
    public void Classify_UsesOnlyTheNumberAfterFor()
    {
        var frame = CreateFrame(width: 12, height: 4, blue: 49, green: 49, red: 218);
        Paint(frame, x: 0, y: 0, width: 2, height: 4, blue: 207, green: 81, red: 206);
        Paint(frame, x: 6, y: 0, width: 3, height: 4, blue: 2, green: 118, red: 203);
        RecognizedWord[] words =
        [
            Word("2", 0, 0, 2, 4),
            Word("hits", 2, 0, 2, 4),
            Word("for", 4, 0, 2, 4),
            Word("123", 6, 0, 3, 4),
        ];

        Assert.Equal("green", CombatLogColorClassifier.Classify(frame, words));
    }

    [Fact]
    public void Classify_UsesTheOnlyNumberWhenForIsAbsent()
    {
        var frame = CreateFrame(width: 8, height: 4, blue: 49, green: 49, red: 218);
        Paint(frame, x: 3, y: 0, width: 3, height: 4, blue: 207, green: 81, red: 206);
        RecognizedWord[] words = [Word("You", 0, 0, 3, 4), Word("123", 3, 0, 3, 4)];

        Assert.Equal("blue", CombatLogColorClassifier.Classify(frame, words));
    }

    [Fact]
    public void Classify_CombinesOcrSplitCommaSeparatedDamageNumbers()
    {
        var frame = CreateFrame(width: 12, height: 4, blue: 49, green: 49, red: 218);
        Paint(frame, x: 4, y: 0, width: 5, height: 4, blue: 2, green: 118, red: 203);
        RecognizedWord[] words =
        [
            Word("You", 0, 0, 3, 4),
            Word("hit", 3, 0, 1, 4),
            Word("1", 4, 0, 1, 4),
            Word(",", 5, 0, 1, 4),
            Word("234", 6, 0, 3, 4),
        ];

        Assert.Equal("green", CombatLogColorClassifier.Classify(frame, words));
    }

    [Fact]
    public void Classify_ReturnsUnknownForAmbiguousNumbersWithoutFor()
    {
        var frame = CreateFrame(width: 8, height: 4, blue: 49, green: 49, red: 218);
        RecognizedWord[] words = [Word("123", 0, 0, 3, 4), Word("and", 3, 0, 2, 4), Word("456", 5, 0, 3, 4)];

        Assert.Equal("unknown", CombatLogColorClassifier.Classify(frame, words));
    }

    [Fact]
    public void Classify_ReturnsUnknownWhenTheLineHasNoNumber()
    {
        var frame = CreateFrame(width: 6, height: 4, blue: 2, green: 118, red: 203);
        RecognizedWord[] words = [Word("You", 0, 0, 3, 4), Word("dodged", 3, 0, 3, 4)];

        Assert.Equal("unknown", CombatLogColorClassifier.Classify(frame, words));
    }

    private static CapturedFrame CreateFrame(int width, int height, byte blue, byte green, byte red)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = 255;
        }

        return new CapturedFrame(0, width, height, width * 4, pixels);
    }

    private static void Paint(CapturedFrame frame, int x, int y, int width, int height, byte blue, byte green, byte red)
    {
        for (var currentY = y; currentY < y + height; currentY++)
        {
            for (var currentX = x; currentX < x + width; currentX++)
            {
                var index = currentY * frame.Stride + currentX * 4;
                frame.BgraPixels[index] = blue;
                frame.BgraPixels[index + 1] = green;
                frame.BgraPixels[index + 2] = red;
                frame.BgraPixels[index + 3] = 255;
            }
        }
    }

    private static RecognizedWord Word(string text, double x, double y, double width, double height) =>
        new(text, x, y, width, height);
}
