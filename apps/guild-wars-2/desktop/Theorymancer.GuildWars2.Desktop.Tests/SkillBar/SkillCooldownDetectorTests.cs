using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class SkillCooldownDetectorTests
{
    [Theory]
    [InlineData("reaper-greatsword-skills-used-1")]
    [InlineData("reaper-greatsword-skills-used-2")]
    public void Detect_ClassifiesEverySkillSlotAndMeasuresCooldownWipes(string fixtureName)
    {
        var fixture = LoadCooldownFixture(fixtureName);
        Assert.Equal(Enum.GetValues<SkillBarComponentKind>(), fixture.Slots.Select(slot => slot.ComponentKind));
        var frame = LoadFrame(Path.Combine(FixturesDirectory, fixtureName, fixture.Screenshot));
        var layout = CreateLayout(frame, fixture.Slots);
        var references = CreateReferences(fixture);
        var detection = new SkillCooldownDetector().Detect(
            frame,
            layout,
            references);

        Assert.Equal(frame.QpcTimestamp, detection.QpcTimestamp);
        Assert.Equal(fixture.Slots.Count, detection.Observations.Count);
        var stateErrors = new List<string>();
        foreach (var expected in fixture.Slots)
        {
            var observation = Assert.Single(detection.Observations, observation => observation.Kind == expected.ComponentKind);
            if (expected.State != observation.State)
            {
                stateErrors.Add(
                    $"{expected.ComponentKind}: expected {expected.State}, got {observation.State}; " +
                    $"confidence {observation.Confidence:F3}, visible wipe {observation.VisibleWipeFraction:F3}.");
                continue;
            }

            Assert.InRange(observation.Confidence, 0, 1);
            if (expected.State == SkillCooldownState.OnCooldown)
            {
                Assert.NotNull(observation.VisibleWipeFraction);
                Assert.InRange(observation.VisibleWipeFraction!.Value, 0, 1);
            }
            else
            {
                Assert.Null(observation.VisibleWipeFraction);
            }
        }

        Assert.True(
            stateErrors.Count == 0,
            string.Join(Environment.NewLine, stateErrors) + Environment.NewLine +
            "Measurements: " + string.Join(", ", detection.Observations.Select(observation =>
                $"{observation.Kind}={observation.VisibleWipeFraction:F3}")));
    }

    private static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "SkillBar", "Fixtures");

    private static SkillBarLayout CreateLayout(CapturedFrame frame, IReadOnlyList<CooldownSlot> slots) => new(
        slots.Select(slot => SkillBarComponent.FromPixelBounds(
                slot.ComponentKind,
                new ScreenBounds(slot.X, slot.Y, slot.Width, slot.Height),
                frame.Width,
                frame.Height,
                1))
            .ToList());

    private static IReadOnlyList<SkillCooldownReference> CreateReferences(CooldownFixture fixture)
    {
        var reference = JsonSerializer.Deserialize<ReferenceFixture>(
            File.ReadAllText(Path.Combine(FixturesDirectory, fixture.ReferenceFixture, "expectations.json")),
            JsonOptions)
            ?? throw new InvalidOperationException($"Reference fixture is invalid: {fixture.ReferenceFixture}");
        return reference.Slots.Select(slot =>
        {
            var expected = fixture.Slots.Single(expected => expected.ComponentKind == slot.ComponentKind);
            var iconPath = Path.Combine(FixturesDirectory, fixture.ReferenceFixture, "icons", slot.IconFile);
            var resolvedReference = SkillCooldownDetector.ResolveReference(new SkillCooldownReference(
                slot.ComponentKind,
                slot.SkillId,
                iconPath,
                new ScreenBounds(expected.X, expected.Y, expected.Width, expected.Height)));
            // A runtime match or template load would fail against this deliberately invalid path.
            return resolvedReference with { IconPath = $"startup-resolved-{slot.SkillId}.png" };
        }).ToList();
    }

    private static CooldownFixture LoadCooldownFixture(string fixtureName) =>
        JsonSerializer.Deserialize<CooldownFixture>(
            File.ReadAllText(Path.Combine(FixturesDirectory, fixtureName, "expectations.json")),
            JsonOptions)
        ?? throw new InvalidOperationException($"Cooldown fixture is invalid: {fixtureName}");

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

        return new CapturedFrame(123, bitmap.Width, bitmap.Height, stride, pixels);
    }

    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record CooldownFixture(
        string Screenshot,
        string ReferenceFixture,
        IReadOnlyList<CooldownSlot> Slots);

    private sealed record CooldownSlot(
        string Kind,
        int X,
        int Y,
        int Width,
        int Height,
        SkillCooldownState State)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);
    }

    private sealed record ReferenceFixture(IReadOnlyList<ReferenceSlot> Slots);

    private sealed record ReferenceSlot(string Kind, int SkillId, string IconFile)
    {
        public SkillBarComponentKind ComponentKind => Enum.Parse<SkillBarComponentKind>(Kind);
    }
}
