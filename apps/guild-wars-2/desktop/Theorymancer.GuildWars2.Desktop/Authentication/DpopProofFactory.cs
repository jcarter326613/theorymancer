using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Theorymancer.GuildWars2.Desktop.Authentication;

public sealed class DpopProofFactory : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ECDsa _key;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _keyLock = new();

    public DpopProofFactory(byte[] privateKeyPkcs8, Func<DateTimeOffset>? utcNow = null)
    {
        _key = ECDsa.Create();
        _key.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public string Create(HttpMethod method, Uri uri, string? accessToken = null)
    {
        var header = new
        {
            typ = "dpop+jwt",
            alg = "ES256",
            jwk = GetPublicJwk(),
        };
        var claims = new Dictionary<string, object>
        {
            ["htm"] = method.Method.ToUpperInvariant(),
            ["htu"] = uri.GetLeftPart(UriPartial.Path),
            ["iat"] = _utcNow().ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("D"),
        };
        if (accessToken is not null)
        {
            claims["ath"] = Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        }

        var encodedHeader = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions));
        var encodedClaims = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(claims, JsonOptions));
        var signingInput = Encoding.ASCII.GetBytes($"{encodedHeader}.{encodedClaims}");
        byte[] signature;
        lock (_keyLock)
        {
            signature = _key.SignData(
                signingInput,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        return $"{encodedHeader}.{encodedClaims}.{Base64Url.Encode(signature)}";
    }

    public string GetPublicJwkJson() => JsonSerializer.Serialize(GetPublicJwk(), JsonOptions);

    public void Dispose() => _key.Dispose();

    private object GetPublicJwk()
    {
        ECParameters publicKey;
        lock (_keyLock)
        {
            publicKey = _key.ExportParameters(includePrivateParameters: false);
        }

        return new
        {
            kty = "EC",
            crv = "P-256",
            x = Base64Url.Encode(publicKey.Q.X!),
            y = Base64Url.Encode(publicKey.Q.Y!),
        };
    }
}
