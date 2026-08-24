using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public partial class CalibrationOverlay : Window
{
    private readonly ScreenBounds _clientBounds;
    private Point? _dragStart;

    public CalibrationOverlay(ScreenBounds clientBounds)
    {
        _clientBounds = clientBounds;
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    public ScreenBounds? SelectedBounds { get; private set; }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(OverlayCanvas);
        SelectionRectangle.Visibility = Visibility.Visible;
        Mouse.Capture(OverlayCanvas);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DrawSelection(_dragStart.Value, e.GetPosition(OverlayCanvas));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        var end = e.GetPosition(OverlayCanvas);
        Mouse.Capture(null);
        var selection = ToScreenBounds(_dragStart.Value, end);
        _dragStart = null;

        if (selection.Width < 20 || selection.Height < 20)
        {
            MessageBox.Show(this, "The combat-log crop is too small.", "Theorymancer collector", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_clientBounds.Contains(selection))
        {
            MessageBox.Show(this, "Keep the crop entirely inside the Guild Wars 2 game window.", "Theorymancer collector", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedBounds = selection;
        DialogResult = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void DrawSelection(Point start, Point end)
    {
        var x = Math.Min(start.X, end.X);
        var y = Math.Min(start.Y, end.Y);
        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = Math.Abs(end.X - start.X);
        SelectionRectangle.Height = Math.Abs(end.Y - start.Y);
    }

    private ScreenBounds ToScreenBounds(Point start, Point end)
    {
        var topLeft = OverlayCanvas.PointToScreen(new Point(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y)));
        var bottomRight = OverlayCanvas.PointToScreen(new Point(
            Math.Max(start.X, end.X),
            Math.Max(start.Y, end.Y)));
        return new ScreenBounds(
            (int)Math.Floor(topLeft.X),
            (int)Math.Floor(topLeft.Y),
            (int)Math.Ceiling(bottomRight.X - topLeft.X),
            (int)Math.Ceiling(bottomRight.Y - topLeft.Y));
    }
}
