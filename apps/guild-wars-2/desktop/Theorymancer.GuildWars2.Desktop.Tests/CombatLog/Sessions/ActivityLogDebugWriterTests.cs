using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;
using Theorymancer.GuildWars2.Desktop.CombatLog.Sessions;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogActivityLogDebugWriterTests
{
    [Fact]
    public async Task WritesVisibleActivityAndCorrelatedFrameMatchRecords()
    {
        var sessionDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await using (var writer = new CombatLogActivityLogDebugWriter(sessionDirectory))
            {
                Assert.False(Directory.Exists(sessionDirectory));

                writer.WriteActivity(
                    new DateTimeOffset(2026, 8, 27, 14, 0, 0, TimeSpan.Zero),
                    "14:00:00  Recognized",
                    "matched_line",
                    new { row = 0 });
                writer.WriteFrameMatch(
                    46,
                    [new RecognizedCombatLogLine(123, 0, 0xABC, "Recognized", "red", [])],
                    new FrameMatchResult(FrameMatchDecision.Overlap, [], 1, 0.98, 1));
            }

            var records = await File.ReadAllLinesAsync(Path.Combine(sessionDirectory, "activity_log.jsonl"));
            using var activityRecord = JsonDocument.Parse(records[0]);
            using var frameRecord = JsonDocument.Parse(records[1]);

            Assert.Equal("activity_displayed", activityRecord.RootElement.GetProperty("event_name").GetString());
            Assert.Equal("14:00:00  Recognized", activityRecord.RootElement.GetProperty("fields").GetProperty("displayed_text").GetString());
            Assert.Equal("matched_line", activityRecord.RootElement.GetProperty("fields").GetProperty("source").GetString());
            Assert.Equal("ocr_frame_matched", frameRecord.RootElement.GetProperty("event_name").GetString());
            Assert.Equal("46.jsonl", frameRecord.RootElement.GetProperty("fields").GetProperty("raw_frame_file").GetString());
            Assert.Equal("Overlap", frameRecord.RootElement.GetProperty("fields").GetProperty("match").GetProperty("decision").GetString());
            Assert.Equal(0, frameRecord.RootElement.GetProperty("fields").GetProperty("match").GetProperty("emitted_line_count").GetInt32());
        }
        finally
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
    }
}
