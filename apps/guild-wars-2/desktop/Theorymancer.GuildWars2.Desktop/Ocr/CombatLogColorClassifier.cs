using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Ocr;

public static class CombatLogColorClassifier
{
    public static string Classify(CapturedFrame frame, IReadOnlyList<RecognizedWord> words)
    {
        if (!TryFindDamageNumberBounds(words, out var bounds))
        {
            return "unknown";
        }

        var left = Math.Clamp((int)Math.Floor(bounds.Left), 0, frame.Width);
        var top = Math.Clamp((int)Math.Floor(bounds.Top), 0, frame.Height);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right), 0, frame.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), 0, frame.Height);
        if (left >= right || top >= bottom)
        {
            return "unknown";
        }

        var samples = new ColorSamples();
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = y * frame.Stride + x * 4;
                if (index + 3 >= frame.BgraPixels.Length)
                {
                    continue;
                }

                samples.Add(frame.BgraPixels[index], frame.BgraPixels[index + 1], frame.BgraPixels[index + 2]);
            }
        }

        return samples.Classify();
    }

    public static string Classify(ReadOnlySpan<byte> bgraPixels)
    {
        var samples = new ColorSamples();
        for (var index = 0; index <= bgraPixels.Length - 4; index += 16)
        {
            samples.Add(bgraPixels[index], bgraPixels[index + 1], bgraPixels[index + 2]);
        }

        return samples.Classify();
    }

    private static bool TryFindDamageNumberBounds(IReadOnlyList<RecognizedWord> words, out WordBounds bounds)
    {
        var forIndex = FindForWord(words);
        if (forIndex >= 0)
        {
            return TryFindNumberSequence(words, forIndex + 1, out bounds, out _);
        }

        var numberCount = 0;
        bounds = default;
        for (var index = 0; index < words.Count;)
        {
            if (!IsNumberToken(words[index].Text))
            {
                index++;
                continue;
            }

            if (!TryFindNumberSequence(words, index, out var candidate, out var nextIndex))
            {
                index++;
                continue;
            }

            numberCount++;
            if (numberCount > 1)
            {
                return false;
            }

            bounds = candidate;
            index = nextIndex;
        }

        return numberCount == 1;
    }

    private static int FindForWord(IReadOnlyList<RecognizedWord> words)
    {
        for (var index = 0; index < words.Count; index++)
        {
            if (string.Equals(words[index].Text.Trim(' ', ',', '.', ':', ';', '!', '?'), "for", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryFindNumberSequence(
        IReadOnlyList<RecognizedWord> words,
        int startIndex,
        out WordBounds bounds,
        out int nextIndex)
    {
        for (var index = startIndex; index < words.Count; index++)
        {
            if (!IsNumberToken(words[index].Text))
            {
                continue;
            }

            bounds = WordBounds.From(words[index]);
            nextIndex = index + 1;
            while (nextIndex + 1 < words.Count && IsComma(words[nextIndex].Text) && IsNumberToken(words[nextIndex + 1].Text))
            {
                bounds = bounds.Include(words[nextIndex]);
                bounds = bounds.Include(words[nextIndex + 1]);
                nextIndex += 2;
            }

            return true;
        }

        bounds = default;
        nextIndex = words.Count;
        return false;
    }

    private static bool IsNumberToken(string text)
    {
        var value = text.Trim().TrimEnd(',', '.', ':', ';', '!', '?');
        if (value.Length == 0)
        {
            return false;
        }

        var previousWasComma = false;
        var hasDigit = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsDigit(character))
            {
                previousWasComma = false;
                hasDigit = true;
                continue;
            }

            if (character != ',' || index == 0 || previousWasComma)
            {
                return false;
            }

            previousWasComma = true;
        }

        return hasDigit && !previousWasComma;
    }

    private static bool IsComma(string text) => text.Trim() == ",";

    private readonly record struct WordBounds(double Left, double Top, double Right, double Bottom)
    {
        public static WordBounds From(RecognizedWord word) => new(word.X, word.Y, word.X + word.Width, word.Y + word.Height);

        public WordBounds Include(RecognizedWord word) => new(
            Math.Min(Left, word.X),
            Math.Min(Top, word.Y),
            Math.Max(Right, word.X + word.Width),
            Math.Max(Bottom, word.Y + word.Height));
    }

    private struct ColorSamples
    {
        private const double MaximumColorDistanceSquared = 40 * 40;

        private long _red;
        private long _green;
        private long _blue;
        private int _count;

        public void Add(byte blue, byte green, byte red)
        {
            var maximum = Math.Max(red, Math.Max(green, blue));
            var minimum = Math.Min(red, Math.Min(green, blue));
            if (maximum < 80 || maximum - minimum < 35)
            {
                return;
            }

            _red += red;
            _green += green;
            _blue += blue;
            _count++;
        }

        public readonly string Classify()
        {
            if (_count < 4)
            {
                return "unknown";
            }

            var redDistance = DistanceFrom(218, 49, 49);
            var blueDistance = DistanceFrom(206, 81, 207);
            var greenDistance = DistanceFrom(203, 118, 2);
            var closestDistance = Math.Min(redDistance, Math.Min(blueDistance, greenDistance));
            if (closestDistance > MaximumColorDistanceSquared)
            {
                return "unknown";
            }

            if (redDistance < blueDistance && redDistance < greenDistance)
            {
                return "red";
            }

            return blueDistance < greenDistance ? "blue" : "green";
        }

        private readonly double DistanceFrom(byte red, byte green, byte blue)
        {
            var averageRed = (double)_red / _count;
            var averageGreen = (double)_green / _count;
            var averageBlue = (double)_blue / _count;
            var redDifference = averageRed - red;
            var greenDifference = averageGreen - green;
            var blueDifference = averageBlue - blue;
            return redDifference * redDifference + greenDifference * greenDifference + blueDifference * blueDifference;
        }
    }
}
