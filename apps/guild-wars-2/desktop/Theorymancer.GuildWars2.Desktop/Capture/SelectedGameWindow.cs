using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Theorymancer.GuildWars2.Desktop.Capture;

public sealed record SelectedGameWindow(nint Handle, int ProcessId, string Title)
{
    public string DisplayName => $"{Title} (PID {ProcessId})";

    public bool TryGetClientBounds(out ScreenBounds bounds)
    {
        bounds = default;
        if (!IsWindow(Handle) || !GetClientRect(Handle, out var rect))
        {
            return false;
        }

        var topLeft = new Point { X = rect.Left, Y = rect.Top };
        var bottomRight = new Point { X = rect.Right, Y = rect.Bottom };
        if (!ClientToScreen(Handle, ref topLeft) || !ClientToScreen(Handle, ref bottomRight))
        {
            return false;
        }

        bounds = new ScreenBounds(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
        return bounds.IsUsable;
    }

    public static IReadOnlyList<SelectedGameWindow> FindCandidates()
    {
        var candidates = new List<SelectedGameWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0)
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || !TryGetProcessName(processId, out var processName))
            {
                return true;
            }

            var title = GetTitle(handle);
            if (processName.Contains("gw2", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("guild wars 2", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(new SelectedGameWindow(handle, unchecked((int)processId), title));
            }

            return true;
        }, nint.Zero);

        return candidates.OrderBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryGetProcessName(uint processId, out string processName)
    {
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            processName = process.ProcessName;
            return true;
        }
        catch (ArgumentException)
        {
            processName = string.Empty;
            return false;
        }
        catch (InvalidOperationException)
        {
            processName = string.Empty;
            return false;
        }
    }

    private static string GetTitle(nint handle)
    {
        var title = new StringBuilder(GetWindowTextLength(handle) + 1);
        _ = GetWindowText(handle, title, title.Capacity);
        return title.ToString();
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint handle, ref Point point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
