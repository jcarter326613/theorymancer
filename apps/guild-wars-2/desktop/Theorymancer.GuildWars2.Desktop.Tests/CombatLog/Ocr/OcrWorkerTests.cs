using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogOcrWorkerTests
{
    [Fact]
    public async Task ProcessQueueAsync_CountsRecognizedAndEmptyCropResults()
    {
        var recognized = new List<RecognizedCombatLogLine>();
        await using var worker = new OcrWorker(
            new StubOcrEngine(
                [new RecognizedCombatLogLine(1, 0, 1, "Recognized", "other", [])],
                []),
            line =>
            {
                recognized.Add(line);
                return Task.CompletedTask;
            },
            _ => { },
            _ => { },
            _ => { });

        Assert.True(worker.TryQueue(CreateFrame(0)));
        await WaitUntilAsync(() => worker.RecognizedRows == 1);
        Assert.True(worker.TryQueue(CreateFrame(1)));
        await WaitUntilAsync(() => worker.RecognizedRows + worker.EmptyRows == 2);

        Assert.Single(recognized);
        Assert.Equal(1, worker.RecognizedRows);
        Assert.Equal(1, worker.EmptyRows);
    }

    [Fact]
    public async Task ProcessQueueAsync_ReportsEveryOcrResultBeforeFrameMatching()
    {
        var rawOcrResults = new List<IReadOnlyList<RecognizedCombatLogLine>>();
        var line = new RecognizedCombatLogLine(1, 0, 1, "Recognized", "other", []);
        await using var worker = new OcrWorker(
            new StubOcrEngine([line], [line]),
            _ => Task.CompletedTask,
            _ => { },
            _ => { },
            _ => { },
            lines =>
            {
                rawOcrResults.Add(lines);
                return Task.FromResult<long?>(null);
            });

        Assert.True(worker.TryQueue(CreateFrame(0)));
        await WaitUntilAsync(() => worker.RecognizedRows == 1);
        Assert.True(worker.TryQueue(CreateFrame(1)));
        await WaitUntilAsync(() => rawOcrResults.Count == 2);

        Assert.Equal(2, rawOcrResults.Count);
        Assert.Single(rawOcrResults[0]);
        Assert.Single(rawOcrResults[1]);
        Assert.Equal(1, worker.RecognizedRows);
    }

    [Fact]
    public async Task ProcessQueueAsync_ReportsRawFrameAndMatchResultTogether()
    {
        var processedFrames = new List<(long? RawFrameSequence, int RawLineCount, FrameMatchResult? Match)>();
        var line = new RecognizedCombatLogLine(1, 0, 1, "Recognized", "other", []);
        await using var worker = new OcrWorker(
            new StubOcrEngine([line]),
            _ => Task.CompletedTask,
            _ => { },
            _ => { },
            _ => { },
            _ => Task.FromResult<long?>(12),
            (rawFrameSequence, lines, match) => processedFrames.Add((rawFrameSequence, lines.Count, match)));

        Assert.True(worker.TryQueue(CreateFrame(0)));
        await WaitUntilAsync(() => processedFrames.Count == 1);

        var processedFrame = Assert.Single(processedFrames);
        Assert.Equal(12, processedFrame.RawFrameSequence);
        Assert.Equal(1, processedFrame.RawLineCount);
        var match = Assert.IsType<FrameMatchResult>(processedFrame.Match);
        Assert.Equal(FrameMatchDecision.Initial, match.Decision);
        Assert.Single(match.LinesToEmit);
    }

    private static CapturedFrame CreateFrame(int timestamp) => new(
        QpcTimestamp: timestamp,
        Width: 1,
        Height: 1,
        Stride: 4,
        BgraPixels: [0, 0, 0, 255]);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class StubOcrEngine(params IReadOnlyList<RecognizedCombatLogLine>[] results) : ICombatLogOcrEngine
    {
        private int _nextResult;

        public Task<IReadOnlyList<RecognizedCombatLogLine>> RecognizeAsync(
            CapturedFrame sourceFrame,
            CapturedFrame ocrFrame,
            CancellationToken cancellationToken) =>
            Task.FromResult(results[Interlocked.Increment(ref _nextResult) - 1]);
    }
}
