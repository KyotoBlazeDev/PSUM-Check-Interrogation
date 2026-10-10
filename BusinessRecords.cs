using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PSUM_Check_Interrogation_WinUI_3;

internal sealed record BusinessEvent(Guid Id, DateTimeOffset At, string Operator, string Machine,
    string Asset, string BatteryId, string Kind, BatteryDiagnostics? Reading,
    string? Swelling, string? Heat, string? Leakage, string? Odor, string? Visual,
    string Notes, DateTimeOffset? NextDue, string Manufacturer, string Model, string Serial, string Fru);

internal static class BusinessPolicy
{
    public static BusinessEvent[] Lifecycle(IEnumerable<BusinessEvent> records, string machine, string asset, string? batteryId)
    {
        var current = records.Where(e => e.Machine == machine && e.Asset == asset && e.BatteryId == batteryId).OrderBy(e => e.At).ThenBy(e => e.Id).ToArray();
        int replacement = Array.FindLastIndex(current, e => e.Kind == "Replacement");
        return replacement < 0 ? current : current.Skip(replacement).ToArray();
    }

    public static BatteryDiagnostics InspectionReading(BatteryDiagnostics? sample, DateTimeOffset capturedAt, DateTimeOffset now, string? knownId, string serial)
    {
        if (sample is { Issue: null } && now - capturedAt <= TimeSpan.FromMinutes(5)) return sample;
        if (string.IsNullOrWhiteSpace(knownId) && string.IsNullOrWhiteSpace(serial))
            throw new InvalidDataException("Enter the battery serial / inventory ID to identify an inspection without telemetry.");
        return BatteryDiagnostics.Unknown("Physical observation only; current telemetry unavailable. " + (sample?.Issue ?? "No recent valid capture.")) with
        { BatteryId = knownId ?? BatteryIdentity.Create("label", "inventory", serial.Trim()), Status = "Unavailable" };
    }

    public static IReadOnlyList<string> Alerts(IEnumerable<BusinessEvent> records, string machine, DateTimeOffset now)
    {
        var result = new List<string>();
        foreach (var group in records.Where(e => e.Machine == machine && !string.IsNullOrWhiteSpace(e.Asset)).GroupBy(e => (e.Asset, e.BatteryId)))
        {
            var active = Lifecycle(group, machine, group.Key.Asset, group.Key.BatteryId);
            var inspection = active.LastOrDefault(e => e.Kind == "Inspection");
            if (inspection is not null && Hazard(inspection)) result.Add($"{group.Key.Asset}: physical hazard reported. {Action(inspection, null)}");
            if (inspection?.NextDue is not DateTimeOffset due) result.Add($"{group.Key.Asset}: physical inspection due; none completed for this lifecycle.");
            else if (due <= now.AddDays(14)) result.Add($"{group.Key.Asset}: inspection {(due <= now ? "overdue" : "due soon")} ({due.ToLocalTime():d}).");
        }
        return result;
    }
    public static string Classify(double? health) => health is null || !double.IsFinite(health.Value) || health < 0 ? "Unknown" : health >= 80 ? "Healthy" : health >= 70 ? "Watch" : health >= 50 ? "Degraded" : "Critical";
    public static bool Hazard(BusinessEvent e) => new[] { e.Swelling, e.Heat, e.Leakage, e.Odor }.Contains("Present") || e.Visual == "Damaged";
    public static string Action(BusinessEvent? inspection, double? health) => inspection is not null && Hazard(inspection)
        ? "Stop use and charging. Follow your organization's battery incident procedure; contact IT for replacement."
        : Classify(health) switch { "Critical" => "Prioritize replacement and protect work from unexpected shutdown.", "Degraded" => "Plan replacement; inspect condition and confirm required runtime.", "Watch" => "Review capacity trend and shorten the next inspection interval.", "Healthy" => "Continue routine inspections.", _ => "Obtain a valid reading and complete a physical inspection." };
}

