using Theorymancer.GuildWars2.Desktop.Authentication;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class PkceTests
{
    [Fact]
    public void CreateChallenge_MatchesRfc7636Vector()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        var challenge = PkceParameters.CreateChallenge(verifier);

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
    }
}
