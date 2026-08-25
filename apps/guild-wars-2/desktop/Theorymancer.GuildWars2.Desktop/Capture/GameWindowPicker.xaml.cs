using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Theorymancer.GuildWars2.Desktop.Capture;

public partial class GameWindowPicker : Window, INotifyPropertyChanged
{
    private string _candidateStatus = string.Empty;
    private readonly WindowHighlightOverlay _highlightOverlay = new();

    public GameWindowPicker()
    {
        InitializeComponent();
        DataContext = this;
        RefreshCandidates();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SelectedGameWindow? SelectedWindow { get; private set; }

    public string CandidateStatus
    {
        get => _candidateStatus;
        private set
        {
            _candidateStatus = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CandidateStatus)));
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshCandidates();

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectedWindow = WindowList.SelectedItem as SelectedGameWindow;
        HideHighlight();
        DialogResult = SelectedWindow is not null;
    }

    private void WindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectButton.IsEnabled = WindowList.SelectedItem is SelectedGameWindow;
        if (WindowList.SelectedItem is SelectedGameWindow selectedWindow &&
            _highlightOverlay.TryHighlight(selectedWindow))
        {
            if (!_highlightOverlay.IsVisible)
            {
                _highlightOverlay.Show();
            }

            return;
        }

        HideHighlight();
    }

    private void RefreshCandidates()
    {
        HideHighlight();
        var candidates = SelectedGameWindow.FindCandidates();
        WindowList.ItemsSource = candidates;
        CandidateStatus = candidates.Count == 0
            ? "No Guild Wars 2 window found. Start the game, then refresh."
            : $"Found {candidates.Count} matching window(s).";
    }

    protected override void OnClosed(EventArgs e)
    {
        _highlightOverlay.Close();
        base.OnClosed(e);
    }

    private void HideHighlight()
    {
        _highlightOverlay.HideHighlight();
        if (_highlightOverlay.IsVisible)
        {
            _highlightOverlay.Hide();
        }
    }
}