internal sealed class BusinessStore
{
    public string Folder { get; }
    public BusinessStore(string? folder = null) => Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSUM", "BusinessEdition", "events");
    public IReadOnlyList<BusinessEvent> Read()
    {
        Directory.CreateDirectory(Folder);
        return Directory.EnumerateFiles(Folder, "*.json").Select(path =>
        {
            var item = JsonSerializer.Deserialize<BusinessEvent>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty audit record: " + path);
            Validate(item);
            return item;
        }).OrderBy(e => e.At).ThenBy(e => e.Id).ToArray();
    }
    public void Append(BusinessEvent item)
    {
        Validate(item);
        Read(); // Do not add to a corrupt or unreadable audit history.
        Directory.CreateDirectory(Folder);
        if (File.Exists(Path.Combine(Folder, item.Id + ".json"))) throw new IOException("Audit record already exists.");
        string temporary = Path.Combine(Folder, item.Id + ".tmp");
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, item, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        File.Move(temporary, Path.Combine(Folder, item.Id + ".json"), false);
    }
    private static void Validate(BusinessEvent item)
    {
        if (item.Id == Guid.Empty || item.At == default || string.IsNullOrWhiteSpace(item.Operator) || string.IsNullOrWhiteSpace(item.Machine) || string.IsNullOrWhiteSpace(item.BatteryId) || item.Kind is not ("Reading" or "Inspection" or "Replacement") || item.Reading is null)
            throw new InvalidDataException("Audit record is missing required identity, timestamp or reading fields.");
        if (item.Kind == "Inspection" && (string.IsNullOrWhiteSpace(item.Asset) || item.NextDue is null || new[] { item.Swelling, item.Heat, item.Leakage, item.Odor }.Any(value => value is not ("Absent" or "Present")) || item.Visual is not ("Normal" or "Damaged")))
            throw new InvalidDataException("Audit inspection is incomplete.");
        if (item.Kind == "Replacement" && (string.IsNullOrWhiteSpace(item.Asset) || string.IsNullOrWhiteSpace(item.Serial) || string.IsNullOrWhiteSpace(item.Notes)))
            throw new InvalidDataException("Replacement requires an asset, new identifier and service evidence.");
    }
    public static string Csv(IEnumerable<BusinessEvent> events)
    {
        static string Q(object? value) => "\"" + (value is string text ? Safe(text) : Convert.ToString(value, CultureInfo.InvariantCulture))?.Replace("\"", "\"\"") + "\"";
        static string Safe(string? value) => !string.IsNullOrEmpty(value) && "=+-@\t\r\n".Contains(value[0]) ? "'" + value : value ?? "";
        var lines = new List<string> { "Id,Timestamp,Operator,Machine,Asset,BatteryId,Kind,HealthPercent,Classification,DesignCapacity,FCC,Cycles,VoltageMv,TemperatureC,Swelling,Heat,Leakage,Odor,Visual,Notes,NextDue,Manufacturer,Model,Serial,FRU" };
        foreach (var e in events) lines.Add(string.Join(",", new object?[] { e.Id,e.At.ToString("O"),Safe(e.Operator),Safe(e.Machine),Safe(e.Asset),e.BatteryId,e.Kind,e.Reading?.HealthPercent,BusinessPolicy.Classify(e.Reading?.HealthPercent),e.Reading?.DesignCapacityMWh,e.Reading?.FullChargeCapacityMWh,e.Reading?.CycleCount,e.Reading?.VoltageMv,e.Reading?.TemperatureC,e.Swelling,e.Heat,e.Leakage,e.Odor,e.Visual,Safe(e.Notes),e.NextDue?.ToString("O"),Safe(e.Manufacturer),Safe(e.Model),Safe(e.Serial),Safe(e.Fru) }.Select(Q)));
        return string.Join(Environment.NewLine, lines);
    }
}
