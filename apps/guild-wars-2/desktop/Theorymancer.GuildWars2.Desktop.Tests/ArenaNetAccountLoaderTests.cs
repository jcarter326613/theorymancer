using Theorymancer.GuildWars2.Desktop.ArenaNet;

namespace Theorymancer.GuildWars2.Desktop.Tests;

public sealed class ArenaNetAccountLoaderTests
{
    [Fact]
    public async Task Load_RestoresTheRememberedCharacter()
    {
        var client = new FakeArenaNetApiClient(
            new ArenaNetTokenInfo("key-id", "Test key", ["account", "characters", "builds"]),
            ["Briar Rose", "Caithe"]);
        var loader = new ArenaNetAccountLoader(client);

        var account = await loader.LoadAsync("secret-key", "Caithe", CancellationToken.None);

        Assert.Equal(["Briar Rose", "Caithe"], account.Characters);
        Assert.Equal("Caithe", account.SelectedCharacterName);
        Assert.Equal("secret-key", client.TokenInfoApiKey);
        Assert.Equal("secret-key", client.CharactersApiKey);
    }

    [Fact]
    public async Task Load_RejectsKeysWithoutEveryRequiredPermission()
    {
        var client = new FakeArenaNetApiClient(
            new ArenaNetTokenInfo("key-id", "Test key", ["account", "characters"]),
            ["Caithe"]);
        var loader = new ArenaNetAccountLoader(client);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.LoadAsync("secret-key", "Caithe", CancellationToken.None));

        Assert.Equal("The ArenaNet API key must include: builds.", exception.Message);
        Assert.Null(client.CharactersApiKey);
    }

    [Fact]
    public async Task Load_DoesNotSelectAnotherCharacterWhenTheRememberedOneIsGone()
    {
        var client = new FakeArenaNetApiClient(
            new ArenaNetTokenInfo("key-id", "Test key", ["account", "characters", "builds"]),
            ["Briar Rose"]);
        var loader = new ArenaNetAccountLoader(client);

        var account = await loader.LoadAsync("secret-key", "Caithe", CancellationToken.None);

        Assert.Null(account.SelectedCharacterName);
    }

    private sealed class FakeArenaNetApiClient : IArenaNetApiClient
    {
        private readonly ArenaNetTokenInfo _tokenInfo;
        private readonly IReadOnlyList<string> _characters;

        public FakeArenaNetApiClient(ArenaNetTokenInfo tokenInfo, IReadOnlyList<string> characters)
        {
            _tokenInfo = tokenInfo;
            _characters = characters;
        }

        public string? TokenInfoApiKey { get; private set; }

        public string? CharactersApiKey { get; private set; }

        public Task<ArenaNetTokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken)
        {
            TokenInfoApiKey = apiKey;
            return Task.FromResult(_tokenInfo);
        }

        public Task<IReadOnlyList<string>> GetCharactersAsync(string apiKey, CancellationToken cancellationToken)
        {
            CharactersApiKey = apiKey;
            return Task.FromResult(_characters);
        }

        public Task<ArenaNetBuildTab> GetActiveBuildAsync(string apiKey, string characterName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ArenaNetEquipmentTab> GetActiveEquipmentAsync(string apiKey, string characterName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ArenaNetItem>> GetItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ArenaNetProfession> GetProfessionAsync(string profession, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
