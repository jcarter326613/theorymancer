using System.Net;
using System.Net.Http;
using System.Text;
using Theorymancer.GuildWars2.Desktop.ArenaNet;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class ArenaNetApiClientTests
{
    [Fact]
    public async Task GetTokenInfo_SendsTheUserKeyOnlyToArenaNet()
    {
        var handler = new RecordingHandler("""{"id":"key-id","name":"Test key","permissions":["account","characters","builds"]}""");
        using var httpClient = new HttpClient(handler);
        var client = new ArenaNetApiClient(httpClient);

        var token = await client.GetTokenInfoAsync("secret-key", CancellationToken.None);

        Assert.Equal("key-id", token.Id);
        Assert.Equal("https://api.guildwars2.com/v2/tokeninfo", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("secret-key", handler.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetItems_UsesThePublicEndpointWithoutTheUserKey()
    {
        var handler = new RecordingHandler("""[{"id":1,"details":{"type":"Axe"}}]""");
        using var httpClient = new HttpClient(handler);
        var client = new ArenaNetApiClient(httpClient);

        var items = await client.GetItemsAsync([1], CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("https://api.guildwars2.com/v2/items?ids=1", handler.RequestUri?.AbsoluteUri);
        Assert.Null(handler.Authorization);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _json;

        public RecordingHandler(string json)
        {
            _json = json;
        }

        public Uri? RequestUri { get; private set; }

        public System.Net.Http.Headers.AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
