namespace Theorymancer.GuildWars2.Desktop.Capture;

public sealed record CapturedFrame(
    long QpcTimestamp,
    int Width,
    int Height,
    int Stride,
    byte[] BgraPixels);

public sealed record ChangedRow(
    long FirstSeenQpc,
    int RowIndex,
    ulong PixelHash,
    int Width,
    int Height,
    byte[] BgraPixels);

public interface IScreenRegionCapture
{
    ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken);
}
