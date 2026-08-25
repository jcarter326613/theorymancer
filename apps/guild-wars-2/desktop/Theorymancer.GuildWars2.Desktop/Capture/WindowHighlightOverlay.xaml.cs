using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Theorymancer.GuildWars2.Desktop.Capture;

public partial class WindowHighlightOverlay : Window
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExTransparent = 0x00000020L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private ScreenBounds? _highlightBounds;

    public WindowHighlightOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            EnableClickThrough();
            PositionHighlight();
        };
        IsVisibleChanged += (_, _) => PositionHighlight();
    }

    public bool TryHighlight(SelectedGameWindow window)
    {
        if (!window.TryGetClientBounds(out var bounds))
        {
            HideHighlight();
            return false;
        }

        _highlightBounds = bounds;
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;
        HighlightLabel.Text = $"Selected: {window.DisplayName}";
        HighlightBorder.Visibility = Visibility.Visible;
        PositionHighlight();
        return true;
    }

    public void HideHighlight()
    {
        _highlightBounds = null;
        HighlightBorder.Visibility = Visibility.Collapsed;
    }

    private void EnableClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyles = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(
            handle,
            GwlExStyle,
            new nint(extendedStyles | WsExNoActivate | WsExTransparent));
    }

    private void PositionHighlight()
    {
        if (_highlightBounds is not { } bounds || !IsVisible)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        _ = SetWindowPos(
            handle,
            new nint(-1),
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SwpNoActivate | SwpShowWindow);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
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
