using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.Ocr;

namespace Theorymancer.GuildWars2.Desktop.Sessions;

public sealed record CaptureStatistics(
    long FramesCaptured,
    long ChangedRows,
    long RecognizedLines,
    long EmptyOcrRows,
    long DroppedOcrRows);

public sealed record CaptureDiagnostics(
    CaptureStatistics Statistics,
    int CaptureWidth,
    int CaptureHeight,
    int RowHeightPixels,
    CapturedFrame? OriginalPreviewFrame,
    PreprocessedCombatLogFrame? ProcessedPreviewFrame);
