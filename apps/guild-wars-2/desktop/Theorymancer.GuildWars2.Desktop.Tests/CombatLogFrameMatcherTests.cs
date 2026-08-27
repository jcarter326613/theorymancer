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
                Spec(0, "A.", "red"),
                Spec(1, "G.", "green"),
                Spec(2, "H.", "blue"),
            ],
            current:
            [
                Spec(0, "G.", "green"),
                Spec(1, "H.", "blue"),
                Spec(2, "R.", "red"),
                Spec(3, "Q.", "green"),
            ],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 2,
            expectedLines: [Spec(2, "R.", "red"), Spec(3, "Q.", "green")]);

        yield return Case(
            history: [Spec(0, "You hit the monster for 123 using Storm Strike.", "red")],
            current: [Spec(0, "You hit the monster for 123 using Storm Strike.", "blue")],
            decision: FrameMatchDecision.NoOverlap,
            matchedLineCount: 0,
            expectedLines: [Spec(0, "You hit the monster for 123 using Storm Strike.", "blue")]);

        yield return Case(
            history: [Spec(0, "You hit the monster for 123 using Storm Strike.", "green")],
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
            history: [Spec(0, "G.", "green"), Spec(1, "H.", "red")],
            current: [Spec(0, "G.", "green"), Spec(1, "H.", "blue"), Spec(2, "R.", "red")],
            decision: FrameMatchDecision.NoOverlap,
            matchedLineCount: 0,
            expectedLines: [Spec(0, "G.", "green"), Spec(1, "H.", "blue"), Spec(2, "R.", "red")]);
            
        yield return Case(
            history: [Spec(0, "G.", "green"), Spec(1, "H.", "blue")],
            current: [Spec(0, "G.", "green"), Spec(1, "H.", "blue"), Spec(2, "R.", "red")],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 2,
            expectedLines: [Spec(2, "R.", "red")]);
            
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
            history: [Spec(4, "Great.", "green"), Spec(5, "Hello.", "blue")],
            current: [Spec(0, "Gr3at.", "green"), Spec(1, "Hello.", "blue"), Spec(2, "Ready.", "red")],
            decision: FrameMatchDecision.Overlap,
            matchedLineCount: 2,
            expectedLines: [Spec(2, "Ready.", "red")]);

        yield return Case(
            history: [Spec(4, "A.", "green"), Spec(5, "B.", "blue")],
            current: [Spec(0, "New event.", "red"), Spec(1, "A.", "green"), Spec(2, "B.", "blue")],
            decision: FrameMatchDecision.NoOverlap,
            matchedLineCount: 0,
            expectedLines: [Spec(0, "New event.", "red"), Spec(1, "A.", "green"), Spec(2, "B.", "blue")]);
    }

    [Fact]
    public void Match_DoesNotEmitPersistedFrameWhenOnlyTimestampAndPixelHashChange()
    {
        var matcher = new CombatLogFrameMatcher();
        _ = matcher.Match(PersistedFrame(182686418828, 0x3EAD2BE9BFBDA57D));

        var result = matcher.Match(PersistedFrame(182688900545, 0x24BE4E3DA637F385));

        Assert.Equal(FrameMatchDecision.Overlap, result.Decision);
        Assert.Equal(27, result.MatchedLineCount);
        Assert.Empty(result.LinesToEmit);
    }

    [Fact]
    public void Match_DoesNotReemitShiftedViewportAfterHistoryCrossesFrameBoundary()
    {
        var matcher = new CombatLogFrameMatcher();
        _ = matcher.Match(
        [
            Line(Spec(0, "A.", "red", firstSeenQpc: 100)),
            Line(Spec(1, "B.", "green", firstSeenQpc: 100)),
            Line(Spec(2, "C.", "blue", firstSeenQpc: 100)),
            Line(Spec(3, "D.", "red", firstSeenQpc: 100)),
        ]);
        var shiftedViewport = new[]
        {
            Line(Spec(0, "B.", "green", firstSeenQpc: 200)),
            Line(Spec(1, "C.", "blue", firstSeenQpc: 200)),
            Line(Spec(2, "D.", "red", firstSeenQpc: 200)),
            Line(Spec(3, "E.", "green", firstSeenQpc: 200)),
        };

        var shifted = matcher.Match(shiftedViewport);
        var repeated = matcher.Match(shiftedViewport.Select(line => line with { FirstSeenQpc = 300 }).ToList());

        Assert.Equal(FrameMatchDecision.Overlap, shifted.Decision);
        Assert.Equal(["E."], shifted.LinesToEmit.Select(line => line.Text));
        Assert.Equal(FrameMatchDecision.Overlap, repeated.Decision);
        Assert.Equal(4, repeated.MatchedLineCount);
        Assert.Empty(repeated.LinesToEmit);
    }

    [Fact]
    public void Match_DoesNotReemitUnchangedViewportAfterAnOcrGap()
    {
        var matcher = new CombatLogFrameMatcher();
        _ = matcher.Match(
        [
            Line(Spec(0, "A.", "red", firstSeenQpc: 100)),
            Line(Spec(1, "B.", "green", firstSeenQpc: 100)),
            Line(Spec(2, "C.", "blue", firstSeenQpc: 100)),
            Line(Spec(3, "D.", "red", firstSeenQpc: 100)),
            Line(Spec(4, "Missing OCR row.", "green", firstSeenQpc: 100)),
            Line(Spec(5, "E.", "blue", firstSeenQpc: 100)),
            Line(Spec(6, "F.", "red", firstSeenQpc: 100)),
        ]);
        var changedViewport = new[]
        {
            Line(Spec(0, "B.", "green", firstSeenQpc: 200)),
            Line(Spec(1, "C.", "blue", firstSeenQpc: 200)),
            Line(Spec(2, "D.", "red", firstSeenQpc: 200)),
            Line(Spec(3, "E.", "blue", firstSeenQpc: 200)),
            Line(Spec(4, "F.", "red", firstSeenQpc: 200)),
            Line(Spec(5, "G.", "green", firstSeenQpc: 200)),
        };

        _ = matcher.Match(changedViewport);
        var repeated = matcher.Match(changedViewport.Select(line => line with { FirstSeenQpc = 300 }).ToList());

        Assert.Equal(FrameMatchDecision.Overlap, repeated.Decision);
        Assert.Equal(changedViewport.Length, repeated.MatchedLineCount);
        Assert.Empty(repeated.LinesToEmit);
    }

    private static object[] Case(
        IReadOnlyList<LineSpec> history,
        IReadOnlyList<LineSpec> current,
        FrameMatchDecision decision,
        int matchedLineCount,
        IReadOnlyList<LineSpec> expectedLines) =>
        [new FrameMatchCase(history, current, decision, matchedLineCount, expectedLines)];

    private static IReadOnlyList<RecognizedCombatLogLine> PersistedFrame(long firstSeenQpc, ulong pixelHash) =>
    [
        Line(Spec(0, "You hit Standard Kitty Golem for 954 using [Dusk Strike].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(1, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(2, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(3, "You hit Standard Kitty Golem for 938 using [Dusk Strike].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(4, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(5, "You hit Standard Kitty Golem for 2,932 using [Gravedigger].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(6, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(7, "You hit Standard Kitty Golem for 906 using [Nightfall].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(8, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(9, "You hit Standard Kitty Golem for 906 using [Nightfall].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(10, "You hit Standard Kitty Golem for 1,160 using [Grasping Darkness].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(11, "You hit Standard Kitty Golem for 1,096 using [Nightfall].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(12, "You hit Standard Kitty Golem for 319 using [Bleeding].", "unknown", firstSeenQpc, pixelHash)),
        Line(Spec(13, "You hit Standard Kitty Golem for 1,096 using [Nightfall].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(14, "You hit Standard Kitty Golem for 326 using [Bleeding].", "unknown", firstSeenQpc, pixelHash)),
        Line(Spec(15, "You hit Standard Kitty Golem for 326 using [Bleeding].", "unknown", firstSeenQpc, pixelHash)),
        Line(Spec(16, "You hit Standard Kitty Golem for 326 using [Bleeding].", "unknown", firstSeenQpc, pixelHash)),
        Line(Spec(17, "You hit Standard Kitty Golem for 208 using [Bleeding].", "unknown", firstSeenQpc, pixelHash)),
        Line(Spec(18, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(19, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(20, "You hit Standard Kitty Golem for 938 using [Dusk Strike].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(21, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(22, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(23, "You hit Standard Kitty Golem for 2,721 using [Gravedigger].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(24, "You hit Standard Kitty Golem for 170 using Signet of Vampirism.", "red", firstSeenQpc, pixelHash)),
        Line(Spec(25, "You critically hit Standard Kitty Golem for 1,681 using [Dusk Strike].", "red", firstSeenQpc, pixelHash)),
        Line(Spec(26, "You hit Standard Kitty Golem for 417 using Flame Blast.", "red", firstSeenQpc, pixelHash)),
    ];

    private static LineSpec Spec(
        int rowIndex,
        string text,
        string colorClass,
        long firstSeenQpc = 0,
        ulong pixelHash = 0) =>
        new(rowIndex, text, colorClass, firstSeenQpc, pixelHash);

    private static RecognizedCombatLogLine Line(LineSpec line) =>
        new(line.FirstSeenQpc, line.RowIndex, line.PixelHash, line.Text, line.ColorClass, []);

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

    public sealed record LineSpec(
        int RowIndex,
        string Text,
        string ColorClass,
        long FirstSeenQpc = 0,
        ulong PixelHash = 0);
}
