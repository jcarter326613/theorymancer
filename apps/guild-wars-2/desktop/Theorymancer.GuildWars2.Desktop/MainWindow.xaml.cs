using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.Ocr;
using Theorymancer.GuildWars2.Desktop.Sessions;

namespace Theorymancer.GuildWars2.Desktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly CollectorSettingsStore _settingsStore = new();
    private CollectorSettings _settings;
    private SelectedGameWindow? _selectedWindow;
    private CalibrationOverlay? _calibrationOverlay;
    private CaptureSession? _captureSession;
    private string _setupStatus = "Select the Guild Wars 2 window, then calibrate its combat-log crop.";
    private string _captureStatus = "Not recording";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _settings = _settingsStore.Load();
        RowHeightTextBox.Text = _settings.RowHeightPixels.ToString(CultureInfo.InvariantCulture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SetupStatus
    {
        get => _setupStatus;
        private set => SetField(ref _setupStatus, value);
    }

    public string CaptureStatus
    {
        get => _captureStatus;
        private set => SetField(ref _captureStatus, value);
    }

    private void SelectWindow_Click(object sender, RoutedEventArgs e)
    {
        var picker = new GameWindowPicker { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedWindow is null)
        {
            return;
        }

        _selectedWindow = picker.SelectedWindow;
        SetupStatus = $"Selected {_selectedWindow.Title}. Calibrate the visible combat-log crop.";
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (_calibrationOverlay is not null)
        {
            SetupStatus = "Calibration is already active on the selected game window.";
            return;
        }

        if (_selectedWindow is null)
        {
            ShowSetupError("Select the Guild Wars 2 window before calibrating.");
            return;
        }

        if (!TryGetRowHeight(out var rowHeight))
        {
            return;
        }

        if (!_selectedWindow.TryGetClientBounds(out var clientBounds))
        {
            ShowSetupError("Guild Wars 2 is no longer available. Select its window again.");
            return;
        }

        var overlay = new CalibrationOverlay(clientBounds, _settings.Regions);
        _calibrationOverlay = overlay;
        overlay.RegionCountChanged += UpdateCalibrationControls;
        overlay.Confirmed += regions =>
        {
            _settings = _settings with { Regions = regions, RowHeightPixels = rowHeight };
            _settingsStore.Save(_settings);
            SetupStatus = "Calibration saved. Start capture when the dedicated combat tab is visible.";
            EndCalibration();
        };
        overlay.Cancelled += () =>
        {
            SetupStatus = "Calibration canceled.";
            EndCalibration();
        };
        UpdateCalibrationControls(overlay.RegionCount);
        overlay.Show();
        SetupStatus = "Calibration is active. Draw or move regions on the Guild Wars 2 window, then confirm here.";
    }

    private void ConfirmCalibration_Click(object sender, RoutedEventArgs e) => _calibrationOverlay?.Confirm();

    private void CancelCalibration_Click(object sender, RoutedEventArgs e) => _calibrationOverlay?.Cancel();

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWindow is null || _settings.CombatLogCrop is null)
        {
            ShowSetupError("Select the GW2 window and calibrate the combat-log crop before recording.");
            return;
        }

        if (!TryGetRowHeight(out var rowHeight))
        {
            return;
        }

        if (!_selectedWindow.TryGetClientBounds(out _))
        {
            ShowSetupError("Guild Wars 2 is no longer available. Select its window again.");
            return;
        }

        _settings = _settings with { RowHeightPixels = rowHeight };
        _settingsStore.Save(_settings);

        try
        {
            _captureSession = await CaptureSession.StartAsync(_selectedWindow, _settings);
            _captureSession.StatusChanged += CaptureSession_StatusChanged;
            _captureSession.LineRecognized += CaptureSession_LineRecognized;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            CaptureStatus = "Recording";
            AddActivity("Recording started. Press Stop capture before moving or minimizing Guild Wars 2.");
        }
        catch (Exception exception)
        {
            _captureSession?.Dispose();
            _captureSession = null;
            ShowSetupError(exception.Message);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await StopCaptureAsync();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        _calibrationOverlay?.Cancel();
        await StopCaptureAsync();
    }

    private void UpdateCalibrationControls(int regionCount)
    {
        CalibrationControls.Visibility = Visibility.Visible;
        CalibrationStatusText.Text = regionCount == 0
            ? "Draw the combat-log region on the game window."
            : $"{regionCount} region(s) ready to confirm.";
        ConfirmCalibrationButton.IsEnabled = regionCount > 0;
    }

    private void EndCalibration()
    {
        _calibrationOverlay = null;
        CalibrationControls.Visibility = Visibility.Collapsed;
    }

    private async Task StopCaptureAsync()
    {
        if (_captureSession is null)
        {
            return;
        }

        var captureSession = _captureSession;
        _captureSession = null;
        await captureSession.DisposeAsync();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        CaptureStatus = "Not recording";
        AddActivity("Recording stopped.");
    }

    private void CaptureSession_StatusChanged(string message)
    {
        Dispatcher.Invoke(() =>
        {
            CaptureStatus = message;
            AddActivity(message);
        });
    }

    private void CaptureSession_LineRecognized(RecognizedCombatLogLine line)
    {
        Dispatcher.Invoke(() => AddActivity($"{line.FirstSeenQpc}: {line.Text}"));
    }

    private bool TryGetRowHeight(out int rowHeight)
    {
        if (int.TryParse(RowHeightTextBox.Text, CultureInfo.InvariantCulture, out rowHeight) &&
            rowHeight is >= 10 and <= 80)
        {
            return true;
        }

        ShowSetupError("Row height must be a whole number from 10 to 80 pixels.");
        return false;
    }

    private void ShowSetupError(string message)
    {
        CaptureStatus = message;
        MessageBox.Show(this, message, "Theorymancer collector", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void AddActivity(string message)
    {
        ActivityList.Items.Insert(0, $"{DateTimeOffset.Now:HH:mm:ss}  {message}");
        while (ActivityList.Items.Count > 100)
        {
            ActivityList.Items.RemoveAt(ActivityList.Items.Count - 1);
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
