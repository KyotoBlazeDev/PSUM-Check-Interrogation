using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PSUM_Check_Interrogation_WinUI_3;

internal sealed record StorageEntry(RegistryBatteryProfile Profile, string Lifecycle, StoredBatteryInspection? Latest);

public sealed partial class MainWindow
{
    private IReadOnlyList<StorageEntry> _storageEntries = [];
    private bool _storageRefreshing;
    private bool _updatingInspectionForm;
    private IReadOnlyList<StoredBatteryInspection> _inspectionHistory = [];
    private IReadOnlyList<StoredBatteryInspection> _shownInspectionHistory = [];

    private void InitializeStorageControls()
    {
        foreach (string state in BatteryLifecycles.All) StorageLifecycle.Items.Add(state);
        foreach (ComboBox answer in InspectionAnswers())
        {
            answer.Items.Add("Yes");
            answer.Items.Add("No");
        }
    }

    private ComboBox[] InspectionAnswers() =>
    [
        StorageInspected, StorageSwelling, StorageLeakage, StorageOdor, StorageWarmth,
        StorageChargeMeasured, StorageVoltageMeasured
    ];

    private async void RefreshStorage_Click(object sender, RoutedEventArgs e) => await RefreshStorageAsync();

    private async Task RefreshStorageAsync()
    {
        if (_storageRefreshing || _checkInStorage is null) return;
        _storageRefreshing = true;
        string? selectedId = (StorageProfiles.SelectedItem as DisplayRow)?.Value is StorageEntry selected
            ? selected.Profile.ProfileId : null;
        try
        {
            var entries = await Task.Run(() =>
            {
                var profiles = LenovoProfileReader.Read().ToList();
                var known = new HashSet<string>(profiles.Select(profile => profile.ProfileId), StringComparer.OrdinalIgnoreCase);
                foreach (string id in _checkInStorage.KnownProfileIds())
                    if (known.Add(id)) profiles.Add(new RegistryBatteryProfile(id, false, null, null, null, null, null, null, null, null));
                return profiles.OrderBy(profile => !profile.IsCurrent)
                    .ThenBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
                    .Select(profile =>
                    {
                        var latest = _checkInStorage.StoredInspections(profile.ProfileId, 1).FirstOrDefault();
                        string lifecycle = _checkInStorage.GetLifecycle(profile.ProfileId, StoredBatteryRules.DefaultLifecycle(latest));
                        return new StorageEntry(profile, lifecycle, latest);
                    }).ToList();
            });
            _storageEntries = entries;
            UpdateAlerts();
            UpdateSources();
            var reminderIds = entries.Where(entry =>
            {
                var due = StoredBatteryRules.NextCheck(entry.Latest, entry.Lifecycle, DateTimeOffset.Now);
                return due is not null && due.Value.LocalDateTime.Date <= DateTime.Today.AddDays(14);
            }).Select(entry => entry.Profile.ProfileId).ToList();
            StorageReminder.Message = reminderIds.Count == 0 ? string.Empty :
                $"Due now or within 14 days: {string.Join(", ", reminderIds)}";
            StorageReminder.IsOpen = reminderIds.Count > 0;
            StorageIntro.Text = "Read-only Lenovo cache; freshness unknown. A slot association does not verify installation. Pre-reset capacity is cached and is not current battery health. Refresh rereads the cache only.";
            var displayed = entries.Select(entry =>
            {
                string slot = entry.Profile.Slot ?? "Unmapped / historical";
                string action = StoredBatteryRules.Action(entry.Latest, entry.Lifecycle, DateTimeOffset.Now);
                return new DisplayRow(entry, $"{entry.Profile.ProfileId}  ·  {slot}\n{entry.Lifecycle}  ·  {action}");
            }).ToArray();
            StorageProfiles.ItemsSource = displayed;
            if (entries.Count == 0)
            {
                StorageDetails.Text = "No retained or saved battery profiles found.";
                ApplyLifecycleButton.IsEnabled = SaveInspectionButton.IsEnabled = false;
            }
            else
            {
                StorageProfiles.SelectedItem = displayed.FirstOrDefault(item =>
                    (item.Value as StorageEntry)?.Profile.ProfileId == selectedId) ?? displayed.First();
            }
        }
        catch (Exception ex)
        {
            ShowStorageError(ex);
        }
        finally
        {
            _storageRefreshing = false;
        }
        await ShowSelectedStorageAsync();
    }

