using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PSUM_Check_Interrogation_WinUI_3;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _refreshTimer;
    private bool _diagnosticsReading;
    private CheckInStorage? _checkInStorage;
    private BatteryDiagnostics _diagnostics = BatteryDiagnostics.Unknown();
    private int? _chargePercent;
    private bool? _acConnected;
    private bool _charging;
    private bool? _batterySaver;
    private IReadOnlyList<BatteryCheckIn> _allHistory = [];
    private IReadOnlyList<BatteryCheckIn> _shownHistory = [];

    public MainWindow()
    {
        InitializeComponent();
        BusinessPage.SetOwner(this);
        BusinessPage.RecordsChanged += (_, _) => UpdateAlerts();
        BusinessPage.ReadingCaptured += ApplyDiagnostics;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 900));
        InitializeChassisImage();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "battery_paper_app_icon.ico"));
        InitializeStorageControls();
        InitializeOriginalFeatures();
        InitializeMode();
        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(10);
        _refreshTimer.Tick += (_, _) => RefreshPowerStatus();
        _refreshTimer.Start();
        Closed += (_, _) => { _refreshTimer.Stop(); DiagnosticsLog.Info("App.Close"); };
        RefreshPowerStatus();
        _ = RefreshDiagnosticsAsync();
        _ = InitializeHistoryAsync();
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        string section = item.Tag?.ToString() ?? "dashboard";
        if (section == "business" && !_businessMode) { AppNavigation.SelectedItem = DashboardNav; return; }
        ModePage.Visibility = section == "mode" ? Visibility.Visible : Visibility.Collapsed;
        BusinessPage.Visibility = section == "business" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsLog.Info("Navigation.Change", $"section={section}");
        DashboardPage.Visibility = section == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        CheckInPage.Visibility = section == "checkin" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = section == "history" ? Visibility.Visible : Visibility.Collapsed;
        StoragePage.Visibility = section == "storage" ? Visibility.Visible : Visibility.Collapsed;
        SnapshotsPage.Visibility = section == "snapshots" ? Visibility.Visible : Visibility.Collapsed;
        PartsPage.Visibility = section == "parts" ? Visibility.Visible : Visibility.Collapsed;
        AlertsPage.Visibility = section == "alerts" ? Visibility.Visible : Visibility.Collapsed;
        SourcesPage.Visibility = section == "sources" ? Visibility.Visible : Visibility.Collapsed;
        DataPage.Visibility = section == "data" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = section == "about" ? Visibility.Visible : Visibility.Collapsed;
        AppNavigation.Header = item.Content;
        if (section == "history" && _checkInStorage is not null)
            _ = ReloadHistoryAsync();
        if (section == "snapshots") { UpdateVantagePolicy(); _ = ReloadSnapshotsAsync(); }
        if (section == "alerts") UpdateAlerts();
        if (section == "sources") UpdateSources();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshPowerStatus();
        await RefreshDiagnosticsAsync();
    }

    private async System.Threading.Tasks.Task RefreshDiagnosticsAsync()
    {
        if (_diagnosticsReading) return;
        _diagnosticsReading = true;
        SaveCheckInButton.IsEnabled = false;
        try
        {
            DiagnosticsLog.Info("Telemetry.Capture.Start");
            var reading = await BatteryDiagnosticsProvider.ReadAsync();
            DiagnosticsLog.Info("Telemetry.Capture.Complete", $"healthAvailable={reading.HealthPercent is not null}");
            if (reading.Issue is not null) DiagnosticsLog.Warning("Telemetry.Capture.Unavailable", DiagnosticsLog.BatteryIssue(reading.Issue));
            if (!string.IsNullOrWhiteSpace(reading.SourceWarning)) DiagnosticsLog.Warning("Telemetry.Capture.Partial", DiagnosticsLog.BatteryIssue(reading.SourceWarning));
            ApplyDiagnostics(reading);
            BusinessPage.AcceptTelemetry(reading);
        }
        finally
        {
            _diagnosticsReading = false;
            SaveCheckInButton.IsEnabled = _checkInStorage is not null;
        }
    }

    private void ApplyDiagnostics(BatteryDiagnostics reading)
    {
            reading = reading with { HealthLabel = ModePolicy.Classify(reading.HealthPercent, _businessMode) };
            _diagnostics = reading;
            HealthText.Text = reading.HealthPercent is double health
                ? $"Health: {health:0.0}% — {reading.HealthLabel}" : "Health: Unknown";
            WearText.Text = reading.WearPercent is double wear ? $"Wear: {wear:0.0}%" : "Wear: Unknown";
            DesignText.Text = reading.DesignCapacityMWh is uint design
                ? $"Design capacity: {design / 1000.0:0.00} Wh" : "Design capacity: Unknown";
            FullText.Text = reading.FullChargeCapacityMWh is uint full
                ? $"Full-charge capacity: {full / 1000.0:0.00} Wh" : "Full-charge capacity: Unknown";
            CycleText.Text = reading.CycleCount is uint cycles ? $"Cycle count: {cycles}" : "Cycle count: Unknown";
            LiveVoltageText.Text = reading.VoltageMv is uint voltage ? $"Voltage: {voltage / 1000.0:0.00} V" : "Voltage: Unknown";
            LiveVoltageBar.Value = reading.VoltageMv is uint v ? Math.Clamp(v / 1000.0, 0, 20) : 0;
            LiveTemperatureText.Text = reading.TemperatureC is double temp ? $"Temperature: {temp:0.0} °C" : "Temperature: Unknown";
            LiveTemperatureBar.Value = reading.TemperatureC is double t ? Math.Clamp(t, 0, 60) : 0;
            LiveRateText.Text = reading.RateMw is int rate ? $"Power flow: {rate / 1000.0:+0.00;-0.00} W" : "Power flow: Unknown";
            LiveRemainingText.Text = reading.RemainingCapacityMWh is uint remaining ? $"Remaining capacity: {remaining / 1000.0:0.00} Wh" : "Remaining capacity: Unknown";
            ReplacementAlert.IsOpen = reading.HealthLabel == "Critical";
            DiagnosticsIssue.Message = reading.Issue ?? reading.SourceWarning ?? string.Empty;
            DiagnosticsIssue.IsOpen = !string.IsNullOrWhiteSpace(DiagnosticsIssue.Message);
            UpdateCheckInSummary();
            UpdateSources();
            UpdateAlerts();
    }

    private void RefreshPowerStatus()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            _chargePercent = null;
            _acConnected = null;
            _charging = false;
            _batterySaver = null;
            CriticalChargeAlert.IsOpen = false;
            ReadError.Message = $"Windows could not read power status (error {Marshal.GetLastWin32Error()}).";
            ReadError.IsOpen = true;
            ChargeText.Text = "Charge: Unknown";
            ChargeBar.IsIndeterminate = true;
            PowerText.Text = "Power source: Unknown";
            BatteryText.Text = "Battery state: Unknown";
            RuntimeText.Text = "Estimated remaining time: Unknown";
            UpdateCheckInSummary();
            UpdateSources();
            UpdateAlerts();
            return;
        }

        ReadError.IsOpen = false;
        bool noBattery = (status.BatteryFlag & 128) != 0;
        bool chargeKnown = !noBattery && status.BatteryLifePercent <= 100;
        _chargePercent = chargeKnown ? status.BatteryLifePercent : null;
        _acConnected = status.ACLineStatus switch { 0 => false, 1 => true, _ => null };
        _charging = !noBattery && (status.BatteryFlag & 8) != 0;
        _batterySaver = status.SystemStatusFlag switch { 0 => false, 1 => true, _ => null };
        CriticalChargeAlert.IsOpen = _chargePercent is int low && low <= 5;
        CriticalChargeAlert.Message = _acConnected == true ? "Keep the charger connected." : "Connect the charger and save your work.";
        ChargeText.Text = chargeKnown ? $"Charge: {status.BatteryLifePercent}%" : "Charge: Unknown";
        ChargeBar.IsIndeterminate = !chargeKnown;
        if (chargeKnown) ChargeBar.Value = status.BatteryLifePercent;

        PowerText.Text = status.ACLineStatus switch
        {
            0 => "Power source: Battery",
            1 => "Power source: AC adapter",
            _ => "Power source: Unknown"
        };
        BatteryText.Text = noBattery ? "Battery state: No battery reported" :
            (status.BatteryFlag & 8) != 0 ? "Battery state: Charging" :
            (status.BatteryFlag & 4) != 0 ? "Battery state: Critical charge" :
            (status.BatteryFlag & 2) != 0 ? "Battery state: Low charge" :
            status.BatteryFlag == 255 ? "Battery state: Unknown" : "Battery state: Discharging or idle";

        RuntimeText.Text = noBattery || status.BatteryLifeTime == uint.MaxValue
            ? "Estimated remaining time: Unknown"
            : $"Estimated remaining time: {TimeSpan.FromSeconds(status.BatteryLifeTime):hh\\:mm}";
        UpdatedText.Text = $"Updated {DateTime.Now:t}";
        UpdateCheckInSummary();
        UpdateSources();
        UpdateAlerts();
    }

    private void UpdateCheckInSummary()
    {
        string charge = _chargePercent is int percent ? $"{percent}%" : "Unknown";
        string health = _diagnostics.HealthPercent is double value ? $"{value:0.0}%" : "Unknown";
        string cycles = _diagnostics.CycleCount?.ToString() ?? "Unknown";
        CheckInSummary.Text = $"Current reading: charge {charge} · {CheckInState()} · health {health} · cycles {cycles}";
    }

    private async Task InitializeHistoryAsync()
    {
        try
        {
            var result = await Task.Run(() =>
            {
                var storage = new CheckInStorage();
                return (storage, rows: storage.Recent(1_000_000));
            });
            _checkInStorage = result.storage;
            HistoryRowsLoaded(result.rows);
            SaveCheckInButton.IsEnabled = !_diagnosticsReading;
            BackupButton.IsEnabled = true;
            RestoreButton.IsEnabled = true;
            await RefreshStorageAsync();
            LoadThresholds();
            await ReloadSnapshotsAsync();
            LoadOriginalSettings();
            await QueryLenovoAsync();
        }
        catch (Exception ex)
        { DiagnosticsLog.Error("Interrogation.Failed", ex);
            CheckInMessage.Severity = InfoBarSeverity.Error;
            CheckInMessage.Title = "Check-in history unavailable";
            CheckInMessage.Message = ex.Message;
            CheckInMessage.IsOpen = true;
            ShowHistoryError(ex);
        }
    }

    private async void ReloadHistory_Click(object sender, RoutedEventArgs e)
    {
        await ReloadHistoryAsync();
    }

    private async Task ReloadHistoryAsync()
    {
        if (_checkInStorage is null) return;
        try
        {
            var rows = await Task.Run(() => _checkInStorage.Recent(1_000_000));
            HistoryRowsLoaded(rows);
        }
        catch (Exception ex)
        { DiagnosticsLog.Error("Interrogation.Failed", ex);
            ShowHistoryError(ex);
        }
    }

    private async void SaveCheckIn_Click(object sender, RoutedEventArgs e)
    {
        if (_checkInStorage is null) return;
        RefreshPowerStatus();
        var record = new BatteryCheckIn(
            0, DateTimeOffset.Now, _diagnostics.BatteryId, _chargePercent,
            CheckInState(), _diagnostics.HealthPercent,
            _diagnostics.CycleCount is uint cycles && cycles <= int.MaxValue ? (int)cycles : null,
            CheckInNote.Text, Guid.NewGuid().ToString());
        SaveCheckInButton.IsEnabled = false;
        try
        {
            var rows = await Task.Run(() =>
            {
                _checkInStorage.Add(record);
                return _checkInStorage.Recent(1_000_000);
            });
            CheckInNote.Text = string.Empty;
            HistoryRowsLoaded(rows);
            CheckInMessage.Severity = InfoBarSeverity.Success;
            CheckInMessage.Title = "Check-in saved";
            CheckInMessage.Message = $"Evidence ID: {record.EvidenceId}";
            CheckInMessage.IsOpen = true;
        }
        catch (Exception ex)
        { DiagnosticsLog.Error("Interrogation.Failed", ex);
            ShowCheckInError(ex);
        }
        finally
        {
            SaveCheckInButton.IsEnabled = !_diagnosticsReading;
        }
    }

    private string CheckInState()
    {
        if (_chargePercent is null) return "No battery";
        if (_charging) return "Charging";
        if (_acConnected == true && _chargePercent == 100) return "Full";
        if (_acConnected == true) return "Idle";
        return _acConnected == false ? "Discharging" : "Unknown";
    }

    private void HistoryRowsLoaded(IReadOnlyList<BatteryCheckIn> rows)
    {
        _allHistory = rows;
        UpdateFilterOptions(BatteryFilter, "All batteries", rows.Select(row => row.BatteryId));
        UpdateFilterOptions(StateFilter, "All states", rows.Select(row => row.State));
        ExportCsvButton.IsEnabled = true;
        ApplyHistoryFilters();
    }

    private static void UpdateFilterOptions(ComboBox selector, string allLabel, IEnumerable<string> values)
    {
        string? previous = selector.SelectedItem as string;
        selector.Items.Clear();
        selector.Items.Add(allLabel);
        foreach (var value in values.Distinct().OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            selector.Items.Add(value);
        selector.SelectedItem = previous is not null && selector.Items.Contains(previous) ? previous : allLabel;
    }

    private void ApplyFilters_Click(object sender, RoutedEventArgs e) => ApplyHistoryFilters();

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        BatteryFilter.SelectedIndex = 0;
        StateFilter.SelectedIndex = 0;
        FromDateFilter.Text = string.Empty;
        ToDateFilter.Text = string.Empty;
        ApplyHistoryFilters();
    }

    private void ApplyHistoryFilters()
    {
        try
        {
            string? battery = BatteryFilter.SelectedIndex > 0 ? BatteryFilter.SelectedItem as string : null;
            string? state = StateFilter.SelectedIndex > 0 ? StateFilter.SelectedItem as string : null;
            var filter = HistoryFeatures.ParseFilter(battery, state, FromDateFilter.Text, ToDateFilter.Text);
            _shownHistory = HistoryFeatures.Apply(_allHistory, filter);
            ShowHistory(_shownHistory);
            HistoryMessage.IsOpen = false;
        }
        catch (ArgumentException ex)
        { DiagnosticsLog.Error("Interrogation.Failed", ex);
            ShowHistoryError(ex);
        }
    }

    private void ShowHistory(IReadOnlyList<BatteryCheckIn> rows)
    {
        ExportTextButton.IsEnabled = false;
        DeleteCheckInButton.IsEnabled = false;
        HistoryCount.Text = $"{rows.Count} of {_allHistory.Count} check-ins shown";
        CheckInHistory.ItemsSource = rows.Select(row =>
        {
            string charge = row.ChargePercent is int percent ? $"{percent}%" : "Unknown";
            string health = row.HealthPercent is double value ? $"{value:0.0}%" : "Unknown";
            string cycles = row.CycleCount?.ToString() ?? "Unknown";
            return new DisplayRow(row, $"{row.RecordedAt.LocalDateTime:g}  •  {charge}  •  {row.State}\n" +
                       $"Health {health}  •  Cycles {cycles}\n" +
                       $"Battery {row.BatteryId}  •  Evidence {row.EvidenceId}" +
                       (string.IsNullOrWhiteSpace(row.Note) ? "" : $"\n{row.Note}"));
        }).ToArray();
    }

    private void HistorySelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        ExportTextButton.IsEnabled = CheckInHistory.SelectedItem is DisplayRow { Value: BatteryCheckIn };
        DeleteCheckInButton.IsEnabled = ExportTextButton.IsEnabled;
    }

    private async void DeleteCheckIn_Click(object sender, RoutedEventArgs e)
    {
        if (_checkInStorage is null || CheckInHistory.SelectedItem is not DisplayRow { Value: BatteryCheckIn record }) return;
        var dialog = new ContentDialog { XamlRoot = HistoryPage.XamlRoot, Title = "Delete saved check-in?",
            Content = $"Delete Evidence ID {record.EvidenceId}? This cannot be undone.",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await Task.Run(() => _checkInStorage.DeleteCheckIn(record.Id));
            await ReloadHistoryAsync();
            HistoryMessage.Severity = InfoBarSeverity.Success;
            HistoryMessage.Title = "Check-in deleted";
            HistoryMessage.Message = record.EvidenceId;
            HistoryMessage.IsOpen = true;
        }
        catch (Exception ex) { DiagnosticsLog.Error("Interrogation.Failed", ex); ShowHistoryError(ex); }
    }

    private async void ExportText_Click(object sender, RoutedEventArgs e)
    {
        if (CheckInHistory.SelectedItem is not DisplayRow { Value: BatteryCheckIn record }) return;
        await ExportAsync($"psum-checkin-{record.EvidenceId}", ".txt", "Text files", HistoryFeatures.RenderText(record));
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync("psum-checkins", ".csv", "CSV files", HistoryFeatures.RenderCsv(_shownHistory));

    private async Task ExportAsync(string suggestedName, string extension, string typeLabel, string content)
    {
        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = suggestedName,
                DefaultFileExtension = extension,
                Title = "Export PSUM check-ins"
            };
            picker.FileTypeChoices.Add(typeLabel, new List<string> { extension });
            var result = await picker.PickSaveFileAsync();
            if (result is null) return;
            await File.WriteAllTextAsync(result.Path, content, new UTF8Encoding(false));
            HistoryMessage.Severity = InfoBarSeverity.Success;
            HistoryMessage.Title = "Export saved";
            HistoryMessage.Message = result.Path;
            HistoryMessage.IsOpen = true;
        }
        catch (Exception ex)
        { DiagnosticsLog.Error("Interrogation.Failed", ex);
            ShowHistoryError(ex);
        }
    }

    private void ShowHistoryError(Exception ex)
    {
        HistoryMessage.Severity = InfoBarSeverity.Error;
        HistoryMessage.Title = "History action failed";
        HistoryMessage.Message = ex.Message;
        HistoryMessage.IsOpen = true;
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_checkInStorage is null) return;
        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = $"psum-backup-{DateTime.Now:yyyyMMdd-HHmmss}",
                DefaultFileExtension = ".sqlite3",
                Title = "Save PSUM database backup"
            };
            picker.FileTypeChoices.Add("SQLite database", new List<string> { ".sqlite3" });
            var result = await picker.PickSaveFileAsync();
            if (result is null) return;
            BackupButton.IsEnabled = RestoreButton.IsEnabled = false;
            await Task.Run(() => _checkInStorage.BackupDatabase(result.Path));
            ShowDataMessage(InfoBarSeverity.Success, "Backup saved", result.Path);
        }
        catch (Exception ex)
        { DiagnosticsLog.Error("Interrogation.Failed", ex);
            ShowDataMessage(InfoBarSeverity.Error, "Backup failed", ex.Message);
        }
        finally
        {
            BackupButton.IsEnabled = RestoreButton.IsEnabled = true;
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_checkInStorage is null) return;
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id) { Title = "Choose PSUM database backup" };
            picker.FileTypeFilter.Add(".sqlite3");
            picker.FileTypeFilter.Add(".db");
            var result = await picker.PickSingleFileAsync();
            if (result is null) return;
            await Task.Run(() => _checkInStorage.ValidateBackup(result.Path));
            var dialog = new ContentDialog
            {
                XamlRoot = DataPage.XamlRoot,
                Title = "Restore PSUM database?",
                Content = $"This will replace the shared Standard and Business telemetry database with:\n{result.Path}\n\nA safety copy will be created first. Close other PSUM windows and the original Python PSUM app before continuing. Business audit records are stored separately and are not restored here.",
                PrimaryButtonText = "Restore",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            BackupButton.IsEnabled = RestoreButton.IsEnabled = SaveCheckInButton.IsEnabled = false;
            string safety = await Task.Run(() => _checkInStorage.RestoreDatabase(result.Path));
            await ReloadHistoryAsync();
            await RefreshStorageAsync();
            await ReloadSnapshotsAsync();
            LoadThresholds();
            LoadOriginalSettings(false);
            ShowDataMessage(InfoBarSeverity.Success, "Database restored", $"Safety copy: {safety}");
        }
        catch (Exception ex)
        { DiagnosticsLog.Error("Interrogation.Failed", ex);
            ShowDataMessage(InfoBarSeverity.Error, "Restore failed", ex.Message);
        }
        finally
        {
            BackupButton.IsEnabled = RestoreButton.IsEnabled = _checkInStorage is not null;
            SaveCheckInButton.IsEnabled = _checkInStorage is not null && !_diagnosticsReading;
        }
    }

    private void ShowDataMessage(InfoBarSeverity severity, string title, string message)
    {
        DataMessage.Severity = severity;
        DataMessage.Title = title;
        DataMessage.Message = message;
        DataMessage.IsOpen = true;
    }


    private void ShowCheckInError(Exception ex)
    {
        CheckInMessage.Severity = InfoBarSeverity.Error;
        CheckInMessage.Title = "Check-in failed";
        CheckInMessage.Message = ex.Message;
        CheckInMessage.IsOpen = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
