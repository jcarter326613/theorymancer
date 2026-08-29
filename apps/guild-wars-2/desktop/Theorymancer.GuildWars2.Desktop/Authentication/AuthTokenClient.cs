using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Theorymancer.GuildWars2.Desktop.Authentication;

public sealed record AuthToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

public sealed class AuthTokenRequestException : InvalidOperationException
{
    public AuthTokenRequestException(int statusCode, string? error)
        : base($"Authentication token request failed ({statusCode}): {error ?? "unknown_error"}")
    {
        StatusCode = statusCode;
        Error = error;
    }

    public int StatusCode { get; }
    public string? Error { get; }
}

public sealed class AuthTokenClient
{
    public const string ClientId = "theorymancer-guild-wars-2-desktop";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly GuildWars2ApiConfiguration _configuration;
    private readonly DpopProofFactory _proofFactory;
    private readonly Func<DateTimeOffset> _utcNow;

    public AuthTokenClient(
        HttpClient httpClient,
        GuildWars2ApiConfiguration configuration,
        DpopProofFactory proofFactory,
        Func<DateTimeOffset>? utcNow = null)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _proofFactory = proofFactory;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<AuthToken> ExchangeCodeAsync(
        string code,
        Uri redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken) =>
        RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = redirectUri.AbsoluteUri,
                ["code_verifier"] = codeVerifier,
            },
            null,
            cancellationToken);

    public Task<AuthToken> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken,
            },
            null,
            cancellationToken);

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var revokeUri = _configuration.GetRevocationUri();
        using var request = new HttpRequestMessage(HttpMethod.Post, revokeUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = refreshToken,
            }),
        };
        request.Headers.TryAddWithoutValidation("DPoP", _proofFactory.Create(HttpMethod.Post, revokeUri, null));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<AuthToken> RequestTokenAsync(
        IReadOnlyDictionary<string, string> fields,
        string? athValue,
        CancellationToken cancellationToken)
    {
        var tokenUri = _configuration.GetTokenUri();
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri)
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.TryAddWithoutValidation("DPoP", _proofFactory.Create(HttpMethod.Post, tokenUri, athValue));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string? error = null;
            try
            {
                error = JsonSerializer.Deserialize<ErrorResponse>(body, JsonOptions)?.Error;
            }
            catch (JsonException)
            {
            }
            throw new AuthTokenRequestException((int)response.StatusCode, error);
        }

        TokenResponse? token;
        try
        {
            token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Authentication token response was invalid.", exception);
        }

        if (token is null ||
            string.IsNullOrWhiteSpace(token.AccessToken) ||
            token.ExpiresIn <= 0 ||
            !string.Equals(token.TokenType, "DPoP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Authentication token response did not contain a valid DPoP access token.");
        }

        return new AuthToken(token.AccessToken, token.RefreshToken, _utcNow().AddSeconds(token.ExpiresIn));
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType);

    private sealed record ErrorResponse(
        [property: JsonPropertyName("error")] string? Error);
}
