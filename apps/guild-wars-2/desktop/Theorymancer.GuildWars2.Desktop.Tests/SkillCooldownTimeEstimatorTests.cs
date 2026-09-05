using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class SkillCooldownTimeEstimatorTests
{
    [Fact]
    public void Observe_EstimatesCooldownFirstSeenPastHalfway()
    {
        var estimator = new SkillCooldownTimeEstimator(1_000);

        Assert.Null(estimator.Observe(Sample(0, SkillCooldownState.OnCooldown, 0.7)));
        Assert.Null(estimator.Observe(Sample(1_000, SkillCooldownState.OnCooldown, 0.8)));

        var estimate = Assert.IsType<SkillCooldownTimeEstimate>(
            estimator.Observe(Sample(2_000, SkillCooldownState.OnCooldown, 0.9)));

        Assert.Equal(SkillCooldownEstimateState.Tracking, estimate.State);
        Assert.Equal(TimeSpan.FromSeconds(1), estimate.Remaining);
        Assert.Equal(3, estimate.SampleCount);
    }

    [Fact]
    public void Observe_FitsOverlappingSkillsIndependently()
    {
        var estimator = new SkillCooldownTimeEstimator(1_000);

        Assert.Null(estimator.Observe(Sample(0, SkillCooldownState.OnCooldown, 0.1, SkillBarComponentKind.WeaponSkill2, 30163)));
        Assert.Null(estimator.Observe(Sample(500, SkillCooldownState.OnCooldown, 0.1, SkillBarComponentKind.WeaponSkill3, 30860)));
        Assert.Null(estimator.Observe(Sample(1_000, SkillCooldownState.OnCooldown, 0.2, SkillBarComponentKind.WeaponSkill2, 30163)));
        Assert.Null(estimator.Observe(Sample(1_500, SkillCooldownState.OnCooldown, 0.2, SkillBarComponentKind.WeaponSkill3, 30860)));

        var weapon2 = Assert.IsType<SkillCooldownTimeEstimate>(
            estimator.Observe(Sample(2_000, SkillCooldownState.OnCooldown, 0.3, SkillBarComponentKind.WeaponSkill2, 30163)));
        var weapon3 = Assert.IsType<SkillCooldownTimeEstimate>(
            estimator.Observe(Sample(2_500, SkillCooldownState.OnCooldown, 0.3, SkillBarComponentKind.WeaponSkill3, 30860)));

        Assert.Equal(SkillBarComponentKind.WeaponSkill2, weapon2.Kind);
        Assert.Equal(30163, weapon2.SkillId);
        Assert.Equal(TimeSpan.FromSeconds(7), weapon2.Remaining);
        Assert.Equal(SkillBarComponentKind.WeaponSkill3, weapon3.Kind);
        Assert.Equal(30860, weapon3.SkillId);
        Assert.Equal(TimeSpan.FromSeconds(7), weapon3.Remaining);
    }

    [Fact]
    public void Observe_ReportsCompletionAndDoesNotReuseCompletedSamples()
    {
        var estimator = new SkillCooldownTimeEstimator(1_000);

        _ = estimator.Observe(Sample(0, SkillCooldownState.OnCooldown, 0.1));
        _ = estimator.Observe(Sample(1_000, SkillCooldownState.OnCooldown, 0.2));
        _ = estimator.Observe(Sample(2_000, SkillCooldownState.OnCooldown, 0.3));

        var completed = Assert.IsType<SkillCooldownTimeEstimate>(
            estimator.Observe(Sample(9_000, SkillCooldownState.Available, null)));
        Assert.Equal(SkillCooldownEstimateState.Completed, completed.State);
        Assert.Equal(TimeSpan.Zero, completed.Remaining);

        Assert.Null(estimator.Observe(Sample(10_000, SkillCooldownState.OnCooldown, 0.7)));
        Assert.Null(estimator.Observe(Sample(11_000, SkillCooldownState.OnCooldown, 0.6)));
    }

    [Fact]
    public void Observe_ReopensCooldownWhenARecastOccursBeforeAnAvailableSample()
    {
        var estimator = new SkillCooldownTimeEstimator(1_000);

        _ = estimator.Observe(Sample(0, SkillCooldownState.OnCooldown, 0.1));
        _ = estimator.Observe(Sample(1_000, SkillCooldownState.OnCooldown, 0.3));
        _ = estimator.Observe(Sample(2_000, SkillCooldownState.OnCooldown, 0.5));

        Assert.Null(estimator.Observe(Sample(3_000, SkillCooldownState.OnCooldown, 0.1)));
        Assert.Null(estimator.Observe(Sample(4_000, SkillCooldownState.OnCooldown, 0.2)));

        var recast = Assert.IsType<SkillCooldownTimeEstimate>(
            estimator.Observe(Sample(5_000, SkillCooldownState.OnCooldown, 0.3)));

        Assert.Equal(SkillCooldownEstimateState.Tracking, recast.State);
        Assert.Equal(3, recast.SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(7), recast.Remaining);
    }

    [Fact]
    public void Observe_CompletesInsteadOfReportingAZeroSecondCooldown()
    {
        var estimator = new SkillCooldownTimeEstimator(1_000);

        Assert.Null(estimator.Observe(Sample(0, SkillCooldownState.OnCooldown, 0.8)));
        Assert.Null(estimator.Observe(Sample(1_000, SkillCooldownState.OnCooldown, 0.95)));

        var completion = Assert.IsType<SkillCooldownTimeEstimate>(
            estimator.Observe(Sample(2_000, SkillCooldownState.OnCooldown, 1)));

        Assert.Equal(SkillCooldownEstimateState.Completed, completion.State);
        Assert.Equal(TimeSpan.Zero, completion.Remaining);
    }

    private static SkillCooldownWipeSample Sample(
        long qpcTimestamp,
        SkillCooldownState state,
        double? visibleWipeFraction,
        SkillBarComponentKind kind = SkillBarComponentKind.WeaponSkill2,
        int skillId = 30163) => new(
        kind,
        skillId,
        qpcTimestamp,
        state,
        visibleWipeFraction,
        1);
}
