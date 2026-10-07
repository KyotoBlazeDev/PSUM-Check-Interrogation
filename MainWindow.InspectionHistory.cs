using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PSUM_Check_Interrogation_WinUI_3;

public sealed partial class MainWindow
{
    private void ApplyInspectionFilters_Click(object sender, RoutedEventArgs e) => ApplyInspectionFilters();

    private void ApplyInspectionFilters()
    {
        try
        {
            var dates = HistoryFeatures.ParseFilter(null, null, InspectionFromFilter.Text, InspectionToFilter.Text);
            string result = InspectionResultFilter.SelectedItem as string ?? "All results";
            _shownInspectionHistory = _inspectionHistory.Where(check =>
                (result == "All results" || check.Result == result) &&
                (dates.From is null || DateOnly.FromDateTime(check.CheckedAt.LocalDateTime) >= dates.From) &&
                (dates.To is null || DateOnly.FromDateTime(check.CheckedAt.LocalDateTime) <= dates.To)).ToArray();
            StoredInspectionHistory.ItemsSource = _shownInspectionHistory.Select(check =>
                new DisplayRow(check, $"{check.CheckedAt.LocalDateTime:g} · {check.Result} · {check.EvidenceId}")).ToArray();
            InspectionComparison.Text = _shownInspectionHistory.Count == 0 ? "No matching inspections." : "";
        }
        catch (ArgumentException ex) { ShowStorageMessage(InfoBarSeverity.Warning, "Invalid inspection filter", ex.Message); }
    }

    private static string AnswerText(bool? value) => value is null ? "Not checked" : value.Value ? "Yes" : "No";
    private static string InspectionText(StoredBatteryInspection check) =>
        $"PSUM CHECK INTERROGATION\nStored battery inspection evidence\n\nEvidence ID: {check.EvidenceId}\nLocal record ID: {check.Id}\nBattery profile: {check.ProfileId}\nSaved at: {check.CheckedAt:o}\nResult: {check.Result}\nPhysically inspected: {AnswerText(check.PhysicallyInspected)}\nSwelling/deformation: {AnswerText(check.Swelling)}\nLeakage: {AnswerText(check.Leakage)}\nUnusual odor: {AnswerText(check.UnusualOdor)}\nUnexpected warmth: {AnswerText(check.UnexpectedWarmth)}\nCharge: {(check.ChargeMeasured && check.ChargePercent is int c ? $"{c}%" : "Not checked")}\nVoltage: {(check.VoltageMeasured && check.Voltage is double v ? $"{v:g} V" : "Not checked")}\n";

    private void CompareInspections_Click(object sender, RoutedEventArgs e)
    {
        var selected = StoredInspectionHistory.SelectedItems.OfType<DisplayRow>().Select(item => item.Value).OfType<StoredBatteryInspection>().ToArray();
        if (selected.Length != 2) { InspectionComparison.Text = "Select exactly two inspections to compare."; return; }
        var left = selected[0]; var right = selected[1];
        InspectionComparison.Text = $"Saved inspection comparison: {left.CheckedAt:g} | {right.CheckedAt:g}\n" +
            $"Result: {left.Result} | {right.Result}\n" +
            $"Physical inspection: {AnswerText(left.PhysicallyInspected)} | {AnswerText(right.PhysicallyInspected)}\n" +
            $"Swelling: {AnswerText(left.Swelling)} | {AnswerText(right.Swelling)}\n" +
            $"Leakage: {AnswerText(left.Leakage)} | {AnswerText(right.Leakage)}\n" +
            $"Odor: {AnswerText(left.UnusualOdor)} | {AnswerText(right.UnusualOdor)}\n" +
            $"Warmth: {AnswerText(left.UnexpectedWarmth)} | {AnswerText(right.UnexpectedWarmth)}\n" +
            $"Charge: {left.ChargePercent?.ToString() ?? "Not checked"} | {right.ChargePercent?.ToString() ?? "Not checked"}\n" +
            $"Voltage: {left.Voltage?.ToString(CultureInfo.InvariantCulture) ?? "Not checked"} | {right.Voltage?.ToString(CultureInfo.InvariantCulture) ?? "Not checked"}";
    }

    private async void ExportInspectionText_Click(object sender, RoutedEventArgs e)
    {
        if (StoredInspectionHistory.SelectedItems.Count != 1 || StoredInspectionHistory.SelectedItems[0] is not DisplayRow { Value: StoredBatteryInspection check })
        { ShowStorageMessage(InfoBarSeverity.Warning, "Select one inspection", "Select exactly one inspection for a text export."); return; }
        await SaveInspectionExportAsync("psum-inspection-" + check.EvidenceId, ".txt", InspectionText(check));
    }

    private async void ExportInspectionsCsv_Click(object sender, RoutedEventArgs e)
    {
        var csv = new StringBuilder("evidence_id,checked_at,profile_id,result,physically_inspected,swelling,leakage,unusual_odor,unexpected_warmth,charge_percent,voltage\r\n");
        foreach (var check in _shownInspectionHistory)
            csv.AppendLine(string.Join(',', new[] { check.EvidenceId, check.CheckedAt.ToString("o"), check.ProfileId, check.Result,
                AnswerText(check.PhysicallyInspected), AnswerText(check.Swelling), AnswerText(check.Leakage), AnswerText(check.UnusualOdor), AnswerText(check.UnexpectedWarmth),
                check.ChargePercent?.ToString(), check.Voltage?.ToString(CultureInfo.InvariantCulture) }.Select(CsvValue)));
        await SaveInspectionExportAsync("psum-stored-inspections", ".csv", csv.ToString());
    }

    private async System.Threading.Tasks.Task SaveInspectionExportAsync(string name, string extension, string content)
    {
        try
        {
            var picker = new FileSavePicker(AppWindow.Id) { SuggestedFileName = name, DefaultFileExtension = extension, Title = "Export stored battery inspections" };
            picker.FileTypeChoices.Add(extension == ".csv" ? "CSV files" : "Text files", new List<string> { extension });
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await File.WriteAllTextAsync(file.Path, content, new UTF8Encoding(false));
            ShowStorageMessage(InfoBarSeverity.Success, "Export saved", file.Path);
        }
        catch (Exception ex) { ShowStorageError(ex); }
    }
}
