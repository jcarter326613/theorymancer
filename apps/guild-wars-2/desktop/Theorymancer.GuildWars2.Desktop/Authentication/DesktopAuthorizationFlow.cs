using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Theorymancer.GuildWars2.Desktop.Authentication;

public interface ISystemBrowser
{
    void Open(Uri uri);
}

public sealed class SystemBrowser : ISystemBrowser
{
    public void Open(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
}

public sealed record AuthorizationCode(string Code, Uri RedirectUri, string CodeVerifier);

public sealed class DesktopAuthorizationFlow
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
    private readonly GuildWars2ApiConfiguration _configuration;
    private readonly ISystemBrowser _browser;
    private readonly DpopProofFactory _proofFactory;
    private readonly TimeSpan _timeout;

    public DesktopAuthorizationFlow(
        GuildWars2ApiConfiguration configuration,
        ISystemBrowser browser,
        DpopProofFactory proofFactory,
        TimeSpan? timeout = null)
    {
        _configuration = configuration;
        _browser = browser;
        _proofFactory = proofFactory;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<AuthorizationCode> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var pkce = PkceParameters.Create();
        var state = CreateRandomValue();
        var port = GetAvailableLoopbackPort();
        var listenerPrefix = $"http://127.0.0.1:{port}/";
        var redirectUri = new Uri($"{listenerPrefix}callback");
        using var listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        var authorizeUri = BuildAuthorizationUri(redirectUri, state, pkce.Challenge);
        _browser.Open(authorizeUri);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Sign-in timed out before the browser returned to the desktop app.");
            }

            if (!string.Equals(context.Request.Url?.AbsolutePath, "/callback", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                continue;
            }

            try
            {
                var query = ParseQuery(context.Request.Url?.Query);
                if (!query.TryGetValue("state", out var returnedState) || !FixedTimeEquals(state, returnedState))
                {
                    await WriteBrowserResponseAsync(context.Response, "This callback did not match the active sign-in request.");
                    continue;
                }

                if (query.TryGetValue("error", out var error))
                {
                    query.TryGetValue("error_description", out var description);
                    throw new InvalidOperationException($"Sign-in was rejected: {description ?? error}");
                }

                if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                {
                    await WriteBrowserResponseAsync(context.Response, "This callback did not include an authorization code.");
                    continue;
                }

                await WriteBrowserResponseAsync(context.Response, "Sign-in complete. You can return to Theorymancer.");
                return new AuthorizationCode(code, redirectUri, pkce.Verifier);
            }
            catch
            {
                await WriteBrowserResponseAsync(context.Response, "Sign-in could not be completed. Return to Theorymancer for details.");
                throw;
            }
        }
    }

    private Uri BuildAuthorizationUri(Uri redirectUri, string state, string challenge)
    {
        var fields = new Dictionary<string, string>
        {
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["installation_jwk"] = _proofFactory.GetPublicJwkJson(),
        };
        var query = string.Join("&", fields.Select(field =>
            $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}"));
        return new UriBuilder(_configuration.GetAuthorizationUri()) { Query = query }.Uri;
    }

    private static int GetAvailableLoopbackPort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static string CreateRandomValue()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.Encode(bytes);
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
            CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = field.Split('=', 2);
            values[Uri.UnescapeDataString(parts[0].Replace('+', ' '))] =
                Uri.UnescapeDataString((parts.Length == 2 ? parts[1] : string.Empty).Replace('+', ' '));
        }

        return values;
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, string message)
    {
        var bytes = Encoding.UTF8.GetBytes($"<!doctype html><html><body><p>{WebUtility.HtmlEncode(message)}</p></body></html>");
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
