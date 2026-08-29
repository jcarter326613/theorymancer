using System.Net.Http;
using System.Windows;
using Theorymancer.GuildWars2.Desktop.Authentication;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop;

public partial class App : Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private HttpClient? _mainApiHttpClient;
    private HttpClient? _guildWars2HttpClient;
    private DpopProofFactory? _proofFactory;
    private DesktopAuthenticationService? _authentication;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var configuration = GuildWars2ApiConfiguration.Load();
            var store = InstallationCredentialStore.CreateDefault();
            var credentials = store.LoadOrCreate();
            _proofFactory = new DpopProofFactory(credentials.PrivateKeyPkcs8);
            _mainApiHttpClient = new HttpClient();
            var tokenClient = new AuthTokenClient(_mainApiHttpClient, configuration, _proofFactory);
            var authorizationFlow = new DesktopAuthorizationFlow(configuration, new SystemBrowser(), _proofFactory);
            _authentication = new DesktopAuthenticationService(
                store,
                authorizationFlow,
                tokenClient,
                _proofFactory,
                credentials);
            _guildWars2HttpClient = new HttpClient();
            var guildWars2ApiClient = new GuildWars2ApiClient(_guildWars2HttpClient, _authentication);
            var referenceIcons = new ReferenceIcons(guildWars2ApiClient, configuration);

            MainWindow = new MainWindow(_authentication, referenceIcons, _shutdown.Token);
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Theorymancer could not start: {exception.Message}",
                "Theorymancer collector",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        _authentication?.Dispose();
        _proofFactory?.Dispose();
        _guildWars2HttpClient?.Dispose();
        _mainApiHttpClient?.Dispose();
        _shutdown.Dispose();
        base.OnExit(e);
    }
}
