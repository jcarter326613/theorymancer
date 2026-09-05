using System.Net;
using Theorymancer.GuildWars2.Desktop.Authentication;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class ReferenceIconsAuthenticationTests
{
    [Fact]
    public async Task GetSkillPath_RequiresAuthenticationBeforeDownloading()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"theorymancer-icons-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var configurationDirectory = Path.Combine(directory, "configuration");
            var cacheDirectory = Path.Combine(directory, "cache");
            Directory.CreateDirectory(configurationDirectory);
            File.WriteAllText(
                Path.Combine(configurationDirectory, "appsettings.json"),
                """
                {
                  "mainApiUrl": "https://api.example.test",
                  "websiteUrl": "https://www.example.test",
                  "guildWars2ApiUrl": "https://guild-wars-2-api.example.test",
                  "authScope": "guild-wars-2.assets.read",
                  "authAudience": "theorymancer:games:guild-wars-2:test"
                }
                """);
            var manifestPath = Path.Combine(directory, "icons.manifest.json");
            File.WriteAllText(
                manifestPath,
                """
                {
                  "version": 2,
                  "assets": [{ "asset_id": "dusk-strike-asset" }],
                  "skills": [{ "skill_id": 29705, "name": "Dusk Strike", "icon_asset_id": "dusk-strike-asset" }]
                }
                """);
            var handler = new UnexpectedRequestHandler();
            using var httpClient = new HttpClient(handler);
            var apiClient = new GuildWars2ApiClient(httpClient, new SignedOutSession());
            var icons = new ReferenceIcons(
                apiClient,
                GuildWars2ApiConfiguration.Load(configurationDirectory),
                cacheDirectory,
                manifestPath);

            var exception = await Assert.ThrowsAsync<AuthenticationRequiredException>(
                () => icons.GetSkillPathAsync(29705, CancellationToken.None));

            Assert.Contains("Sign in", exception.Message, StringComparison.Ordinal);
            Assert.False(handler.WasCalled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SignedOutSession : IAuthenticationSession
    {
        public bool IsSignedIn => false;

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            throw new AuthenticationRequiredException();

        public string CreateResourceProof(HttpMethod method, Uri uri, string accessToken) =>
            throw new InvalidOperationException("No proof should be created while signed out.");
    }

    private sealed class UnexpectedRequestHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
