using System.Net.Http;

namespace Theorymancer.GuildWars2.Desktop.Authentication;

public enum AuthenticationState
{
    SignedOut,
    SigningIn,
    SignedIn,
}

public sealed class AuthenticationRequiredException : InvalidOperationException
{
    public AuthenticationRequiredException()
        : base("Sign in to Theorymancer before downloading Guild Wars 2 assets.")
    {
    }
}

public interface IAuthenticationSession
{
    bool IsSignedIn { get; }
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
    string CreateResourceProof(HttpMethod method, Uri uri, string accessToken);
}

public sealed class DesktopAuthenticationService : IAuthenticationSession, IDisposable
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(1);
    private readonly InstallationCredentialStore _store;
    private readonly DesktopAuthorizationFlow _authorizationFlow;
    private readonly AuthTokenClient _tokenClient;
    private readonly DpopProofFactory _proofFactory;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private InstallationCredentials _credentials;
    private AuthToken? _accessToken;

    public DesktopAuthenticationService(
        InstallationCredentialStore store,
        DesktopAuthorizationFlow authorizationFlow,
        AuthTokenClient tokenClient,
        DpopProofFactory proofFactory,
        InstallationCredentials credentials,
        Func<DateTimeOffset>? utcNow = null)
    {
        _store = store;
        _authorizationFlow = authorizationFlow;
        _tokenClient = tokenClient;
        _proofFactory = proofFactory;
        _credentials = credentials;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        State = string.IsNullOrWhiteSpace(credentials.RefreshToken)
            ? AuthenticationState.SignedOut
            : AuthenticationState.SignedIn;
    }

    public event Action? StateChanged;

    public AuthenticationState State { get; private set; }

    public bool IsSignedIn => State == AuthenticationState.SignedIn;

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        SetState(AuthenticationState.SigningIn);
        try
        {
            var code = await _authorizationFlow.AuthorizeAsync(cancellationToken);
            var token = await _tokenClient.ExchangeCodeAsync(
                code.Code,
                code.RedirectUri,
                code.CodeVerifier,
                cancellationToken);
            SetToken(token, requireRefreshToken: true);
            SetState(AuthenticationState.SignedIn);
        }
        catch
        {
            SetState(string.IsNullOrWhiteSpace(_credentials.RefreshToken)
                ? AuthenticationState.SignedOut
                : AuthenticationState.SignedIn);
            throw;
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(CancellationToken.None);
        try
        {
            if (!string.IsNullOrWhiteSpace(_credentials.RefreshToken))
            {
                try
                {
                    await _tokenClient.RevokeAsync(_credentials.RefreshToken, cancellationToken);
                }
                catch (Exception)
                {
                    // Local sign-out must succeed even when server revocation is unavailable.
                }
            }
            _accessToken = null;
            _credentials = _credentials with { RefreshToken = null };
            _store.Save(_credentials);
            SetState(AuthenticationState.SignedOut);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!IsSignedIn || string.IsNullOrWhiteSpace(_credentials.RefreshToken))
        {
            throw new AuthenticationRequiredException();
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsSignedIn || string.IsNullOrWhiteSpace(_credentials.RefreshToken))
            {
                throw new AuthenticationRequiredException();
            }

            if (!forceRefresh && _accessToken is { } current && current.ExpiresAt > _utcNow().Add(RefreshWindow))
            {
                return current.AccessToken;
            }

            try
            {
                var token = await _tokenClient.RefreshAsync(_credentials.RefreshToken, cancellationToken);
                SetToken(token, requireRefreshToken: false);
                return token.AccessToken;
            }
            catch (AuthTokenRequestException exception) when (
                exception.Error is "invalid_grant" or "invalid_dpop_proof")
            {
                _accessToken = null;
                _credentials = _credentials with { RefreshToken = null };
                _store.Save(_credentials);
                SetState(AuthenticationState.SignedOut);
                throw new AuthenticationRequiredException();
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public string CreateResourceProof(HttpMethod method, Uri uri, string accessToken) =>
        _proofFactory.Create(method, uri, accessToken);

    public void Dispose() => _tokenLock.Dispose();

    private void SetToken(AuthToken token, bool requireRefreshToken)
    {
        var refreshToken = token.RefreshToken ?? _credentials.RefreshToken;
        if (requireRefreshToken && string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Authentication token response did not contain a refresh token.");
        }

        _accessToken = token;
        if (!string.Equals(refreshToken, _credentials.RefreshToken, StringComparison.Ordinal))
        {
            _credentials = _credentials with { RefreshToken = refreshToken };
            _store.Save(_credentials);
        }
    }

    private void SetState(AuthenticationState state)
    {
        State = state;
        StateChanged?.Invoke();
    }
}
