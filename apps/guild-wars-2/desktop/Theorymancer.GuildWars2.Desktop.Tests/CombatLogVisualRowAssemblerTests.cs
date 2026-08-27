using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogVisualRowAssemblerTests
{
    [Fact]
    public void Assemble_CombinesSamePhysicalRowAndPropagatesItsColor()
    {
        IReadOnlyList<RecognizedWord>? classifiedWords = null;
        var lines = CombatLogVisualRowAssembler.Assemble(
            123,
            0xABC,
            [
                Row("You hit Standard Kitty Golem for", 31, 610, 16),
                Row("170", 329, 612, 16),
                Row("using Signet of Vampirism.", 365, 609, 20),
            ],
            words =>
            {
                classifiedWords = words;
                return words.Any(word => word.Text == "170") ? "red" : "unknown";
            });

        var line = Assert.Single(lines);
        Assert.Equal(0, line.RowIndex);
        Assert.Equal("You hit Standard Kitty Golem for 170 using Signet of Vampirism.", line.Text);
        Assert.Equal("red", line.ColorClass);
        Assert.NotNull(classifiedWords);
        Assert.Equal(11, classifiedWords!.Count);
        Assert.Equal("170", classifiedWords[6].Text);
    }

    [Fact]
    public void Assemble_KeepsFragmentsBeyondHalfACharacterHeightSeparate()
    {
        var lines = CombatLogVisualRowAssembler.Assemble(
            123,
            0xABC,
            [
                Row("You hit Standard Kitty Golem for", 31, 610, 20),
                Row("170", 329, 621, 20),
            ],
            _ => "unknown");

        Assert.Collection(
            lines,
            line => Assert.Equal("You hit Standard Kitty Golem for", line.Text),
            line => Assert.Equal("170", line.Text));
    }

    [Fact]
    public void Assemble_KeepsPunctuationFreeRowsWithoutBoundsSeparate()
    {
        var lines = CombatLogVisualRowAssembler.Assemble(
            123,
            0xABC,
            [
                new OcrVisualRow("Screenshot saved as Wars", []),
                new OcrVisualRow("You critically hit Standard Kitty Golem for 1,681 using", []),
                new OcrVisualRow("You critically hit Standard Kitty Golem for 1,681 using [Bleed].", []),
            ],
            _ => "unknown");

        Assert.Collection(
            lines,
            line =>
            {
                Assert.Equal(0, line.RowIndex);
                Assert.Equal("Screenshot saved as Wars", line.Text);
            },
            line =>
            {
                Assert.Equal(1, line.RowIndex);
                Assert.Equal("You critically hit Standard Kitty Golem for 1,681 using", line.Text);
            },
            line =>
            {
                Assert.Equal(2, line.RowIndex);
                Assert.Equal("You critically hit Standard Kitty Golem for 1,681 using [Bleed].", line.Text);
            });
    }

    private static OcrVisualRow Row(string text, double x, double y, double height) =>
        new(text, text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((word, index) => new RecognizedWord(word, x + index * 30, y, 20, height))
            .ToList());
}
