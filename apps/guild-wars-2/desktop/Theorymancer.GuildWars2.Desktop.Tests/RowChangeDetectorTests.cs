using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class RowChangeDetectorTests
{
    [Fact]
    public void FindChangedRows_OnlyReturnsRowsWhosePixelsChanged()
    {
        var detector = new RowChangeDetector(rowHeightPixels: 2);
        var firstFrame = CreateFrame(qpcTimestamp: 10, firstRowValue: 1, secondRowValue: 2);

        Assert.Equal(new[] { 0, 1 }, detector.FindChangedRows(firstFrame).Select(row => row.RowIndex));
        Assert.Empty(detector.FindChangedRows(firstFrame));

        var changedFrame = CreateFrame(qpcTimestamp: 20, firstRowValue: 1, secondRowValue: 3);
        var changedRow = Assert.Single(detector.FindChangedRows(changedFrame));
        Assert.Equal(1, changedRow.RowIndex);
        Assert.Equal(20, changedRow.FirstSeenQpc);
    }

    [Fact]
    public void FindChangedRows_ResetsWhenCaptureDimensionsChange()
    {
        var detector = new RowChangeDetector(rowHeightPixels: 2);
        _ = detector.FindChangedRows(CreateFrame(qpcTimestamp: 10, firstRowValue: 1, secondRowValue: 2));
        var resizedFrame = new CapturedFrame(20, 3, 2, 12, Enumerable.Repeat((byte)1, 24).ToArray());

        Assert.Single(detector.FindChangedRows(resizedFrame));
    }

    [Fact]
    public void Fnv1a64_IsDeterministic()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        Assert.Equal(RowChangeDetector.Fnv1a64(bytes), RowChangeDetector.Fnv1a64(bytes));
        Assert.NotEqual(RowChangeDetector.Fnv1a64(bytes), RowChangeDetector.Fnv1a64(new byte[] { 4, 3, 2, 1 }));
    }

    private static CapturedFrame CreateFrame(long qpcTimestamp, byte firstRowValue, byte secondRowValue)
    {
        var pixels = new byte[2 * 4 * 4];
        for (var index = 0; index < 2 * 2 * 4; index++)
        {
            pixels[index] = firstRowValue;
        }

        for (var index = 2 * 2 * 4; index < pixels.Length; index++)
        {
            pixels[index] = secondRowValue;
        }

        return new CapturedFrame(
            QpcTimestamp: qpcTimestamp,
            Width: 2,
            Height: 4,
            Stride: 8,
            BgraPixels: pixels);
    }
}
