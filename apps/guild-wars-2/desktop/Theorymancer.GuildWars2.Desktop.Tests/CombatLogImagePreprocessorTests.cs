using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogImagePreprocessorTests
{
    [Fact]
    public void Process_UpscalesAndPreservesColorPixels()
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
        Assert.Equal([10, 10, 10, 255, 10, 10, 10, 255, 10, 10, 10, 255], processed.Frame.BgraPixels[..12]);
        Assert.Equal([240, 240, 240, 255, 240, 240, 240, 255, 240, 240, 240, 255], processed.Frame.BgraPixels[12..24]);
    }
}
