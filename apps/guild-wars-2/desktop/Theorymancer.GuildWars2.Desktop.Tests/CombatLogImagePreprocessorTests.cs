using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogImagePreprocessorTests
{
    [Fact]
    public void Process_UpscalesAndProducesBlackAndWhitePixels()
    {
        var source = new CapturedFrame(
            QpcTimestamp: 123,
            Width: 2,
            Height: 1,
            Stride: 8,
            BgraPixels: [10, 10, 10, 255, 240, 240, 240, 255]);

        var processed = CombatLogImagePreprocessor.Process(source);

        Assert.Equal(123, processed.Frame.QpcTimestamp);
        Assert.Equal(6, processed.Frame.Width);
        Assert.Equal(3, processed.Frame.Height);
        Assert.Equal(24, processed.Frame.Stride);
        foreach (var pixel in processed.Frame.BgraPixels)
        {
            Assert.True(pixel is 0 or 255);
        }
    }
}
