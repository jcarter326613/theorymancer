using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Theorymancer.GuildWars2.Desktop.Authentication;

namespace Theorymancer.GuildWars2.Desktop;

public sealed class GuildWars2ApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationSession _authentication;

    public GuildWars2ApiClient(HttpClient httpClient, IAuthenticationSession authentication)
    {
        _httpClient = httpClient;
        _authentication = authentication;
    }

    public async Task<byte[]> GetByteArrayAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = await _authentication.GetAccessTokenAsync(attempt > 0, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", accessToken);
            request.Headers.TryAddWithoutValidation(
                "DPoP",
                _authentication.CreateResourceProof(HttpMethod.Get, uri, accessToken));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        throw new InvalidOperationException("The authenticated Guild Wars 2 asset request could not be completed.");
    }
}
