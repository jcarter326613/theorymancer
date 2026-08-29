using System.Net;
using Theorymancer.GuildWars2.Desktop.Authentication;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class GuildWars2ApiClientTests
{
    [Fact]
    public async Task GetByteArray_RefreshesAndRetriesOnceAfterUnauthorized()
    {
        var handler = new RetryHandler();
        using var httpClient = new HttpClient(handler);
        var authentication = new RecordingAuthenticationSession();
        var client = new GuildWars2ApiClient(httpClient, authentication);

        var bytes = await client.GetByteArrayAsync(
            new Uri("https://guild-wars-2-api.example.test/icons/asset.png"),
            CancellationToken.None);

        Assert.Equal("icon-bytes", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Equal([false, true], authentication.ForceRefreshValues);
        Assert.Equal(["DPoP token-1", "DPoP token-2"], handler.AuthorizationValues);
        Assert.Equal(["proof-token-1", "proof-token-2"], handler.DpopValues);
    }

    private sealed class RecordingAuthenticationSession : IAuthenticationSession
    {
        public List<bool> ForceRefreshValues { get; } = [];
        public bool IsSignedIn => true;

        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            ForceRefreshValues.Add(forceRefresh);
            return Task.FromResult($"token-{ForceRefreshValues.Count}");
        }

        public string CreateResourceProof(HttpMethod method, Uri uri, string accessToken) =>
            $"proof-{accessToken}";
    }

    private sealed class RetryHandler : HttpMessageHandler
    {
        public List<string> AuthorizationValues { get; } = [];
        public List<string> DpopValues { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationValues.Add(request.Headers.Authorization!.ToString());
            DpopValues.Add(request.Headers.GetValues("DPoP").Single());
            return Task.FromResult(AuthorizationValues.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("icon-bytes"),
                });
        }
    }
}
