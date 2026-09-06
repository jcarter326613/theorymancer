using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.Authentication;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class AuthTokenClientTests
{
    [Fact]
    public async Task ExchangeCode_SendsFormAndDpopProofToTheMainApi()
    {
        var directory = CreateConfigurationDirectory();
        try
        {
            var configuration = GuildWars2ApiConfiguration.Load(directory);
            var handler = new RecordingHandler();
            using var httpClient = new HttpClient(handler);
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var proofs = new DpopProofFactory(
                key.ExportPkcs8PrivateKey(),
                () => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
            var client = new AuthTokenClient(
                httpClient,
                configuration,
                proofs,
                () => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

            var token = await client.ExchangeCodeAsync(
                "authorization-code",
                new Uri("http://127.0.0.1:54321/callback/"),
                "code-verifier",
                CancellationToken.None);

            Assert.Equal("access-token", token.AccessToken);
            Assert.Equal("refresh-token", token.RefreshToken);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_300), token.ExpiresAt);
            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal("https://api.example.test/v1/auth/token", handler.Uri?.AbsoluteUri);
            Assert.NotNull(handler.Dpop);
            Assert.Contains("grant_type=authorization_code", handler.Content, StringComparison.Ordinal);
            Assert.Contains("client_id=theorymancer-guild-wars-2-desktop", handler.Content, StringComparison.Ordinal);
            Assert.Contains("code=authorization-code", handler.Content, StringComparison.Ordinal);
            Assert.Contains("code_verifier=code-verifier", handler.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("scope=", handler.Content, StringComparison.Ordinal);

            var proofSegments = handler.Dpop!.Split('.');
            using var claims = JsonDocument.Parse(DpopProofFactoryTests.Decode(proofSegments[1]));
            Assert.Equal("POST", claims.RootElement.GetProperty("htm").GetString());
            Assert.Equal("https://api.example.test/v1/auth/token", claims.RootElement.GetProperty("htu").GetString());
            Assert.False(claims.RootElement.TryGetProperty("ath", out _));

            await client.RefreshAsync("stored-refresh-token", CancellationToken.None);

            Assert.Contains("grant_type=refresh_token", handler.Content, StringComparison.Ordinal);
            Assert.Contains("refresh_token=stored-refresh-token", handler.Content, StringComparison.Ordinal);
            var refreshProofSegments = handler.Dpop!.Split('.');
            using var refreshClaims = JsonDocument.Parse(DpopProofFactoryTests.Decode(refreshProofSegments[1]));
            Assert.False(refreshClaims.RootElement.TryGetProperty("ath", out _));

            await client.RevokeAsync("stored-refresh-token", CancellationToken.None);

            Assert.Equal("https://api.example.test/v1/auth/revoke", handler.Uri?.AbsoluteUri);
            Assert.Contains("token=stored-refresh-token", handler.Content, StringComparison.Ordinal);
            var revocationProofSegments = handler.Dpop!.Split('.');
            using var revocationClaims = JsonDocument.Parse(DpopProofFactoryTests.Decode(revocationProofSegments[1]));
            Assert.False(revocationClaims.RootElement.TryGetProperty("ath", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateConfigurationDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"theorymancer-token-{Guid.NewGuid():N}");
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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Dpop { get; private set; }
        public string Content { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Dpop = request.Headers.GetValues("DPoP").Single();
            Content = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"access_token\":\"access-token\",\"refresh_token\":\"refresh-token\",\"expires_in\":300,\"token_type\":\"DPoP\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
