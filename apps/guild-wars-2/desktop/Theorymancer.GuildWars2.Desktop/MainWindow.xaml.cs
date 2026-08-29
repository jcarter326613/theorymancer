using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Theorymancer.GuildWars2.Desktop.Authentication;
using Theorymancer.GuildWars2.Desktop.Calibration;
using Theorymancer.GuildWars2.Desktop.Capture;
using Theorymancer.GuildWars2.Desktop.CombatLog.Ocr;
using Theorymancer.GuildWars2.Desktop.CombatLog.Sessions;
using Theorymancer.GuildWars2.Desktop.SkillBar;

namespace Theorymancer.GuildWars2.Desktop;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly CollectorSettingsStore _settingsStore = new();
    private readonly DesktopAuthenticationService _authentication;
    private readonly ReferenceIcons _referenceIcons;
    private readonly CancellationToken _shutdownToken;
    private CollectorSettings _settings;
    private SelectedGameWindow? _selectedWindow;
    private CombatLogCaptureSession? _captureSession;
    private CombatLogActivityLogDebugWriter? _activityLogDebugWriter;
    private bool _diagnosticsEnabled;
    private string _setupStatus = "Select the Guild Wars 2 window, then calibrate the combat log and skill bar.";
    private string _captureStatus = "Not recording";
    private string _authenticationStatus = "Signed out";

    public MainWindow(
        DesktopAuthenticationService authentication,
        ReferenceIcons referenceIcons,
        CancellationToken shutdownToken)
    {
        _authentication = authentication;
        _referenceIcons = referenceIcons;
        _shutdownToken = shutdownToken;
        InitializeComponent();
        DataContext = this;
        _settings = _settingsStore.Load();
        _authentication.StateChanged += Authentication_StateChanged;
        UpdateAuthenticationControls();
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

    public string AuthenticationStatus
    {
        get => _authenticationStatus;
        private set => SetField(ref _authenticationStatus, value);
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _authentication.SignInAsync(_shutdownToken);
        }
        catch (OperationCanceledException) when (_shutdownToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowSetupError($"Sign-in failed: {exception.Message}");
        }
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _authentication.SignOutAsync(_shutdownToken);
        }
        catch (Exception exception)
        {
            ShowSetupError($"Sign-out failed: {exception.Message}");
        }
    }

    private void SelectWindow_Click(object sender, RoutedEventArgs e)
    {
        var picker = new GameWindowPicker { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedWindow is null)
        {
            return;
        }

        _selectedWindow = picker.SelectedWindow;
        SetupStatus = $"Selected {_selectedWindow.Title}. Calibrate the required interface regions.";
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (!_authentication.IsSignedIn)
        {
            ShowSetupError("Sign in to Theorymancer before calibrating and downloading Guild Wars 2 assets.");
            return;
        }

        if (_selectedWindow is null)
        {
            ShowSetupError("Select the Guild Wars 2 window before calibrating.");
            return;
        }

        if (!_selectedWindow.TryGetClientBounds(out _))
        {
            ShowSetupError("Guild Wars 2 is no longer available. Select its window again.");
            return;
        }

        var dialog = new CalibrationDialog(_selectedWindow, _settings, _referenceIcons) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = dialog.Settings;
            _settingsStore.Save(_settings);
            SetupStatus = "Calibration saved. Start capture when the dedicated combat tab is visible.";
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWindow is null ||
            _settings.CombatLogCrop is null ||
            _settings.SkillBarCrop is null ||
            _settings.SkillBarLayout is not { HasWeaponSkillSlots: true })
        {
            ShowSetupError("Select the GW2 window, calibrate both required interface regions, and analyze the skill-bar layout before recording.");
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
            _captureSession = await CombatLogCaptureSession.StartAsync(_selectedWindow, _settings, _diagnosticsEnabled);
            _captureSession.StatusChanged += CombatLogCaptureSession_StatusChanged;
            _captureSession.LineRecognized += CombatLogCaptureSession_LineRecognized;
            _captureSession.DiagnosticsUpdated += CombatLogCaptureSession_DiagnosticsUpdated;
            if (_diagnosticsEnabled)
            {
                _activityLogDebugWriter = _captureSession.DebugActivityWriter;
            }

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            CaptureStatus = "Recording";
            AddActivity(
                "Recording started. Press Stop capture before moving or minimizing Guild Wars 2.",
                "capture_started");
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
        _authentication.StateChanged -= Authentication_StateChanged;
        await StopCaptureAsync();
    }

    private void Authentication_StateChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateAuthenticationControls);
            return;
        }

        UpdateAuthenticationControls();
    }

    private void UpdateAuthenticationControls()
    {
        AuthenticationStatus = _authentication.State switch
        {
            AuthenticationState.SigningIn => "Signing in...",
            AuthenticationState.SignedIn => "Signed in",
            _ => "Signed out",
        };
        SignInButton.IsEnabled = _authentication.State == AuthenticationState.SignedOut;
        SignOutButton.IsEnabled = _authentication.State == AuthenticationState.SignedIn;
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
        AddActivity(
            $"Recording stopped. {FormatStatistics(captureSession.Statistics)}",
            "capture_stopped",
            captureSession.Statistics);
        if (_activityLogDebugWriter is { } activityLogDebugWriter)
        {
            await activityLogDebugWriter.DisposeAsync();
            _activityLogDebugWriter = null;
        }
    }

    private void CombatLogCaptureSession_StatusChanged(string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            CaptureStatus = message;
            AddActivity(message, "capture_status");
        });
    }

    private void CombatLogCaptureSession_LineRecognized(RecognizedCombatLogLine line)
    {
        Dispatcher.BeginInvoke(() => AddActivity(line.Text, "matched_line", line));
    }

    private void CombatLogCaptureSession_DiagnosticsUpdated(CombatLogCaptureDiagnostics diagnostics)
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
        if (_selectedWindow is null ||
            _settings.CombatLogCrop is null ||
            _settings.SkillBarCrop is null ||
            _settings.SkillBarLayout is not { HasWeaponSkillSlots: true })
        {
            ShowSetupError("Select the Guild Wars 2 window, calibrate both required interface regions, and analyze the skill-bar layout before running diagnostic OCR.");
            return;
        }

        RunDiagnosticOcrButton.IsEnabled = false;
        try
        {
            AddActivity("Running diagnostic OCR on the current combat-log crop.", "diagnostic_ocr_started");
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
            AddActivity($"Diagnostic OCR found {lines.Count} line(s).", "diagnostic_ocr_completed");
            foreach (var line in lines)
            {
                AddActivity(line.Text, "diagnostic_ocr_line", line);
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
        if (_diagnosticsEnabled && _captureSession is not null)
        {
            _activityLogDebugWriter = _captureSession.DebugActivityWriter;
        }

        if (!_diagnosticsEnabled)
        {
            DiagnosticsSummaryText.Text = string.Empty;
            OriginalDiagnosticPreview.Source = null;
            ProcessedDiagnosticPreview.Source = null;
        }

        AddActivity(
            _diagnosticsEnabled ? "Diagnostics enabled." : "Diagnostics disabled.",
            _diagnosticsEnabled ? "diagnostics_enabled" : "diagnostics_disabled");
    }

    private void ShowSetupError(string message)
    {
        CaptureStatus = message;
        MessageBox.Show(this, message, "Theorymancer collector", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void AddActivity(string message, string source = "application", object? details = null)
    {
        var displayedAt = DateTimeOffset.Now;
        var displayedText = $"{displayedAt:HH:mm:ss}  {message}";
        ActivityLog.Add(displayedText);
        _activityLogDebugWriter?.WriteActivity(displayedAt, displayedText, source, details);
        while (ActivityLog.Count > 500)
        {
            ActivityLog.RemoveAt(0);
        }

        ActivityList.ScrollIntoView(ActivityLog[^1]);
    }

    private static string FormatStatistics(CombatLogCaptureStatistics statistics) =>
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
