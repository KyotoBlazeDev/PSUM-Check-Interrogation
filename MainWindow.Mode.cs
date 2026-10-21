using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace PSUM_Check_Interrogation_WinUI_3;

public sealed partial class MainWindow
{
    private bool _businessMode;
    private bool _modeReady;
    private readonly ModeSettings _modeSettings = new();

    private void InitializeMode()
    {
        ModePreferenceText.Text = "Preference loaded for this Windows account. First launch defaults to Standard.";
        try { _businessMode = _modeSettings.Load(); }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("Mode.Load", ex);
            ModePreferenceText.Text = "Using the Standard fallback. Select a mode to save a new preference.";
            ModeMessage.Severity = InfoBarSeverity.Warning;
            ModeMessage.Title = "Saved mode could not be read";
            ModeMessage.Message = "Using Standard mode because the saved preference could not be read. " + ex.Message;
            ModeMessage.IsOpen = true;
        }
        ModeSelector.SelectedIndex = _businessMode ? 1 : 0;
        ApplyMode();
        AppNavigation.SelectedItem = _businessMode ? BusinessNav : DashboardNav;
        _modeReady = true;
    }

    private void ModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_modeReady) return;
        bool business = ModeSelector.SelectedIndex == 1;
        try
        {
            _modeSettings.Save(business);
            _businessMode = business;
            ApplyMode();
            ModePreferenceText.Text = "Preference saved for this Windows account and future launches.";
            ModeMessage.Severity = InfoBarSeverity.Success;
            ModeMessage.Title = "Mode saved";
            ModeMessage.Message = $"{(_businessMode ? "Business" : "Standard")} mode is active.";
            ModeMessage.IsOpen = true;
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("Mode.Save", ex);
            _modeReady = false;
            ModeSelector.SelectedIndex = _businessMode ? 1 : 0;
            _modeReady = true;
            ModeMessage.Severity = InfoBarSeverity.Error;
            ModeMessage.Title = "Mode preference could not be saved";
            ModeMessage.Message = ex.Message;
            ModeMessage.IsOpen = true;
        }
    }

    private void ApplyMode()
    {
        ActiveModeText.Text = _businessMode ? "Active mode: Business" : "Active mode: Standard";
        Title = _businessMode ? "PSUM Check — Business" : "PSUM Check — Standard";
        AppNavigation.PaneTitle = _businessMode ? "PSUM Business" : "PSUM Standard";
        BusinessNav.Visibility = _businessMode ? Visibility.Visible : Visibility.Collapsed;
        OpenBusinessButton.Visibility = BusinessNav.Visibility;
        if (!_businessMode && ReferenceEquals(AppNavigation.SelectedItem, BusinessNav))
            AppNavigation.SelectedItem = DashboardNav;
        DashboardNav.Content = _businessMode ? "Live telemetry" : "Dashboard";
        HistoryNav.Content = _businessMode ? "Telemetry history" : "History";
        ModePolicyText.Text = _businessMode
            ? "Business capacity policy: Healthy ≥80%; Watch 70–<80%; Degraded 50–<70%; Critical <50%. Physical hazards take precedence over capacity advice."
            : "Standard capacity policy: Good ≥80%; Service recommended 60–<80%; Poor 30–<60%; Critical <30%.";
        ReplacementAlert.Message = _businessMode ? "Capacity health is below 50%." : "Capacity health is below 30%.";
        _lastKnownHealth = null;
        ApplyDiagnostics(_diagnostics);
    }

    private void ModeBackup_Click(object sender, RoutedEventArgs e) => AppNavigation.SelectedItem = DataNav;
    private void OpenBusiness_Click(object sender, RoutedEventArgs e)
    {
        if (_businessMode) AppNavigation.SelectedItem = BusinessNav;
    }
}
