namespace Theorymancer.GuildWars2.Desktop.Ocr;

public static class CombatLogColorClassifier
{
    public static string Classify(ReadOnlySpan<byte> bgraPixels)
    {
        if (bgraPixels.Length < 4)
        {
            return "unknown";
        }

        long red = 0;
        long green = 0;
        long blue = 0;
        var samples = 0;
        for (var index = 0; index <= bgraPixels.Length - 4; index += 16)
        {
            var b = bgraPixels[index];
            var g = bgraPixels[index + 1];
            var r = bgraPixels[index + 2];
            var maximum = Math.Max(r, Math.Max(g, b));
            var minimum = Math.Min(r, Math.Min(g, b));
            if (maximum < 80 || maximum - minimum < 35)
            {
                continue;
            }

            red += r;
            green += g;
            blue += b;
            samples++;
        }

        if (samples < 4)
        {
            return "unknown";
        }

        if (red > green * 13 / 10 && red > blue * 13 / 10)
        {
            return "red";
        }

        if (green > red * 13 / 10 && green > blue * 13 / 10)
        {
            return "green";
        }

        if (blue > red * 13 / 10 && blue > green * 13 / 10)
        {
            return "blue";
        }

        if (red > blue * 13 / 10 && green > blue * 13 / 10)
        {
            return "yellow";
        }

        return "unknown";
    }
}
