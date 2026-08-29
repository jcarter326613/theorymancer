using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Theorymancer.GuildWars2.Desktop.Authentication;

public interface IDataProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}

public sealed class DpapiCurrentUserDataProtector : IDataProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Theorymancer.GuildWars2.Desktop.Credentials.v1");

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData) =>
        ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
}

public sealed class CredentialStoreCorruptedException : InvalidOperationException
{
    public CredentialStoreCorruptedException(string path, Exception? innerException = null)
        : base($"Desktop authentication data is corrupted and cannot be read: {path}", innerException)
    {
    }
}

public sealed record InstallationCredentials(byte[] PrivateKeyPkcs8, string? RefreshToken);

public sealed class InstallationCredentialStore
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly IDataProtector _protector;

    public InstallationCredentialStore(string path, IDataProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public static InstallationCredentialStore CreateDefault() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Theorymancer",
            "guild-wars-2",
            "authentication.v1.json"),
        new DpapiCurrentUserDataProtector());

    public InstallationCredentials LoadOrCreate()
    {
        if (File.Exists(_path))
        {
            return Load();
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var credentials = new InstallationCredentials(key.ExportPkcs8PrivateKey(), null);
        Save(credentials);
        return credentials;
    }

    public void Save(InstallationCredentials credentials)
    {
        ValidatePrivateKey(credentials.PrivateKeyPkcs8);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The authentication data path has no parent directory.");
        Directory.CreateDirectory(directory);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new ProtectedPayload(Convert.ToBase64String(credentials.PrivateKeyPkcs8), credentials.RefreshToken),
            JsonOptions);
        var document = new StoreDocument(CurrentVersion, Convert.ToBase64String(_protector.Protect(payload)));
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

    private InstallationCredentials Load()
    {
        try
        {
            var document = JsonSerializer.Deserialize<StoreDocument>(File.ReadAllBytes(_path), JsonOptions);
            if (document is not { Version: CurrentVersion } || string.IsNullOrWhiteSpace(document.ProtectedData))
            {
                throw new FormatException("Unsupported or incomplete authentication data.");
            }

            var protectedData = Convert.FromBase64String(document.ProtectedData);
            var payload = JsonSerializer.Deserialize<ProtectedPayload>(_protector.Unprotect(protectedData), JsonOptions);
            if (payload is null || string.IsNullOrWhiteSpace(payload.PrivateKeyPkcs8))
            {
                throw new FormatException("Authentication credentials are incomplete.");
            }

            var privateKey = Convert.FromBase64String(payload.PrivateKeyPkcs8);
            ValidatePrivateKey(privateKey);
            return new InstallationCredentials(privateKey, payload.RefreshToken);
        }
        catch (CredentialStoreCorruptedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or CryptographicException)
        {
            throw new CredentialStoreCorruptedException(_path, exception);
        }
    }

    private static void ValidatePrivateKey(byte[] privateKey)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
        if (bytesRead != privateKey.Length || key.KeySize != 256)
        {
            throw new CryptographicException("The installation key is not an ECDSA P-256 key.");
        }
    }

    private sealed record StoreDocument(int Version, string ProtectedData);
    private sealed record ProtectedPayload(string PrivateKeyPkcs8, string? RefreshToken);
}