    private async void StorageSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_storageRefreshing) await ShowSelectedStorageAsync();
    }

    private async Task ShowSelectedStorageAsync()
    {
        if (StorageProfiles.SelectedItem is not DisplayRow { Value: StorageEntry entry } || _checkInStorage is null)
        {
            ApplyLifecycleButton.IsEnabled = SaveInspectionButton.IsEnabled = false;
            return;
        }
        StorageLifecycle.SelectedItem = entry.Lifecycle;
        ApplyLifecycleButton.IsEnabled = true;
        SaveInspectionButton.IsEnabled = entry.Lifecycle is not (BatteryLifecycles.Retired or BatteryLifecycles.Recycled);
        var profile = entry.Profile;
        string ratio = profile.CachedRatio is double value ? $"{value:0.0}%" : "Unknown";
        string design = profile.DesignCapacityMWh?.ToString("N0") ?? "Unknown";
        string full = profile.FullChargeBeforeResetMWh?.ToString("N0") ?? "Unknown";
        string start = profile.ChargeStartPercent?.ToString() ?? "Unknown";
        string stop = profile.ChargeStopPercent?.ToString() ?? "Unknown";
        string action = StoredBatteryRules.Action(entry.Latest, entry.Lifecycle, DateTimeOffset.Now);
        StorageDetails.Text = $"Battery: {profile.ProfileId}\nCached slot: {profile.Slot ?? "Unmapped"}\n" +
            $"Cached design: {design} mWh · pre-reset full: {full} mWh · ratio: {ratio}\n" +
            $"Cached thresholds: start {start}% ({Flag(profile.ChargeStartEnabled)}), stop {stop}% ({Flag(profile.ChargeStopEnabled)})\n" +
            $"Inspection action: {action}\n" +
            $"Last inspection: {(entry.Latest is null ? "Never" : $"{entry.Latest.CheckedAt.LocalDateTime:g} · {entry.Latest.Result}")}";
        ResetInspectionForm();
        try
        {
            var history = await Task.Run(() => _checkInStorage.StoredInspections(profile.ProfileId));
            if ((StorageProfiles.SelectedItem as DisplayRow)?.Value is not StorageEntry current || current.Profile.ProfileId != profile.ProfileId) return;
            _inspectionHistory = history;
            ApplyInspectionFilters();
        }
        catch (Exception ex)
        {
            ShowStorageError(ex);
        }
    }

    private static string Flag(bool? value) => value is null ? "unknown" : value.Value ? "enabled" : "disabled";

    private void ResetInspectionForm()
    {
        _updatingInspectionForm = true;
        foreach (ComboBox answer in InspectionAnswers()) answer.SelectedIndex = -1;
        StorageChargeValue.Text = StorageVoltageValue.Text = string.Empty;
        _updatingInspectionForm = false;
        UpdateInspectionFields();
    }

    private void InspectionAnswer_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingInspectionForm) UpdateInspectionFields();
    }

    private void UpdateInspectionFields()
    {
        if (StorageInspected is null || _updatingInspectionForm) return;
        _updatingInspectionForm = true;
        bool inspected = Answer(StorageInspected) == true;
        foreach (ComboBox condition in new[] { StorageSwelling, StorageLeakage, StorageOdor, StorageWarmth })
        {
            condition.IsEnabled = inspected;
            if (!inspected) condition.SelectedIndex = -1;
        }
        bool hazard = inspected && new[] { StorageSwelling, StorageLeakage, StorageOdor, StorageWarmth }
            .Any(condition => Answer(condition) == true);
        StorageChargeMeasured.IsEnabled = StorageVoltageMeasured.IsEnabled = !hazard;
        if (hazard)
        {
            StorageChargeMeasured.SelectedItem = "No";
            StorageVoltageMeasured.SelectedItem = "No";
            StorageChargeValue.Text = StorageVoltageValue.Text = string.Empty;
        }
        StorageChargeValue.IsEnabled = !hazard && Answer(StorageChargeMeasured) == true;
        StorageVoltageValue.IsEnabled = !hazard && Answer(StorageVoltageMeasured) == true;
        InspectionWarning.Text = hazard
            ? "STOP CHECK · Do not measure, charge, or use this battery. Arrange help from a Lenovo-authorized service provider."
            : "If a hazard is observed, stop the check and skip measurements.";
        _updatingInspectionForm = false;
    }

    private static bool? Answer(ComboBox answer) => (answer.SelectedItem as string) switch
    {
        "Yes" => true,
        "No" => false,
        _ => null
    };

    private async void ApplyLifecycle_Click(object sender, RoutedEventArgs e)
    {
        if (StorageProfiles.SelectedItem is not DisplayRow { Value: StorageEntry entry } ||
            StorageLifecycle.SelectedItem is not string lifecycle || _checkInStorage is null) return;
        try
        {
            ApplyLifecycleButton.IsEnabled = false;
            await Task.Run(() => _checkInStorage.SetLifecycle(entry.Profile.ProfileId, lifecycle));
            await RefreshStorageAsync();
            ShowStorageMessage(InfoBarSeverity.Success, "Lifecycle saved", $"{entry.Profile.ProfileId}: {lifecycle}");
        }
        catch (Exception ex)
        {
            ShowStorageError(ex);
        }
        finally
        {
            ApplyLifecycleButton.IsEnabled = true;
        }
    }

    private async void SaveInspection_Click(object sender, RoutedEventArgs e)
    {
        if (StorageProfiles.SelectedItem is not DisplayRow { Value: StorageEntry entry } || _checkInStorage is null) return;
        try
        {
            bool? inspected = Answer(StorageInspected);
            bool? chargeMeasured = Answer(StorageChargeMeasured);
            bool? voltageMeasured = Answer(StorageVoltageMeasured);
            if (inspected is null || chargeMeasured is null || voltageMeasured is null)
                throw new ArgumentException("Answer every Yes/No question.");
            bool? swelling = inspected.Value ? Answer(StorageSwelling) : null;
            bool? leakage = inspected.Value ? Answer(StorageLeakage) : null;
            bool? odor = inspected.Value ? Answer(StorageOdor) : null;
            bool? warmth = inspected.Value ? Answer(StorageWarmth) : null;
            int? charge = null;
            double? voltage = null;
            if (chargeMeasured.Value)
            {
                if (!int.TryParse(StorageChargeValue.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    throw new ArgumentException("Charge must be a whole number.");
                charge = number;
            }
            if (voltageMeasured.Value)
            {
                if (!double.TryParse(StorageVoltageValue.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                    throw new ArgumentException("Voltage must be a number.");
                voltage = number;
            }
            var inspection = new StoredBatteryInspection(0, entry.Profile.ProfileId, DateTimeOffset.Now,
                inspected.Value, swelling, leakage, odor, warmth, chargeMeasured.Value, voltageMeasured.Value,
                charge, voltage, Guid.NewGuid().ToString());
            StoredBatteryRules.Validate(inspection);
            SaveInspectionButton.IsEnabled = false;
            await Task.Run(() => _checkInStorage.AddStoredInspection(inspection));
            await RefreshStorageAsync();
            ShowStorageMessage(InfoBarSeverity.Success, "Inspection saved", $"Result: {inspection.Result} · Evidence ID: {inspection.EvidenceId}");
        }
        catch (Exception ex)
        {
            ShowStorageError(ex);
        }
        finally
        {
            SaveInspectionButton.IsEnabled = StorageProfiles.SelectedItem is DisplayRow { Value: StorageEntry current }
                && current.Lifecycle is not (BatteryLifecycles.Retired or BatteryLifecycles.Recycled);
        }
    }

    private void ShowStorageError(Exception ex) => ShowStorageMessage(InfoBarSeverity.Error, "Battery Storage action failed", ex.Message);

    private void ShowStorageMessage(InfoBarSeverity severity, string title, string message)
    {
        StorageMessage.Severity = severity;
        StorageMessage.Title = title;
        StorageMessage.Message = message;
        StorageMessage.IsOpen = true;
    }
}
