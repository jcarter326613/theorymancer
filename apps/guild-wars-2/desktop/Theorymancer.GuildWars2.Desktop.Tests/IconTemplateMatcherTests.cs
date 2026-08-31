using System.Drawing;
using System.Drawing.Imaging;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class IconTemplateMatcherTests
{
    [Fact]
    public void FindBestMatch_LocatesAnIconDespiteABlackReferenceBorder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"theorymancer-{Guid.NewGuid():N}.png");
        try
        {
            using (var icon = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using var graphics = Graphics.FromImage(icon);
                graphics.Clear(Color.Black);
                using var brush = new SolidBrush(Color.LimeGreen);
                graphics.FillEllipse(brush, 5, 4, 22, 24);
                using var darkBrush = new SolidBrush(Color.DarkGreen);
                graphics.FillRectangle(darkBrush, 13, 7, 5, 17);
                icon.Save(path, ImageFormat.Png);
            }

            var frame = CreateFrameWithIcon(path, 80, 60, 8, 12, 40);

            var match = IconTemplateMatcher.FindBestMatch(frame, path, "Test", 1);

            Assert.NotNull(match);
            Assert.InRange(match.Bounds.X, 0, 20);
            Assert.InRange(match.Bounds.Y, 0, 24);
            Assert.InRange(match.Bounds.Width, 20, 60);
            Assert.True(match.Score > 0.75, $"Raw-pixel score: {match.Score:F3}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CapturedFrame CreateFrameWithIcon(string iconPath, int width, int height, int x, int y, int size)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        using var icon = new Bitmap(iconPath);
        using var resized = new Bitmap(icon, new Size(size, size));
        for (var sourceY = 0; sourceY < size; sourceY++)
        {
            for (var sourceX = 0; sourceX < size; sourceX++)
            {
                var color = resized.GetPixel(sourceX, sourceY);
                var index = (y + sourceY) * stride + (x + sourceX) * 4;
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = color.A;
            }
        }

        return new CapturedFrame(1, width, height, stride, pixels);
    }
}
