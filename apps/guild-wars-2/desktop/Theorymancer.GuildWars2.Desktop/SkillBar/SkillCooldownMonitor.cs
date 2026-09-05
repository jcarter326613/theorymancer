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

public sealed record SkillCooldownDiagnosticsSnapshot(IReadOnlyList<SkillCooldownDiagnosticsRow> Rows);

public static class SkillCooldownDiagnostics
{
    public static SkillCooldownDiagnosticsSnapshot CreateSnapshot(
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

        return new SkillCooldownDiagnosticsSnapshot(rows);
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
    private const int CaptureFramesPerSecond = 4;
    private const double IdentificationMinimumScore = 0.60;
    private const double IdentificationMinimumMargin = 0.035;
    private readonly IScreenRegionCapture _capture;
    private readonly SkillBarLayout _layout;
    private readonly IReadOnlyList<SkillCooldownCandidate> _candidates;
    private readonly IReadOnlyDictionary<SkillBarComponentKind, IReadOnlyList<SkillCooldownCandidate>> _candidatesByKind;
    private readonly SkillCooldownDetector _detector = new();
    private readonly SkillCooldownTimeEstimator _estimator = new(Stopwatch.Frequency);
    private readonly SkillCooldownIdentityLock _activeCandidates = new();
    private readonly Dictionary<(SkillBarComponentKind Kind, int SkillId), SkillCooldownDisplay> _displays = [];
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _captureTask;
    private bool _disposed;

    private SkillCooldownMonitor(
        IScreenRegionCapture capture,
        SkillBarLayout layout,
        IReadOnlyList<SkillCooldownCandidate> candidates)
    {
        _capture = capture;
        _layout = layout;
        _candidates = candidates;
        _candidatesByKind = candidates
            .GroupBy(candidate => candidate.Kind)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<SkillCooldownCandidate>)group.ToList());
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

        var monitor = new SkillCooldownMonitor(
            new VisibleScreenRegionCapture(gameWindow, skillBarCrop),
            layout,
            candidates);
        monitor.PublishSnapshot();
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
        var frameIntervalTicks = Stopwatch.Frequency / CaptureFramesPerSecond;
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
        foreach (var component in _layout.Components)
        {
            if (!_activeCandidates.IsLocked(component.Kind) &&
                TryIdentifyActiveCandidate(frame, component, out var candidate))
            {
                _activeCandidates.TryLock(candidate);
            }
        }

        var references = _activeCandidates.Candidates.Values
            .Select(candidate => new SkillCooldownReference(candidate.Kind, candidate.SkillId, candidate.IconPath!))
            .ToList();
        var detection = _detector.Detect(frame, _layout, references);
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

        PublishSnapshot();
    }

    private bool TryIdentifyActiveCandidate(
        CapturedFrame frame,
        SkillBarComponent component,
        out SkillCooldownCandidate candidate)
    {
        candidate = null!;
        if (!_candidatesByKind.TryGetValue(component.Kind, out var candidates))
        {
            return false;
        }

        var bounds = component.ToPixelBounds(frame.Width, frame.Height);
        var ranked = candidates
            .Where(value => value.IconPath is not null && _activeCandidates.CanIdentify(value))
            .Select(value => (Candidate: value, Score: IconTemplateMatcher.MatchAt(
                frame,
                bounds,
                value.IconPath!,
                value.Name,
                value.SkillId).Score))
            .OrderByDescending(match => match.Score)
            .ToList();
        if (ranked.Count == 0 || ranked[0].Score < IdentificationMinimumScore)
        {
            return false;
        }

        if (ranked.Count > 1 && ranked[0].Score - ranked[1].Score < IdentificationMinimumMargin)
        {
            return false;
        }

        candidate = ranked[0].Candidate;
        return true;
    }

    private static SkillCooldownDisplay ToDisplay(
        SkillCooldownObservation observation,
        SkillCooldownTimeEstimate? estimate)
    {
        if (estimate is { State: SkillCooldownEstimateState.Tracking })
        {
            return new SkillCooldownDisplay(SkillCooldownDisplayState.Cooling, estimate.Remaining);
        }

        if (observation.State == SkillCooldownState.Available && observation.VisibleWipeFraction is null)
        {
            return new SkillCooldownDisplay(SkillCooldownDisplayState.Ready, null);
        }

        return observation.VisibleWipeFraction is not null
            ? new SkillCooldownDisplay(SkillCooldownDisplayState.Measuring, null)
            : new SkillCooldownDisplay(SkillCooldownDisplayState.Unknown, null);
    }

    private void PublishSnapshot() => SnapshotUpdated?.Invoke(SkillCooldownDiagnostics.CreateSnapshot(
        _candidates,
        _activeCandidates.Candidates.ToDictionary(pair => pair.Key, pair => pair.Value.SkillId),
        _displays));
}
