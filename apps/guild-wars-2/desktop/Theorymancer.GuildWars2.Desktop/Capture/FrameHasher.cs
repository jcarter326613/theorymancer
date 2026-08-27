namespace Theorymancer.GuildWars2.Desktop.Capture;

public static class FrameHasher
{
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
