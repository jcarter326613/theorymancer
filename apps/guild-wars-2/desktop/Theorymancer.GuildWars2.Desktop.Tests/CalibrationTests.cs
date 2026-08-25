using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.Capture;
using System.IO;

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

    [Fact]
    public void CollectorSettings_UsesTheNamedCombatLogRegion()
    {
        var combatLogCrop = new NormalizedCrop(0.1, 0.2, 0.3, 0.4);
        var settings = new CollectorSettings(
            [
                new CalibratedRegion("interface-map", "Map", new NormalizedCrop(0, 0, 0.2, 0.2)),
                new CalibratedRegion(CalibratedRegion.CombatLogId, "Combat log", combatLogCrop),
            ],
            RowHeightPixels: 20);

        Assert.Equal(combatLogCrop, settings.CombatLogCrop);
    }

    [Fact]
    public void CollectorSettingsStore_MigratesTheLegacyCrop()
    {
        var path = Path.Combine(Path.GetTempPath(), $"theorymancer-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {"Crop":{"X":0.1,"Y":0.2,"Width":0.3,"Height":0.4},"RowHeightPixels":24}
                """);

            var settings = new CollectorSettingsStore(path).Load();

            var region = Assert.Single(settings.Regions);
            Assert.Equal(CalibratedRegion.CombatLogId, region.Id);
            Assert.Equal("Combat log", region.Name);
            Assert.Equal(new NormalizedCrop(0.1, 0.2, 0.3, 0.4), region.Crop);
            Assert.Equal(24, settings.RowHeightPixels);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
