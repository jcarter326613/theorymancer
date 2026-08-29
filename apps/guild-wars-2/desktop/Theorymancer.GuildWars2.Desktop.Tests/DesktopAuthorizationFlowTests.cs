using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.Authentication;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class DesktopAuthorizationFlowTests
{
    [Fact]
    public async Task Authorize_BindsLoopbackAndSendsPkceStateAndPublicInstallationKey()
    {
        var directory = CreateConfigurationDirectory();
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var proofs = new DpopProofFactory(key.ExportPkcs8PrivateKey());
            var browser = new CallbackBrowser(useReturnedState: true);
            var flow = new DesktopAuthorizationFlow(
                GuildWars2ApiConfiguration.Load(directory),
                browser,
                proofs,
                TimeSpan.FromSeconds(5));

            var result = await flow.AuthorizeAsync(CancellationToken.None);
            await browser.CallbackTask;

            Assert.Equal("authorization-code", result.Code);
            Assert.Matches(@"^http://127\.0\.0\.1:\d+/callback$", result.RedirectUri.AbsoluteUri);
            Assert.Equal(
                PkceParameters.CreateChallenge(result.CodeVerifier),
                browser.Parameters["code_challenge"]);
            Assert.Equal("S256", browser.Parameters["code_challenge_method"]);
            using var publicJwk = JsonDocument.Parse(browser.Parameters["installation_jwk"]);
            Assert.Equal("EC", publicJwk.RootElement.GetProperty("kty").GetString());
            Assert.Equal("P-256", publicJwk.RootElement.GetProperty("crv").GetString());
            Assert.False(publicJwk.RootElement.TryGetProperty("d", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Authorize_IgnoresAMismatchedStateUntilTimeout()
    {
        var directory = CreateConfigurationDirectory();
        try
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var proofs = new DpopProofFactory(key.ExportPkcs8PrivateKey());
            var browser = new CallbackBrowser(useReturnedState: false);
            var flow = new DesktopAuthorizationFlow(
                GuildWars2ApiConfiguration.Load(directory),
                browser,
                proofs,
                TimeSpan.FromMilliseconds(100));

            var exception = await Assert.ThrowsAsync<TimeoutException>(
                () => flow.AuthorizeAsync(CancellationToken.None));
            await browser.CallbackTask;

            Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateConfigurationDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"theorymancer-browser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "appsettings.json"),
            """
            {
              "mainApiUrl": "https://api.example.test",
              "websiteUrl": "https://www.example.test",
              "guildWars2ApiUrl": "https://guild-wars-2-api.example.test",
              "authScope": "guild-wars-2.assets.read",
              "authAudience": "theorymancer:games:guild-wars-2:test"
            }
            """);
        return directory;
    }

    private sealed class CallbackBrowser(bool useReturnedState) : ISystemBrowser
    {
        public Dictionary<string, string> Parameters { get; private set; } = [];
        public Task CallbackTask { get; private set; } = Task.CompletedTask;

        public void Open(Uri uri)
        {
            Parameters = ParseQuery(uri.Query);
            var state = useReturnedState ? Parameters["state"] : "wrong-state";
            var callback = $"{Parameters["redirect_uri"]}?code=authorization-code&state={Uri.EscapeDataString(state)}";
            CallbackTask = Task.Run(async () =>
            {
                using var client = new HttpClient();
                using var response = await client.GetAsync(callback);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            });
        }

        private static Dictionary<string, string> ParseQuery(string query) =>
            query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(field => field.Split('=', 2))
                .ToDictionary(
                    parts => Uri.UnescapeDataString(parts[0]),
                    parts => Uri.UnescapeDataString(parts[1]),
                    StringComparer.Ordinal);
    }
}
