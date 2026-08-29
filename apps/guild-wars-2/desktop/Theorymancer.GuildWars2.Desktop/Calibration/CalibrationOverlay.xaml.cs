using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public partial class CalibrationOverlay : Window
{
    private const uint SwpShowWindow = 0x0040;
    private static readonly Brush RegionBorderBrush = new SolidColorBrush(Color.FromRgb(63, 169, 245));
    private static readonly Brush RegionFillBrush = new SolidColorBrush(Color.FromArgb(34, 63, 169, 245));
    private readonly ScreenBounds _clientBounds;
    private readonly string _regionId;
    private readonly string _regionName;
    private Point? _dragStart;
    private ScreenBounds? _regionBounds;
    private ScreenBounds? _draftBounds;
    private bool _movingRegion;
    private ScreenBounds _movingStartBounds;

    public CalibrationOverlay(
        ScreenBounds clientBounds,
        string regionId,
        string regionName,
        NormalizedCrop? existingCrop)
    {
        _clientBounds = clientBounds;
        _regionId = regionId;
        _regionName = regionName;
        InitializeComponent();
        Left = clientBounds.X;
        Top = clientBounds.Y;
        Width = clientBounds.Width;
        Height = clientBounds.Height;
        InstructionsText.Text = $"Drag to set the {regionName.ToLowerInvariant()} region. Drag the existing region to move it. Right-click to clear it.";
        if (existingCrop is not null)
        {
            try
            {
                _regionBounds = existingCrop.ToScreenBounds(clientBounds);
            }
            catch (InvalidOperationException)
            {
            }
        }

        UpdateControls();

        SourceInitialized += (_, _) => PositionOverlay();
        IsVisibleChanged += (_, _) =>
        {
            PositionOverlay();
            if (IsVisible)
            {
                RenderRegion();
            }
        };
    }

    public event Action<CalibratedRegion>? Confirmed;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = ToScreenPoint(e.GetPosition(OverlayCanvas));
        _movingRegion = _regionBounds is { } bounds && Contains(bounds, _dragStart.Value);
        if (_movingRegion)
        {
            _movingStartBounds = _regionBounds!.Value;
        }
        else
        {
            _draftBounds = new ScreenBounds((int)_dragStart.Value.X, (int)_dragStart.Value.Y, 0, 0);
        }

        Mouse.Capture(OverlayCanvas);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = ToScreenPoint(e.GetPosition(OverlayCanvas));
        if (_movingRegion)
        {
            var horizontalDelta = (int)Math.Round(current.X - _dragStart.Value.X);
            var verticalDelta = (int)Math.Round(current.Y - _dragStart.Value.Y);
            _regionBounds = MoveWithinClient(_movingStartBounds, horizontalDelta, verticalDelta);
        }
        else
        {
            _draftBounds = BoundsBetween(_dragStart.Value, current);
        }

        RenderRegion();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        Mouse.Capture(null);
        if (!_movingRegion && _draftBounds is { } draftBounds && IsUsable(draftBounds))
        {
            _regionBounds = draftBounds;
        }

        _draftBounds = null;
        _dragStart = null;
        _movingRegion = false;
        UpdateControls();
        RenderRegion();
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_regionBounds is not { } bounds || !Contains(bounds, ToScreenPoint(e.GetPosition(OverlayCanvas))))
        {
            return;
        }

        _regionBounds = null;
        UpdateControls();
        RenderRegion();
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_regionBounds is not { } regionBounds)
        {
            return;
        }

        Confirmed?.Invoke(new CalibratedRegion(
            _regionId,
            _regionName,
            NormalizedCrop.FromScreenBounds(regionBounds, _clientBounds)));
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateControls() => ConfirmButton.IsEnabled = _regionBounds is not null;

    private void RenderRegion()
    {
        OverlayCanvas.Children.Clear();
        var bounds = _draftBounds ?? _regionBounds;
        if (bounds is null)
        {
            return;
        }

        var topLeft = OverlayCanvas.PointFromScreen(new Point(bounds.Value.X, bounds.Value.Y));
        var bottomRight = OverlayCanvas.PointFromScreen(new Point(bounds.Value.Right, bounds.Value.Bottom));
        var visual = new Border
        {
            BorderBrush = RegionBorderBrush,
            BorderThickness = new Thickness(2),
            Background = RegionFillBrush,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Margin = new Thickness(6, 3, 6, 3),
                Foreground = Brushes.White,
                Text = _regionName,
            },
        };
        Canvas.SetLeft(visual, topLeft.X);
        Canvas.SetTop(visual, topLeft.Y);
        visual.Width = bottomRight.X - topLeft.X;
        visual.Height = bottomRight.Y - topLeft.Y;
        OverlayCanvas.Children.Add(visual);
    }

    private Point ToScreenPoint(Point point)
    {
        var screenPoint = OverlayCanvas.PointToScreen(point);
        return new Point(
            Math.Clamp(screenPoint.X, _clientBounds.X, _clientBounds.Right),
            Math.Clamp(screenPoint.Y, _clientBounds.Y, _clientBounds.Bottom));
    }

    private static bool Contains(ScreenBounds bounds, Point point) =>
        point.X >= bounds.X && point.X <= bounds.Right &&
        point.Y >= bounds.Y && point.Y <= bounds.Bottom;

    private static bool IsUsable(ScreenBounds bounds) => bounds.Width >= 20 && bounds.Height >= 20;

    private static ScreenBounds BoundsBetween(Point start, Point end)
    {
        var left = (int)Math.Min(start.X, end.X);
        var top = (int)Math.Min(start.Y, end.Y);
        var right = (int)Math.Max(start.X, end.X);
        var bottom = (int)Math.Max(start.Y, end.Y);
        return new ScreenBounds(left, top, right - left, bottom - top);
    }

    private ScreenBounds MoveWithinClient(ScreenBounds bounds, int horizontalDelta, int verticalDelta) => new(
        Math.Clamp(bounds.X + horizontalDelta, _clientBounds.X, _clientBounds.Right - bounds.Width),
        Math.Clamp(bounds.Y + verticalDelta, _clientBounds.Y, _clientBounds.Bottom - bounds.Height),
        bounds.Width,
        bounds.Height);

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
