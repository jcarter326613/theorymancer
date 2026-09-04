namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public sealed record SkillCooldownWipeSample(
    SkillBarComponentKind Kind,
    int SkillId,
    long QpcTimestamp,
    SkillCooldownState State,
    double? VisibleWipeFraction,
    double Confidence);

public enum SkillCooldownEstimateState
{
    Tracking,
    Completed,
}

public sealed record SkillCooldownTimeEstimate(
    SkillBarComponentKind Kind,
    int SkillId,
    long QpcTimestamp,
    SkillCooldownEstimateState State,
    TimeSpan Remaining,
    double Confidence,
    int SampleCount);

public interface ISkillCooldownTimeEstimator
{
    SkillCooldownTimeEstimate? Observe(SkillCooldownWipeSample sample);
}

public sealed class SkillCooldownTimeEstimator : ISkillCooldownTimeEstimator
{
    public SkillCooldownTimeEstimator(long qpcFrequency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(qpcFrequency, 0);
    }

    public SkillCooldownTimeEstimate? Observe(SkillCooldownWipeSample sample) =>
        throw new NotImplementedException("Cooldown-time estimation has not been implemented.");
}
