using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public partial class CalibrationOverlay : Window
{
    private const uint SwpShowWindow = 0x0040;
    private static readonly Brush RegionBorderBrush = new SolidColorBrush(Color.FromRgb(63, 169, 245));
    private static readonly Brush RegionFillBrush = new SolidColorBrush(Color.FromArgb(34, 63, 169, 245));
    private readonly ScreenBounds _clientBounds;
    private readonly List<RegionDraft> _regions = [];
    private Point? _dragStart;
    private RegionDraft? _movingRegion;
    private ScreenBounds _movingStartBounds;

    public CalibrationOverlay(ScreenBounds clientBounds, IReadOnlyList<CalibratedRegion> regions)
    {
        _clientBounds = clientBounds;
        InitializeComponent();
        Left = clientBounds.X;
        Top = clientBounds.Y;
        Width = clientBounds.Width;
        Height = clientBounds.Height;
        foreach (var region in regions)
        {
            try
            {
                _regions.Add(new RegionDraft(region.Id, region.Name, region.Crop.ToScreenBounds(clientBounds)));
            }
            catch (InvalidOperationException)
            {
            }
        }

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

    public event Action<int>? RegionCountChanged;

    public event Action<IReadOnlyList<CalibratedRegion>>? Confirmed;

    public event Action? Cancelled;

    public int RegionCount => _regions.Count;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = ToScreenPoint(e.GetPosition(OverlayCanvas));
        _movingRegion = FindRegion(_dragStart.Value);
        if (_movingRegion is null)
        {
            _regions.Add(new RegionDraft(
                HasCombatLogRegion() ? $"region-{Guid.NewGuid():N}" : CalibratedRegion.CombatLogId,
                HasCombatLogRegion() ? $"Region {_regions.Count + 1}" : "Combat log",
                new ScreenBounds((int)_dragStart.Value.X, (int)_dragStart.Value.Y, 0, 0)));
        }
        else
        {
            _movingStartBounds = _movingRegion.Bounds;
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
        if (_movingRegion is not null)
        {
            var horizontalDelta = (int)Math.Round(current.X - _dragStart.Value.X);
            var verticalDelta = (int)Math.Round(current.Y - _dragStart.Value.Y);
            _movingRegion.Bounds = MoveWithinClient(_movingStartBounds, horizontalDelta, verticalDelta);
        }
        else
        {
            _regions[^1].Bounds = BoundsBetween(_dragStart.Value, current);
        }

        RenderRegions();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        Mouse.Capture(null);
        if (_movingRegion is null && (_regions[^1].Bounds.Width < 20 || _regions[^1].Bounds.Height < 20))
        {
            _regions.RemoveAt(_regions.Count - 1);
        }

        _dragStart = null;
        _movingRegion = null;
        RenderRegions();
        RegionCountChanged?.Invoke(RegionCount);
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var region = FindRegion(ToScreenPoint(e.GetPosition(OverlayCanvas)));
        if (region is null)
        {
            return;
        }

        _regions.Remove(region);
        RenderRegions();
        RegionCountChanged?.Invoke(RegionCount);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
        }
    }

    public void Confirm()
    {
        if (RegionCount == 0)
        {
            return;
        }

        var regions = _regions
            .Select(region => new CalibratedRegion(
                region.Id,
                region.Name,
                NormalizedCrop.FromScreenBounds(region.Bounds, _clientBounds)))
            .ToList();
        Confirmed?.Invoke(regions);
        Close();
    }

    public void Cancel()
    {
        Cancelled?.Invoke();
        Close();
    }

    private void RenderRegions()
    {
        OverlayCanvas.Children.Clear();
        foreach (var region in _regions)
        {
            var topLeft = OverlayCanvas.PointFromScreen(new Point(region.Bounds.X, region.Bounds.Y));
            var bottomRight = OverlayCanvas.PointFromScreen(new Point(region.Bounds.Right, region.Bounds.Bottom));
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
                    Text = region.Name,
                },
            };
            Canvas.SetLeft(visual, topLeft.X);
            Canvas.SetTop(visual, topLeft.Y);
            visual.Width = bottomRight.X - topLeft.X;
            visual.Height = bottomRight.Y - topLeft.Y;
            OverlayCanvas.Children.Add(visual);
        }
    }

    private Point ToScreenPoint(Point point)
    {
        var screenPoint = OverlayCanvas.PointToScreen(point);
        return new Point(
            Math.Clamp(screenPoint.X, _clientBounds.X, _clientBounds.Right),
            Math.Clamp(screenPoint.Y, _clientBounds.Y, _clientBounds.Bottom));
    }

    private RegionDraft? FindRegion(Point point) => _regions.LastOrDefault(region =>
        point.X >= region.Bounds.X && point.X <= region.Bounds.Right &&
        point.Y >= region.Bounds.Y && point.Y <= region.Bounds.Bottom);

    private bool HasCombatLogRegion() => _regions.Any(region => region.Id == CalibratedRegion.CombatLogId);

    private ScreenBounds BoundsBetween(Point start, Point end)
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

    private sealed class RegionDraft(string id, string name, ScreenBounds bounds)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public ScreenBounds Bounds { get; set; } = bounds;
    }
}
