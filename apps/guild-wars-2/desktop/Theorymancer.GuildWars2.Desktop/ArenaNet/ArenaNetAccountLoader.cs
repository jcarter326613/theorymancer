namespace Theorymancer.GuildWars2.Desktop.ArenaNet;

public sealed record ArenaNetAccount(
    IReadOnlyList<string> Characters,
    string? SelectedCharacterName);

public sealed class ArenaNetAccountLoader
{
    private static readonly string[] RequiredPermissions = ["account", "characters", "builds"];
    private readonly IArenaNetApiClient _client;

    public ArenaNetAccountLoader(IArenaNetApiClient client)
    {
        _client = client;
    }

    public async Task<ArenaNetAccount> LoadAsync(
        string apiKey,
        string? rememberedCharacterName,
        CancellationToken cancellationToken)
    {
        var token = await _client.GetTokenInfoAsync(apiKey, cancellationToken);
        var missingPermissions = RequiredPermissions
            .Where(permission => !token.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (missingPermissions.Count > 0)
        {
            throw new InvalidOperationException($"The ArenaNet API key must include: {string.Join(", ", missingPermissions)}.");
        }

        var characters = await _client.GetCharactersAsync(apiKey, cancellationToken);
        var selectedCharacterName = characters.FirstOrDefault(character =>
            string.Equals(character, rememberedCharacterName, StringComparison.Ordinal));
        return new ArenaNetAccount(characters, selectedCharacterName);
    }
}
