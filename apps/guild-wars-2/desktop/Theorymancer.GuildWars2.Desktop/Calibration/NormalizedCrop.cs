using Theorymancer.GuildWars2.Desktop.Capture;

namespace Theorymancer.GuildWars2.Desktop.Calibration;

public sealed record NormalizedCrop(double X, double Y, double Width, double Height)
{
    public static NormalizedCrop FromScreenBounds(ScreenBounds crop, ScreenBounds clientBounds)
    {
        if (!clientBounds.Contains(crop))
        {
            throw new ArgumentException("The calibrated region must be inside the game window.", nameof(crop));
        }

        return new NormalizedCrop(
            (double)(crop.X - clientBounds.X) / clientBounds.Width,
            (double)(crop.Y - clientBounds.Y) / clientBounds.Height,
            (double)crop.Width / clientBounds.Width,
            (double)crop.Height / clientBounds.Height);
    }

    public ScreenBounds ToScreenBounds(ScreenBounds clientBounds)
    {
        var width = Math.Max(1, (int)Math.Round(Width * clientBounds.Width));
        var height = Math.Max(1, (int)Math.Round(Height * clientBounds.Height));
        var x = clientBounds.X + (int)Math.Round(X * clientBounds.Width);
        var y = clientBounds.Y + (int)Math.Round(Y * clientBounds.Height);
        var crop = new ScreenBounds(x, y, width, height);
        if (!clientBounds.Contains(crop))
        {
            throw new InvalidOperationException("The saved calibrated region no longer fits in the game window.");
        }

        return crop;
    }
}
