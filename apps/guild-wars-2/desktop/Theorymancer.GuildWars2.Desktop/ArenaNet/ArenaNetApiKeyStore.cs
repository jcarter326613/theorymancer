using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Theorymancer.GuildWars2.Desktop.ArenaNet;

public sealed class ArenaNetApiKeyStore
{
    private const int CurrentVersion = 1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Theorymancer.GuildWars2.Desktop.ArenaNetApiKey.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public ArenaNetApiKeyStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Theorymancer",
            "guild-wars-2",
            "arenanet-api-key.v1.json");
    }

    public string? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<StoreDocument>(File.ReadAllBytes(_path), JsonOptions);
            if (document is not { Version: CurrentVersion } || string.IsNullOrWhiteSpace(document.ProtectedData))
            {
                throw new FormatException("The stored ArenaNet API key is incomplete.");
            }

            var protectedData = Convert.FromBase64String(document.ProtectedData);
            var apiKey = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser));
            return string.IsNullOrWhiteSpace(apiKey)
                ? throw new FormatException("The stored ArenaNet API key is empty.")
                : apiKey;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException)
        {
            throw new InvalidOperationException("The stored ArenaNet API key is corrupted and cannot be read.", exception);
        }
    }

    public void Save(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("An ArenaNet API key is required.", nameof(apiKey));
        }

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The ArenaNet API key path has no parent directory.");
        Directory.CreateDirectory(directory);
        var protectedData = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey.Trim()), Entropy, DataProtectionScope.CurrentUser);
        var document = new StoreDocument(CurrentVersion, Convert.ToBase64String(protectedData));
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private sealed record StoreDocument(int Version, string ProtectedData);
}
