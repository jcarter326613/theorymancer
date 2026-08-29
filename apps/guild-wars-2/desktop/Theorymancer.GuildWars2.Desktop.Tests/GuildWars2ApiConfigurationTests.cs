using Theorymancer.GuildWars2.Desktop;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class GuildWars2ApiConfigurationTests
{
    [Fact]
    public void Load_ReadsTheApiUrlAndBuildsIconUrls()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"theorymancer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                """
                {
                  "mainApiUrl": "https://api.example.test/root",
                  "websiteUrl": "https://www.example.test",
                  "guildWars2ApiUrl": "https://guild-wars-2-api.example.test",
                  "authScope": "guild-wars-2.assets.read",
                  "authAudience": "theorymancer:games:guild-wars-2:test"
                }
                """);

            var configuration = GuildWars2ApiConfiguration.Load(directory);

            Assert.Equal("https://api.example.test/root/v1/auth/token", configuration.GetTokenUri().AbsoluteUri);
            Assert.Equal("https://www.example.test/desktop/authorize", configuration.GetAuthorizationUri().AbsoluteUri);
            Assert.Equal("guild-wars-2.assets.read", configuration.AuthScope);
            Assert.Equal("theorymancer:games:guild-wars-2:test", configuration.AuthAudience);
            Assert.Equal(
                "https://guild-wars-2-api.example.test/icons/a784986f-696d-4c63-8f46-4cc53efc9b47.png",
                configuration.GetIconUri("a784986f-696d-4c63-8f46-4cc53efc9b47").AbsoluteUri);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_RejectsANonHttpsApiUrl()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"theorymancer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                """
                {
                  "mainApiUrl": "https://api.example.test",
                  "websiteUrl": "https://www.example.test",
                  "guildWars2ApiUrl": "http://guild-wars-2-api.example.test",
                  "authScope": "guild-wars-2.assets.read",
                  "authAudience": "theorymancer:games:guild-wars-2:test"
                }
                """);

            Assert.Throws<InvalidOperationException>(() => GuildWars2ApiConfiguration.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_RejectsAUrlWithAQuery()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"theorymancer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "appsettings.json"),
                """
                {
                  "mainApiUrl": "https://api.example.test?environment=test",
                  "websiteUrl": "https://www.example.test",
                  "guildWars2ApiUrl": "https://guild-wars-2-api.example.test",
                  "authScope": "guild-wars-2.assets.read",
                  "authAudience": "theorymancer:games:guild-wars-2:test"
                }
                """);

            Assert.Throws<InvalidOperationException>(() => GuildWars2ApiConfiguration.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
