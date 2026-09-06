using System.Diagnostics;
using Theorymancer.GuildWars2.Desktop.ArenaNet;
using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public sealed record SkillCooldownCandidate(
    SkillBarComponentKind Kind,
    int SkillId,
    string Name,
    string? IconPath,
    int? WeaponSet = null);

public sealed class SkillCooldownIdentityLock
{
    private readonly Dictionary<SkillBarComponentKind, SkillCooldownCandidate> _candidates = [];
    private int? _weaponSet;

    public IReadOnlyDictionary<SkillBarComponentKind, SkillCooldownCandidate> Candidates => _candidates;

    public int? WeaponSet => _weaponSet;

    public bool IsLocked(SkillBarComponentKind kind) => _candidates.ContainsKey(kind);

    public bool CanIdentify(SkillCooldownCandidate candidate) =>
        !IsWeaponSkill(candidate.Kind) ||
        candidate.WeaponSet is null ||
        _weaponSet is null ||
        candidate.WeaponSet == _weaponSet;

    public bool TryLock(SkillCooldownCandidate candidate)
    {
        if (!CanIdentify(candidate) || !_candidates.TryAdd(candidate.Kind, candidate))
        {
            return false;
        }

        if (IsWeaponSkill(candidate.Kind) && candidate.WeaponSet is not null)
        {
            _weaponSet ??= candidate.WeaponSet;
        }

        return true;
    }

    private static bool IsWeaponSkill(SkillBarComponentKind kind) => kind is
        SkillBarComponentKind.WeaponSkill1 or
        SkillBarComponentKind.WeaponSkill2 or
        SkillBarComponentKind.WeaponSkill3 or
        SkillBarComponentKind.WeaponSkill4 or
        SkillBarComponentKind.WeaponSkill5;
}

public enum SkillCooldownDisplayState
{
    NotOnActiveBar,
    Ready,
    Measuring,
    Cooling,
    Unknown,
}

public sealed record SkillCooldownDisplay(
    SkillCooldownDisplayState State,
    TimeSpan? Remaining);

public sealed record SkillCooldownDiagnosticsRow(
    SkillBarComponentKind Kind,
    int SkillId,
    string SkillName,
    bool IsActive,
    SkillCooldownDisplayState State,
    TimeSpan? Remaining,
    bool StartsSection)
{
    public string SlotLabel => Kind switch
    {
        SkillBarComponentKind.WeaponSkill1 => "Weapon 1",
        SkillBarComponentKind.WeaponSkill2 => "Weapon 2",
        SkillBarComponentKind.WeaponSkill3 => "Weapon 3",
        SkillBarComponentKind.WeaponSkill4 => "Weapon 4",
        SkillBarComponentKind.WeaponSkill5 => "Weapon 5",
        SkillBarComponentKind.HealSkill => "Heal",
        SkillBarComponentKind.UtilitySkill1 => "Utility 1",
        SkillBarComponentKind.UtilitySkill2 => "Utility 2",
        SkillBarComponentKind.UtilitySkill3 => "Utility 3",
        SkillBarComponentKind.EliteSkill => "Elite",
        _ => Kind.ToString(),
    };

    public string StatusText => State switch
    {
        SkillCooldownDisplayState.NotOnActiveBar => "Not on active bar",
        SkillCooldownDisplayState.Ready => "Ready",
        SkillCooldownDisplayState.Measuring => "Measuring",
        SkillCooldownDisplayState.Cooling => "Cooling",
        _ => "Unknown",
    };

    public string RemainingText => Remaining is { } remaining
        ? $"{remaining.TotalSeconds:F1}s"
        : "-";
}

public sealed record SkillCooldownDiagnosticsSnapshot(
    long QpcTimestamp,
    IReadOnlyList<SkillCooldownDiagnosticsRow> Rows);

