namespace Theorymancer.GuildWars2.Desktop.Capture;

public sealed class RowChangeDetector
{
    private readonly int _rowHeightPixels;
    private ulong[]? _previousHashes;
    private int _previousWidth;
    private int _previousHeight;

    public RowChangeDetector(int rowHeightPixels)
    {
        if (rowHeightPixels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rowHeightPixels));
        }

        _rowHeightPixels = rowHeightPixels;
    }

    public IReadOnlyList<ChangedRow> FindChangedRows(CapturedFrame frame)
    {
        if (frame.Width < 1 || frame.Height < 1 || frame.Stride < frame.Width * 4)
        {
            throw new ArgumentException("The captured frame has invalid dimensions.", nameof(frame));
        }

        var rowCount = (frame.Height + _rowHeightPixels - 1) / _rowHeightPixels;
        var previousHashes = EnsurePreviousHashes(rowCount, frame.Width, frame.Height);
        var changedRows = new List<ChangedRow>();

        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var sourceY = rowIndex * _rowHeightPixels;
            var rowHeight = Math.Min(_rowHeightPixels, frame.Height - sourceY);
            var bytesPerRow = checked(frame.Width * 4);
            var rowPixels = GC.AllocateUninitializedArray<byte>(checked(bytesPerRow * rowHeight));
            for (var y = 0; y < rowHeight; y++)
            {
                Buffer.BlockCopy(
                    frame.BgraPixels,
                    checked((sourceY + y) * frame.Stride),
                    rowPixels,
                    y * bytesPerRow,
                    bytesPerRow);
            }

            var hash = Fnv1a64(rowPixels);
            if (hash == previousHashes[rowIndex])
            {
                continue;
            }

            previousHashes[rowIndex] = hash;
            changedRows.Add(new ChangedRow(
                frame.QpcTimestamp,
                rowIndex,
                hash,
                frame.Width,
                rowHeight,
                rowPixels));
        }

        return changedRows;
    }

    private ulong[] EnsurePreviousHashes(int rowCount, int width, int height)
    {
        if (_previousHashes is not null && _previousHashes.Length == rowCount &&
            _previousWidth == width && _previousHeight == height)
        {
            return _previousHashes;
        }

        _previousHashes = new ulong[rowCount];
        _previousWidth = width;
        _previousHeight = height;
        return _previousHashes;
    }

    public static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offsetBasis;
        foreach (var value in bytes)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }
}
