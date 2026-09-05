using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;
using Theorymancer.GuildWars2.Desktop.CombatLog.Sessions;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogOcrFrameDebugWriterTests
{
    [Fact]
    public async Task WriteFrameAsync_CreatesSequentialJsonLineFrameFiles()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDirectory = Path.Combine(workingDirectory, "debug-combat-log-ocr-frames", "2026-08-27_12-34-56-789");
        try
        {
            using var writer = new CombatLogOcrFrameDebugWriter(
                workingDirectory,
                new DateTimeOffset(2026, 8, 27, 12, 34, 56, 789, TimeSpan.Zero));

            Assert.False(Directory.Exists(sessionDirectory));

            await writer.WriteFrameAsync(
            [
                new RecognizedCombatLogLine(
                    123,
                    4,
                    0xABC,
                    "Player dealt 1,234 damage.",
                    "red",
                    [new RecognizedWord("1,234", 1.5, 2.5, 3.5, 4.5)]),
                new RecognizedCombatLogLine(456, 5, 0xDEF, "Player healed 500.", "green", []),
            ]);
            await writer.WriteFrameAsync([]);

            var firstFrameLines = await File.ReadAllLinesAsync(Path.Combine(sessionDirectory, "1.jsonl"));
            var secondFrameLines = await File.ReadAllLinesAsync(Path.Combine(sessionDirectory, "2.jsonl"));
            using var firstLine = JsonDocument.Parse(firstFrameLines[0]);
            using var secondLine = JsonDocument.Parse(firstFrameLines[1]);

            Assert.Equal(2, firstFrameLines.Length);
            Assert.Equal(4, firstLine.RootElement.GetProperty("rowIndex").GetInt32());
            Assert.Equal("Player dealt 1,234 damage.", firstLine.RootElement.GetProperty("text").GetString());
            Assert.Equal("red", firstLine.RootElement.GetProperty("color").GetString());
            Assert.Equal(123, firstLine.RootElement.GetProperty("firstSeenQpc").GetInt64());
            Assert.Equal("0000000000000ABC", firstLine.RootElement.GetProperty("pixelHash").GetString());
            Assert.Equal("1,234", firstLine.RootElement.GetProperty("words")[0].GetProperty("text").GetString());
            Assert.Equal(5, secondLine.RootElement.GetProperty("rowIndex").GetInt32());
            Assert.Empty(secondFrameLines);
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }
}
