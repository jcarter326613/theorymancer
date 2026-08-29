using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;

namespace Theorymancer.GuildWars2.Desktop.CombatLog.Sessions;

public sealed record CombatLogCaptureStatistics(
    long FramesCaptured,
    long OcrFramesQueued,
    long RecognizedLines,
    long EmptyOcrRows,
    long DroppedOcrRows);

public sealed record CombatLogCaptureDiagnostics(
    CombatLogCaptureStatistics Statistics,
    int CaptureWidth,
    int CaptureHeight,
    CapturedFrame? OriginalPreviewFrame,
    PreprocessedCombatLogFrame? ProcessedPreviewFrame,
    FrameMatchResult? LastFrameMatch);
