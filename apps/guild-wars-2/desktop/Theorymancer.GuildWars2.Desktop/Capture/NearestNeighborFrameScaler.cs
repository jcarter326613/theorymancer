namespace Theorymancer.GuildWars2.Desktop.Capture;

public static class NearestNeighborFrameScaler
{
    public static CapturedFrame Scale(CapturedFrame source, int scaleFactor)
    {
        if (scaleFactor < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }

        var width = checked(source.Width * scaleFactor);
        var height = checked(source.Height * scaleFactor);
        var stride = checked(width * 4);
        var pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));

        for (var sourceY = 0; sourceY < source.Height; sourceY++)
        {
            for (var sourceX = 0; sourceX < source.Width; sourceX++)
            {
                var sourceIndex = sourceY * source.Stride + sourceX * 4;
                for (var scaleY = 0; scaleY < scaleFactor; scaleY++)
                {
                    var targetY = sourceY * scaleFactor + scaleY;
                    for (var scaleX = 0; scaleX < scaleFactor; scaleX++)
                    {
                        var targetIndex = targetY * stride + (sourceX * scaleFactor + scaleX) * 4;
                        pixels[targetIndex] = source.BgraPixels[sourceIndex];
                        pixels[targetIndex + 1] = source.BgraPixels[sourceIndex + 1];
                        pixels[targetIndex + 2] = source.BgraPixels[sourceIndex + 2];
                        pixels[targetIndex + 3] = source.BgraPixels[sourceIndex + 3];
                    }
                }
            }
        }

        return new CapturedFrame(source.QpcTimestamp, width, height, stride, pixels);
    }
}
