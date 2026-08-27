using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Sessions;

public sealed class ActivityLogDebugWriter : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly object _sync = new();
    private Channel<string>? _queue;
    private Task? _writerTask;
    private long _sequence;
    private bool _disposed;

    public ActivityLogDebugWriter(string sessionDirectory)
    {
        _path = Path.Combine(sessionDirectory, "activity_log.jsonl");
    }

    public void WriteActivity(
        DateTimeOffset displayedAt,
        string displayedText,
        string source,
        object? details = null) =>
        Enqueue("activity_displayed", new
        {
            displayed_at = displayedAt,
            displayed_text = displayedText,
            source,
            details,
        });

    public void WriteFrameMatch(
        long? rawFrameSequence,
        IReadOnlyList<RecognizedCombatLogLine> recognizedLines,
        FrameMatchResult? match)
    {
        var sourceLine = recognizedLines.FirstOrDefault();
        Enqueue("ocr_frame_matched", new
        {
            raw_frame_file = rawFrameSequence is { } sequence ? $"{sequence}.jsonl" : null,
            raw_line_count = recognizedLines.Count,
            source_first_seen_qpc = sourceLine?.FirstSeenQpc,
            source_pixel_hash = sourceLine is null ? null : sourceLine.PixelHash.ToString("X16"),
            match = match is null ? null : new
            {
                decision = match.Decision.ToString(),
                matched_line_count = match.MatchedLineCount,
                confidence = match.Confidence,
                best_line_similarity = match.BestLineSimilarity,
                emitted_line_count = match.LinesToEmit.Count,
                emitted_lines = match.LinesToEmit.Select(ToLogLine),
            },
        });
    }

    public async ValueTask DisposeAsync()
    {
        Task? writerTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue?.Writer.TryComplete();
            writerTask = _writerTask;
        }

        if (writerTask is not null)
        {
            await writerTask;
        }
    }

    private void Enqueue(string eventName, object fields)
    {
        var record = JsonSerializer.Serialize(new
        {
            sequence = Interlocked.Increment(ref _sequence),
            written_at_utc = DateTimeOffset.UtcNow,
            event_name = eventName,
            fields,
        }, JsonOptions);

        ChannelWriter<string>? writer;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _queue ??= Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
            });
            _writerTask ??= Task.Run(WriteQueuedRecordsAsync);
            writer = _queue.Writer;
        }

        writer.TryWrite(record);
    }

    private async Task WriteQueuedRecordsAsync()
    {
        var queue = _queue ?? throw new InvalidOperationException("Activity log queue was not initialized.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };
        await foreach (var record in queue.Reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(record);
        }
    }

    private static object ToLogLine(RecognizedCombatLogLine line) => new
    {
        row_index = line.RowIndex,
        text = line.Text,
        color = line.ColorClass,
        first_seen_qpc = line.FirstSeenQpc,
        pixel_hash = line.PixelHash.ToString("X16"),
        words = line.Words,
    };
}
