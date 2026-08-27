using System.Threading.Channels;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Ocr;

public sealed class OcrWorker : IAsyncDisposable
{
    private readonly Channel<CapturedFrame> _queue = Channel.CreateBounded<CapturedFrame>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly ICombatLogOcrEngine _engine;
    private readonly Func<RecognizedCombatLogLine, Task> _onRecognized;
    private readonly Action<PreprocessedCombatLogFrame> _onPreprocessed;
    private readonly Action<FrameMatchResult> _onFrameMatched;
    private readonly Action<string> _onStatus;
    private readonly Func<IReadOnlyList<RecognizedCombatLogLine>, Task> _onOcrCompleted;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _workerTask;
    private readonly CombatLogFrameMatcher _frameMatcher = new();
    private long _droppedRows;
    private long _recognizedRows;
    private long _emptyRows;

    public OcrWorker(
        ICombatLogOcrEngine engine,
        Func<RecognizedCombatLogLine, Task> onRecognized,
        Action<PreprocessedCombatLogFrame> onPreprocessed,
        Action<FrameMatchResult> onFrameMatched,
        Action<string> onStatus,
        Func<IReadOnlyList<RecognizedCombatLogLine>, Task>? onOcrCompleted = null)
    {
        _engine = engine;
        _onRecognized = onRecognized;
        _onPreprocessed = onPreprocessed;
        _onFrameMatched = onFrameMatched;
        _onStatus = onStatus;
        _onOcrCompleted = onOcrCompleted ?? (_ => Task.CompletedTask);
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public bool TryQueue(CapturedFrame frame)
    {
        if (_queue.Writer.TryWrite(frame))
        {
            return true;
        }

        var droppedRows = Interlocked.Increment(ref _droppedRows);
        if (droppedRows == 1 || droppedRows % 100 == 0)
        {
            _onStatus($"OCR queue is full; skipped {droppedRows} changed crop(s).");
        }

        return false;
    }

    public long DroppedRows => Interlocked.Read(ref _droppedRows);

    public long RecognizedRows => Interlocked.Read(ref _recognizedRows);

    public long EmptyRows => Interlocked.Read(ref _emptyRows);

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        _cancellationTokenSource.Cancel();
        try
        {
            await _workerTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var frame in _queue.Reader.ReadAllAsync(_cancellationTokenSource.Token))
            {
                var preprocessed = CombatLogImagePreprocessor.Process(frame);
                _onPreprocessed(preprocessed);
                var lines = await _engine.RecognizeAsync(
                    frame,
                    preprocessed.Frame,
                    _cancellationTokenSource.Token);
                await _onOcrCompleted(lines);
                if (lines.Count == 0)
                {
                    Interlocked.Increment(ref _emptyRows);
                    continue;
                }

                var match = _frameMatcher.Match(lines);
                _onFrameMatched(match);
                foreach (var line in match.LinesToEmit)
                {
                    Interlocked.Increment(ref _recognizedRows);
                    await _onRecognized(line);
                }
            }
        }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _onStatus($"OCR stopped: {exception.Message}");
        }
    }
}
