using System.Security.Cryptography;
using System.Text;

namespace Theorymancer.GuildWars2.Desktop.Authentication;

public sealed record PkceParameters(string Verifier, string Challenge)
{
    public static PkceParameters Create()
    {
        Span<byte> random = stackalloc byte[32];
        RandomNumberGenerator.Fill(random);
        var verifier = Base64Url.Encode(random);
        return new PkceParameters(verifier, CreateChallenge(verifier));
    }

    public static string CreateChallenge(string verifier) =>
        Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}
