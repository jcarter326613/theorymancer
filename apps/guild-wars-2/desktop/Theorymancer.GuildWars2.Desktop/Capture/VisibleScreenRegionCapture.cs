using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Theorymancer.GuildWars2.Desktop.Calibration;

namespace Theorymancer.GuildWars2.Desktop.Capture;

// This source captures only the calibrated screen rectangle. It deliberately
// never reads process memory or loads code into the game client.
public sealed class VisibleScreenRegionCapture : IScreenRegionCapture
{
    private readonly SelectedGameWindow _gameWindow;
    private readonly NormalizedCrop _crop;

    public VisibleScreenRegionCapture(SelectedGameWindow gameWindow, NormalizedCrop crop)
    {
        _gameWindow = gameWindow;
        _crop = crop;
    }

    public ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_gameWindow.TryGetClientBounds(out var clientBounds))
        {
            throw new InvalidOperationException("Guild Wars 2 is no longer available.");
        }

        var captureBounds = _crop.ToScreenBounds(clientBounds);
        using var bitmap = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                captureBounds.X,
                captureBounds.Y,
                0,
                0,
                new Size(captureBounds.Width, captureBounds.Height),
                CopyPixelOperation.SourceCopy);
        }

        var bitmapBounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(bitmapBounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var byteCount = checked(Math.Abs(bitmapData.Stride) * bitmapData.Height);
            var pixels = GC.AllocateUninitializedArray<byte>(byteCount);
            Marshal.Copy(bitmapData.Scan0, pixels, 0, byteCount);
            return ValueTask.FromResult(new CapturedFrame(
                Stopwatch.GetTimestamp(),
                bitmapData.Width,
                bitmapData.Height,
                bitmapData.Stride,
                pixels));
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }
}
