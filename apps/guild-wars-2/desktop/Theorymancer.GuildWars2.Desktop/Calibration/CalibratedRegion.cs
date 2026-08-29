namespace Theorymancer.GuildWars2.Desktop.Calibration;

public sealed record CalibratedRegion(string Id, string Name, NormalizedCrop Crop)
{
    public const string CombatLogId = "combat-log";
    public const string SkillBarId = "skill-bar";
}
