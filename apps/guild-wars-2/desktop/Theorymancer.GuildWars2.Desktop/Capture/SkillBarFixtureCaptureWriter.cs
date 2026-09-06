using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Capture;

public sealed class SkillBarFixtureCaptureWriter
{
    private const long JpegQuality = 90;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _sessionDirectory;
    private readonly string _framesDirectory;
    private readonly SkillBarLayout _layout;
    private readonly int _framesPerSecond;
    private readonly DateTimeOffset _captureStartedAt;
    private readonly List<FixtureFrame> _frames = [];
    private IReadOnlyList<FixtureSlot>? _slots;
    private long? _firstFrameQpc;
    private int? _frameWidth;
    private int? _frameHeight;
    private bool _completed;

    public SkillBarFixtureCaptureWriter(
        string workingDirectory,
        DateTimeOffset captureStartedAt,
        SkillBarLayout layout,
        int framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(framesPerSecond, 0);
        _sessionDirectory = Path.Combine(
            workingDirectory,
            "debug-skill-bar-cooldown-fixtures",
            captureStartedAt.ToString("yyyy-MM-dd_HH-mm-ss-fff"));
        _framesDirectory = Path.Combine(_sessionDirectory, "frames");
        _layout = layout;
        _framesPerSecond = framesPerSecond;
        _captureStartedAt = captureStartedAt;
    }

    public string SessionDirectory => _sessionDirectory;

    public int FramesWritten => _frames.Count;

    public void WriteFrame(CapturedFrame frame)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        EnsureDimensions(frame);
        Directory.CreateDirectory(_framesDirectory);

        var sequence = _frames.Count + 1;
        var fileName = $"{sequence:D6}.jpg";
        WriteJpeg(frame, Path.Combine(_framesDirectory, fileName));
        var firstFrameQpc = _firstFrameQpc ??= frame.QpcTimestamp;
        var elapsedQpc = frame.QpcTimestamp - firstFrameQpc;
        _frames.Add(new FixtureFrame(
            sequence,
            Path.Combine("frames", fileName).Replace(Path.DirectorySeparatorChar, '/'),
            frame.QpcTimestamp,
            elapsedQpc,
            elapsedQpc * 1000.0 / Stopwatch.Frequency));
    }

    public async Task CompleteAsync()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        Directory.CreateDirectory(_sessionDirectory);
        var timeline = new FixtureTimeline(
            _captureStartedAt,
            _framesPerSecond,
            Stopwatch.Frequency,
            _frameWidth ?? 0,
            _frameHeight ?? 0,
            _slots ?? [],
            _frames);
        await File.WriteAllTextAsync(
            Path.Combine(_sessionDirectory, "timeline.json"),
            JsonSerializer.Serialize(timeline, JsonOptions));
    }

    private void EnsureDimensions(CapturedFrame frame)
    {
        if (_frameWidth is { } width && _frameHeight is { } height)
        {
            if (frame.Width != width || frame.Height != height)
            {
                throw new InvalidOperationException("The skill-bar capture dimensions changed during fixture recording.");
            }

            return;
        }

        _frameWidth = frame.Width;
        _frameHeight = frame.Height;
        _slots = _layout.Components
            .OrderBy(component => component.Kind)
            .Select(component =>
            {
                var bounds = component.ToPixelBounds(frame.Width, frame.Height);
                return new FixtureSlot(component.Kind, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            })
            .ToList();
    }

    private static void WriteJpeg(CapturedFrame frame, string path)
    {
        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
        var bitmapBounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(bitmapBounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(
                    frame.BgraPixels,
                    y * frame.Stride,
                    IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride),
                    frame.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        var jpegCodec = ImageCodecInfo.GetImageEncoders()
            .Single(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
        bitmap.Save(path, jpegCodec, encoderParameters);
    }

    private sealed record FixtureTimeline(
        DateTimeOffset CaptureStartedAtUtc,
        int CaptureFramesPerSecond,
        long QpcFrequency,
        int CaptureWidth,
        int CaptureHeight,
        IReadOnlyList<FixtureSlot> Slots,
        IReadOnlyList<FixtureFrame> Frames);

    private sealed record FixtureSlot(
        SkillBarComponentKind Kind,
        int X,
        int Y,
        int Width,
        int Height);

    private sealed record FixtureFrame(
        int Sequence,
        string File,
        long QpcTimestamp,
        long ElapsedQpc,
        double ElapsedMilliseconds);
}
