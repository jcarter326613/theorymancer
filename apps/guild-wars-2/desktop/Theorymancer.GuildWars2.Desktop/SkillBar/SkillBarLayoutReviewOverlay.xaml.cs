using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public partial class SkillBarLayoutReviewOverlay : Window
{
    private const uint SwpShowWindow = 0x0040;
    private readonly ScreenBounds _clientBounds;
    private readonly CalibratedRegion _skillBarRegion;
    private readonly IReadOnlyList<CalibratedRegion> _contextRegions;
    private readonly SkillBarLayoutDetection _detection;
    private readonly IReadOnlyList<SkillBarSlotMatch> _matches;

    public SkillBarLayoutReviewOverlay(
        ScreenBounds clientBounds,
        CalibratedRegion skillBarRegion,
        IReadOnlyList<CalibratedRegion> contextRegions,
        SkillBarLayoutDetection detection,
        IReadOnlyList<SkillBarSlotMatch> matches)
    {
        _clientBounds = clientBounds;
        _skillBarRegion = skillBarRegion;
        _contextRegions = contextRegions;
        _detection = detection;
        _matches = matches;
        InitializeComponent();
        Left = clientBounds.X;
        Top = clientBounds.Y;
        Width = clientBounds.Width;
        Height = clientBounds.Height;
        MessageText.Text = detection.Message;
        DiagnosticsText.Text = FormatDiagnostics(detection.DebugInfo);
        AcceptButton.IsEnabled = detection.IsUsable;
        SourceInitialized += (_, _) => PositionOverlay();
        IsVisibleChanged += (_, _) =>
        {
            PositionOverlay();
            if (IsVisible)
            {
                RenderRegions();
            }
        };
    }

    public SkillBarLayout? AcceptedLayout { get; private set; }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        AcceptedLayout = _detection.Layout;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RenderRegions()
    {
        OverlayCanvas.Children.Clear();
        foreach (var region in _contextRegions.Where(region => region.Id != _skillBarRegion.Id))
        {
            try
            {
                AddVisual(region.Crop.ToScreenBounds(_clientBounds), region.Name, Brushes.SlateGray, 24);
            }
            catch (InvalidOperationException)
            {
            }
        }

        var skillBarBounds = _skillBarRegion.Crop.ToScreenBounds(_clientBounds);
        AddVisual(skillBarBounds, "Skill bar", Brushes.DeepSkyBlue, 34);
        if (_detection.Layout is null)
        {
            return;
        }

        var slotBrush = _detection.Confidence >= 0.75 ? Brushes.LimeGreen : Brushes.Goldenrod;
        var matchesByKind = _matches.ToDictionary(match => match.Kind);
        foreach (var component in _detection.Layout.Components)
        {
            var localBounds = component.ToPixelBounds(skillBarBounds.Width, skillBarBounds.Height);
            var match = matchesByKind.GetValueOrDefault(component.Kind);
            var label = match?.Skill is { } skill
                ? $"{skill.Name} {match.Score:P0}"
                : $"{component.Kind.ToString().Replace("WeaponSkill", "Weapon ")}: {match?.Message ?? "Unknown"}";
            AddVisual(
                new ScreenBounds(
                    skillBarBounds.X + localBounds.X,
                    skillBarBounds.Y + localBounds.Y,
                    localBounds.Width,
                    localBounds.Height),
                label,
                match?.Skill is null ? Brushes.Goldenrod : slotBrush,
                42);
        }
    }

    private static string FormatDiagnostics(SkillBarLayoutDebugInfo debugInfo)
    {
        return
            $"Group spacing: {FormatNumber(debugInfo.LabelSpacing)}; visual confidence: {FormatNumber(debugInfo.LabelConfidence)}\n" +
            $"Square: {debugInfo.SquareSize?.ToString() ?? "n/a"}; x offset: {debugInfo.HorizontalOffset?.ToString() ?? "n/a"}; top: {debugInfo.SquareTop?.ToString() ?? "n/a"}; border evidence: {FormatNumber(debugInfo.BorderEvidence)}";
    }

    private static string FormatNumber(double? value) => value is null ? "n/a" : value.Value.ToString("F3");

    private void AddVisual(ScreenBounds bounds, string label, Brush borderBrush, byte fillAlpha)
    {
        var topLeft = OverlayCanvas.PointFromScreen(new Point(bounds.X, bounds.Y));
        var bottomRight = OverlayCanvas.PointFromScreen(new Point(bounds.Right, bounds.Bottom));
        var visual = new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(fillAlpha, 63, 169, 245)),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Margin = new Thickness(6, 3, 6, 3),
                Foreground = Brushes.White,
                Text = label,
            },
        };
        Canvas.SetLeft(visual, topLeft.X);
        Canvas.SetTop(visual, topLeft.Y);
        visual.Width = bottomRight.X - topLeft.X;
        visual.Height = bottomRight.Y - topLeft.Y;
        OverlayCanvas.Children.Add(visual);
    }

    private void PositionOverlay()
    {
        if (!IsVisible)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        _ = SetWindowPos(
            handle,
            new nint(-1),
            _clientBounds.X,
            _clientBounds.Y,
            _clientBounds.Width,
            _clientBounds.Height,
            SwpShowWindow);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint handle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
