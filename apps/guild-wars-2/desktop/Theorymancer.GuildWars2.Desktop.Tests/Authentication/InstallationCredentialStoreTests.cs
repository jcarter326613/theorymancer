using System.Text;
using Theorymancer.GuildWars2.Desktop.Authentication;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class InstallationCredentialStoreTests
{
    [Fact]
    public void LoadOrCreate_ProtectsAndPreservesTheInstallationKeyAndRefreshToken()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "authentication.v1.json");
            var protector = new ReversingProtector();
            var store = new InstallationCredentialStore(path, protector);

            var created = store.LoadOrCreate();
            store.Save(created with { RefreshToken = "refresh-secret" });
            var loaded = store.LoadOrCreate();

            Assert.Equal(created.PrivateKeyPkcs8, loaded.PrivateKeyPkcs8);
            Assert.Equal("refresh-secret", loaded.RefreshToken);
            Assert.True(protector.ProtectCalls >= 2);
            Assert.True(protector.UnprotectCalls >= 1);
            var file = File.ReadAllText(path);
            Assert.DoesNotContain("refresh-secret", file, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(created.PrivateKeyPkcs8), file, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadOrCreate_ReportsCorruptedProtectedDataExplicitly()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "authentication.v1.json");
            File.WriteAllText(path, "{\"version\":1,\"protectedData\":\"not-base64!\"}");
            var store = new InstallationCredentialStore(path, new ReversingProtector());

            var exception = Assert.Throws<CredentialStoreCorruptedException>(() => store.LoadOrCreate());

            Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"theorymancer-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class ReversingProtector : IDataProtector
    {
        public int ProtectCalls { get; private set; }
        public int UnprotectCalls { get; private set; }

        public byte[] Protect(byte[] plaintext)
        {
            ProtectCalls++;
            return Transform(plaintext);
        }

        public byte[] Unprotect(byte[] protectedData)
        {
            UnprotectCalls++;
            return Transform(protectedData);
        }

        private static byte[] Transform(byte[] value) => value.Reverse().Select(item => (byte)(item ^ 0x5a)).ToArray();
    }
}
