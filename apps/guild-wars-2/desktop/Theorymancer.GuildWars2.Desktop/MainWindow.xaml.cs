using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private bool _diagnosticsEnabled;
    private string _setupStatus = "Select the Guild Wars 2 window, then calibrate its combat-log crop.";
    private string _captureStatus = "Not recording";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _settings = _settingsStore.Load();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> ActivityLog { get; } = [];

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
            _settings = _settings with { Regions = regions };
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

        if (!_selectedWindow.TryGetClientBounds(out _))
        {
            ShowSetupError("Guild Wars 2 is no longer available. Select its window again.");
            return;
        }

        _settingsStore.Save(_settings);

        try
        {
            _captureSession = await CaptureSession.StartAsync(_selectedWindow, _settings, _diagnosticsEnabled);
            _captureSession.StatusChanged += CaptureSession_StatusChanged;
            _captureSession.LineRecognized += CaptureSession_LineRecognized;
            _captureSession.DiagnosticsUpdated += CaptureSession_DiagnosticsUpdated;
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
        AddActivity($"Recording stopped. {FormatStatistics(captureSession.Statistics)}");
    }

    private void CaptureSession_StatusChanged(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CaptureStatus = message;
            AddActivity(message);
        });
    }

    private void CaptureSession_LineRecognized(RecognizedCombatLogLine line)
    {
        Dispatcher.BeginInvoke(() => AddActivity(line.Text));
    }

    private void CaptureSession_DiagnosticsUpdated(CaptureDiagnostics diagnostics)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_diagnosticsEnabled)
            {
                return;
            }

            DiagnosticsSummaryText.Text =
                $"Frame: {diagnostics.CaptureWidth} x {diagnostics.CaptureHeight}\n" +
                $"OCR input: {diagnostics.ProcessedPreviewFrame?.Frame.Width} x {diagnostics.ProcessedPreviewFrame?.Frame.Height}\n" +
                $"Match: {diagnostics.LastFrameMatch?.Decision}; " +
                $"overlap {diagnostics.LastFrameMatch?.MatchedLineCount}; " +
                $"confidence {diagnostics.LastFrameMatch?.Confidence:P1}\n" +
                FormatStatistics(diagnostics.Statistics);
            OriginalDiagnosticPreview.Source = diagnostics.OriginalPreviewFrame is { } frame
                ? ToBitmapSource(frame)
                : null;
            ProcessedDiagnosticPreview.Source = diagnostics.ProcessedPreviewFrame is { } processed
                ? ToBitmapSource(processed.Frame)
                : null;
        });
    }

    private async void RunDiagnosticOcr_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWindow is null || _settings.CombatLogCrop is null)
        {
            ShowSetupError("Select the Guild Wars 2 window and calibrate the combat-log crop before running diagnostic OCR.");
            return;
        }

        RunDiagnosticOcrButton.IsEnabled = false;
        try
        {
            AddActivity("Running diagnostic OCR on the current combat-log crop.");
            var capture = new VisibleScreenRegionCapture(_selectedWindow, _settings.CombatLogCrop);
            var sourceFrame = await capture.CaptureAsync(CancellationToken.None);
            var processedFrame = CombatLogImagePreprocessor.Process(sourceFrame);
            var engine = WindowsCombatLogOcrEngine.CreateEnglish();
            var lines = await engine.RecognizeAsync(sourceFrame, processedFrame.Frame, CancellationToken.None);

            OriginalDiagnosticPreview.Source = ToBitmapSource(sourceFrame);
            ProcessedDiagnosticPreview.Source = ToBitmapSource(processedFrame.Frame);
            DiagnosticsSummaryText.Text =
                $"Frame: {sourceFrame.Width} x {sourceFrame.Height}\n" +
                $"OCR input: {processedFrame.Frame.Width} x {processedFrame.Frame.Height}\n" +
                $"Diagnostic OCR found {lines.Count} line(s).";
            AddActivity($"Diagnostic OCR found {lines.Count} line(s).");
            foreach (var line in lines)
            {
                AddActivity(line.Text);
            }
        }
        catch (Exception exception)
        {
            ShowSetupError($"Diagnostic OCR failed: {exception.Message}");
        }
        finally
        {
            RunDiagnosticOcrButton.IsEnabled = true;
        }
    }

    private void DiagnosticsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _diagnosticsEnabled = DiagnosticsCheckBox.IsChecked == true;
        DiagnosticsPanel.Visibility = _diagnosticsEnabled ? Visibility.Visible : Visibility.Collapsed;
        _captureSession?.SetDiagnosticsEnabled(_diagnosticsEnabled);
        if (!_diagnosticsEnabled)
        {
            DiagnosticsSummaryText.Text = string.Empty;
            OriginalDiagnosticPreview.Source = null;
            ProcessedDiagnosticPreview.Source = null;
        }

        AddActivity(_diagnosticsEnabled ? "Diagnostics enabled." : "Diagnostics disabled.");
    }

    private void ShowSetupError(string message)
    {
        CaptureStatus = message;
        MessageBox.Show(this, message, "Theorymancer collector", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void AddActivity(string message)
    {
        ActivityLog.Add($"{DateTimeOffset.Now:HH:mm:ss}  {message}");
        while (ActivityLog.Count > 500)
        {
            ActivityLog.RemoveAt(0);
        }

        ActivityList.ScrollIntoView(ActivityLog[^1]);
    }

    private static string FormatStatistics(CaptureStatistics statistics) =>
        $"Frames {statistics.FramesCaptured}; OCR queued {statistics.OcrFramesQueued}; " +
        $"recognized {statistics.RecognizedLines}; OCR empty {statistics.EmptyOcrRows}; " +
        $"dropped {statistics.DroppedOcrRows}.";

    private static BitmapSource ToBitmapSource(CapturedFrame frame)
    {
        var source = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            frame.BgraPixels,
            frame.Stride);
        source.Freeze();
        return source;
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
