using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using Windows.System;
using Windows.UI.Core;

namespace PSUM_Check_Interrogation_WinUI_3;

public sealed partial class MainWindow
{
    private int _tipIndex;
    private bool _compact;
    private (double percent, string label)? _lastKnownHealth;
    private static readonly (string title, string body)[] Tips = [
        ("Know the source", "Live charge comes from Windows. Lenovo snapshots are saved vendor-cache observations."),
        ("Unknown is useful", "A missing temperature or voltage means the firmware did not provide a trustworthy reading."),
        ("Check stored batteries", "Use Battery Storage for physical inspections. Alerts shows checks due soon or overdue."),
        ("Keep your evidence", "Back up the PSUM database before moving to another PC or changing its storage.")
    ];

    private void InitializeOriginalFeatures()
    {
        foreach (int seconds in new[] { 10, 30, 60, 120, 300 }) RefreshInterval.Items.Add(seconds);
        RefreshInterval.SelectedItem = 10;
        InspectionResultFilter.Items.Add("All results");
        foreach (string result in new[] { "CHECK COMPLETE", "CHECK INCOMPLETE", "STOP CHECK" }) InspectionResultFilter.Items.Add(result);
        InspectionResultFilter.SelectedIndex = 0;
        try
        {
            MascotImage.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Serviam_robot_mascot.png")));
            PartsImage.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "l15_gen2_hmm_exploded.png")));
        }
        catch (Exception) { /* Text content remains usable when an optional image is missing. */ }
        ShowTip();
        AppNavigation.KeyDown += App_KeyDown;
    }

    private void ShowTip()
    {
        TipTitle.Text = Tips[_tipIndex].title;
        TipBody.Text = Tips[_tipIndex].body;
    }
    private void PreviousTip_Click(object sender, RoutedEventArgs e) { _tipIndex = (_tipIndex + Tips.Length - 1) % Tips.Length; ShowTip(); }
    private void NextTip_Click(object sender, RoutedEventArgs e) { _tipIndex = (_tipIndex + 1) % Tips.Length; ShowTip(); }

    private void RefreshInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshTimer is null || RefreshInterval.SelectedItem is not int seconds) return;
        _refreshTimer.Interval = TimeSpan.FromSeconds(seconds);
        _checkInStorage?.SetSetting("refresh_interval_seconds", seconds.ToString());
    }

    private void LoadOriginalSettings(bool onLaunch = true)
    {
        if (_checkInStorage is null) return;
        if (int.TryParse(_checkInStorage.GetSetting("refresh_interval_seconds"), out int seconds) && RefreshInterval.Items.Contains(seconds))
            RefreshInterval.SelectedItem = seconds;
        RemindAtLaunch.IsChecked = _checkInStorage.GetSetting("launch_reminders") == "1";
        UpdateAlerts();
        if (onLaunch && RemindAtLaunch.IsChecked == true && AlertsText.Text != "No current alerts. Saved data and firmware availability may be incomplete.")
            AppNavigation.SelectedItem = AlertsNav;
    }

    private void RemindAtLaunch_Click(object sender, RoutedEventArgs e)
    {
        _checkInStorage?.SetSetting("launch_reminders", RemindAtLaunch.IsChecked == true ? "1" : "0");
        UpdateAlerts();
    }

    private void RefreshAlerts_Click(object sender, RoutedEventArgs e) => UpdateAlerts();

    private void UpdateAlerts()
    {
        if (AlertsText is null) return;
        var messages = _storageEntries.Select(entry =>
        {
            var due = StoredBatteryRules.NextCheck(entry.Latest, entry.Lifecycle, DateTimeOffset.Now);
            return (entry, due);
        }).Where(item => item.due is not null && item.due.Value.LocalDateTime.Date <= DateTime.Today.AddDays(14))
          .Select(item => $"{item.entry.Profile.ProfileId}: inspection due {item.due!.Value.LocalDateTime:d} · {StoredBatteryRules.Action(item.entry.Latest, item.entry.Lifecycle, DateTimeOffset.Now)}").ToList();
        if (_chargePercent is int charge && charge <= 5)
            messages.Insert(0, $"CRITICAL CHARGE {charge}% / {(_acConnected == true ? "Keep the charger connected." : "Connect the charger and save your work.")}");
        if (_diagnostics.HealthPercent is double health)
        {
            _lastKnownHealth = (health, _diagnostics.HealthLabel);
            if (_diagnostics.HealthLabel == "Critical") messages.Insert(0, $"CRITICAL BATTERY HEALTH {health:0.0}% / Arrange battery replacement.");
            else if (_diagnostics.HealthLabel != "Good") messages.Insert(0, $"CURRENT BATTERY HEALTH {health:0.0}% / {_diagnostics.HealthLabel}.");
        }
        else if (_lastKnownHealth is { } last && last.label != "Good")
            messages.Insert(0, $"LAST KNOWN BATTERY HEALTH {last.percent:0.0}% / Current health reading unavailable.");
        foreach (var entry in _storageEntries.Where(entry => entry.Lifecycle == BatteryLifecycles.AttentionRequired))
            messages.Add($"{entry.Profile.ProfileId}: service attention required.");
        foreach (var snapshot in _snapshots.GroupBy(Label).Select(group => group.OrderByDescending(row => row.CapturedAt).First()))
        {
            string change = SnapshotChange(snapshot);
            if (change != "No significant change" && change != "No previous observation" && change != "Change unavailable")
                messages.Add($"Saved Lenovo cache / {Label(snapshot)}: {change} (Vantage update time unknown)");
        }
        AlertsText.Text = messages.Count == 0 ? "No current alerts. Saved data and firmware availability may be incomplete." : string.Join(Environment.NewLine + Environment.NewLine, messages);
    }

    private void UpdateVantagePolicy()
    {
        const string prefix = "feature.device-settings.power.wmi-battery";
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(@"SOFTWARE\Policies\Lenovo\Commercial Vantage", false);
            object? enabled = key?.GetValue(prefix);
            if (enabled is null) { VantagePolicyText.Text = "Vantage WMI policy: not configured"; return; }
            if (!int.TryParse(enabled.ToString(), out int on) || on == 0) { VantagePolicyText.Text = "Vantage WMI policy: disabled"; return; }
            string type = key?.GetValue(prefix + ".scheduletype")?.ToString() switch { "0" => "daily", "1" => "weekly", "2" => "monthly", string other => other, _ => "schedule unknown" };
            string? day = key?.GetValue(prefix + ".scheduleday")?.ToString();
            string? time = key?.GetValue(prefix + ".scheduletime")?.ToString();
            VantagePolicyText.Text = $"Vantage WMI policy: enabled; {type}{(type is "weekly" or "monthly" && day is not null ? " (day " + day + ")" : "")}{(time is not null ? " at " + time : "")}. Vantage controls cache updates.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            VantagePolicyText.Text = "Vantage WMI policy: unavailable (" + ex.Message + ")";
        }
    }

    private async void App_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
        NavigationViewItem? item = e.Key switch
        {
            VirtualKey.F1 => DashboardNav, VirtualKey.F2 => StorageNav, VirtualKey.F3 => PartsNav,
            VirtualKey.F4 => SourcesNav, VirtualKey.F6 => HistoryNav, VirtualKey.F7 => AlertsNav,
            VirtualKey.F8 => SourcesNav, VirtualKey.F10 => CheckInNav,
            VirtualKey.Number1 => DashboardNav, VirtualKey.Number2 => StorageNav,
            VirtualKey.Number3 => PartsNav, VirtualKey.Number4 => SourcesNav,
            VirtualKey.Number5 => CheckInNav,
            VirtualKey.H when ctrl => HistoryNav,
            VirtualKey.Escape => DashboardNav,
            _ => null
        };
        if (e.Key == VirtualKey.F5 || (ctrl && e.Key == VirtualKey.R))
        { RefreshPowerStatus(); await RefreshDiagnosticsAsync(); e.Handled = true; return; }
        if (e.Key == VirtualKey.F9) { _compact = !_compact; TipsCard.Visibility = HealthCard.Visibility = DashboardThresholdText.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible; AppNavigation.SelectedItem = DashboardNav; e.Handled = true; return; }
        if (e.Key == VirtualKey.F11)
        {
            AppWindow.SetPresenter(AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen ?
                Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped : Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            e.Handled = true; return;
        }
        if (ctrl && shift && e.Key == VirtualKey.C) { AppNavigation.SelectedItem = StorageNav; e.Handled = true; return; }
        if (item is null) return;
        if (e.OriginalSource is TextBox or NumberBox && e.Key is >= VirtualKey.Number1 and <= VirtualKey.Number5 && !ctrl) return;
        AppNavigation.SelectedItem = item;
        e.Handled = true;
    }
}
