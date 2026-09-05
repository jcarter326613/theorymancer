using System.Drawing;
using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class SkillBarFixtureCaptureWriterTests
{
    [Fact]
    public async Task WriteFrame_WritesJpegsAndATimestampedTimeline()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"theorymancer-{Guid.NewGuid():N}");
        var captureStartedAt = new DateTimeOffset(2026, 9, 4, 12, 34, 56, TimeSpan.Zero);
        var sessionDirectory = Path.Combine(
            workingDirectory,
            "debug-skill-bar-cooldown-fixtures",
            "2026-09-04_12-34-56-000");
        try
        {
            var layout = new SkillBarLayout(
            [
                SkillBarComponent.FromPixelBounds(
                    SkillBarComponentKind.WeaponSkill1,
                    new ScreenBounds(2, 3, 4, 5),
                    10,
                    12,
                    1),
            ]);
            var writer = new SkillBarFixtureCaptureWriter(workingDirectory, captureStartedAt, layout, 4);

            writer.WriteFrame(Frame(100));
            writer.WriteFrame(Frame(600));
            await writer.CompleteAsync();

            Assert.Equal(2, writer.FramesWritten);
            using var firstFrame = new Bitmap(Path.Combine(sessionDirectory, "frames", "000001.jpg"));
            Assert.Equal(10, firstFrame.Width);
            Assert.Equal(12, firstFrame.Height);

            using var timeline = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(sessionDirectory, "timeline.json")));
            var root = timeline.RootElement;
            Assert.Equal(4, root.GetProperty("captureFramesPerSecond").GetInt32());
            Assert.Equal(10, root.GetProperty("captureWidth").GetInt32());
            Assert.Equal(12, root.GetProperty("captureHeight").GetInt32());
            Assert.Equal("WeaponSkill1", root.GetProperty("slots")[0].GetProperty("kind").GetString());
            Assert.Equal(2, root.GetProperty("slots")[0].GetProperty("x").GetInt32());
            Assert.Equal("frames/000001.jpg", root.GetProperty("frames")[0].GetProperty("file").GetString());
            Assert.Equal(0, root.GetProperty("frames")[0].GetProperty("elapsedQpc").GetInt64());
            Assert.Equal(500, root.GetProperty("frames")[1].GetProperty("elapsedQpc").GetInt64());
        }
        finally
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
    }

    private static CapturedFrame Frame(long qpcTimestamp)
    {
        const int width = 10;
        const int height = 12;
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 30;
            pixels[index + 1] = 60;
            pixels[index + 2] = 90;
            pixels[index + 3] = 255;
        }

        return new CapturedFrame(qpcTimestamp, width, height, width * 4, pixels);
    }
}