public static class SkillCooldownDiagnostics
{
    public static SkillCooldownDiagnosticsSnapshot CreateSnapshot(
        long qpcTimestamp,
        IReadOnlyList<SkillCooldownCandidate> candidates,
        IReadOnlyDictionary<SkillBarComponentKind, int> activeSkillIds,
        IReadOnlyDictionary<(SkillBarComponentKind Kind, int SkillId), SkillCooldownDisplay> displays)
    {
        var rows = new List<SkillCooldownDiagnosticsRow>();
        var hasWeaponRows = false;
        foreach (var weaponSet in new[] { 1, 2 })
        {
            var firstInSet = true;
            foreach (var kind in WeaponKinds)
            {
                var slotCandidates = candidates
                    .Where(candidate => candidate.Kind == kind && candidate.WeaponSet == weaponSet)
                    .OrderBy(candidate => candidate.SkillId)
                    .ToList();
                AddRows(slotCandidates, hasWeaponRows && firstInSet, rows, activeSkillIds, displays);
                if (slotCandidates.Count > 0)
                {
                    firstInSet = false;
                    hasWeaponRows = true;
                }
            }
        }

        var firstUnassignedWeapon = true;
        foreach (var kind in WeaponKinds)
        {
            var slotCandidates = candidates
                .Where(candidate => candidate.Kind == kind && candidate.WeaponSet is null)
                .OrderBy(candidate => candidate.SkillId)
                .ToList();
            AddRows(slotCandidates, hasWeaponRows && firstUnassignedWeapon, rows, activeSkillIds, displays);
            if (slotCandidates.Count > 0)
            {
                firstUnassignedWeapon = false;
                hasWeaponRows = true;
            }
        }

        var firstUtility = true;
        foreach (var kind in UtilityKinds)
        {
            var slotCandidates = candidates
                .Where(candidate => candidate.Kind == kind)
                .OrderBy(candidate => candidate.SkillId)
                .ToList();
            AddRows(slotCandidates, hasWeaponRows && firstUtility, rows, activeSkillIds, displays);
            if (slotCandidates.Count > 0)
            {
                firstUtility = false;
            }
        }

        return new SkillCooldownDiagnosticsSnapshot(qpcTimestamp, rows);
    }

    private static readonly SkillBarComponentKind[] WeaponKinds =
    [
        SkillBarComponentKind.WeaponSkill1,
        SkillBarComponentKind.WeaponSkill2,
        SkillBarComponentKind.WeaponSkill3,
        SkillBarComponentKind.WeaponSkill4,
        SkillBarComponentKind.WeaponSkill5,
    ];

    private static readonly SkillBarComponentKind[] UtilityKinds =
    [
        SkillBarComponentKind.HealSkill,
        SkillBarComponentKind.UtilitySkill1,
        SkillBarComponentKind.UtilitySkill2,
        SkillBarComponentKind.UtilitySkill3,
        SkillBarComponentKind.EliteSkill,
    ];

    private static void AddRows(
        IReadOnlyList<SkillCooldownCandidate> candidates,
        bool startsSection,
        ICollection<SkillCooldownDiagnosticsRow> rows,
        IReadOnlyDictionary<SkillBarComponentKind, int> activeSkillIds,
        IReadOnlyDictionary<(SkillBarComponentKind Kind, int SkillId), SkillCooldownDisplay> displays)
    {
        foreach (var candidate in candidates)
        {
            var isActive = activeSkillIds.GetValueOrDefault(candidate.Kind) == candidate.SkillId;
            var display = isActive
                ? displays.GetValueOrDefault(
                    (candidate.Kind, candidate.SkillId),
                    new SkillCooldownDisplay(SkillCooldownDisplayState.Unknown, null))
                : new SkillCooldownDisplay(SkillCooldownDisplayState.NotOnActiveBar, null);
            rows.Add(new SkillCooldownDiagnosticsRow(
                candidate.Kind,
                candidate.SkillId,
                candidate.Name,
                isActive,
                display.State,
                display.Remaining,
                startsSection));
            startsSection = false;
        }
    }
}

public sealed class SkillCooldownMonitor : IAsyncDisposable, IDisposable
{
    private const int CaptureFramesPerSecond = 12;
    private readonly IScreenRegionCapture _capture;
    private readonly SkillBarLayout _layout;
    private readonly IReadOnlyList<SkillCooldownCandidate> _candidates;
    private readonly IReadOnlyDictionary<SkillBarComponentKind, SkillCooldownReference> _references;
    private readonly ISkillCooldownDetector _detector;
    private readonly ISkillCooldownTimeEstimator _estimator;
    private readonly TimeSpan _captureInterval;
    private readonly SkillCooldownIdentityLock _activeCandidates = new();
    private readonly Dictionary<(SkillBarComponentKind Kind, int SkillId), SkillCooldownDisplay> _displays = [];
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _captureTask;
    private bool _disposed;

