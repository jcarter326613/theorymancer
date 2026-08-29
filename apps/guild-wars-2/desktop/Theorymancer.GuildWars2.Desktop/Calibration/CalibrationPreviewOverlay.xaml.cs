using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public partial class CalibrationPreviewOverlay : Window
{
    private const int GwlExStyle = -20;
    private const nint WsExTransparent = 0x20;
    private const nint WsExNoActivate = 0x08000000;
    private const uint SwpShowWindow = 0x0040;
    private readonly ScreenBounds _clientBounds;
    private readonly IReadOnlyList<CalibratedRegion> _regions;
    private readonly SkillBarLayout? _skillBarLayout;

    public CalibrationPreviewOverlay(
        ScreenBounds clientBounds,
        IReadOnlyList<CalibratedRegion> regions,
        SkillBarLayout? skillBarLayout)
    {
        _clientBounds = clientBounds;
        _regions = regions;
        _skillBarLayout = skillBarLayout;
        InitializeComponent();
        Left = clientBounds.X;
        Top = clientBounds.Y;
        Width = clientBounds.Width;
        Height = clientBounds.Height;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExStyle);
            _ = SetWindowLongPtr(handle, GwlExStyle, style | WsExTransparent | WsExNoActivate);
            PositionOverlay();
        };
        IsVisibleChanged += (_, _) =>
        {
            PositionOverlay();
            if (IsVisible)
            {
                RenderRegions();
            }
        };
    }

    private void RenderRegions()
    {
        OverlayCanvas.Children.Clear();
        foreach (var region in _regions)
        {
            try
            {
                AddVisual(region.Crop.ToScreenBounds(_clientBounds), region.Name, Brushes.DeepSkyBlue, 24);
            }
            catch (InvalidOperationException)
            {
            }
        }

        var skillBarRegion = _regions.FirstOrDefault(region => region.Id == CalibratedRegion.SkillBarId);
        if (skillBarRegion is null || _skillBarLayout is null)
        {
            return;
        }

        var skillBarBounds = skillBarRegion.Crop.ToScreenBounds(_clientBounds);
        foreach (var component in _skillBarLayout.Components)
        {
            var localBounds = component.ToPixelBounds(skillBarBounds.Width, skillBarBounds.Height);
            AddVisual(
                new ScreenBounds(
                    skillBarBounds.X + localBounds.X,
                    skillBarBounds.Y + localBounds.Y,
                    localBounds.Width,
                    localBounds.Height),
                component.Kind.ToString().Replace("WeaponSkill", "Weapon "),
                Brushes.LimeGreen,
                32);
        }
    }

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
    private static extern nint GetWindowLongPtr(nint handle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint handle, int index, nint value);

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
