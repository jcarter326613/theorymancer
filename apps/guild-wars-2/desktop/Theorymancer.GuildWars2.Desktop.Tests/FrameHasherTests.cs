using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class FrameHasherTests
{
    [Fact]
    public void Fnv1a64_IsDeterministic()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        Assert.Equal(FrameHasher.Fnv1a64(bytes), FrameHasher.Fnv1a64(bytes));
        Assert.NotEqual(FrameHasher.Fnv1a64(bytes), FrameHasher.Fnv1a64(new byte[] { 4, 3, 2, 1 }));
    }
}
