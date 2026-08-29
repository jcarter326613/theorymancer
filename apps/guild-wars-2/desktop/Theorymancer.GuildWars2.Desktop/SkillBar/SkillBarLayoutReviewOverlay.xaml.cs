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
    private readonly IconTemplateMatch? _nightfallMatch;

    public SkillBarLayoutReviewOverlay(
        ScreenBounds clientBounds,
        CalibratedRegion skillBarRegion,
        IReadOnlyList<CalibratedRegion> contextRegions,
        SkillBarLayoutDetection detection,
        IconTemplateMatch? nightfallMatch)
    {
        _clientBounds = clientBounds;
        _skillBarRegion = skillBarRegion;
        _contextRegions = contextRegions;
        _detection = detection;
        _nightfallMatch = nightfallMatch;
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

    private void ShowOcrEvidence_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            RenderRegions();
        }
    }

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
        if (ShowOcrEvidenceCheckBox.IsChecked == true)
        {
            RenderOcrEvidence(skillBarBounds);
        }

        if (_nightfallMatch is { } nightfallMatch)
        {
            AddVisual(
                new ScreenBounds(
                    skillBarBounds.X + nightfallMatch.Bounds.X,
                    skillBarBounds.Y + nightfallMatch.Bounds.Y,
                    nightfallMatch.Bounds.Width,
                    nightfallMatch.Bounds.Height),
                $"Nightfall {nightfallMatch.Score:P0}",
                Brushes.OrangeRed,
                48);
        }

        if (_detection.Layout is null)
        {
            return;
        }

        var slotBrush = _detection.Confidence >= 0.75 ? Brushes.LimeGreen : Brushes.Goldenrod;
        foreach (var component in _detection.Layout.Components)
        {
            var localBounds = component.ToPixelBounds(skillBarBounds.Width, skillBarBounds.Height);
            AddVisual(
                new ScreenBounds(
                    skillBarBounds.X + localBounds.X,
                    skillBarBounds.Y + localBounds.Y,
                    localBounds.Width,
                    localBounds.Height),
                component.Kind.ToString().Replace("WeaponSkill", "Weapon "),
                slotBrush,
                42);
        }
    }

    private void RenderOcrEvidence(ScreenBounds skillBarBounds)
    {
        foreach (var word in _detection.DebugInfo.RecognizedWords)
        {
            AddVisual(ToScreenBounds(skillBarBounds, word), word.Text, Brushes.Cyan, 24);
        }

        foreach (var word in _detection.DebugInfo.SelectedLabels)
        {
            AddVisual(ToScreenBounds(skillBarBounds, word), $"Selected: {word.Text}", Brushes.MediumPurple, 48);
        }
    }

    private static ScreenBounds ToScreenBounds(ScreenBounds skillBarBounds, HudOcrWord word) => new(
        skillBarBounds.X + (int)Math.Floor(word.X),
        skillBarBounds.Y + (int)Math.Floor(word.Y),
        Math.Max(1, (int)Math.Ceiling(word.Width)),
        Math.Max(1, (int)Math.Ceiling(word.Height)));

    private static string FormatDiagnostics(SkillBarLayoutDebugInfo debugInfo)
    {
        var selectedLabels = debugInfo.SelectedLabels.Count == 0
            ? "none"
            : string.Join(", ", debugInfo.SelectedLabels.Select(word => $"{word.Text}@{word.X:F0},{word.Y:F0}"));
        var words = debugInfo.RecognizedWords.Count == 0
            ? "none"
            : string.Join(", ", debugInfo.RecognizedWords.Take(12).Select(word => $"{word.Text}@{word.X:F0},{word.Y:F0}"));
        var remainingWordCount = debugInfo.RecognizedWords.Count - Math.Min(12, debugInfo.RecognizedWords.Count);
        return
            $"OCR words ({debugInfo.RecognizedWords.Count}): {words}{(remainingWordCount > 0 ? $", +{remainingWordCount} more" : string.Empty)}\n" +
            $"Selected labels: {selectedLabels}\n" +
            $"Spacing: {FormatNumber(debugInfo.LabelSpacing)}; label confidence: {FormatNumber(debugInfo.LabelConfidence)}\n" +
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
