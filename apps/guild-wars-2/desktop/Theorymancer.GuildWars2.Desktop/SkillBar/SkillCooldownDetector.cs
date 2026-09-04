using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public enum SkillCooldownState
{
    Unknown,
    Available,
    OnCooldown,
}

public sealed record SkillCooldownReference(
    SkillBarComponentKind Kind,
    int SkillId,
    string IconPath);

public sealed record SkillCooldownObservation(
    SkillBarComponentKind Kind,
    int SkillId,
    SkillCooldownState State,
    double Confidence,
    double? VisibleWipeFraction);

public sealed record SkillCooldownDetection(
    long QpcTimestamp,
    IReadOnlyList<SkillCooldownObservation> Observations);

public interface ISkillCooldownDetector
{
    SkillCooldownDetection Detect(
        CapturedFrame frame,
        SkillBarLayout layout,
        IReadOnlyList<SkillCooldownReference> references);
}

public sealed class SkillCooldownDetector : ISkillCooldownDetector
{
    public SkillCooldownDetection Detect(
        CapturedFrame frame,
        SkillBarLayout layout,
        IReadOnlyList<SkillCooldownReference> references) =>
        throw new NotImplementedException("Skill cooldown detection has not been implemented.");
}
