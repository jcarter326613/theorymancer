using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class CombatLogFrameMatcherTests
{
    [Theory]
    [MemberData(nameof(MatchCases))]
    public void Match_EmitsExpectedLinesForViewportTransition(FrameMatchCase testCase)
    {
        var matcher = new CombatLogFrameMatcher();
        _ = matcher.Match(testCase.History.Select(Line).ToList());

        var result = matcher.Match(testCase.Current.Select(Line).ToList());

        Assert.Equal(testCase.ExpectedDecision, result.Decision);
        Assert.Equal(testCase.ExpectedMatchedLineCount, result.MatchedLineCount);
        Assert.Equal(testCase.ExpectedLines.Select(Signature), result.LinesToEmit.Select(Signature));
    }

    public static IEnumerable<object[]> MatchCases()
    {
        yield return Case(
            history:
            [
                Spec(10, "A.", "red"),
                Spec(11, "G.", "green"),
                Spec(12, "H.", "blue"),
            ],
            current:
            [
                Spec(3, "G.", "green"),
                Spec(4, "H.", "blue"),
                Spec(5, "R.", "red"),
                Spec(6, "Q.", "green"),
            ],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 2,
            expectedLines: [Spec(5, "R.", "red"), Spec(6, "Q.", "green")]);

        yield return Case(
            history: [Spec(0, "You hit the monster for 123 using Storm Strike.", "red")],
            current: [Spec(4, "You hit the monster for 123 using Storm Strike.", "blue")],
            decision: FrameMatchDecision.NoOverlap,
            matchedLineCount: 0,
            expectedLines: [Spec(4, "You hit the monster for 123 using Storm Strike.", "blue")]);

        yield return Case(
            history: [Spec(2, "You hit the monster for 123 using Storm Strike.", "green")],
            current: [Spec(0, "You hit the monster for 123 using Storm Strike.", "unknown")],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 1,
            expectedLines: []);

        yield return Case(
            history: [Spec(0, "You entered the dungeon.", "unknown"), Spec(1, "The gate opens.", "unknown")],
            current: [Spec(0, "Objective completed.", "unknown"), Spec(1, "Reward granted.", "unknown")],
            decision: FrameMatchDecision.NoOverlap,
            matchedLineCount: 0,
            expectedLines: [Spec(0, "Objective completed.", "unknown"), Spec(1, "Reward granted.", "unknown")]);

        yield return Case(
            history: [Spec(0, "You dealt 1,234 damage.", "red")],
            current: [Spec(0, "You dealt 9,999 damage.", "red")],
            decision: FrameMatchDecision.NoOverlap,
            matchedLineCount: 0,
            expectedLines: [Spec(0, "You dealt 9,999 damage.", "red")]);

        /**
         * In this case, there was something between the 2 items in the history that was never recognized.
         * The current has 2 identical lines that it starts with but positionally, they are next to eachother.
         * So this is not a repeat of the old sequence.  This is a new sequence.
         */
        yield return Case(
            history: [Spec(0, "G.", "green"), Spec(2, "H.", "blue")],
            current: [Spec(4, "G.", "green"), Spec(5, "H.", "blue"), Spec(6, "R.", "red")],
            decision: FrameMatchDecision.NoOverlap,
            matchedLineCount: 0,
            expectedLines: [Spec(4, "G.", "green"), Spec(5, "H.", "blue"), Spec(6, "R.", "red")]);
            
        yield return Case(
            history: [Spec(0, "G.", "green"), Spec(1, "H.", "blue")],
            current: [Spec(4, "G.", "green"), Spec(5, "H.", "blue"), Spec(6, "R.", "red")],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 2,
            expectedLines: [Spec(6, "R.", "red")]);
            
        yield return Case(
            history: [
                Spec(0, "You hit the monster for 11122 damage.", "green"),
                Spec(1, "You hit the monster for 11122 damage.", "green"),
                Spec(2, "You hit the monater for 1122 damage.", "green"),
                Spec(3, "You hit the monster for 2511 damage.", "blue"),
                Spec(4, "You hit the monster for 11122 damage.", "green"),
                Spec(5, "You hit the monster for 11122 damage.", "green"),
                Spec(6, "You hit the monster for 5132 damage.", "green"),
                Spec(7, "You hit the monster for 11122 damage.", "red")
            ],
            current: [
                Spec(0, "You hit the monster for 1122 damage.", "green"),
                Spec(1, "You hit the monster for 2512 damage.", "blue"),
                Spec(2, "You hit the monster for 11122 damage.", "green"),
                Spec(3, "You hit the monster for 11122 damage.", "green"),
                Spec(4, "You hit the monster for 5132 damage.", "green"),
                Spec(5, "You hit the monste for 11122 damage.", "red"),
                Spec(6, "You hit the monster for 112 damage.", "blue"),
                Spec(7, "You hit the monster for 30000 damage.", "red"),
            ],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 6,
            expectedLines: [
                Spec(6, "You hit the monster for 112 damage.", "blue"),
                Spec(7, "You hit the monster for 30000 damage.", "red"),
            ]);
            
        yield return Case(
            history: [
                Spec(0, "You hit the monster for 11122 damage.", "green"),
                Spec(1, "You hit the monster for 11122 damage.", "green"),
                Spec(2, "You hit the monater for 1122 damage.", "green"),
                Spec(3, "You hit the monster for 2511 damage.", "green"),
                Spec(4, "You hit the monster for 11122 damage.", "green"),
                Spec(5, "You hit the monster for 11122 damage.", "green"),
                Spec(6, "You hit the monster for 5132 damage.", "green"),
                Spec(7, "You hit the monster for 11122 damage.", "green")
            ],
            current: [
                Spec(0, "You hit the monster for 1122 damage.", "green"),
                Spec(1, "You hit the monster for 2512 damage.", "green"),
                Spec(2, "You hit the monster for 11122 damage.", "green"),
                Spec(3, "You hit the monster for 11122 damage.", "green"),
                Spec(4, "You hit the monster for 5132 damage.", "green"),
                Spec(5, "You hit the monste for 11122 damage.", "green"),
                Spec(6, "You hit the monster for 112 damage.", "green"),
                Spec(7, "You hit the monster for 30000 damage.", "green"),
            ],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 6,
            expectedLines: [
                Spec(6, "You hit the monster for 112 damage.", "green"),
                Spec(7, "You hit the monster for 30000 damage.", "green"),
            ]);
            
        yield return Case(
            history: [
                Spec(0, "You hit the monster for 1 damage.", "green"),
                Spec(1, "You hit the monster for 2 damage.", "green"),
                Spec(2, "You hit the monster for 3 damage.", "green"),
                Spec(3, "You hit the monster for 1 damage.", "green"),
                Spec(4, "You hit the monster for 2 damage.", "green"),
                Spec(5, "You hit the monster for 3 damage.", "green"),
            ],
            current: [
                Spec(0, "You hit the monster for 1 damage.", "green"),
                Spec(1, "You hit the monster for 2 damage.", "green"),
                Spec(2, "You hit the monster for 3 damage.", "green"),
                Spec(3, "You hit the monster for 1 damage.", "green"),
                Spec(4, "You hit the monster for 2 damage.", "green"),
                Spec(5, "You hit the monster for 3 damage.", "green"),
            ],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 6,
            expectedLines: []);
            
        yield return Case(
            history: [
                Spec(0, "You hit the monster for 1 damage.", "green"),
                Spec(1, "You hit the monster for 2 damage.", "green"),
                Spec(2, "You hit the monster for 3 damage.", "green"),
                Spec(4, "You hit the monster for 1 damage.", "green"),
                Spec(5, "You hit the monster for 2 damage.", "green"),
                Spec(6, "You hit the monster for 3 damage.", "green"),
            ],
            current: [
                Spec(0, "You hit the monster for 1 damage.", "green"),
                Spec(1, "You hit the monster for 2 damage.", "green"),
                Spec(2, "You hit the monster for 3 damage.", "green"),
                Spec(3, "You hit the monster for 1 damage.", "green"),
                Spec(4, "You hit the monster for 2 damage.", "green"),
                Spec(5, "You hit the monster for 3 damage.", "green"),
            ],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 3,
            expectedLines: [
                Spec(3, "You hit the monster for 1 damage.", "green"),
                Spec(4, "You hit the monster for 2 damage.", "green"),
                Spec(5, "You hit the monster for 3 damage.", "green"),
            ]);

        yield return Case(
            history: [Spec(0, "Great.", "green"), Spec(1, "Hello.", "blue")],
            current: [Spec(4, "Gr3at.", "green"), Spec(5, "Hello.", "blue"), Spec(6, "Ready.", "red")],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 2,
            expectedLines: [Spec(6, "Ready.", "red")]);
    }

    private static object[] Case(
        IReadOnlyList<LineSpec> history,
        IReadOnlyList<LineSpec> current,
        FrameMatchDecision decision,
        int matchedLineCount,
        IReadOnlyList<LineSpec> expectedLines) =>
        [new FrameMatchCase(history, current, decision, matchedLineCount, expectedLines)];

    private static LineSpec Spec(int rowIndex, string text, string colorClass) => new(rowIndex, text, colorClass);

    private static RecognizedCombatLogLine Line(LineSpec line) =>
        new(0, line.RowIndex, 0, line.Text, line.ColorClass, []);

    private static (int RowIndex, string Text, string ColorClass) Signature(LineSpec line) =>
        (line.RowIndex, line.Text, line.ColorClass);

    private static (int RowIndex, string Text, string ColorClass) Signature(RecognizedCombatLogLine line) =>
        (line.RowIndex, line.Text, line.ColorClass);

    public sealed record FrameMatchCase(
        IReadOnlyList<LineSpec> History,
        IReadOnlyList<LineSpec> Current,
        FrameMatchDecision ExpectedDecision,
        int ExpectedMatchedLineCount,
        IReadOnlyList<LineSpec> ExpectedLines);

    public sealed record LineSpec(int RowIndex, string Text, string ColorClass);
}
