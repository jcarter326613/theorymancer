using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class OcrWorkerTests
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
