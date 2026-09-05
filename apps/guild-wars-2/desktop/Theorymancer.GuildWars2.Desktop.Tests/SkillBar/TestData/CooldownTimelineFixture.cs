using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

internal sealed class CooldownTimelineFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private CooldownTimelineFixture(
        long qpcFrequency,
        SkillBarLayout layout,
        IReadOnlyList<CapturedFrame> frames,
        IReadOnlyList<SkillCooldownCandidate> candidates,
        IReadOnlyList<SkillCooldownReference> references,
        IReadOnlyList<CooldownExpectation> cooldowns)
    {
        QpcFrequency = qpcFrequency;
        Layout = layout;
        Frames = frames;
        Candidates = candidates;
        References = references;
        Cooldowns = cooldowns;
    }

    public long QpcFrequency { get; }
    public SkillBarLayout Layout { get; }
    public IReadOnlyList<CapturedFrame> Frames { get; }
    public IReadOnlyList<SkillCooldownCandidate> Candidates { get; }
    public IReadOnlyList<SkillCooldownReference> References { get; }
    public IReadOnlyList<CooldownExpectation> Cooldowns { get; }

    public static CooldownTimelineFixture Load()
    {
        var fixture = Deserialize<CooldownTimesFile>(Path.Combine(FixtureDirectory, "expectations.json"));
        var timeline = Deserialize<TimelineFile>(Path.Combine(FixtureDirectory, "timeline.json"));
        var referenceDirectory = Path.Combine(FixturesDirectory, fixture.ReferenceFixture);
        var reference = Deserialize<ReferenceFile>(Path.Combine(referenceDirectory, "expectations.json"));
        var layout = new SkillBarLayout(timeline.Slots.Select(slot => SkillBarComponent.FromPixelBounds(
            slot.ComponentKind,
            new ScreenBounds(slot.X, slot.Y, slot.Width, slot.Height),
            timeline.CaptureWidth,
            timeline.CaptureHeight,
            1)).ToList());
        var candidates = reference.Slots.Select(slot => new SkillCooldownCandidate(
            slot.ComponentKind,
            slot.SkillId,
            slot.IconFile,
            Path.Combine(referenceDirectory, "icons", slot.IconFile),
            IsWeaponSkill(slot.ComponentKind) ? 2 : null)).ToList();
        var references = candidates.Select(candidate =>
        {
            var component = layout.Components.Single(value => value.Kind == candidate.Kind);
            var resolved = SkillCooldownDetector.ResolveReference(new SkillCooldownReference(
                candidate.Kind,
                candidate.SkillId,
                candidate.IconPath!,
                component.ToPixelBounds(timeline.CaptureWidth, timeline.CaptureHeight)));
            return resolved with { IconPath = $"startup-resolved-{candidate.SkillId}.png" };
        }).ToList();
        var frames = timeline.Frames.Select(frame => LoadFrame(
            Path.Combine(FixtureDirectory, frame.File),
            frame.QpcTimestamp)).ToList();
        return new CooldownTimelineFixture(
            timeline.QpcFrequency,
            layout,
            frames,
            candidates,
            references,
            fixture.Cooldowns);
    }

    public CapturedFrame GetFrame(int sequence) => Frames[sequence - 1];

    public long GetTimestamp(int sequence) => GetFrame(sequence).QpcTimestamp;

    private static T Deserialize<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"Fixture is invalid: {path}");

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

    private static bool IsWeaponSkill(SkillBarComponentKind kind) => kind is
        SkillBarComponentKind.WeaponSkill1 or
        SkillBarComponentKind.WeaponSkill2 or
        SkillBarComponentKind.WeaponSkill3 or
        SkillBarComponentKind.WeaponSkill4 or
        SkillBarComponentKind.WeaponSkill5;

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "SkillBar", "Fixtures");
    private static string FixtureDirectory => Path.Combine(FixturesDirectory, "reaper-greatsword-cooldown-times");

    public sealed record CooldownExpectation(
        string Kind,
        int FirstCooldownSequence,
        int FirstAvailableSequence,
        IReadOnlyList<int> SampleSequences,
        IReadOnlyList<EstimateCheckpoint> EstimateCheckpoints)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);

        public bool TryGetCheckpoint(int sequence, out EstimateCheckpoint checkpoint)
        {
            checkpoint = EstimateCheckpoints.SingleOrDefault(value => value.Sequence == sequence)
                ?? new EstimateCheckpoint(0, 0);
            return checkpoint.Sequence != 0;
        }
    }

    public sealed record EstimateCheckpoint(int Sequence, double MaximumErrorMilliseconds);

    private sealed record CooldownTimesFile(string ReferenceFixture, IReadOnlyList<CooldownExpectation> Cooldowns);
    private sealed record TimelineFile(long QpcFrequency, int CaptureWidth, int CaptureHeight, IReadOnlyList<TimelineSlot> Slots, IReadOnlyList<TimelineFrame> Frames);
    private sealed record TimelineSlot(string Kind, int X, int Y, int Width, int Height)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);
    }

    private sealed record TimelineFrame(int Sequence, string File, long QpcTimestamp);
    private sealed record ReferenceFile(IReadOnlyList<ReferenceSlot> Slots);
    private sealed record ReferenceSlot(string Kind, int SkillId, string IconFile)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);
    }
}

internal sealed class FixtureScreenRegionCapture : IScreenRegionCapture
{
    private readonly Queue<CapturedFrame> _frames;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FixtureScreenRegionCapture(IEnumerable<CapturedFrame> frames)
    {
        _frames = new Queue<CapturedFrame>(frames);
    }

    public Task Drained => _drained.Task;

    public void Start() => _started.TrySetResult();

    public ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken) =>
        new(CaptureCoreAsync(cancellationToken));

    private async Task<CapturedFrame> CaptureCoreAsync(CancellationToken cancellationToken)
    {
        await _started.Task.WaitAsync(cancellationToken);
        if (_frames.TryDequeue(out var frame))
        {
            if (_frames.Count == 0)
            {
                _drained.TrySetResult();
            }

            return frame;
        }

        _drained.TrySetResult();
        return await WaitForCancellationAsync(cancellationToken);
    }

    private static async Task<CapturedFrame> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The frame capture should only complete when cancelled.");
    }
}
