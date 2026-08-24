using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CalibrationTests
{
    [Fact]
    public void NormalizedCrop_RoundTripsWithinTheSameClientBounds()
    {
        var clientBounds = new ScreenBounds(100, 200, 1000, 800);
        var cropBounds = new ScreenBounds(200, 400, 500, 200);

        var crop = NormalizedCrop.FromScreenBounds(cropBounds, clientBounds);

        Assert.Equal(cropBounds, crop.ToScreenBounds(clientBounds));
    }

    [Fact]
    public void NormalizedCrop_RejectsACropOutsideTheGameWindow()
    {
        var clientBounds = new ScreenBounds(100, 200, 1000, 800);
        var cropBounds = new ScreenBounds(50, 200, 500, 200);

        Assert.Throws<ArgumentException>(() => NormalizedCrop.FromScreenBounds(cropBounds, clientBounds));
    }
}
