using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Theorymancer.GuildWars2.Desktop.Authentication;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class DpopProofFactoryTests
{
    [Fact]
    public void Create_ProducesVerifiableEs256ProofWithResourceClaimsAndRawSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var factory = new DpopProofFactory(
            key.ExportPkcs8PrivateKey(),
            () => DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));

        var proof = factory.Create(
            HttpMethod.Get,
            new Uri("https://guild-wars-2.example.test/icons/asset.png?ignored=true"),
            "access-token");

        var segments = proof.Split('.');
        Assert.Equal(3, segments.Length);
        using var header = JsonDocument.Parse(Decode(segments[0]));
        using var claims = JsonDocument.Parse(Decode(segments[1]));
        Assert.Equal("dpop+jwt", header.RootElement.GetProperty("typ").GetString());
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("EC", header.RootElement.GetProperty("jwk").GetProperty("kty").GetString());
        Assert.Equal("P-256", header.RootElement.GetProperty("jwk").GetProperty("crv").GetString());
        Assert.Equal("GET", claims.RootElement.GetProperty("htm").GetString());
        Assert.Equal(
            "https://guild-wars-2.example.test/icons/asset.png",
            claims.RootElement.GetProperty("htu").GetString());
        Assert.Equal(1_700_000_000, claims.RootElement.GetProperty("iat").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(claims.RootElement.GetProperty("jti").GetString()));
        Assert.Equal(
            Encode(SHA256.HashData(Encoding.ASCII.GetBytes("access-token"))),
            claims.RootElement.GetProperty("ath").GetString());

        var signature = Decode(segments[2]);
        Assert.Equal(64, signature.Length);
        Assert.True(key.VerifyData(
            Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    internal static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    internal static string Encode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
