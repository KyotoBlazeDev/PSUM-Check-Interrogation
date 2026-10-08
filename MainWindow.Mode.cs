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
        try { _businessMode = _modeSettings.Load(); }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("Mode.Load", ex);
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
            ModeMessage.IsOpen = false;
            ApplyMode();
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Error("Mode.Save", ex);
            _modeReady = false;
            ModeSelector.SelectedIndex = _businessMode ? 1 : 0;
            _modeReady = true;
            ModeMessage.Message = ex.Message;
            ModeMessage.IsOpen = true;
        }
    }

    private void ApplyMode()
    {
        Title = _businessMode ? "PSUM Check — Business" : "PSUM Check — Standard";
        AppNavigation.PaneTitle = _businessMode ? "PSUM Business" : "PSUM Standard";
        BusinessNav.Visibility = _businessMode ? Visibility.Visible : Visibility.Collapsed;
        DashboardNav.Content = _businessMode ? "Live telemetry" : "Dashboard";
        HistoryNav.Content = _businessMode ? "Telemetry history" : "History";
        ModePolicyText.Text = _businessMode
            ? "Business capacity policy: Healthy ≥80%; Watch 70–<80%; Degraded 50–<70%; Critical <50%. Physical hazards take precedence over capacity advice."
            : "Standard capacity policy: Good ≥80%; Service recommended 60–<80%; Poor 30–<60%; Critical <30%.";
        ReplacementAlert.Message = _businessMode ? "Capacity health is below 50%." : "Capacity health is below 30%.";
        _lastKnownHealth = null;
        ApplyDiagnostics(_diagnostics);
    }
}
