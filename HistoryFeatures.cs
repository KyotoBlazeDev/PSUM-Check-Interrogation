using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PSUM_Check_Interrogation_WinUI_3;

internal sealed record HistoryFilter(string? BatteryId, string? State, DateOnly? From, DateOnly? To);

internal static class HistoryFeatures
{
    public static HistoryFilter ParseFilter(string? batteryId, string? state, string fromText, string toText)
    {
        DateOnly? from = ParseDate(fromText);
        DateOnly? to = ParseDate(toText);
        if (from is not null && to is not null && from > to)
            throw new ArgumentException("From date must be on or before To date.");
        return new HistoryFilter(batteryId, state, from, to);
    }

    public static IReadOnlyList<BatteryCheckIn> Apply(IEnumerable<BatteryCheckIn> records, HistoryFilter filter) =>
        records.Where(record =>
        (filter.BatteryId is null || record.BatteryId == filter.BatteryId) &&
        (filter.State is null || record.State == filter.State) &&
        (filter.From is null || DateOnly.FromDateTime(record.RecordedAt.LocalDateTime) >= filter.From) &&
        (filter.To is null || DateOnly.FromDateTime(record.RecordedAt.LocalDateTime) <= filter.To)).ToList();

    public static string RenderText(BatteryCheckIn record)
    {
        string[] lines =
        [
            "PSUM CHECK INTERROGATION",
            "Battery check-in evidence",
            "",
            $"Evidence ID: {Available(record.EvidenceId)}",
            $"Local record ID: {record.Id}",
            $"Battery identity: {record.BatteryId}",
            $"Saved at: {Timestamp(record)}",
            "Telemetry acquisition time: Not recorded",
            "Original field sources: Not recorded with this check-in",
            "",
            $"Charge: {Number(record.ChargePercent, "%")}",
            $"State: {record.State}",
            $"Health: {Number(record.HealthPercent, "%")}",
            $"Cycle count: {Number(record.CycleCount)}",
            $"Note: {(string.IsNullOrEmpty(record.Note) ? "None" : record.Note)}",
            ""
        ];
        return string.Join('\n', lines);
    }

    public static string RenderCsv(IEnumerable<BatteryCheckIn> records)
    {
        var result = new StringBuilder();
        result.Append("evidence_id,recorded_at,battery_id,charge_percent,state,health_percent,cycle_count,note\r\n");
        foreach (var record in records)
        {
            object?[] values =
            [
                record.EvidenceId, Timestamp(record), record.BatteryId, record.ChargePercent,
                record.State, record.HealthPercent, record.CycleCount, record.Note
            ];
            result.AppendJoin(',', values.Select(CsvField));
            result.Append("\r\n");
        }
        return result.ToString();
    }

    private static DateOnly? ParseDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day;
        throw new ArgumentException("Use YYYY-MM-DD for dates, or leave a date blank.");
    }

    private static string Timestamp(BatteryCheckIn record) =>
        record.SavedTimestamp ?? record.RecordedAt.ToString("o", CultureInfo.InvariantCulture);

    private static string Available(string value) => string.IsNullOrEmpty(value) ? "Unavailable" : value;
    private static string Number(int? value, string unit = "") =>
        value is null ? "Unavailable" : value.Value.ToString(CultureInfo.InvariantCulture) + unit;
    private static string Number(double? value, string unit) =>
        value is null ? "Unavailable" : value.Value.ToString("0.0################", CultureInfo.InvariantCulture) + unit;

    private static string CsvField(object? value)
    {
        if (value is null) return "";
        string text = value switch
        {
            double number => number.ToString("0.0################", CultureInfo.InvariantCulture),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
        if (value is string && text.TrimStart().StartsWith('=') ||
            value is string && text.TrimStart().StartsWith('+') ||
            value is string && text.TrimStart().StartsWith('-') ||
            value is string && text.TrimStart().StartsWith('@'))
            text = "'" + text;
        if (text.IndexOfAny([',', '"', '\r', '\n']) >= 0)
            text = "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }
}
