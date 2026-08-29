using System.Windows;
using System.Windows.Media;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public partial class CalibrationDialog : Window
{
    private readonly SelectedGameWindow _gameWindow;
    private readonly IReadOnlyList<CalibratedRegion> _otherRegions;
    private NormalizedCrop? _combatLogCrop;
    private NormalizedCrop? _skillBarCrop;

    public CalibrationDialog(SelectedGameWindow gameWindow, CollectorSettings settings)
    {
        _gameWindow = gameWindow;
        _otherRegions = settings.Regions
            .Where(region => region.Id != CalibratedRegion.CombatLogId && region.Id != CalibratedRegion.SkillBarId)
            .ToList();
        _combatLogCrop = settings.CombatLogCrop;
        _skillBarCrop = settings.SkillBarCrop;
        InitializeComponent();
        UpdateControls();
    }

    public CollectorSettings Settings => new(
        [
            .. _otherRegions,
            new CalibratedRegion(CalibratedRegion.CombatLogId, "Combat log", _combatLogCrop!),
            new CalibratedRegion(CalibratedRegion.SkillBarId, "Skill bar", _skillBarCrop!),
        ]);

    private void CalibrateCombatLog_Click(object sender, RoutedEventArgs e) =>
        CalibrateRegion(CalibratedRegion.CombatLogId, "Combat log", _combatLogCrop, region => _combatLogCrop = region.Crop);

    private void CalibrateSkillBar_Click(object sender, RoutedEventArgs e) =>
        CalibrateRegion(CalibratedRegion.SkillBarId, "Skill bar", _skillBarCrop, region => _skillBarCrop = region.Crop);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!IsComplete)
        {
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private bool IsComplete => _combatLogCrop is not null && _skillBarCrop is not null;

    private void CalibrateRegion(
        string regionId,
        string regionName,
        NormalizedCrop? existingCrop,
        Action<CalibratedRegion> setRegion)
    {
        if (!_gameWindow.TryGetClientBounds(out var clientBounds))
        {
            MessageBox.Show(this, "Guild Wars 2 is no longer available. Select its window again.", "Theorymancer collector", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var overlay = new CalibrationOverlay(clientBounds, regionId, regionName, existingCrop) { Owner = this };
        overlay.Confirmed += region =>
        {
            setRegion(region);
            UpdateControls();
        };
        _ = overlay.ShowDialog();
    }

    private void UpdateControls()
    {
        UpdateStatus(CombatLogStatusText, _combatLogCrop is not null);
        UpdateStatus(SkillBarStatusText, _skillBarCrop is not null);
        SaveButton.IsEnabled = IsComplete;
    }

    private static void UpdateStatus(System.Windows.Controls.TextBlock textBlock, bool isConfigured)
    {
        textBlock.Text = isConfigured ? "Configured" : "Required";
        textBlock.Foreground = isConfigured ? Brushes.ForestGreen : Brushes.Firebrick;
    }
}
