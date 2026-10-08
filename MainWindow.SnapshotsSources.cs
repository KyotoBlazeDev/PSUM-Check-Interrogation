using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PSUM_Check_Interrogation_WinUI_3;

public sealed partial class MainWindow
{
    private IReadOnlyList<LenovoSnapshotRow> _snapshots = [];
    private IReadOnlyList<LenovoSnapshotRow> _shownSnapshots = [];
    private bool _lenovoReading;

    private void InitializeChassisImage()
    {
        string image = Path.Combine(AppContext.BaseDirectory, "Assets", "battery_chassis_status.png");
        if (File.Exists(image)) ChassisImage.Source = new BitmapImage(new Uri(image));
    }

    private void UpdateChassis(JsonElement? data, LenovoSnapshotRow? row)
    {
        JsonElement? item = data;
        string Saved(string name) => item is JsonElement element && element.TryGetProperty(name, out var value) &&
            value.ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(value.ToString()) ? value.ToString() : "Not available";
        ChassisIdentity.Text = row is null ? "--" : Label(row);
        ChassisStatus.Text = Saved("Status");
        ChassisHealth.Text = Saved("BatteryHealth");
        ChassisCapacity.Text = item is JsonElement capacity && Ratio(capacity) is double ratio ? $"{ratio:0.0}%" : "Not available";
        ChassisVoltage.Text = Saved("Voltage");
        ChassisTemperature.Text = Saved("Temperature");
        ChassisPower.Text = Saved("Wattage");
        ChassisAdapter.Text = Saved("Adapter");
    }

    private async void QueryLenovo_Click(object sender, RoutedEventArgs e) => await QueryLenovoAsync();
    private async void ReloadSnapshots_Click(object sender, RoutedEventArgs e) => await ReloadSnapshotsAsync();

    private async Task QueryLenovoAsync()
    {
        if (_lenovoReading) return;
        _lenovoReading = true;
        SnapshotMessage.IsOpen = false;
        try
        {
            var readings = await LenovoSnapshots.ReadAsync();
            if (readings.Count > 0)
            {
                var storage = _checkInStorage ?? new CheckInStorage();
                await Task.Run(() => storage.AddLenovoSnapshotBatch(readings, DateTimeOffset.Now));
                SnapshotMessage.Message = $"Saved {readings.Count} Lenovo battery reading(s). Vantage cache freshness is unknown.";
            }
            else SnapshotMessage.Message = "Lenovo Battery WMI returned no batteries; saved snapshots remain available.";
            SnapshotMessage.Severity = InfoBarSeverity.Informational;
        }
        catch (Exception ex)
        {
            SnapshotMessage.Severity = InfoBarSeverity.Warning;
            SnapshotMessage.Message = ex.Message + " Saved snapshots remain available.";
        }
        finally
        {
            _lenovoReading = false;
            SnapshotMessage.Title = "Lenovo WMI query";
            SnapshotMessage.IsOpen = true;
            await ReloadSnapshotsAsync();
        }
    }

    private async Task ReloadSnapshotsAsync()
    {
        try
        {
            var storage = _checkInStorage ?? new CheckInStorage();
            _snapshots = await Task.Run(() => storage.RecentLenovoSnapshots(1_000_000));
            string? previous = SnapshotBatteryFilter.SelectedItem as string;
            SnapshotBatteryFilter.Items.Clear();
            SnapshotBatteryFilter.Items.Add("All batteries");
            foreach (string label in _snapshots.Select(Label).Distinct().OrderBy(value => value)) SnapshotBatteryFilter.Items.Add(label);
            SnapshotBatteryFilter.SelectedItem = previous is not null && SnapshotBatteryFilter.Items.Contains(previous) ? previous : "All batteries";
            ApplySnapshotFilters();
            UpdateAlerts();
        }
        catch (Exception ex)
        {
            SnapshotMessage.Severity = InfoBarSeverity.Error;
            SnapshotMessage.Title = "Saved snapshots unavailable";
            SnapshotMessage.Message = ex.Message;
            SnapshotMessage.IsOpen = true;
        }
    }