    internal SkillCooldownMonitor(
        IScreenRegionCapture capture,
        SkillBarLayout layout,
        IReadOnlyList<SkillCooldownCandidate> candidates,
        IReadOnlyList<SkillCooldownReference> references,
        ISkillCooldownDetector detector,
        ISkillCooldownTimeEstimator estimator,
        TimeSpan captureInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(captureInterval, TimeSpan.Zero);
        _capture = capture;
        _layout = layout;
        _candidates = candidates;
        _references = references.ToDictionary(reference => reference.Kind);
        _detector = detector;
        _estimator = estimator;
        _captureInterval = captureInterval;
        foreach (var reference in references)
        {
            var candidate = candidates.Single(candidate =>
                candidate.Kind == reference.Kind && candidate.SkillId == reference.SkillId);
            _activeCandidates.TryLock(candidate);
        }

        _captureTask = Task.Run(CaptureLoopAsync);
    }

    public event Action<SkillCooldownDiagnosticsSnapshot>? SnapshotUpdated;

    public event Action<string>? StatusChanged;

    public static async Task<SkillCooldownMonitor> StartAsync(
        SelectedGameWindow gameWindow,
        NormalizedCrop skillBarCrop,
        SkillBarLayout layout,
        BuildSkillCandidates buildCandidates,
        ReferenceIcons referenceIcons,
        CancellationToken cancellationToken)
    {
        var candidates = new List<SkillCooldownCandidate>();
        foreach (var kind in Enum.GetValues<SkillBarComponentKind>())
        {
            foreach (var skillId in buildCandidates.GetSkillIds(kind))
            {
                var skill = referenceIcons.FindSkill(skillId);
                string? iconPath = null;
                if (skill is not null)
                {
                    try
                    {
                        iconPath = await referenceIcons.GetSkillPathAsync(skillId, cancellationToken);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                    }
                }

                candidates.Add(new SkillCooldownCandidate(
                    kind,
                    skillId,
                    skill?.Name ?? $"Skill {skillId}",
                    iconPath,
                    buildCandidates.GetWeaponSet(kind, skillId)));
            }
        }

        var capture = new VisibleScreenRegionCapture(gameWindow, skillBarCrop);
        var startupFrame = await capture.CaptureAsync(cancellationToken);
        var references = IdentifyInitialReferences(startupFrame, layout, candidates);
        var monitor = new SkillCooldownMonitor(
            capture,
            layout,
            candidates,
            references,
            new SkillCooldownDetector(),
            new SkillCooldownTimeEstimator(Stopwatch.Frequency),
            TimeSpan.FromSeconds(1.0 / CaptureFramesPerSecond));
        monitor.PublishSnapshot(startupFrame.QpcTimestamp);
        return monitor;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellationTokenSource.Cancel();
        try
        {
            await _captureTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task CaptureLoopAsync()
    {
        var frameIntervalTicks = (long)Math.Round(_captureInterval.TotalSeconds * Stopwatch.Frequency);
        var nextFrameQpc = Stopwatch.GetTimestamp();
        try
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var frame = await _capture.CaptureAsync(_cancellationTokenSource.Token);
                ProcessFrame(frame);

                nextFrameQpc += frameIntervalTicks;
                var remainingTicks = nextFrameQpc - Stopwatch.GetTimestamp();
                if (remainingTicks > 0)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency),
                        _cancellationTokenSource.Token);
                }
                else
                {
                    nextFrameQpc = Stopwatch.GetTimestamp();
                }
            }
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Cooldown monitoring stopped: {exception.Message}");
        }
    }

    private void ProcessFrame(CapturedFrame frame)
    {
        var detection = _detector.Detect(frame, _layout, _references.Values.ToList());
        foreach (var observation in detection.Observations)
        {
            if (!_activeCandidates.Candidates.TryGetValue(observation.Kind, out var candidate))
            {
                continue;
            }

            var estimate = _estimator.Observe(new SkillCooldownWipeSample(
                observation.Kind,
                observation.SkillId,
                frame.QpcTimestamp,
                observation.State,
                observation.VisibleWipeFraction,
                observation.Confidence));
            _displays[(candidate.Kind, candidate.SkillId)] = ToDisplay(observation, estimate);
        }

        PublishSnapshot(frame.QpcTimestamp);
    }

    private static IReadOnlyList<SkillCooldownReference> IdentifyInitialReferences(
        CapturedFrame frame,
        SkillBarLayout layout,
        IReadOnlyList<SkillCooldownCandidate> candidates)
    {
        var componentsByKind = layout.Components.ToDictionary(component => component.Kind);
        var weaponSet = SelectWeaponSet(frame, componentsByKind, candidates);
        var references = new List<SkillCooldownReference>();
        foreach (var component in layout.Components.OrderBy(component => component.Kind))
        {
            if (IsWeaponSkill(component.Kind) && weaponSet is null)
            {
                continue;
            }

            var candidate = candidates.SingleOrDefault(candidate =>
                candidate.Kind == component.Kind &&
                (!IsWeaponSkill(component.Kind) || candidate.WeaponSet == weaponSet));
            if (candidate?.IconPath is not null)
            {
                // Calibration owns the slot position. Startup matching only selects the weapon set.
                var bounds = component.ToPixelBounds(frame.Width, frame.Height);
                references.Add(SkillCooldownDetector.ResolveReference(new SkillCooldownReference(
                    candidate.Kind,
                    candidate.SkillId,
                    candidate.IconPath,
                    bounds)));
            }
        }

        return references;
    }

    private static int? SelectWeaponSet(
        CapturedFrame frame,
        IReadOnlyDictionary<SkillBarComponentKind, SkillBarComponent> componentsByKind,
        IReadOnlyList<SkillCooldownCandidate> candidates)
    {
        var scores = candidates
            .Where(candidate => IsWeaponSkill(candidate.Kind) && candidate.WeaponSet is not null)
            .GroupBy(candidate => candidate.WeaponSet!.Value)
            .Select(group =>
            {
                var scoredMatches = group
                    .Where(candidate => candidate.IconPath is not null)
                    .Select(candidate => IconTemplateMatcher.MatchAt(
                        frame,
                        componentsByKind[candidate.Kind].ToPixelBounds(frame.Width, frame.Height),
                        candidate.IconPath!,
                        candidate.Name,
                        candidate.SkillId).Score)
                    .ToList();
                return (WeaponSet: group.Key, Score: scoredMatches.Count == 0 ? 0 : scoredMatches.Average());
            })
            .OrderByDescending(result => result.Score)
            .ToList();
        return scores.Count == 0 ? null : scores[0].WeaponSet;
    }

    private static bool IsWeaponSkill(SkillBarComponentKind kind) => kind is
        SkillBarComponentKind.WeaponSkill1 or
        SkillBarComponentKind.WeaponSkill2 or
        SkillBarComponentKind.WeaponSkill3 or
        SkillBarComponentKind.WeaponSkill4 or
        SkillBarComponentKind.WeaponSkill5;

    private static SkillCooldownDisplay ToDisplay(
        SkillCooldownObservation observation,
        SkillCooldownTimeEstimate? estimate)
    {
        if (estimate is { State: SkillCooldownEstimateState.Tracking })
        {
            return new SkillCooldownDisplay(SkillCooldownDisplayState.Cooling, estimate.Remaining);
        }

        if (estimate is { State: SkillCooldownEstimateState.Completed })
        {
            return new SkillCooldownDisplay(SkillCooldownDisplayState.Ready, null);
        }

        if (observation.State == SkillCooldownState.Available && observation.VisibleWipeFraction is null)
        {
            return new SkillCooldownDisplay(SkillCooldownDisplayState.Ready, null);
        }

        return observation.VisibleWipeFraction is not null
            ? new SkillCooldownDisplay(SkillCooldownDisplayState.Measuring, null)
            : new SkillCooldownDisplay(SkillCooldownDisplayState.Unknown, null);
    }

    private void PublishSnapshot(long qpcTimestamp) => SnapshotUpdated?.Invoke(SkillCooldownDiagnostics.CreateSnapshot(
        qpcTimestamp,
        _candidates,
        _activeCandidates.Candidates.ToDictionary(pair => pair.Key, pair => pair.Value.SkillId),
        _displays));
}
