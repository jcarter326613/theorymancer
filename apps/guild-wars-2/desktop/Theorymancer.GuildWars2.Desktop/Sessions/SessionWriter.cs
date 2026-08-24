using System.Text.Json;
using System.Diagnostics;
using System.IO;
using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Sessions;

public sealed class SessionWriter : IAsyncDisposable
{
    private const long MaximumSessionBytes = 512L * 1024 * 1024;

    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private long _sequence;
    private bool _isCapped;

    private SessionWriter(Guid sessionId, string path, FileStream stream)
    {
        SessionId = sessionId;
        Path = path;
        _stream = stream;
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public Guid SessionId { get; }

    public string Path { get; }

    public static async Task<SessionWriter> CreateAsync()
    {
        var sessionId = Guid.NewGuid();
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Theorymancer",
            "guild-wars-2",
            "screen-capture-sessions");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{sessionId:N}.jsonl");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var writer = new SessionWriter(sessionId, path, stream);
        await writer.WriteAsync("session_started", new
        {
            qpc_frequency = Stopwatch.Frequency,
            capture_mode = "visible_screen_crop",
        });
        return writer;
    }

    public Task WriteRecognizedLineAsync(RecognizedCombatLogLine line) =>
        WriteAsync("combat_log_line", new
        {
            first_seen_qpc = line.FirstSeenQpc,
            source_row = line.RowIndex,
            row_hash = line.PixelHash.ToString("X16"),
            text = line.Text,
            color_class = line.ColorClass,
            words = line.Words.Select(word => new
            {
                text = word.Text,
                x = word.X,
                y = word.Y,
                width = word.Width,
                height = word.Height,
            }),
        });

    public async Task WriteAsync(string eventName, object fields)
    {
        await _writeLock.WaitAsync();
        try
        {
            if (_isCapped && eventName != "session_stopped")
            {
                return;
            }

            if (_stream.Length >= MaximumSessionBytes && eventName != "session_stopped")
            {
                _isCapped = true;
                await WriteRecordAsync("capture_stopped", new { reason = "session_size_limit" });
                return;
            }

            await WriteRecordAsync(eventName, fields);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await WriteAsync("session_stopped", new { reason = "user_requested" });
        await _writer.DisposeAsync();
        await _stream.DisposeAsync();
        _writeLock.Dispose();
    }

    private async Task WriteRecordAsync(string eventName, object fields)
    {
        var record = new
        {
            session_id = SessionId,
            sequence = Interlocked.Increment(ref _sequence),
            event_name = eventName,
            written_at_utc = DateTimeOffset.UtcNow,
            fields,
        };
        await _writer.WriteLineAsync(JsonSerializer.Serialize(record, _jsonOptions));
    }
}
