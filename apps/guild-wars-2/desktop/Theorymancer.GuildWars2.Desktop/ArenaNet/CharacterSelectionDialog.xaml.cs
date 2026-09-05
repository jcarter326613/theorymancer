using System.Windows;

namespace Theorymancer.GuildWars2.Desktop.ArenaNet;

public partial class CharacterSelectionDialog : Window
{
    private readonly MainWindow _mainWindow;

    public CharacterSelectionDialog(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        DataContext = mainWindow;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) =>
        await _mainWindow.SignInAsync();

    private async void SignOut_Click(object sender, RoutedEventArgs e) =>
        await _mainWindow.SignOutAsync();

    private async void ConnectArenaNet_Click(object sender, RoutedEventArgs e)
    {
        ConnectKeyButton.IsEnabled = false;
        try
        {
            if (await _mainWindow.ConnectArenaNetAsync(ArenaNetApiKeyBox.Password))
            {
                ArenaNetApiKeyBox.Password = string.Empty;
            }
        }
        finally
        {
            ConnectKeyButton.IsEnabled = true;
        }
    }

    private void ClearArenaNetKey_Click(object sender, RoutedEventArgs e)
    {
        ArenaNetApiKeyBox.Password = string.Empty;
        _mainWindow.ClearArenaNetKey();
    }

    private async void LoadArenaNetBuild_Click(object sender, RoutedEventArgs e)
    {
        LoadBuildButton.IsEnabled = false;
        try
        {
            await _mainWindow.LoadSelectedArenaNetBuildAsync();
        }
        finally
        {
            LoadBuildButton.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
