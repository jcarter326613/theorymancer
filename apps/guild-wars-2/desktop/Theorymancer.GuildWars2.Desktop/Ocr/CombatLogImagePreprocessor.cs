using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Ocr;

public sealed record PreprocessedCombatLogFrame(CapturedFrame Frame);

public static class CombatLogImagePreprocessor
{
    public const int ScaleFactor = 3;

    public static PreprocessedCombatLogFrame Process(CapturedFrame source)
    {
        var width = checked(source.Width * ScaleFactor);
        var height = checked(source.Height * ScaleFactor);
        var stride = checked(width * 4);
        var pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));

        for (var sourceY = 0; sourceY < source.Height; sourceY++)
        {
            for (var sourceX = 0; sourceX < source.Width; sourceX++)
            {
                var sourceIndex = sourceY * source.Stride + sourceX * 4;
                for (var scaleY = 0; scaleY < ScaleFactor; scaleY++)
                {
                    var targetY = sourceY * ScaleFactor + scaleY;
                    for (var scaleX = 0; scaleX < ScaleFactor; scaleX++)
                    {
                        var targetIndex = targetY * stride + (sourceX * ScaleFactor + scaleX) * 4;
                        pixels[targetIndex] = source.BgraPixels[sourceIndex];
                        pixels[targetIndex + 1] = source.BgraPixels[sourceIndex + 1];
                        pixels[targetIndex + 2] = source.BgraPixels[sourceIndex + 2];
                        pixels[targetIndex + 3] = source.BgraPixels[sourceIndex + 3];
                    }
                }
            }
        }

        return new PreprocessedCombatLogFrame(
            new CapturedFrame(source.QpcTimestamp, width, height, stride, pixels));
    }
}
