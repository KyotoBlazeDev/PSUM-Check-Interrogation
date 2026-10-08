using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Windows.Storage.Pickers;

namespace PSUM_Check_Interrogation_WinUI_3;
public sealed partial class BusinessWorkspace : UserControl
{
    private Window? owner;
    internal void SetOwner(Window window) => owner = window;
    public event EventHandler? RecordsChanged;
    internal event Action<BatteryDiagnostics>? ReadingCaptured;
    internal void AcceptTelemetry(BatteryDiagnostics sample)
    {
        if (capturing) return;
        var previousId = reading?.BatteryId ?? events.LastOrDefault(e => e.Machine == Environment.MachineName)?.BatteryId;
        if (sample.Issue is null && previousId is not null && previousId != sample.BatteryId)
        {
            Manufacturer.Text = Model.Text = Serial.Text = Fru.Text = Notes.Text = "";
            ResetObservations();
        }
        reading = sample;
        capturedAt = DateTimeOffset.UtcNow;
        Render();
    }
    internal IReadOnlyList<string> MaintenanceAlerts()
    {
        try { return BusinessPolicy.Alerts(store.Read(), Environment.MachineName, DateTimeOffset.UtcNow); }
        catch (Exception ex) { DiagnosticsLog.Error("Audit.Alerts", ex); return new[] { "Business audit history could not be read. Review diagnostic logs." }; }
    }
    private readonly BusinessStore store = new();
    private BatteryDiagnostics? reading;
    private IReadOnlyList<BusinessEvent> events = Array.Empty<BusinessEvent>();
    private DateTimeOffset capturedAt;
    private bool capturing;
    private readonly DispatcherTimer dueTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    public BusinessWorkspace()
    {
        InitializeComponent();
        AppMetadata.Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3)} • Developed by KyotoBlazeDev";


        foreach (var box in new[] { Swelling, Heat, Leakage, Odor }) { box.ItemsSource = new[] { "Unknown", "Absent", "Present" }; box.SelectedIndex = 0; }
        Visual.ItemsSource = new[] { "Unknown", "Normal", "Damaged" }; Visual.SelectedIndex = 0;
        try
        {
            events = store.Read();
            DiagnosticsLog.Info("Audit.Load", $"records={events.Count}");
            var last = events.LastOrDefault(e => e.Machine == Environment.MachineName && !string.IsNullOrWhiteSpace(e.Asset));
            if (last is not null)
            {
                Asset.Text = last.Asset; Manufacturer.Text = last.Manufacturer; Model.Text = last.Model;
                Serial.Text = last.Serial; Fru.Text = last.Fru;
            }
            Render();
        }
        catch (Exception ex) { DiagnosticsLog.Error("Audit.Load", ex); Notify(ex.Message, true); }
        dueTimer.Tick += (_, _) => Render();
        Loaded += (_, _) => dueTimer.Start();
        Unloaded += (_, _) => dueTimer.Stop();
    }
    private void Notify(string text, bool error = false) { Message.Message = text; Message.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success; Message.IsOpen = true; }
    private BusinessEvent Create(string kind, DateTimeOffset? due = null) => new(Guid.NewGuid(), DateTimeOffset.UtcNow,
        Environment.UserDomainName + "\\" + Environment.UserName, Environment.MachineName, Asset.Text.Trim(), reading!.BatteryId, kind, reading,
        kind == "Inspection" ? Swelling.SelectedItem?.ToString() : null, kind == "Inspection" ? Heat.SelectedItem?.ToString() : null,
        kind == "Inspection" ? Leakage.SelectedItem?.ToString() : null, kind == "Inspection" ? Odor.SelectedItem?.ToString() : null,
        kind == "Inspection" ? Visual.SelectedItem?.ToString() : null, Notes.Text.Trim(), due, Manufacturer.Text.Trim(), Model.Text.Trim(), Serial.Text.Trim(), Fru.Text.Trim());
    private bool Ready()
    {
        if (capturing) { Notify("Wait for the battery capture to finish.", true); return false; }
        if (reading is null || reading.Issue is not null || DateTimeOffset.UtcNow - capturedAt > TimeSpan.FromMinutes(5)) { Notify("Capture a valid battery reading within five minutes before recording an event.", true); return false; }
        if (string.IsNullOrWhiteSpace(Asset.Text)) { Notify("Enter the asset tag.", true); return false; }
        return true;
    }
    private bool Save(BusinessEvent item)
    {
        try { store.Append(item); events = store.Read(); Render(); RecordsChanged?.Invoke(this, EventArgs.Empty); DiagnosticsLog.Info("Audit.Save", $"kind={item.Kind} records={events.Count}"); Notify("Record saved. " + (BusinessPolicy.Hazard(item) ? BusinessPolicy.Action(item, reading?.HealthPercent) : "")); return true; }
        catch (Exception ex) { DiagnosticsLog.Error("Audit.Save", ex); Notify("Could not save audit record: " + ex.Message, true); return false; }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        capturing = true;
        InspectionButton.IsEnabled = ReplacementButton.IsEnabled = false;
        try
        {
            DiagnosticsLog.Info("Battery.Capture.Start");
            var previousId = reading?.BatteryId ?? events.LastOrDefault(e => e.Machine == Environment.MachineName)?.BatteryId;
            reading = await BatteryDiagnosticsProvider.ReadAsync(); capturedAt = DateTimeOffset.UtcNow;
            ReadingCaptured?.Invoke(reading);
            DiagnosticsLog.Info("Battery.Capture.Complete", $"healthAvailable={reading.HealthPercent is not null} voltageAvailable={reading.VoltageMv is not null} temperatureAvailable={reading.TemperatureC is not null}");
            if (reading.Issue is not null) DiagnosticsLog.Warning("Battery.Capture.Unavailable", "reason=" + DiagnosticsLog.BatteryIssue(reading.Issue));
            if (!string.IsNullOrWhiteSpace(reading.SourceWarning)) DiagnosticsLog.Warning("Battery.Capture.Partial", "reason=" + DiagnosticsLog.BatteryIssue(reading.SourceWarning));
            if (reading.Issue is null)
            {
                if (previousId is not null && previousId != reading.BatteryId)
                {
                    Manufacturer.Text = Model.Text = Serial.Text = Fru.Text = Notes.Text = "";
                    ResetObservations();
                }
                Save(Create("Reading"));
            }
            else { Render(); Notify(reading.Issue, true); }
        }
        catch (Exception ex) { DiagnosticsLog.Error("Battery.Capture", ex); Notify(ex.Message, true); }
        finally { capturing = false; RefreshButton.IsEnabled = InspectionButton.IsEnabled = ReplacementButton.IsEnabled = true; }
    }
    private void Inspection_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        if (new[] { Swelling, Heat, Leakage, Odor, Visual }.Any(c => c.SelectedIndex == 0)) { Notify("Complete all five physical observations.", true); return; }
        if (double.IsNaN(Interval.Value) || Interval.Value < 1 || Interval.Value > 365) { Notify("Choose an inspection interval between 1 and 365 days.", true); return; }
        var item = Create("Inspection", DateTimeOffset.UtcNow.AddDays(Interval.Value));
        if (BusinessPolicy.Hazard(item)) item = item with { NextDue = DateTimeOffset.UtcNow };
        if (Save(item)) { ResetObservations(); Notes.Text = ""; }
    }
    private void Replacement_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready()) return;
        if (string.IsNullOrWhiteSpace(Serial.Text) || string.IsNullOrWhiteSpace(Notes.Text)) { Notify("Enter the new battery serial / inventory ID and replacement ticket or reason.", true); return; }
        var previousSerial = events.LastOrDefault(e => e.Machine == Environment.MachineName && (e.Kind is "Inspection" or "Replacement") && !string.IsNullOrWhiteSpace(e.Serial))?.Serial;
        if (string.Equals(previousSerial, Serial.Text.Trim(), StringComparison.OrdinalIgnoreCase)) { Notify("The replacement needs a different serial / inventory ID from the previous battery.", true); return; }
        if (Save(Create("Replacement")))
        {
            ResetObservations(); Notes.Text = "";
            reading = null; // Require a new capture before inspecting the replacement.
            Render();
        }
    }
    private void ResetObservations() { foreach (var box in new[] { Swelling, Heat, Leakage, Odor, Visual }) box.SelectedIndex = 0; }
    private void Render()
    {
        var batteryId = reading is { Issue: null } ? reading.BatteryId : events.LastOrDefault(e => e.Machine == Environment.MachineName)?.BatteryId;
        var current = events.Where(e => e.Machine == Environment.MachineName && e.BatteryId == batteryId).ToArray();
        var replacement = current.LastOrDefault(e => e.Kind == "Replacement");
        var active = current.Where(e => replacement is null || e.At >= replacement.At).ToArray();
        var check = active.LastOrDefault(e => e.Kind == "Inspection");
        var r = reading ?? active.LastOrDefault()?.Reading;
        string F(object? value, string unit = "") => value is null ? "Unavailable" : value + unit;
        Dashboard.Text = r is null ? "Capture a reading to begin." : $"{Environment.MachineName} • {(reading is null ? "Last recorded reading" : $"Captured {capturedAt.ToLocalTime():g}" + (DateTimeOffset.UtcNow - capturedAt > TimeSpan.FromMinutes(5) ? " • Refresh required" : ""))}\nHealth: {BusinessPolicy.Classify(r.HealthPercent)} • {F(r.HealthPercent, "%")}\nFCC / design: {F(r.FullChargeCapacityMWh)} / {F(r.DesignCapacityMWh)} (reported capacity units)\nCycles: {F(r.CycleCount)} • Voltage: {F(r.VoltageMv, " mV")} • Temperature: {F(r.TemperatureC, " °C")}\nStatus: {F(r.Status)}" + string.Concat(new[] { r.Issue, r.VoltageWarning, r.SourceWarning }.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => "\n" + w));
        Recommendation.Text = (check is null ? "Complete a physical inspection. " : "") + BusinessPolicy.Action(check, r?.HealthPercent);
        Due.Text = check?.NextDue is DateTimeOffset due ? $"Inspection {(due <= DateTimeOffset.UtcNow ? "OVERDUE" : "due")}: {due.ToLocalTime():g}" : "Inspection DUE: no completed inspection for this battery lifecycle.";
        var points = active.Where(e => e.Kind == "Reading" && e.Reading?.HealthPercent is not null).ToArray();
        Trend.Text = points.Length < 2 ? "Trend: capture readings over time to measure degradation." : $"Trend: {points.Length} readings since {points[0].At.ToLocalTime():g}; capacity change {points[^1].Reading!.HealthPercent - points[0].Reading!.HealthPercent:+0.0;-0.0;0.0} percentage points.\n" + string.Join("  →  ", points.TakeLast(8).Select(p => $"{p.At.ToLocalTime():d}: {p.Reading!.HealthPercent:0.0}%"));
        History.Text = string.Join("\n\n", events.Reverse().Select(e => $"{e.At.ToLocalTime():g} • {e.Kind} • {e.Asset} • {e.Operator}\nBattery {e.BatteryId} • {BusinessPolicy.Classify(e.Reading?.HealthPercent)} • {e.Reading?.HealthPercent}%\n{(e.Kind == "Inspection" ? $"Swelling {e.Swelling}; heat {e.Heat}; leakage {e.Leakage}; odor {e.Odor}; visual {e.Visual}\n" : "")}{e.Manufacturer} {e.Model} • Serial {e.Serial} • FRU {e.Fru}\n{e.Notes}"));
    }
    private async System.Threading.Tasks.Task Export(bool json)
    {
        try
        {
            var snapshot = store.Read();
            var picker = new FileSavePicker { SuggestedFileName = "PSUM-Business-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") };
            picker.FileTypeChoices.Add(json ? "JSON audit records" : "CSV audit records", new[] { json ? ".json" : ".csv" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner ?? throw new InvalidOperationException("Workspace is not attached to its window.")));
            var file = await picker.PickSaveFileAsync();
            if (file is null) { DiagnosticsLog.Info("Export.Cancelled"); return; }
            await File.WriteAllTextAsync(file.Path, json ? JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }) : BusinessStore.Csv(snapshot));
            Notify("Export saved: " + file.Path);
            DiagnosticsLog.Info("Export.Complete", $"format={(json ? "JSON" : "CSV")} records={snapshot.Count}");
        }
        catch (Exception ex) { DiagnosticsLog.Error("Export.Failed", ex); Notify("Export failed: " + ex.Message, true); }
    }
    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsLog.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiagnosticsLog.Folder) { UseShellExecute = true });
        }
        catch (Exception ex) { DiagnosticsLog.Error("Logs.Open", ex); Notify("Could not open diagnostic logs: " + ex.Message, true); }
    }
    private async void Json_Click(object sender, RoutedEventArgs e) => await Export(true);
    private async void Csv_Click(object sender, RoutedEventArgs e) => await Export(false);
}
