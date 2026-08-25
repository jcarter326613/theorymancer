using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.Ocr;
using System.Diagnostics;

namespace Theorymancer.GuildWars2.Desktop.Sessions;

public sealed class CaptureSession : IAsyncDisposable, IDisposable
{
    private const int TargetFramesPerSecond = 60;

    private readonly IScreenRegionCapture _capture;
    private readonly RowChangeDetector _rowChangeDetector;
    private readonly SessionWriter _writer;
    private readonly OcrWorker _ocrWorker;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _captureTask;
    private bool _disposed;

    private CaptureSession(
        IScreenRegionCapture capture,
        RowChangeDetector rowChangeDetector,
        SessionWriter writer,
        OcrWorker ocrWorker)
    {
        _capture = capture;
        _rowChangeDetector = rowChangeDetector;
        _writer = writer;
        _ocrWorker = ocrWorker;
        _captureTask = Task.Run(CaptureLoopAsync);
    }

    public event Action<string>? StatusChanged;

    public event Action<RecognizedCombatLogLine>? LineRecognized;

    public static async Task<CaptureSession> StartAsync(SelectedGameWindow gameWindow, CollectorSettings settings)
    {
        if (settings.CombatLogCrop is null)
        {
            throw new InvalidOperationException("Calibrate a combat-log crop before recording.");
        }

        var writer = await SessionWriter.CreateAsync();
        try
        {
            var capture = new VisibleScreenRegionCapture(gameWindow, settings.CombatLogCrop);
            var detector = new RowChangeDetector(settings.RowHeightPixels);
            CaptureSession? session = null;
            var ocrWorker = new OcrWorker(
                WindowsCombatLogOcrEngine.CreateEnglish(),
                async line =>
                {
                    await writer.WriteRecognizedLineAsync(line);
                    session?.LineRecognized?.Invoke(line);
                },
                message => session?.StatusChanged?.Invoke(message));
            session = new CaptureSession(capture, detector, writer, ocrWorker);
            await writer.WriteAsync("capture_started", new
            {
                target_frames_per_second = TargetFramesPerSecond,
                row_height_pixels = settings.RowHeightPixels,
            });
            return session;
        }
        catch
        {
            await writer.DisposeAsync();
            throw;
        }
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
            await _ocrWorker.DisposeAsync();
            await _writer.WriteAsync("capture_summary", new
            {
                dropped_ocr_rows = _ocrWorker.DroppedRows,
            });
            await _writer.DisposeAsync();
            _cancellationTokenSource.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task CaptureLoopAsync()
    {
        var frameIntervalTicks = Stopwatch.Frequency / TargetFramesPerSecond;
        var nextFrameTick = Stopwatch.GetTimestamp();
        try
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var frame = await _capture.CaptureAsync(_cancellationTokenSource.Token);
                foreach (var row in _rowChangeDetector.FindChangedRows(frame))
                {
                    _ocrWorker.TryQueue(row);
                }

                nextFrameTick += frameIntervalTicks;
                var remainingTicks = nextFrameTick - Stopwatch.GetTimestamp();
                if (remainingTicks > 0)
                {
                    var delay = TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
                    await Task.Delay(delay, _cancellationTokenSource.Token);
                }
                else
                {
                    nextFrameTick = Stopwatch.GetTimestamp();
                }
            }
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Capture stopped: {exception.Message}");
            await _writer.WriteAsync("capture_error", new { message = exception.Message });
        }
    }
}
