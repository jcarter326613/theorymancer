using System.Security.Cryptography;
using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class ReaperGreatswordFixtureContractTests
{
    [Fact]
    public void Fixture_MatchesTheVersionedManifestAndCanonicalIcons()
    {
        var fixture = ReaperGreatswordFixture.Load();

        Assert.Equal(Enum.GetValues<SkillBarComponentKind>(), fixture.Slots.Select(slot => slot.ComponentKind));
        Assert.Equal(fixture.Slots.Count, fixture.Slots.Select(slot => slot.SkillId).Distinct().Count());
        var screenshot = fixture.LoadScreenshot();
        foreach (var slot in fixture.Slots)
        {
            Assert.InRange(slot.X, 0, screenshot.Width - 1);
            Assert.InRange(slot.Y, 0, screenshot.Height - 1);
            Assert.InRange(slot.Width, 1, screenshot.Width - slot.X);
            Assert.InRange(slot.Height, 1, screenshot.Height - slot.Y);
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(fixture.ManifestPath));
        var skillsById = manifest.RootElement.GetProperty("skills").EnumerateArray().ToDictionary(
            skill => skill.GetProperty("skill_id").GetInt32(),
            skill => skill.GetProperty("name").GetString());
        foreach (var slot in fixture.Slots)
        {
            var iconPath = fixture.GetIconPath(slot);
            Assert.Equal(slot.Name, skillsById.GetValueOrDefault(slot.SkillId));
            Assert.True(File.Exists(iconPath), $"Canonical icon fixture is missing: {iconPath}");
            Assert.Equal(slot.IconSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(iconPath))));
        }
    }
}
