using System.Threading.Channels;
using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Ocr;

public sealed class OcrWorker : IAsyncDisposable
{
    private readonly Channel<ChangedRow> _queue = Channel.CreateBounded<ChangedRow>(
        new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly ICombatLogOcrEngine _engine;
    private readonly Func<RecognizedCombatLogLine, Task> _onRecognized;
    private readonly Action<string> _onStatus;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _workerTask;
    private long _droppedRows;

    public OcrWorker(
        ICombatLogOcrEngine engine,
        Func<RecognizedCombatLogLine, Task> onRecognized,
        Action<string> onStatus)
    {
        _engine = engine;
        _onRecognized = onRecognized;
        _onStatus = onStatus;
        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public bool TryQueue(ChangedRow row)
    {
        if (_queue.Writer.TryWrite(row))
        {
            return true;
        }

        var droppedRows = Interlocked.Increment(ref _droppedRows);
        if (droppedRows == 1 || droppedRows % 100 == 0)
        {
            _onStatus($"OCR queue is full; skipped {droppedRows} changed row(s).");
        }

        return false;
    }

    public long DroppedRows => Interlocked.Read(ref _droppedRows);

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
            await foreach (var row in _queue.Reader.ReadAllAsync(_cancellationTokenSource.Token))
            {
                var line = await _engine.RecognizeAsync(row, _cancellationTokenSource.Token);
                if (line is not null)
                {
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
