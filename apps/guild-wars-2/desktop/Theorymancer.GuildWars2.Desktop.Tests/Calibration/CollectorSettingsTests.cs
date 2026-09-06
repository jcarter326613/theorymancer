using Theorymancer.GuildWars2.Desktop.Calibration;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CollectorSettingsTests
{
    [Fact]
    public void CombatLogCrop_UsesTheNamedCombatLogRegion()
    {
        var combatLogCrop = new NormalizedCrop(0.1, 0.2, 0.3, 0.4);
        var settings = new CollectorSettings(
        [
            new CalibratedRegion("interface-map", "Map", new NormalizedCrop(0, 0, 0.2, 0.2)),
            new CalibratedRegion(CalibratedRegion.CombatLogId, "Combat log", combatLogCrop),
        ]);

        Assert.Equal(combatLogCrop, settings.CombatLogCrop);
    }

    [Fact]
    public void SkillBarCrop_UsesTheNamedSkillBarRegion()
    {
        var skillBarCrop = new NormalizedCrop(0.2, 0.7, 0.6, 0.2);
        var settings = new CollectorSettings(
        [
            new CalibratedRegion("interface-map", "Map", new NormalizedCrop(0, 0, 0.2, 0.2)),
            new CalibratedRegion(CalibratedRegion.SkillBarId, "Skill bar", skillBarCrop),
            new CalibratedRegion(CalibratedRegion.CombatLogId, "Combat log", new NormalizedCrop(0.1, 0.2, 0.3, 0.4)),
        ]);

        Assert.Equal(skillBarCrop, settings.SkillBarCrop);
    }
}
