using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Ocr;

public sealed record PreprocessedCombatLogFrame(CapturedFrame Frame, byte Threshold);

public static class CombatLogImagePreprocessor
{
    public const int ScaleFactor = 3;

    public static PreprocessedCombatLogFrame Process(CapturedFrame source)
    {
        var threshold = FindThreshold(source);
        var width = checked(source.Width * ScaleFactor);
        var height = checked(source.Height * ScaleFactor);
        var stride = checked(width * 4);
        var pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));

        for (var sourceY = 0; sourceY < source.Height; sourceY++)
        {
            for (var sourceX = 0; sourceX < source.Width; sourceX++)
            {
                var sourceIndex = sourceY * source.Stride + sourceX * 4;
                var foreground = Brightness(source.BgraPixels, sourceIndex) > threshold;
                var value = foreground ? (byte)0 : byte.MaxValue;
                for (var scaleY = 0; scaleY < ScaleFactor; scaleY++)
                {
                    var targetY = sourceY * ScaleFactor + scaleY;
                    for (var scaleX = 0; scaleX < ScaleFactor; scaleX++)
                    {
                        var targetIndex = targetY * stride + (sourceX * ScaleFactor + scaleX) * 4;
                        pixels[targetIndex] = value;
                        pixels[targetIndex + 1] = value;
                        pixels[targetIndex + 2] = value;
                        pixels[targetIndex + 3] = byte.MaxValue;
                    }
                }

/*
                for (var scaleY = 0; scaleY < ScaleFactor; scaleY++)
                {
                    var targetY = sourceY * ScaleFactor + scaleY;
                    for (var scaleX = 0; scaleX < ScaleFactor; scaleX++)
                    {
                        var targetIndex = targetY * stride + (sourceX * ScaleFactor + scaleX) * 4;
                        pixels[targetIndex] = source.BgraPixels[sourceIndex];
                        pixels[targetIndex + 1] = source.BgraPixels[sourceIndex + 1];
                        pixels[targetIndex + 2] = source.BgraPixels[sourceIndex + 2];
                        pixels[targetIndex + 3] = byte.MaxValue;
                    }
                }
                */
            }
        }

        return new PreprocessedCombatLogFrame(
            new CapturedFrame(source.QpcTimestamp, width, height, stride, pixels),
            threshold);
    }

    private static byte FindThreshold(CapturedFrame source)
    {
        Span<int> histogram = stackalloc int[256];
        var pixelCount = checked(source.Width * source.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                histogram[Brightness(source.BgraPixels, y * source.Stride + x * 4)]++;
            }
        }

        long weightedTotal = 0;
        for (var value = 0; value < histogram.Length; value++)
        {
            weightedTotal += (long)value * histogram[value];
        }

        long backgroundWeight = 0;
        long backgroundTotal = 0;
        var highestVariance = -1.0;
        byte threshold = 128;
        for (var value = 0; value < histogram.Length; value++)
        {
            backgroundWeight += histogram[value];
            if (backgroundWeight == 0)
            {
                continue;
            }

            var foregroundWeight = pixelCount - backgroundWeight;
            if (foregroundWeight == 0)
            {
                break;
            }

            backgroundTotal += (long)value * histogram[value];
            var backgroundMean = (double)backgroundTotal / backgroundWeight;
            var foregroundMean = (double)(weightedTotal - backgroundTotal) / foregroundWeight;
            var variance = backgroundWeight * foregroundWeight * Math.Pow(backgroundMean - foregroundMean, 2);
            if (variance > highestVariance)
            {
                highestVariance = variance;
                threshold = (byte)value;
            }
        }

        return threshold;
    }

    private static byte Brightness(byte[] pixels, int index) => Math.Max(pixels[index], Math.Max(pixels[index + 1], pixels[index + 2]));
}
