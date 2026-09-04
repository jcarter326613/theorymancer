using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CooldownTimeFixtureTests
{
    [Fact]
    public void Observe_TracksOverlappingCapturedCooldownsUntilEachSkillIsAvailable()
    {
        var fixture = LoadFixture();
        var timeline = LoadTimeline();
        var framesBySequence = timeline.Frames.ToDictionary(frame => frame.Sequence);
        var layout = CreateLayout(timeline);
        var references = CreateReferences(fixture.ReferenceFixture);
        var detector = new SkillCooldownDetector();
        var estimator = new SkillCooldownTimeEstimator(timeline.QpcFrequency);
        var latestEstimates = new Dictionary<SkillBarComponentKind, SkillCooldownTimeEstimate>();
        var selectedSequences = fixture.Cooldowns
            .SelectMany(cooldown => cooldown.SampleSequences)
            .Distinct()
            .Order()
            .ToList();

        foreach (var sequence in selectedSequences)
        {
            var frameInfo = framesBySequence[sequence];
            var frame = LoadFrame(Path.Combine(FixtureDirectory, frameInfo.File), frameInfo.QpcTimestamp);
            var detection = detector.Detect(frame, layout, references);
            foreach (var cooldown in fixture.Cooldowns.Where(cooldown => cooldown.SampleSequences.Contains(sequence)))
            {
                var observation = Assert.Single(detection.Observations, candidate => candidate.Kind == cooldown.ComponentKind);
                var isCompletion = sequence == cooldown.FirstAvailableSequence;
                if (sequence == cooldown.FirstCooldownSequence)
                {
                    Assert.Equal(SkillCooldownState.OnCooldown, observation.State);
                }

                if (isCompletion)
                {
                    Assert.Equal(SkillCooldownState.Available, observation.State);
                    Assert.Null(observation.VisibleWipeFraction);
                }
                else
                {
                    Assert.NotNull(observation.VisibleWipeFraction);
                }

                var estimate = estimator.Observe(new SkillCooldownWipeSample(
                    observation.Kind,
                    observation.SkillId,
                    frameInfo.QpcTimestamp,
                    observation.State,
                    observation.VisibleWipeFraction,
                    observation.Confidence));
                if (isCompletion)
                {
                    var completed = Assert.IsType<SkillCooldownTimeEstimate>(estimate);
                    Assert.Equal(SkillCooldownEstimateState.Completed, completed.State);
                    Assert.Equal(TimeSpan.Zero, completed.Remaining);
                    if (cooldown.ComponentKind == SkillBarComponentKind.WeaponSkill2)
                    {
                        Assert.Equal(
                            SkillCooldownEstimateState.Tracking,
                            latestEstimates[SkillBarComponentKind.WeaponSkill3].State);
                    }

                    continue;
                }

                if (cooldown.TryGetCheckpoint(sequence, out var checkpoint))
                {
                    var tracking = Assert.IsType<SkillCooldownTimeEstimate>(estimate);
                    Assert.Equal(SkillCooldownEstimateState.Tracking, tracking.State);
                    var completion = framesBySequence[cooldown.FirstAvailableSequence];
                    var expectedRemainingMilliseconds =
                        (completion.QpcTimestamp - frameInfo.QpcTimestamp) * 1000.0 / timeline.QpcFrequency;
                    Assert.InRange(
                        Math.Abs(tracking.Remaining.TotalMilliseconds - expectedRemainingMilliseconds),
                        0,
                        checkpoint.MaximumErrorMilliseconds);
                    latestEstimates[cooldown.ComponentKind] = tracking;
                }
            }
        }
    }

    private static string FixtureDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "SkillBar",
        "reaper-greatsword-cooldown-times");

    private static TimelineFixture LoadTimeline() =>
        JsonSerializer.Deserialize<TimelineFixture>(
            File.ReadAllText(Path.Combine(FixtureDirectory, "timeline.json")),
            JsonOptions)
        ?? throw new InvalidOperationException("Cooldown timeline fixture is invalid.");

    private static CooldownTimesFixture LoadFixture() =>
        JsonSerializer.Deserialize<CooldownTimesFixture>(
            File.ReadAllText(Path.Combine(FixtureDirectory, "expectations.json")),
            JsonOptions)
        ?? throw new InvalidOperationException("Cooldown time expectations are invalid.");

    private static SkillBarLayout CreateLayout(TimelineFixture timeline) => new(
        timeline.Slots.Select(slot => SkillBarComponent.FromPixelBounds(
                slot.ComponentKind,
                new ScreenBounds(slot.X, slot.Y, slot.Width, slot.Height),
                timeline.CaptureWidth,
                timeline.CaptureHeight,
                1))
            .ToList());

    private static IReadOnlyList<SkillCooldownReference> CreateReferences(string referenceFixture)
    {
        var reference = JsonSerializer.Deserialize<ReferenceFixture>(
            File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "SkillBar",
                referenceFixture,
                "expectations.json")),
            JsonOptions)
            ?? throw new InvalidOperationException($"Reference fixture is invalid: {referenceFixture}");
        return reference.Slots.Select(slot => new SkillCooldownReference(
            slot.ComponentKind,
            slot.SkillId,
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "SkillBar",
                referenceFixture,
                "icons",
                slot.IconFile)))
            .ToList();
    }

    private static CapturedFrame LoadFrame(string path, long qpcTimestamp)
    {
        using var bitmap = new Bitmap(path);
        var stride = bitmap.Width * 4;
        var pixels = new byte[stride * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var index = y * stride + x * 4;
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = color.A;
            }
        }

        return new CapturedFrame(qpcTimestamp, bitmap.Width, bitmap.Height, stride, pixels);
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record CooldownTimesFixture(string ReferenceFixture, IReadOnlyList<CooldownExpectation> Cooldowns);

    private sealed record CooldownExpectation(
        string Kind,
        int FirstCooldownSequence,
        int FirstAvailableSequence,
        IReadOnlyList<int> SampleSequences,
        IReadOnlyList<EstimateCheckpoint> EstimateCheckpoints)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);

        public bool TryGetCheckpoint(int sequence, out EstimateCheckpoint checkpoint)
        {
            checkpoint = EstimateCheckpoints.SingleOrDefault(checkpoint => checkpoint.Sequence == sequence)
                ?? new EstimateCheckpoint(0, 0);
            return checkpoint.Sequence != 0;
        }
    }

    private sealed record EstimateCheckpoint(int Sequence, double MaximumErrorMilliseconds);

    private sealed record TimelineFixture(
        long QpcFrequency,
        int CaptureWidth,
        int CaptureHeight,
        IReadOnlyList<TimelineSlot> Slots,
        IReadOnlyList<TimelineFrame> Frames);

    private sealed record TimelineSlot(string Kind, int X, int Y, int Width, int Height)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);
    }

    private sealed record TimelineFrame(int Sequence, string File, long QpcTimestamp);

    private sealed record ReferenceFixture(IReadOnlyList<ReferenceSlot> Slots);

    private sealed record ReferenceSlot(string Kind, int SkillId, string IconFile)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);
    }
}
