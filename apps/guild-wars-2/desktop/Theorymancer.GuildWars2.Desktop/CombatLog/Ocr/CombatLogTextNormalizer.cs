using System.Text.RegularExpressions;

namespace Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;

public static partial class CombatLogTextNormalizer
{
    public static string AppendFragment(string existing, string fragment) =>
        string.IsNullOrWhiteSpace(existing) ? fragment.Trim() : $"{existing} {fragment.Trim()}";

    public static string NormalizeVisualRow(string text) => DigitCommaWhitespace().Replace(text.Trim(), ",");

    [GeneratedRegex(@"(?<=\d)\s*,\s*(?=\d)")]
    private static partial Regex DigitCommaWhitespace();
}
