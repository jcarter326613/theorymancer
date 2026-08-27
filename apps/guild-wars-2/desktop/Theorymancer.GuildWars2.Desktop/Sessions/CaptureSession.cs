using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.Ocr;
using System.Diagnostics;

namespace Theorymancer.GuildWars2.Desktop.Sessions;

public sealed class CaptureSession : IAsyncDisposable, IDisposable
{
    private const int TargetFramesPerSecond = 60;
    private static readonly long DiagnosticIntervalTicks = Stopwatch.Frequency / 2;
    private static readonly long OcrIntervalTicks = Stopwatch.Frequency / 4;

    private readonly IScreenRegionCapture _capture;
    private readonly RowChangeDetector _rowChangeDetector;
    private readonly SessionWriter _writer;
    private readonly OcrWorker _ocrWorker;
    private readonly int _rowHeightPixels;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _captureTask;
    private volatile bool _diagnosticsEnabled;
    private long _framesCaptured;
    private long _changedRows;
    private long _lastDiagnosticQpc;
    private long _lastOcrQpc;
    private CapturedFrame? _latestChangedFrame;
    private PreprocessedCombatLogFrame? _latestPreprocessedFrame;
    private FrameMatchResult? _latestFrameMatch;
    private bool _disposed;

    private CaptureSession(
        IScreenRegionCapture capture,
        RowChangeDetector rowChangeDetector,
        SessionWriter writer,
        OcrWorker ocrWorker,
        int rowHeightPixels,
        bool diagnosticsEnabled)
    {
        _capture = capture;
        _rowChangeDetector = rowChangeDetector;
        _writer = writer;
        _ocrWorker = ocrWorker;
        _rowHeightPixels = rowHeightPixels;
        _diagnosticsEnabled = diagnosticsEnabled;
        _captureTask = Task.Run(CaptureLoopAsync);
    }

    public event Action<string>? StatusChanged;

    public event Action<RecognizedCombatLogLine>? LineRecognized;

    public event Action<CaptureDiagnostics>? DiagnosticsUpdated;

    public static async Task<CaptureSession> StartAsync(
        SelectedGameWindow gameWindow,
        CollectorSettings settings,
        bool diagnosticsEnabled)
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
                preprocessed =>
                {
                    if (session is not null && session._diagnosticsEnabled)
                    {
                        session._latestPreprocessedFrame = preprocessed;
                    }
                },
                match =>
                {
                    if (session is not null && session._diagnosticsEnabled)
                    {
                        session._latestFrameMatch = match;
                    }
                },
                message => session?.StatusChanged?.Invoke(message));
            session = new CaptureSession(
                capture,
                detector,
                writer,
                ocrWorker,
                settings.RowHeightPixels,
                diagnosticsEnabled);
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

    public CaptureStatistics Statistics => new(
        Interlocked.Read(ref _framesCaptured),
        Interlocked.Read(ref _changedRows),
        _ocrWorker.RecognizedRows,
        _ocrWorker.EmptyRows,
        _ocrWorker.DroppedRows);

    public void SetDiagnosticsEnabled(bool enabled)
    {
        _diagnosticsEnabled = enabled;
        if (!enabled)
        {
            _latestPreprocessedFrame = null;
            _latestFrameMatch = null;
            DiagnosticsUpdated?.Invoke(new CaptureDiagnostics(
                Statistics,
                0,
                0,
                _rowHeightPixels,
                null,
                null,
                null));
        }
    }

    private async Task CaptureLoopAsync()
    {
        var frameIntervalTicks = Stopwatch.Frequency / TargetFramesPerSecond;
        var nextFrameTick = Stopwatch.GetTimestamp();
        try
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var frame = await _capture.CaptureAsync(_cancellationTokenSource.Token);
                Interlocked.Increment(ref _framesCaptured);
                var changedRows = _rowChangeDetector.FindChangedRows(frame);
                Interlocked.Add(ref _changedRows, changedRows.Count);
                if (changedRows.Count > 0)
                {
                    _latestChangedFrame = frame;
                }

                QueueLatestChangedFrame(frame.QpcTimestamp);
                PublishDiagnostics(frame);

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

    private void PublishDiagnostics(CapturedFrame frame)
    {
        if (!_diagnosticsEnabled || frame.QpcTimestamp - Interlocked.Read(ref _lastDiagnosticQpc) < DiagnosticIntervalTicks)
        {
            return;
        }

        Interlocked.Exchange(ref _lastDiagnosticQpc, frame.QpcTimestamp);
        DiagnosticsUpdated?.Invoke(new CaptureDiagnostics(
            Statistics,
            frame.Width,
            frame.Height,
            _rowHeightPixels,
            CreatePreviewFrame(frame),
            _latestPreprocessedFrame,
            _latestFrameMatch));
    }

    private static CapturedFrame CreatePreviewFrame(CapturedFrame frame)
    {
        return frame with { BgraPixels = frame.BgraPixels.ToArray() };
    }

    private void QueueLatestChangedFrame(long qpcTimestamp)
    {
        if (_latestChangedFrame is not { } frame ||
            qpcTimestamp - Interlocked.Read(ref _lastOcrQpc) < OcrIntervalTicks)
        {
            return;
        }

        if (_ocrWorker.TryQueue(frame))
        {
            Interlocked.Exchange(ref _lastOcrQpc, qpcTimestamp);
            _latestChangedFrame = null;
        }
        else
        {
            // Keep the newest crop for the next attempt without repeatedly counting the same backlog.
            Interlocked.Exchange(ref _lastOcrQpc, qpcTimestamp);
        }
    }
}
