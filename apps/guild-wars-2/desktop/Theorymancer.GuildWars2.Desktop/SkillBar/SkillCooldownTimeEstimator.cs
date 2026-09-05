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
    private const double AcquisitionMaximumVisibleWipeFraction = 0.5;
    private const int MinimumSamplesForEstimate = 3;
    private readonly long _qpcFrequency;
    private readonly Dictionary<(SkillBarComponentKind Kind, int SkillId), ActiveCooldown> _activeCooldowns = [];

    public SkillCooldownTimeEstimator(long qpcFrequency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(qpcFrequency, 0);
        _qpcFrequency = qpcFrequency;
    }

    public SkillCooldownTimeEstimate? Observe(SkillCooldownWipeSample sample)
    {
        var key = (sample.Kind, sample.SkillId);
        if (!_activeCooldowns.TryGetValue(key, out var cooldown))
        {
            if (sample.State != SkillCooldownState.OnCooldown ||
                sample.VisibleWipeFraction is not { } initialWipe ||
                initialWipe > AcquisitionMaximumVisibleWipeFraction)
            {
                return null;
            }

            cooldown = new ActiveCooldown();
            _activeCooldowns.Add(key, cooldown);
        }

        if (sample.State == SkillCooldownState.Available && sample.VisibleWipeFraction is null)
        {
            _activeCooldowns.Remove(key);
            return new SkillCooldownTimeEstimate(
                sample.Kind,
                sample.SkillId,
                sample.QpcTimestamp,
                SkillCooldownEstimateState.Completed,
                TimeSpan.Zero,
                sample.Confidence,
                cooldown.Samples.Count);
        }

        if (sample.VisibleWipeFraction is not { } visibleWipeFraction ||
            visibleWipeFraction is < 0 or > 1 ||
            cooldown.Samples.Count > 0 && sample.QpcTimestamp <= cooldown.Samples[^1].QpcTimestamp)
        {
            return null;
        }

        cooldown.Samples.Add(new WipeSample(sample.QpcTimestamp, visibleWipeFraction, sample.Confidence));
        if (cooldown.Samples.Count < MinimumSamplesForEstimate ||
            !TryFit(cooldown.Samples, out var slopePerSecond, out var fittedVisibleWipeFraction))
        {
            return null;
        }

        var remainingSeconds = Math.Max(0, (1 - fittedVisibleWipeFraction) / slopePerSecond);
        return new SkillCooldownTimeEstimate(
            sample.Kind,
            sample.SkillId,
            sample.QpcTimestamp,
            SkillCooldownEstimateState.Tracking,
            TimeSpan.FromSeconds(remainingSeconds),
            GetConfidence(cooldown.Samples, slopePerSecond, fittedVisibleWipeFraction),
            cooldown.Samples.Count);
    }

    private bool TryFit(
        IReadOnlyList<WipeSample> samples,
        out double slopePerSecond,
        out double fittedVisibleWipeFraction)
    {
        var firstQpc = samples[0].QpcTimestamp;
        var meanTime = samples.Average(sample => (sample.QpcTimestamp - firstQpc) / (double)_qpcFrequency);
        var meanWipe = samples.Average(sample => sample.VisibleWipeFraction);
        var covariance = 0.0;
        var variance = 0.0;
        foreach (var sample in samples)
        {
            var seconds = (sample.QpcTimestamp - firstQpc) / (double)_qpcFrequency;
            covariance += (seconds - meanTime) * (sample.VisibleWipeFraction - meanWipe);
            variance += (seconds - meanTime) * (seconds - meanTime);
        }

        slopePerSecond = variance == 0 ? 0 : covariance / variance;
        fittedVisibleWipeFraction = meanWipe + slopePerSecond *
            ((samples[^1].QpcTimestamp - firstQpc) / (double)_qpcFrequency - meanTime);
        return slopePerSecond > 0;
    }

    private double GetConfidence(
        IReadOnlyList<WipeSample> samples,
        double slopePerSecond,
        double fittedVisibleWipeFraction)
    {
        var meanWipe = samples.Average(sample => sample.VisibleWipeFraction);
        var totalError = 0.0;
        var totalVariation = 0.0;
        foreach (var sample in samples)
        {
            var predicted = fittedVisibleWipeFraction + slopePerSecond *
                (sample.QpcTimestamp - samples[^1].QpcTimestamp) / (double)_qpcFrequency;
            totalError += Math.Pow(sample.VisibleWipeFraction - predicted, 2);
            totalVariation += Math.Pow(sample.VisibleWipeFraction - meanWipe, 2);
        }

        var fit = totalVariation == 0 ? 0 : Math.Clamp(1 - totalError / totalVariation, 0, 1);
        return fit * samples.Average(sample => sample.Confidence);
    }

    private sealed class ActiveCooldown
    {
        public List<WipeSample> Samples { get; } = [];
    }

    private sealed record WipeSample(long QpcTimestamp, double VisibleWipeFraction, double Confidence);
}
