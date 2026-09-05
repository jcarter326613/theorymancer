using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CollectorSettingsStoreTests
{
    [Fact]
    public void Save_PersistsBothRequiredRegions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"theorymancer-{Guid.NewGuid():N}.json");
        try
        {
            var expected = new CollectorSettings(
            [
                new CalibratedRegion(CalibratedRegion.CombatLogId, "Combat log", new NormalizedCrop(0.1, 0.2, 0.3, 0.4)),
                new CalibratedRegion(CalibratedRegion.SkillBarId, "Skill bar", new NormalizedCrop(0.2, 0.7, 0.6, 0.2)),
            ]);

            var store = new CollectorSettingsStore(path);
            store.Save(expected);

            Assert.Equal(expected.Regions, store.Load().Regions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_PersistsTheDerivedSkillBarLayout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"theorymancer-{Guid.NewGuid():N}.json");
        try
        {
            var layout = new SkillBarLayout(Enum.GetValues<SkillBarComponentKind>()
                .Select(kind => new SkillBarComponent(kind, 0.1, 0.2, 0.1, 0.1, 0.9)).ToList());
            var store = new CollectorSettingsStore(path);
            store.Save(new CollectorSettings([], layout));

            var actual = store.Load().SkillBarLayout;
            Assert.NotNull(actual);
            Assert.Equal(layout.Components, actual.Components);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MigratesTheLegacyCropAndIgnoresObsoleteRowHeight()
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
        }
        finally
        {
            File.Delete(path);
        }
    }
}
