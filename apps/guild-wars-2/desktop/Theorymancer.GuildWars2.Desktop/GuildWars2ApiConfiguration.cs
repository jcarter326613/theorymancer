using System.IO;
using System.Text.Json;

namespace Theorymancer.GuildWars2.Desktop;

public sealed class GuildWars2ApiConfiguration
{
    private const string FileName = "appsettings.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private GuildWars2ApiConfiguration(
        Uri mainApiUri,
        Uri websiteUri,
        Uri guildWars2ApiUri,
        string authScope,
        string authAudience)
    {
        MainApiUri = mainApiUri;
        WebsiteUri = websiteUri;
        BaseUri = guildWars2ApiUri;
        AuthScope = authScope;
        AuthAudience = authAudience;
    }

    public Uri MainApiUri { get; }
    public Uri WebsiteUri { get; }
    public Uri BaseUri { get; }
    public string AuthScope { get; }
    public string AuthAudience { get; }

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

        var mainApiUri = ReadHttpsUri(settings?.MainApiUrl, "mainApiUrl", path);
        var websiteUri = ReadHttpsUri(settings?.WebsiteUrl, "websiteUrl", path);
        var guildWars2ApiUri = ReadHttpsUri(settings?.GuildWars2ApiUrl, "guildWars2ApiUrl", path);
        if (string.IsNullOrWhiteSpace(settings?.AuthScope))
        {
            throw new InvalidOperationException($"Desktop configuration must contain authScope: {path}");
        }

        if (string.IsNullOrWhiteSpace(settings.AuthAudience))
        {
            throw new InvalidOperationException($"Desktop configuration must contain authAudience: {path}");
        }

        return new GuildWars2ApiConfiguration(
            EnsureTrailingSlash(mainApiUri),
            EnsureTrailingSlash(websiteUri),
            EnsureTrailingSlash(guildWars2ApiUri),
            settings.AuthScope,
            settings.AuthAudience);
    }

    public Uri GetIconUri(string assetId) => new(BaseUri, $"icons/{assetId}.png");

    public Uri GetTokenUri() => new(MainApiUri, "v1/auth/token");

    public Uri GetRevocationUri() => new(MainApiUri, "v1/auth/revoke");

    public Uri GetAuthorizationUri() => new(WebsiteUri, "desktop/authorize");

    private static Uri ReadHttpsUri(string? value, string settingName, string path)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"Desktop configuration must contain an HTTPS {settingName} without a query or fragment: {path}");
        }

        return uri;
    }

    private static Uri EnsureTrailingSlash(Uri uri) => new($"{uri.AbsoluteUri.TrimEnd('/')}/");

    private sealed record Settings(
        string? MainApiUrl,
        string? WebsiteUrl,
        string? GuildWars2ApiUrl,
        string? AuthScope,
        string? AuthAudience);
}
