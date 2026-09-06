using System.Diagnostics;
using System.IO;
using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Capture;

public sealed record SkillBarFixtureCaptureResult(
    string SessionDirectory,
    int FramesCaptured,
    bool ReachedMaximumDuration,
    bool WasCancelled,
    string? Error);

public sealed class SkillBarFixtureCaptureSession : IAsyncDisposable, IDisposable
{
    public const int CaptureFramesPerSecond = 4;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(60);

    private readonly IScreenRegionCapture _capture;
    private readonly SkillBarFixtureCaptureWriter _writer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _captureTask;
    private bool _disposed;

    private SkillBarFixtureCaptureSession(
        IScreenRegionCapture capture,
        SkillBarFixtureCaptureWriter writer)
    {
        _capture = capture;
        _writer = writer;
        _captureTask = Task.Run(CaptureLoopAsync);
    }

    public event Action<string>? StatusChanged;

    public event Action<SkillBarFixtureCaptureResult>? Completed;

    public string SessionDirectory => _writer.SessionDirectory;

    public int FramesCaptured => _writer.FramesWritten;

    public static SkillBarFixtureCaptureSession Start(
        SelectedGameWindow gameWindow,
        NormalizedCrop skillBarCrop,
        SkillBarLayout skillBarLayout)
    {
        var capture = new VisibleScreenRegionCapture(gameWindow, skillBarCrop);
        var writer = new SkillBarFixtureCaptureWriter(
            Directory.GetCurrentDirectory(),
            DateTimeOffset.UtcNow,
            skillBarLayout,
            CaptureFramesPerSecond);
        return new SkillBarFixtureCaptureSession(capture, writer);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellationTokenSource.Cancel();
        try
        {
            await _captureTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task CaptureLoopAsync()
    {
        var reachedMaximumDuration = false;
        var wasCancelled = false;
        string? error = null;
        try
        {
            StatusChanged?.Invoke("Cooldown fixture capture starts in 3 seconds. Switch to Guild Wars 2 and use the first skill.");
            await Task.Delay(StartupDelay, _cancellationTokenSource.Token);

            StatusChanged?.Invoke("Recording cooldown fixture frames.");
            var frameIntervalTicks = Stopwatch.Frequency / CaptureFramesPerSecond;
            var startedAtQpc = Stopwatch.GetTimestamp();
            var nextFrameQpc = startedAtQpc;
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                if (Stopwatch.GetTimestamp() - startedAtQpc >= MaximumDuration.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond)
                {
                    reachedMaximumDuration = true;
                    break;
                }

                var frame = await _capture.CaptureAsync(_cancellationTokenSource.Token);
                _writer.WriteFrame(frame);

                nextFrameQpc += frameIntervalTicks;
                var remainingTicks = nextFrameQpc - Stopwatch.GetTimestamp();
                if (remainingTicks > 0)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency),
                        _cancellationTokenSource.Token);
                }
                else
                {
                    nextFrameQpc = Stopwatch.GetTimestamp();
                }
            }

            wasCancelled = _cancellationTokenSource.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
            wasCancelled = true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            StatusChanged?.Invoke($"Cooldown fixture capture stopped: {error}");
        }
        finally
        {
            try
            {
                await _writer.CompleteAsync();
            }
            catch (Exception exception)
            {
                error ??= exception.Message;
            }

            Completed?.Invoke(new SkillBarFixtureCaptureResult(
                SessionDirectory,
                FramesCaptured,
                reachedMaximumDuration,
                wasCancelled,
                error));
        }
    }
}
