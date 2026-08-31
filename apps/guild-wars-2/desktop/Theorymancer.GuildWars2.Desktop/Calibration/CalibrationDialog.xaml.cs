using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Theorymancer.GuildWars2.Desktop.ArenaNet;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public partial class CalibrationDialog : Window
{
    private readonly SelectedGameWindow _gameWindow;
    private readonly IReadOnlyList<CalibratedRegion> _otherRegions;
    private readonly ReferenceIcons _referenceIcons;
    private readonly BuildSkillCandidates _buildCandidates;
    private NormalizedCrop? _combatLogCrop;
    private NormalizedCrop? _skillBarCrop;
    private SkillBarLayout? _skillBarLayout;
    private CalibrationPreviewOverlay? _previewOverlay;

    public CalibrationDialog(
        SelectedGameWindow gameWindow,
        CollectorSettings settings,
        ReferenceIcons referenceIcons,
        BuildSkillCandidates buildCandidates)
    {
        _gameWindow = gameWindow;
        _referenceIcons = referenceIcons;
        _buildCandidates = buildCandidates;
        _otherRegions = settings.Regions
            .Where(region => region.Id != CalibratedRegion.CombatLogId && region.Id != CalibratedRegion.SkillBarId)
            .ToList();
        _combatLogCrop = settings.CombatLogCrop;
        _skillBarCrop = settings.SkillBarCrop;
        _skillBarLayout = settings.SkillBarLayout;
        InitializeComponent();
        Loaded += (_, _) => RefreshPreviewOverlay();
        Closed += (_, _) => ClosePreviewOverlay();
        UpdateControls();
    }

    public CollectorSettings Settings => new(
        [
            .. _otherRegions,
            new CalibratedRegion(CalibratedRegion.CombatLogId, "Combat log", _combatLogCrop!),
            new CalibratedRegion(CalibratedRegion.SkillBarId, "Skill bar", _skillBarCrop!),
        ],
        _skillBarLayout);

    private void CalibrateCombatLog_Click(object sender, RoutedEventArgs e)
    {
        var region = PromptForRegion(CalibratedRegion.CombatLogId, "Combat log", _combatLogCrop);
        if (region is null)
        {
            return;
        }

        _combatLogCrop = region.Crop;
        UpdateControls();
        RefreshPreviewOverlay();
    }

    private async void CalibrateSkillBar_Click(object sender, RoutedEventArgs e)
    {
        var region = PromptForRegion(CalibratedRegion.SkillBarId, "Skill bar", _skillBarCrop);
        if (region is null)
        {
            return;
        }

        try
        {
            HidePreviewOverlay();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var capture = new VisibleScreenRegionCapture(_gameWindow, region.Crop);
            var frame = await capture.CaptureAsync(CancellationToken.None);
            var detection = SkillBarLayoutDetector.Detect(frame, []);
            var matches = detection.Layout is { } detectedLayout
                ? await new SkillBarIconMatcher(_referenceIcons).MatchAsync(frame, detectedLayout, _buildCandidates, CancellationToken.None)
                : [];
            if (!_gameWindow.TryGetClientBounds(out var clientBounds))
            {
                throw new InvalidOperationException("Guild Wars 2 is no longer available. Select its window again.");
            }

            var review = new SkillBarLayoutReviewOverlay(
                clientBounds,
                region,
                BuildContextRegions(region.Crop),
                detection,
                matches)
            {
                Owner = this,
            };
            if (review.ShowDialog() == true && review.AcceptedLayout is { } layout)
            {
                _skillBarCrop = region.Crop;
                _skillBarLayout = layout;
                UpdateControls();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Skill-bar analysis failed: {exception.Message}", "Theorymancer collector", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            RefreshPreviewOverlay();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!IsComplete)
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private bool IsComplete =>
        _combatLogCrop is not null &&
        _skillBarCrop is not null &&
        _skillBarLayout is { HasSkillSlots: true };

    private CalibratedRegion? PromptForRegion(
        string regionId,
        string regionName,
        NormalizedCrop? existingCrop)
    {
        if (!_gameWindow.TryGetClientBounds(out var clientBounds))
        {
            MessageBox.Show(this, "Guild Wars 2 is no longer available. Select its window again.", "Theorymancer collector", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        CalibratedRegion? selectedRegion = null;
        var overlay = new CalibrationOverlay(clientBounds, regionId, regionName, existingCrop, BuildContextRegions()) { Owner = this };
        overlay.Confirmed += region => selectedRegion = region;
        HidePreviewOverlay();
        _ = overlay.ShowDialog();
        RefreshPreviewOverlay();
        return selectedRegion;
    }

    private IReadOnlyList<CalibratedRegion> BuildContextRegions(NormalizedCrop? skillBarCrop = null)
    {
        var regions = _otherRegions.ToList();
        if (_combatLogCrop is not null)
        {
            regions.Add(new CalibratedRegion(CalibratedRegion.CombatLogId, "Combat log", _combatLogCrop));
        }

        if ((skillBarCrop ?? _skillBarCrop) is { } crop)
        {
            regions.Add(new CalibratedRegion(CalibratedRegion.SkillBarId, "Skill bar", crop));
        }

        return regions;
    }

    private void RefreshPreviewOverlay()
    {
        ClosePreviewOverlay();
        if (!_gameWindow.TryGetClientBounds(out var clientBounds))
        {
            return;
        }

        var regions = BuildContextRegions();
        if (regions.Count == 0)
        {
            return;
        }

        _previewOverlay = new CalibrationPreviewOverlay(clientBounds, regions, _skillBarLayout) { Owner = this };
        _previewOverlay.Show();
    }

    private void HidePreviewOverlay() => _previewOverlay?.Hide();

    private void ClosePreviewOverlay()
    {
        _previewOverlay?.Close();
        _previewOverlay = null;
    }

    private void UpdateControls()
    {
        UpdateStatus(CombatLogStatusText, _combatLogCrop is not null);
        UpdateStatus(SkillBarStatusText, _skillBarCrop is not null && _skillBarLayout is { HasSkillSlots: true });
        SaveButton.IsEnabled = IsComplete;
    }

    private static void UpdateStatus(System.Windows.Controls.TextBlock textBlock, bool isConfigured)
    {
        textBlock.Text = isConfigured ? "Configured" : "Required";
        textBlock.Foreground = isConfigured ? Brushes.ForestGreen : Brushes.Firebrick;
    }
}
