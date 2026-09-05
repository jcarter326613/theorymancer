using System.Drawing;
using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.ArenaNet;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

internal sealed class ReaperGreatswordFixture
{
    private ReaperGreatswordFixture(IReadOnlyList<ExpectedSlot> slots, string screenshot)
    {
        Slots = slots;
        Screenshot = screenshot;
    }

    public IReadOnlyList<ExpectedSlot> Slots { get; }
    public string Screenshot { get; }

    public static ReaperGreatswordFixture Load()
    {
        var file = JsonSerializer.Deserialize<FixtureFile>(
            File.ReadAllText(Path.Combine(ScenarioDirectory, "expectations.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The Reaper Greatsword fixture is invalid.");
        return new ReaperGreatswordFixture(file.Slots, file.Screenshot);
    }

    public CapturedFrame LoadScreenshot() => LoadFrame(Path.Combine(ScenarioDirectory, Screenshot));

    public CapturedFrame LoadScaledScreenshot(double scaleFactor)
    {
        if (scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }

        var source = LoadScreenshot();
        if (scaleFactor == 1)
        {
            return source;
        }

        var width = Math.Max(1, (int)Math.Round(source.Width * scaleFactor));
        var height = Math.Max(1, (int)Math.Round(source.Height * scaleFactor));
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)(y / scaleFactor));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)(x / scaleFactor));
                var sourceIndex = sourceY * source.Stride + sourceX * 4;
                var targetIndex = y * stride + x * 4;
                pixels[targetIndex] = source.BgraPixels[sourceIndex];
                pixels[targetIndex + 1] = source.BgraPixels[sourceIndex + 1];
                pixels[targetIndex + 2] = source.BgraPixels[sourceIndex + 2];
                pixels[targetIndex + 3] = source.BgraPixels[sourceIndex + 3];
            }
        }

        return new CapturedFrame(source.QpcTimestamp, width, height, stride, pixels);
    }

    public BuildInput LoadBuildInput() => JsonSerializer.Deserialize<BuildInput>(
        File.ReadAllText(Path.Combine(ScenarioDirectory, "build-input.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("The Reaper build fixture is invalid.");

    public IReadOnlyList<SkillBarIconTemplate> CreateTemplates() => Slots.Select(slot => new SkillBarIconTemplate(
        slot.ComponentKind,
        slot.Name,
        slot.SkillId,
        Path.Combine(ScenarioDirectory, "icons", slot.IconFile))).ToList();

    public string GetIconPath(ExpectedSlot slot) => Path.Combine(ScenarioDirectory, "icons", slot.IconFile);

    public string ManifestPath => Path.Combine(FixturesDirectory, "icons.manifest.json");

    private static CapturedFrame LoadFrame(string path)
    {
        using var bitmap = new Bitmap(path);
        var stride = bitmap.Width * 4;
        var pixels = new byte[stride * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                var index = y * stride + x * 4;
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = color.A;
            }
        }

        return new CapturedFrame(1, bitmap.Width, bitmap.Height, stride, pixels);
    }

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "SkillBar", "Fixtures");
    private static string ScenarioDirectory => Path.Combine(FixturesDirectory, "reaper-greatsword");

    private sealed record FixtureFile(string Screenshot, IReadOnlyList<ExpectedSlot> Slots);

    public sealed record ExpectedSlot(
        string Kind,
        int SkillId,
        string Name,
        int X,
        int Y,
        int Width,
        int Height,
        string IconFile,
        string IconSha256)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);

        public ScreenBounds ToBounds(double scaleFactor) => new(
            (int)Math.Round(X * scaleFactor),
            (int)Math.Round(Y * scaleFactor),
            Math.Max(1, (int)Math.Round(Width * scaleFactor)),
            Math.Max(1, (int)Math.Round(Height * scaleFactor)));
    }

    public sealed record BuildInput(
        string CharacterName,
        ArenaNetBuildTab BuildTab,
        ArenaNetEquipmentTab EquipmentTab,
        IReadOnlyList<ArenaNetItem> Items,
        ArenaNetProfession Profession);
}
