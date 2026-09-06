using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class SkillCooldownMonitorTests
{
    [Fact]
    public async Task CaptureLoop_TracksTheCapturedCooldownTimeline()
    {
        var fixture = CooldownTimelineFixture.Load();
        var capture = new FixtureScreenRegionCapture(fixture.Frames);
        var snapshots = new Dictionary<long, SkillCooldownDiagnosticsSnapshot>();
        await using var monitor = new SkillCooldownMonitor(
            capture,
            fixture.Layout,
            fixture.Candidates,
            fixture.References,
            new SkillCooldownDetector(),
            new SkillCooldownTimeEstimator(fixture.QpcFrequency),
            TimeSpan.Zero);
        monitor.SnapshotUpdated += snapshot => snapshots[snapshot.QpcTimestamp] = snapshot;
        capture.Start();

        await capture.Drained;
        await monitor.DisposeAsync();

        foreach (var cooldown in fixture.Cooldowns)
        {
            for (var sequence = 1; sequence <= fixture.Frames.Count; sequence++)
            {
                var row = FindActiveRow(snapshots[fixture.GetTimestamp(sequence)], cooldown.ComponentKind);
                if (sequence < cooldown.FirstCooldownSequence || sequence == cooldown.FirstAvailableSequence)
                {
                    Assert.Equal(SkillCooldownDisplayState.Ready, row.State);
                    Assert.Null(row.Remaining);
                }
                else if (sequence < cooldown.FirstAvailableSequence)
                {
                    Assert.NotEqual(SkillCooldownDisplayState.Ready, row.State);
                }

                if (!cooldown.TryGetCheckpoint(sequence, out var checkpoint))
                {
                    continue;
                }

                Assert.Equal(SkillCooldownDisplayState.Cooling, row.State);
                var expectedRemainingMilliseconds = (fixture.GetTimestamp(cooldown.FirstAvailableSequence) -
                    fixture.GetTimestamp(sequence)) * 1000.0 / fixture.QpcFrequency;
                Assert.NotNull(row.Remaining);
                Assert.InRange(
                    Math.Abs(row.Remaining.Value.TotalMilliseconds - expectedRemainingMilliseconds),
                    0,
                    checkpoint.MaximumErrorMilliseconds);
            }
        }
    }

    private static SkillCooldownDiagnosticsRow FindActiveRow(
        SkillCooldownDiagnosticsSnapshot snapshot,
        SkillBarComponentKind kind) => Assert.Single(snapshot.Rows, row => row.Kind == kind && row.IsActive);
}
