namespace Theorymancer.GuildWars2.Desktop.Capture;

public readonly record struct ScreenBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsUsable => Width > 0 && Height > 0;

    public bool Contains(ScreenBounds other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
}
