using System.Text;
using System.IO;
using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Sessions;

public sealed class OcrFrameDebugWriter : IDisposable
{
    private readonly string _sessionDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private long _frameSequence;
    private bool _disposed;

    public OcrFrameDebugWriter(string workingDirectory, DateTimeOffset captureStartedAt)
    {
        _sessionDirectory = Path.Combine(
            workingDirectory,
            "debug-ocr-frames",
            captureStartedAt.ToString("yyyy-MM-dd_HH-mm-ss-fff"));
    }

    public void EnsureSessionDirectory() => Directory.CreateDirectory(_sessionDirectory);

    public async Task WriteFrameAsync(IReadOnlyList<RecognizedCombatLogLine> lines)
    {
        await _writeLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureSessionDirectory();

            var frameSequence = ++_frameSequence;
            var path = Path.Combine(_sessionDirectory, $"{frameSequence}.jsonl");
            await File.WriteAllTextAsync(path, Format(lines), Encoding.UTF8);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();
    }

    private static string Format(IReadOnlyList<RecognizedCombatLogLine> lines)
    {
        return string.Join(
            Environment.NewLine,
            lines.Select(line => JsonSerializer.Serialize(
                new
                {
                    rowIndex = line.RowIndex,
                    text = line.Text,
                    color = line.ColorClass,
                    firstSeenQpc = line.FirstSeenQpc,
                    pixelHash = line.PixelHash.ToString("X16"),
                    words = line.Words,
                },
                JsonOptions)));
    }
}