    private void SnapshotSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        var row = (SnapshotList.SelectedItem as DisplayRow)?.Value as LenovoSnapshotRow;
        if (row is null) { SnapshotDetails.Text = "Select a snapshot to see every Lenovo field."; SnapshotHighlights.Text = ""; UpdateChassis(null, null); return; }
        try
        {
            using var document = JsonDocument.Parse(row.DataJson);
            var lines = new List<string> { $"PSUM query time: {row.CapturedAt} (Vantage refresh time unknown)" };
            foreach (string field in LenovoSnapshots.Fields)
                lines.Add($"{field}: {(document.RootElement.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "Unknown")}");
            SnapshotDetails.Text = string.Join(Environment.NewLine, lines);
            SnapshotHighlights.Text = SnapshotSummary(row);
            UpdateChassis(document.RootElement, row);
            SetGauge(SnapshotChargeGauge, SnapshotChargeLabel, "Charge", Number(document.RootElement, "RemainingPercentage"), 100, "%");
            SetGauge(SnapshotCapacityGauge, SnapshotCapacityLabel, "Capacity ratio", Ratio(document.RootElement), 100, "%");
            SetGauge(SnapshotVoltageGauge, SnapshotVoltageLabel, "Voltage", Number(document.RootElement, "Voltage"), 20, " V");
            SetGauge(SnapshotTemperatureGauge, SnapshotTemperatureLabel, "Temperature", Number(document.RootElement, "Temperature"), 60, " °C");
            ShowSnapshotTrends(row);
        }
        catch (JsonException) { SnapshotDetails.Text = "This saved snapshot contains invalid JSON."; UpdateChassis(null, null); }
    }

    private static void SetGauge(ProgressBar bar, TextBlock label, string name, double? value, double maximum, string unit)
    {
        label.Text = value is double number ? $"{name}: {number:0.#}{unit}" : $"{name}: Unknown";
        bar.Value = value is double reading ? Math.Clamp(reading, 0, maximum) : 0;
        bar.Opacity = value is null ? 0.3 : 1;
    }

    private void ShowSnapshotTrends(LenovoSnapshotRow selected)
    {
        SnapshotTrendBars.Children.Clear();
        foreach (var row in _snapshots.Where(other => Label(other) == Label(selected)).OrderBy(other => other.CapturedAt).TakeLast(30))
        {
            try
            {
                using var data = JsonDocument.Parse(row.DataJson);
                double? ratio = Ratio(data.RootElement);
                double? cycles = Number(data.RootElement, "CycleCount");
                var group = new StackPanel { Spacing = 3 };
                group.Children.Add(new TextBlock { Text = $"{row.CapturedAt[..Math.Min(row.CapturedAt.Length, 10)]}: capacity {(ratio is double r ? $"{r:0.0}%" : "Unknown")}, cycles {(cycles is double c ? c.ToString("0") : "Unknown")}" });
                group.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = ratio is double r2 ? Math.Clamp(r2, 0, 100) : 0, Width = 440, Height = 7, HorizontalAlignment = HorizontalAlignment.Left, Opacity = ratio is null ? 0.25 : 1 });
                if (cycles is double count) group.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(100, count), Value = count, Width = 440, Height = 5, HorizontalAlignment = HorizontalAlignment.Left });
                SnapshotTrendBars.Children.Add(group);
            }
            catch (JsonException) { /* Corrupt saved row stays in raw history. */ }
        }
    }

    private static string Label(LenovoSnapshotRow row) => !string.IsNullOrWhiteSpace(row.Barcode) ? row.Barcode :
        !string.IsNullOrWhiteSpace(row.BatteryId) ? row.BatteryId : "Unknown battery";

    private void ApplySnapshotFilters_Click(object sender, RoutedEventArgs e) => ApplySnapshotFilters();

    private void ApplySnapshotFilters()
    {
        try
        {
            var filter = HistoryFeatures.ParseFilter(null, null, SnapshotFromFilter.Text, SnapshotToFilter.Text);
            string battery = SnapshotBatteryFilter.SelectedItem as string ?? "All batteries";
            _shownSnapshots = _snapshots.Where(row =>
            (battery == "All batteries" || Label(row) == battery) &&
            DateTimeOffset.TryParse(row.CapturedAt, out var time) &&
            (filter.From is null || DateOnly.FromDateTime(time.LocalDateTime) >= filter.From) &&
            (filter.To is null || DateOnly.FromDateTime(time.LocalDateTime) <= filter.To)).ToArray();
            SnapshotList.ItemsSource = _shownSnapshots.Select(row => new DisplayRow(row,
                $"{(DateTimeOffset.TryParse(row.CapturedAt, out var time) ? time.LocalDateTime.ToString("g") : row.CapturedAt)}  ·  {Label(row)}\n{SnapshotChange(row)}")).ToArray();
            SnapshotCount.Text = $"{_shownSnapshots.Count} shown of {_snapshots.Count} saved daily snapshot(s)";
        }
        catch (ArgumentException ex)
        {
            SnapshotMessage.Severity = InfoBarSeverity.Warning;
            SnapshotMessage.Message = ex.Message;
            SnapshotMessage.IsOpen = true;
        }
    }

    private static double? Number(JsonElement data, string field)
    {
        if (!data.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        string text = value.ToString();
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[-+]?\d+(?:[.,]\d+)?");
        return match.Success && double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : null;
    }

    private static double? Ratio(JsonElement data) => Number(data, "DesignCapacity") is double design && design > 0 &&
        Number(data, "FullChargeCapacity") is double full ? full / design * 100 : null;

    private string SnapshotChange(LenovoSnapshotRow row)
    {
        var previous = _snapshots.Where(other => other.Id != row.Id && Label(other) == Label(row) &&
            string.CompareOrdinal(other.CapturedAt, row.CapturedAt) < 0).OrderByDescending(other => other.CapturedAt).FirstOrDefault();
        if (previous is null) return "No previous observation";
        try
        {
            using var currentData = JsonDocument.Parse(row.DataJson);
            using var previousData = JsonDocument.Parse(previous.DataJson);
            var findings = new List<string>();
            if (Ratio(currentData.RootElement) is double currentRatio && Ratio(previousData.RootElement) is double oldRatio && Math.Abs(currentRatio - oldRatio) >= 5)
                findings.Add($"capacity ratio {currentRatio - oldRatio:+0.0;-0.0} points");
            if (Number(currentData.RootElement, "CycleCount") is double cycles && Number(previousData.RootElement, "CycleCount") is double oldCycles && Math.Abs(cycles - oldCycles) >= 50)
                findings.Add($"cycle count {cycles - oldCycles:+0;-0}");
            return findings.Count == 0 ? "No significant change" : string.Join("; ", findings);
        }
        catch (JsonException) { return "Change unavailable"; }
    }

    private string SnapshotSummary(LenovoSnapshotRow row)
    {
        using var data = JsonDocument.Parse(row.DataJson);
        var item = data.RootElement;
        string Format(string field, string unit) => Number(item, field) is double value ? $"{value:0.#}{unit}" : "Unknown";
        var history = _snapshots.Where(other => Label(other) == Label(row)).OrderBy(other => other.CapturedAt)
            .Select(other => { try { using var doc = JsonDocument.Parse(other.DataJson); return $"{other.CapturedAt[..Math.Min(10, other.CapturedAt.Length)]}: ratio {(Ratio(doc.RootElement) is double r ? $"{r:0.0}%" : "Unknown")}, cycles {(Number(doc.RootElement, "CycleCount") is double c ? c.ToString("0") : "Unknown")}"; } catch (JsonException) { return "Invalid saved data"; } }).ToArray();
        string Field(string name) => item.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "UNAVAILABLE";
        return $"Saved-cache gauges (visual reference): charge {Format("RemainingPercentage", "%")} · capacity ratio {(Ratio(item) is double ratio ? $"{ratio:0.0}%" : "Unknown")} · voltage {Format("Voltage", " V")} · temperature {Format("Temperature", " °C")}\n" +
            $"Chassis: adapter {Field("Adapter")} · status {Field("Status")} · condition {Field("Condition")} · power {Field("Wattage")}\n" +
            $"Change: {SnapshotChange(row)}\nTrend for {Label(row)}:\n{string.Join(Environment.NewLine, history)}";
    }

    private void CompareSnapshots_Click(object sender, RoutedEventArgs e)
    {
        var selected = SnapshotList.SelectedItems.OfType<DisplayRow>().Select(item => item.Value).OfType<LenovoSnapshotRow>().ToArray();
        if (selected.Length != 2) { SnapshotComparison.Text = "Select exactly two saved snapshots to compare."; return; }
        try
        {
            using var left = JsonDocument.Parse(selected[0].DataJson);
            using var right = JsonDocument.Parse(selected[1].DataJson);
            string Value(JsonElement item, string field) => item.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "Unknown";
            SnapshotComparison.Text = $"Comparison: {selected[0].CapturedAt} | {selected[1].CapturedAt}\n" +
                string.Join(Environment.NewLine, LenovoSnapshots.Fields.Select(field => $"{field}: {Value(left.RootElement, field)} | {Value(right.RootElement, field)}"));
        }
        catch (JsonException) { SnapshotComparison.Text = "Cannot compare invalid saved JSON."; }
    }

    private async void ExportSnapshotsCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rows = new StringBuilder("captured_at,battery_id,barcode," + string.Join(',', LenovoSnapshots.Fields) + "\r\n");
            foreach (var row in _shownSnapshots)
            {
                using var data = JsonDocument.Parse(row.DataJson);
                var values = new[] { row.CapturedAt, row.BatteryId, row.Barcode }.Concat(LenovoSnapshots.Fields.Select(field =>
                    data.RootElement.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : ""));
                rows.AppendLine(string.Join(',', values.Select(CsvValue)));
            }
            var picker = new FileSavePicker(AppWindow.Id) { SuggestedFileName = "psum-lenovo-snapshots", DefaultFileExtension = ".csv", Title = "Export Lenovo snapshots" };
            picker.FileTypeChoices.Add("CSV files", new List<string> { ".csv" });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await File.WriteAllTextAsync(file.Path, rows.ToString(), new UTF8Encoding(false));
            SnapshotMessage.Severity = InfoBarSeverity.Success;
            SnapshotMessage.Message = "Export saved: " + file.Path;
            SnapshotMessage.IsOpen = true;
        }
        catch (Exception ex) { SnapshotMessage.Severity = InfoBarSeverity.Error; SnapshotMessage.Message = ex.Message; SnapshotMessage.IsOpen = true; }
    }

    private static string CsvValue(string? raw)
    {
        string value = raw ?? "";
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private void LoadThresholds()
    {
        if (_checkInStorage is null) return;
        if (int.TryParse(_checkInStorage.GetSetting("threshold_start"), out int start)) StartThreshold.Value = start;
        if (int.TryParse(_checkInStorage.GetSetting("threshold_stop"), out int stop)) StopThreshold.Value = stop;
        UpdateSources();
    }

    private void SaveThresholds_Click(object sender, RoutedEventArgs e)
    {
        int start = (int)StartThreshold.Value, stop = (int)StopThreshold.Value;
        if (double.IsNaN(StartThreshold.Value) || double.IsNaN(StopThreshold.Value) ||
            StartThreshold.Value != start || StopThreshold.Value != stop || start < 0 || start >= stop || stop > 100)
        {
            ThresholdMessage.Severity = InfoBarSeverity.Warning;
            ThresholdMessage.Message = "Enter whole percentages satisfying 0 ≤ start < stop ≤ 100.";
            ThresholdMessage.IsOpen = true;
            return;
        }
        try
        {
            (_checkInStorage ?? new CheckInStorage()).SetSetting("threshold_start", start.ToString(CultureInfo.InvariantCulture));
            (_checkInStorage ?? new CheckInStorage()).SetSetting("threshold_stop", stop.ToString(CultureInfo.InvariantCulture));
            ThresholdMessage.Severity = InfoBarSeverity.Success;
            ThresholdMessage.Message = "Comparison values saved. Active firmware settings remain unverified.";
            ThresholdMessage.IsOpen = true;
            UpdateSources();
        }
        catch (Exception ex)
        {
            ThresholdMessage.Severity = InfoBarSeverity.Error;
            ThresholdMessage.Message = ex.Message;
            ThresholdMessage.IsOpen = true;
        }
    }

    private void UpdateSources()
    {
        if (SourceRows is null) return;
        string charge = _chargePercent is int percent ? $"{percent}%" : "Unknown";
        string ac = _acConnected is bool connected ? connected ? "Connected" : "On battery" : "Unknown";
        string capacity = _diagnostics.DesignCapacityMWh is uint design ? $"{design / 1000.0:0.00} Wh" : "Unknown";
        string full = _diagnostics.FullChargeCapacityMWh is uint value ? $"{value / 1000.0:0.00} Wh" : "Unknown";
        SourceRows.Text = $"Charge: {charge} — Windows power API (live, 10 s)\n" +
            $"AC status: {ac} — Windows power API (live, 10 s)\n" +
            $"Charging: {(_chargePercent is null ? "Unknown" : _charging ? "Yes" : "No")} — Windows power API (live, 10 s)\n" +
            $"Battery saver: {(_batterySaver is bool saver ? saver ? "On" : "Off" : "Unknown")} — Windows power API\n" +
            $"Design capacity: {capacity} — Windows WMI BatteryStaticData\n" +
            $"Full-charge capacity: {full} — Windows WMI BatteryFullChargedCapacity\n" +
            $"Cycle count: {_diagnostics.CycleCount?.ToString() ?? "Unknown"} — Windows WMI BatteryCycleCount\n" +
            $"Remaining capacity: {(_diagnostics.RemainingCapacityMWh is uint remaining ? $"{remaining / 1000.0:0.00} Wh" : "Unknown")} — Windows WMI BatteryStatus\n" +
            $"Voltage: {(_diagnostics.VoltageMv is uint voltage ? $"{voltage / 1000.0:0.000} V" : "Unknown")} — Windows WMI BatteryStatus\n" +
            $"Reported voltage: {(_diagnostics.ReportedVoltageMv is uint reported ? $"{reported / 1000.0:0.000} V" : "Unknown")} — {_diagnostics.VoltageWarning ?? "Windows WMI BatteryStatus"}\n" +
            $"Design voltage: {(_diagnostics.DesignVoltageMv is uint dv ? $"{dv / 1000.0:0.000} V" : "Unknown")} — Windows WMI BatteryStaticData\n" +
            $"Temperature: {(_diagnostics.TemperatureC is double temp ? $"{temp:0.0} °C" : "Unknown")} — Windows WMI BatteryTemperature\n" +
            $"Rate: {(_diagnostics.RateMw is int rate ? $"{rate / 1000.0:+0.00;-0.00} W" : "Unknown")} — Windows WMI BatteryStatus\n" +
            $"Capacity health: {(_diagnostics.HealthPercent is double health ? $"{health:0.0}%" : "Unknown")} — calculated from Windows WMI capacities\n" +
            $"Capacity policy: {(_businessMode ? "Business: Healthy ≥80%, Watch ≥70%, Degraded ≥50%, Critical <50%" : "Standard: Good ≥80%, Service recommended ≥60%, Poor ≥30%, Critical <30%")}\n" +
            "Lenovo battery fields: see Lenovo Snapshots — Lenovo Vantage WMI cache (freshness unknown)\n" +
            "Retained battery profiles: see Battery Storage — Lenovo registry cache (freshness unknown)" +
            (_storageEntries.FirstOrDefault(entry => entry.Profile.IsCurrent) is StorageEntry profile
                ? $"\nCached profile: {profile.Profile.ProfileId} — Lenovo registry; slot mapping unverified\nCached threshold: start {profile.Profile.ChargeStartPercent?.ToString() ?? "Unknown"}% ({(profile.Profile.ChargeStartEnabled == true ? "enabled" : profile.Profile.ChargeStartEnabled == false ? "disabled" : "unknown")}), stop {profile.Profile.ChargeStopPercent?.ToString() ?? "Unknown"}% ({(profile.Profile.ChargeStopEnabled == true ? "enabled" : profile.Profile.ChargeStopEnabled == false ? "disabled" : "unknown")}) — Lenovo registry; freshness unknown"
                : "\nCached threshold: Unknown — Lenovo registry profile unavailable");
        int start = double.IsNaN(StartThreshold.Value) ? 40 : (int)StartThreshold.Value;
        int stop = double.IsNaN(StopThreshold.Value) ? 80 : (int)StopThreshold.Value;
        ThresholdResult.Text = ThresholdDiagnostic.Evaluate(_chargePercent, _acConnected, _charging, start, stop);
        DashboardThresholdText.Text = $"Diagnostic threshold comparison ({start}% start / {stop}% stop): {ThresholdResult.Text}";
    }
}

internal static class ThresholdDiagnostic
{
    public static string Evaluate(int? charge, bool? ac, bool charging, int start, int stop)
    {
        if (charge is null || ac is null) return "Battery or AC status is unavailable; active thresholds are unverified.";
        if (!ac.Value) return "Running on battery; entered thresholds cannot be compared. Active thresholds are unverified.";
        if (charging && charge >= stop) return "Charging is at or above the entered stop value. Active thresholds are unverified.";
        if (!charging && charge <= start) return "Charging has not started at or below the entered start value. Active thresholds are unverified.";
        return "No conflict with the entered values in this snapshot. Active thresholds are unverified.";
    }
}
