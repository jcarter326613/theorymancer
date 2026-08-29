using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
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
        var icon = LoadManifest().Icons.Single(icon => icon.SkillId == NightfallSkillId);
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Theorymancer",
            "guild-wars-2",
            "icon-cache");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{icon.Sha256}.png");
        if (File.Exists(path) && HashMatches(await File.ReadAllBytesAsync(path, cancellationToken), icon.Sha256))
        {
            return path;
        }

        var bytes = await HttpClient.GetByteArrayAsync(ApiConfiguration.GetIconUri(icon.Sha256), cancellationToken);
        if (!HashMatches(bytes, icon.Sha256))
        {
            throw new InvalidOperationException($"The downloaded {icon.Name} icon does not match the manifest hash.");
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
        return path;
    }

    private static IconManifest LoadManifest()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "guild-wars-2", "icons.manifest.json");
        var manifest = JsonSerializer.Deserialize<IconManifest>(File.ReadAllText(path), JsonOptions);
        return manifest is { Version: 1, Icons.Count: > 0 }
            ? manifest
            : throw new InvalidOperationException("The deployed Guild Wars 2 icon manifest is missing or invalid.");
    }

    private static bool HashMatches(byte[] bytes, string expectedHash) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedHash, StringComparison.OrdinalIgnoreCase);

    private sealed record IconManifest(int Version, IReadOnlyList<ManifestIcon> Icons);

    private sealed record ManifestIcon(
        [property: JsonPropertyName("skill_id")] int SkillId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("sha256")] string Sha256);
}
