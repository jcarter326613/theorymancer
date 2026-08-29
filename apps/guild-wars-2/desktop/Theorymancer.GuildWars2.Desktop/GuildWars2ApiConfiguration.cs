using System.IO;
using System.Text.Json;

namespace Theorymancer.GuildWars2.Desktop;

public sealed class GuildWars2ApiConfiguration
{
    private const string FileName = "appsettings.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private GuildWars2ApiConfiguration(Uri baseUri)
    {
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; }

    public static GuildWars2ApiConfiguration Load() => Load(AppContext.BaseDirectory);

    public static GuildWars2ApiConfiguration Load(string configurationDirectory)
    {
        var path = Path.Combine(configurationDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Desktop configuration file is missing: {path}");
        }

        Settings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Desktop configuration file is invalid: {path}", exception);
        }

        if (!Uri.TryCreate(settings?.GuildWars2ApiUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException($"Desktop configuration must contain an HTTPS guildWars2ApiUrl: {path}");
        }

        return new GuildWars2ApiConfiguration(new Uri($"{baseUri.AbsoluteUri.TrimEnd('/')}/"));
    }

    public Uri GetIconUri(string assetId) => new(BaseUri, $"icons/{assetId}.png");

    private sealed record Settings(string? GuildWars2ApiUrl);
}
