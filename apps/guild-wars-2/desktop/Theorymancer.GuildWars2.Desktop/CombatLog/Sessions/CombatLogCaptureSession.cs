using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.ArenaNet;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;
using System.Diagnostics;
using System.IO;

namespace Theorymancer.GuildWars2.Desktop.CombatLog.Sessions;

public sealed class CombatLogCaptureSession : IAsyncDisposable, IDisposable
{
    private const int CaptureFramesPerSecond = 4;
    private static readonly long DiagnosticIntervalTicks = Stopwatch.Frequency / 2;

    private readonly IScreenRegionCapture _capture;
    private readonly CombatLogSessionWriter _writer;
    private readonly OcrWorker _ocrWorker;
    private readonly CombatLogOcrFrameDebugWriter _debugFrameWriter;
    private readonly CombatLogActivityLogDebugWriter _debugActivityWriter;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _captureTask;
    private volatile bool _diagnosticsEnabled;
    private long _framesCaptured;
    private long _ocrFramesQueued;
    private long _lastDiagnosticQpc;
    private PreprocessedCombatLogFrame? _latestPreprocessedFrame;
    private FrameMatchResult? _latestFrameMatch;
    private bool _disposed;

    private CombatLogCaptureSession(
        IScreenRegionCapture capture,
        CombatLogSessionWriter writer,
        OcrWorker ocrWorker,
        CombatLogOcrFrameDebugWriter debugFrameWriter,
        CombatLogActivityLogDebugWriter debugActivityWriter,
        bool diagnosticsEnabled)
    {
        _capture = capture;
        _writer = writer;
        _ocrWorker = ocrWorker;
        _debugFrameWriter = debugFrameWriter;
        _debugActivityWriter = debugActivityWriter;
        _diagnosticsEnabled = diagnosticsEnabled;
        _captureTask = Task.Run(CaptureLoopAsync);
    }

    public event Action<string>? StatusChanged;

    public event Action<RecognizedCombatLogLine>? LineRecognized;

    public event Action<CombatLogCaptureDiagnostics>? DiagnosticsUpdated;

    public static async Task<CombatLogCaptureSession> StartAsync(
        SelectedGameWindow gameWindow,
        CollectorSettings settings,
        bool diagnosticsEnabled,
        BuildSkillCandidates? buildCandidates = null)
    {
        if (settings.CombatLogCrop is null)
        {
            throw new InvalidOperationException("Calibrate a combat-log crop before recording.");
        }

        var writer = await CombatLogSessionWriter.CreateAsync();
        try
        {
            var capture = new VisibleScreenRegionCapture(gameWindow, settings.CombatLogCrop);
            var debugFrameWriter = new CombatLogOcrFrameDebugWriter(Directory.GetCurrentDirectory(), DateTimeOffset.Now);
            var debugActivityWriter = new CombatLogActivityLogDebugWriter(debugFrameWriter.SessionDirectory);
            if (diagnosticsEnabled)
            {
                debugFrameWriter.EnsureSessionDirectory();
            }

            CombatLogCaptureSession? session = null;
            var ocrWorker = new OcrWorker(
                WindowsCombatLogOcrEngine.CreateEnglish(),
                async line =>
                {
                    await writer.WriteCombatLogLineAsync(line);
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
                message => session?.StatusChanged?.Invoke(message),
                lines => session?.WriteDebugOcrFrameAsync(lines) ?? Task.FromResult<long?>(null),
                (rawFrameSequence, lines, match) => session?.WriteDebugOcrFrameMatch(rawFrameSequence, lines, match));
            session = new CombatLogCaptureSession(
                capture,
                writer,
                ocrWorker,
                debugFrameWriter,
                debugActivityWriter,
                diagnosticsEnabled);
            await writer.WriteAsync("capture_started", new
            {
                capture_frames_per_second = CaptureFramesPerSecond,
                ocr_frames_per_second = CaptureFramesPerSecond,
                build = buildCandidates is null ? null : new
                {
                    character_name = buildCandidates.CharacterName,
                    build_name = buildCandidates.BuildName,
                    profession = buildCandidates.Profession,
                    skill_ids_by_slot = buildCandidates.SkillIdsBySlot,
                },
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
            _debugFrameWriter.Dispose();
            _cancellationTokenSource.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public CombatLogCaptureStatistics Statistics => new(
        Interlocked.Read(ref _framesCaptured),
        Interlocked.Read(ref _ocrFramesQueued),
        _ocrWorker.RecognizedRows,
        _ocrWorker.EmptyRows,
        _ocrWorker.DroppedRows);

    public CombatLogActivityLogDebugWriter DebugActivityWriter => _debugActivityWriter;

    public void SetDiagnosticsEnabled(bool enabled)
    {
        _diagnosticsEnabled = enabled;
        if (!enabled)
        {
            _latestPreprocessedFrame = null;
            _latestFrameMatch = null;
            DiagnosticsUpdated?.Invoke(new CombatLogCaptureDiagnostics(
                Statistics,
                0,
                0,
                null,
                null,
                null));
        }
    }

    private async Task CaptureLoopAsync()
    {
        var frameIntervalTicks = Stopwatch.Frequency / CaptureFramesPerSecond;
        var nextFrameTick = Stopwatch.GetTimestamp();
        try
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var frame = await _capture.CaptureAsync(_cancellationTokenSource.Token);
                Interlocked.Increment(ref _framesCaptured);
                QueueFrameForOcr(frame);
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
        DiagnosticsUpdated?.Invoke(new CombatLogCaptureDiagnostics(
            Statistics,
            frame.Width,
            frame.Height,
            CreatePreviewFrame(frame),
            _latestPreprocessedFrame,
            _latestFrameMatch));
    }

    private static CapturedFrame CreatePreviewFrame(CapturedFrame frame)
    {
        return frame with { BgraPixels = frame.BgraPixels.ToArray() };
    }

    private void QueueFrameForOcr(CapturedFrame frame)
    {
        if (_ocrWorker.TryQueue(frame))
        {
            Interlocked.Increment(ref _ocrFramesQueued);
        }
    }

    private async Task<long?> WriteDebugOcrFrameAsync(IReadOnlyList<RecognizedCombatLogLine> lines) =>
        _diagnosticsEnabled ? await _debugFrameWriter.WriteFrameAsync(lines) : null;

    private void WriteDebugOcrFrameMatch(
        long? rawFrameSequence,
        IReadOnlyList<RecognizedCombatLogLine> lines,
        FrameMatchResult? match)
    {
        if (_diagnosticsEnabled)
        {
            _debugActivityWriter.WriteFrameMatch(rawFrameSequence, lines, match);
        }
    }
}
