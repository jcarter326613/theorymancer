using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.ArenaNet;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class ReaperGreatswordSkillBarFixtureTests
{
    [Fact]
    public void Fixture_IsSelfConsistentWithTheVersionedManifestAndCanonicalIcons()
    {
        var fixture = LoadExpectations();
        Assert.Equal(Enum.GetValues<SkillBarComponentKind>(), fixture.Slots.Select(slot => slot.ComponentKind));
        Assert.Equal(fixture.Slots.Count, fixture.Slots.Select(slot => slot.SkillId).Distinct().Count());

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixturesDirectory, "icons.manifest.json")));
        var skillsById = manifest.RootElement
            .GetProperty("skills")
            .EnumerateArray()
            .ToDictionary(
                skill => skill.GetProperty("skill_id").GetInt32(),
                skill => skill.GetProperty("name").GetString());
        foreach (var slot in fixture.Slots)
        {
            Assert.Equal(slot.Name, skillsById.GetValueOrDefault(slot.SkillId));
            var iconPath = Path.Combine(ScenarioDirectory, "icons", slot.IconFile);
            Assert.True(File.Exists(iconPath), $"Canonical icon fixture is missing: {iconPath}");
            Assert.Equal(slot.IconSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(iconPath))));
        }
    }

    [Fact]
    public void BuildCandidates_ResolveTheTenExpectedReaperSkills()
    {
        var fixture = LoadExpectations();
        var input = JsonSerializer.Deserialize<BuildInput>(
            File.ReadAllText(Path.Combine(ScenarioDirectory, "build-input.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("The Reaper build fixture is invalid.");

        var candidates = BuildSkillCandidateResolver.Resolve(
            input.CharacterName,
            input.BuildTab.Build,
            input.EquipmentTab,
            input.Items,
            input.Profession);

        foreach (var expected in fixture.Slots)
        {
            Assert.Equal([expected.SkillId], candidates.GetSkillIds(expected.ComponentKind));
        }
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Detect_FindsExpectedSlotsFromPixelsWithoutOcrOrHotkeys(double scaleFactor)
    {
        var fixture = LoadExpectations();
        var frame = LoadFrame(Path.Combine(ScenarioDirectory, fixture.Screenshot));
        frame = ScaleFrame(frame, scaleFactor);

        var detection = SkillBarLayoutDetector.Detect(frame, []);

        Assert.True(detection.IsUsable, detection.Message);
        var layout = Assert.IsType<SkillBarLayout>(detection.Layout);
        foreach (var expected in fixture.Slots)
        {
            var component = Assert.Single(layout.Components, component => component.Kind == expected.ComponentKind);
            Assert.InRange(component.X + component.Width / 2, expected.CenterX - 0.05, expected.CenterX + 0.05);
            Assert.InRange(component.Y + component.Height / 2, expected.CenterY - 0.05, expected.CenterY + 0.05);
        }
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void MatchAt_RanksTheExpectedCanonicalIconFirstAtEachFixtureSlot(double scaleFactor)
    {
        var fixture = LoadExpectations();
        var frame = LoadFrame(Path.Combine(ScenarioDirectory, fixture.Screenshot));
        frame = ScaleFrame(frame, scaleFactor);

        foreach (var expected in fixture.Slots)
        {
            var bounds = expected.ToBounds(frame.Width, frame.Height);
            var matches = fixture.Slots
                .Select(candidate => IconTemplateMatcher.MatchAt(
                    frame,
                    bounds,
                    Path.Combine(ScenarioDirectory, "icons", candidate.IconFile),
                    candidate.Name,
                    candidate.SkillId))
                .ToList();
            var best = matches.MaxBy(match => match.Score);

            Assert.NotNull(best);
            Assert.True(
                best.SkillId == expected.SkillId,
                $"Expected {expected.Name} ({expected.SkillId}); scores: " +
                string.Join(", ", matches
                    .OrderByDescending(match => match.Score)
                    .Select(match => $"{match.Name}={match.Score:F3}")));
        }
    }

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static string ScenarioDirectory => Path.Combine(FixturesDirectory, "SkillBar", "reaper-greatsword");

    private static SkillBarFixture LoadExpectations()
    {
        var path = Path.Combine(ScenarioDirectory, "expectations.json");
        return JsonSerializer.Deserialize<SkillBarFixture>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Fixture is invalid: {path}");
    }

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

    private static CapturedFrame ScaleFrame(CapturedFrame source, double scaleFactor)
    {
        if (scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleFactor));
        }

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

    private sealed record SkillBarFixture(string Screenshot, IReadOnlyList<ExpectedSlot> Slots);

    private sealed record ExpectedSlot(
        string Kind,
        int SkillId,
        string Name,
        double X,
        double Y,
        double Width,
        double Height,
        string IconFile,
        string IconSha256)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);
        public double CenterX => X + Width / 2;
        public double CenterY => Y + Height / 2;

        public ScreenBounds ToBounds(int frameWidth, int frameHeight) => new(
            (int)Math.Round(X * frameWidth),
            (int)Math.Round(Y * frameHeight),
            Math.Max(1, (int)Math.Round(Width * frameWidth)),
            Math.Max(1, (int)Math.Round(Height * frameHeight)));
    }

    private sealed record BuildInput(
        string CharacterName,
        ArenaNetBuildTab BuildTab,
        ArenaNetEquipmentTab EquipmentTab,
        IReadOnlyList<ArenaNetItem> Items,
        ArenaNetProfession Profession);
}
