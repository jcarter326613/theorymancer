using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Theorymancer.GuildWars2.Desktop.SkillBar;

public static class ReferenceIcons
{
    public const int NightfallSkillId = 29855;
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly GuildWars2ApiConfiguration ApiConfiguration = GuildWars2ApiConfiguration.Load();

    public static async Task<string> GetNightfallPathAsync(CancellationToken cancellationToken)
    {
        var manifest = LoadManifest();
        var icon = manifest.Skills.Single(icon => icon.SkillId == NightfallSkillId);
        var asset = manifest.Assets.Single(asset => asset.AssetId == icon.AssetId);
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Theorymancer",
            "guild-wars-2",
            "icon-cache");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{asset.AssetId}.png");
        if (File.Exists(path))
        {
            return path;
        }

        var bytes = await HttpClient.GetByteArrayAsync(ApiConfiguration.GetIconUri(asset.AssetId), cancellationToken);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
        return path;
    }

    private static IconManifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "guild-wars-2", "icons.manifest.json");
        var manifest = JsonSerializer.Deserialize<IconManifest>(File.ReadAllText(path), JsonOptions);
        return manifest is { Version: 2, Assets.Count: > 0, Skills.Count: > 0 }
            ? manifest
            : throw new InvalidOperationException("The packaged Guild Wars 2 icon manifest is missing or invalid.");
    }

    private sealed record IconManifest(
        int Version,
        IReadOnlyList<ManifestAsset> Assets,
        IReadOnlyList<ManifestSkill> Skills);

    private sealed record ManifestAsset(
        [property: JsonPropertyName("asset_id")] string AssetId);

    private sealed record ManifestSkill(
        [property: JsonPropertyName("skill_id")] int SkillId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("icon_asset_id")] string AssetId);
}
