using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Sessions;

public sealed record CaptureStatistics(
    long FramesCaptured,
    long OcrFramesQueued,
    long RecognizedLines,
    long EmptyOcrRows,
    long DroppedOcrRows);

public sealed record CaptureDiagnostics(
    CaptureStatistics Statistics,
    int CaptureWidth,
    int CaptureHeight,
    CapturedFrame? OriginalPreviewFrame,
    PreprocessedCombatLogFrame? ProcessedPreviewFrame,
    FrameMatchResult? LastFrameMatch);
