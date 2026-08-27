namespace Theorymancer.GuildWars2.Desktop.Capture;

public sealed record CapturedFrame(
    long QpcTimestamp,
    int Width,
    int Height,
    int Stride,
    byte[] BgraPixels);

public interface IScreenRegionCapture
{
    ValueTask<CapturedFrame> CaptureAsync(CancellationToken cancellationToken);
}
