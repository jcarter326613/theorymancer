using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Theorymancer.GuildWars2.Desktop.ArenaNet;

public interface IArenaNetApiClient
{
    Task<ArenaNetTokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetCharactersAsync(string apiKey, CancellationToken cancellationToken);
    Task<ArenaNetBuildTab> GetActiveBuildAsync(string apiKey, string characterName, CancellationToken cancellationToken);
    Task<ArenaNetEquipmentTab> GetActiveEquipmentAsync(string apiKey, string characterName, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArenaNetItem>> GetItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken);
    Task<ArenaNetProfession> GetProfessionAsync(string profession, CancellationToken cancellationToken);
}

public sealed class ArenaNetApiException : InvalidOperationException
{
    public ArenaNetApiException(HttpStatusCode statusCode)
        : base($"ArenaNet API request failed with {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class ArenaNetApiClient : IArenaNetApiClient
{
    private static readonly Uri BaseUri = new("https://api.guildwars2.com/v2/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public ArenaNetApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ArenaNetTokenInfo> GetTokenInfoAsync(string apiKey, CancellationToken cancellationToken) =>
        GetAsync<ArenaNetTokenInfo>("tokeninfo", apiKey, cancellationToken);

    public async Task<IReadOnlyList<string>> GetCharactersAsync(string apiKey, CancellationToken cancellationToken)
    {
        var characters = await GetAsync<string[]>("characters", apiKey, cancellationToken);
        return characters.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<ArenaNetBuildTab> GetActiveBuildAsync(string apiKey, string characterName, CancellationToken cancellationToken) =>
        GetAsync<ArenaNetBuildTab>($"characters/{Uri.EscapeDataString(characterName)}/buildtabs/active", apiKey, cancellationToken);

    public Task<ArenaNetEquipmentTab> GetActiveEquipmentAsync(string apiKey, string characterName, CancellationToken cancellationToken) =>
        GetAsync<ArenaNetEquipmentTab>($"characters/{Uri.EscapeDataString(characterName)}/equipmenttabs/active", apiKey, cancellationToken);

    public async Task<IReadOnlyList<ArenaNetItem>> GetItemsAsync(IReadOnlyList<int> itemIds, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        return await GetAsync<ArenaNetItem[]>(
            $"items?ids={string.Join(',', itemIds.Distinct().Order())}",
            null,
            cancellationToken);
    }

    public Task<ArenaNetProfession> GetProfessionAsync(string profession, CancellationToken cancellationToken) =>
        GetAsync<ArenaNetProfession>($"professions/{Uri.EscapeDataString(profession)}", null, cancellationToken);

    private async Task<T> GetAsync<T>(string relativePath, string? apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, relativePath));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ArenaNetApiException(response.StatusCode);
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(content, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("ArenaNet returned an empty response.");
    }
}

public sealed record ArenaNetTokenInfo(string Id, string Name, IReadOnlyList<string> Permissions);

public sealed record ArenaNetBuildTab(int Tab, bool IsActive, ArenaNetBuild Build);

public sealed record ArenaNetBuild(
    string Name,
    string Profession,
    IReadOnlyList<ArenaNetSpecialization?> Specializations,
    ArenaNetEquippedSkills Skills);

public sealed record ArenaNetSpecialization(int? Id, IReadOnlyList<int?> Traits);

public sealed record ArenaNetEquippedSkills(int? Heal, IReadOnlyList<int?> Utilities, int? Elite);

public sealed record ArenaNetEquipmentTab(int Tab, bool IsActive, IReadOnlyList<ArenaNetEquipment> Equipment);

public sealed record ArenaNetEquipment(int Id, string Slot);

public sealed record ArenaNetItem(int Id, ArenaNetItemDetails? Details);

public sealed record ArenaNetItemDetails(string? Type);

public sealed record ArenaNetProfession(
    string Id,
    IReadOnlyDictionary<string, ArenaNetWeapon> Weapons);

public sealed record ArenaNetWeapon(
    IReadOnlyList<string> Flags,
    int? Specialization,
    IReadOnlyList<ArenaNetWeaponSkill> Skills);

public sealed record ArenaNetWeaponSkill(int Id, string Slot, string? Offhand);
