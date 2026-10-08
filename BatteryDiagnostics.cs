using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PSUM_Check_Interrogation_WinUI_3;

internal sealed record BatteryDiagnostics(
    double? HealthPercent,
    double? WearPercent,
    string HealthLabel,
    uint? DesignCapacityMWh,
    uint? FullChargeCapacityMWh,
    uint? CycleCount,
    string? Issue,
    string BatteryId,
    uint? RemainingCapacityMWh = null, uint? VoltageMv = null, uint? ReportedVoltageMv = null,
    uint? DesignVoltageMv = null, double? TemperatureC = null, int? RateMw = null,
    string? VoltageWarning = null, string? Status = null)
{
    public string? SourceWarning { get; init; }
    public static BatteryDiagnostics Unknown(string? issue = null) =>
        new(null, null, "Unknown", null, null, null, issue, BatteryIdentity.Create(null, null, null));
}

internal static class BatteryIdentity
{
    public static string Create(string? manufacturer, string? name, string? deviceId)
    {
        string source = string.Join("|", new[] { manufacturer, name, deviceId }.Select(value =>
            string.IsNullOrEmpty(value) ? "unknown" : value));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16].ToLowerInvariant();
    }
}

internal static class BatteryDiagnosticsProvider
{
    // Windows PowerShell 5.1 is intentional: BatteryStaticData may fail with
    // "Generic failure" through CIM on machines where Get-WmiObject succeeds.
    private const string Query = """
        $queryIssues = New-Object 'System.Collections.Generic.List[string]'
        function Read-BatteryClass($class, $ns, $legacy) {
            try {
                if ($legacy) { Get-WmiObject -Namespace $ns -Class $class -ErrorAction Stop }
                else { Get-CimInstance -Namespace $ns -ClassName $class -ErrorAction Stop }
            } catch { $queryIssues.Add($class + ': ' + $_.Exception.Message) }
        }
        $win32 = @(Read-BatteryClass 'Win32_Battery' 'root/cimv2' $false | Select-Object Name,DeviceID)
        $static = @(Read-BatteryClass 'BatteryStaticData' 'root/wmi' $true | Select-Object InstanceName,Tag,DesignedCapacity,DesignedVoltage,ManufactureName)
        $full = @(Read-BatteryClass 'BatteryFullChargedCapacity' 'root/wmi' $false | Select-Object InstanceName,Tag,FullChargedCapacity)
        $cycles = @(Read-BatteryClass 'BatteryCycleCount' 'root/wmi' $false | Select-Object InstanceName,Tag,CycleCount)
        $dynamic = @(Read-BatteryClass 'BatteryStatus' 'root/wmi' $false | Select-Object InstanceName,Tag,RemainingCapacity,Voltage,ChargeRate,DischargeRate,PowerOnline,Charging,Discharging)
        $temperature = @(Read-BatteryClass 'BatteryTemperature' 'root/wmi' $false | Select-Object InstanceName,Tag,Temperature)
        [pscustomobject]@{Win32=$win32;Static=$static;Full=$full;Cycle=$cycles;Dynamic=$dynamic;Temperature=$temperature;QueryIssues=@($queryIssues.ToArray())} | ConvertTo-Json -Depth 4 -Compress
        """;

    public static async Task<BatteryDiagnostics> ReadAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(Query)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            if (!process.Start()) return BatteryDiagnostics.Unknown("Windows WMI query could not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                DiagnosticsLog.Warning("BatteryQuery.Timeout");
                return BatteryDiagnostics.Unknown("Windows WMI query timed out.");
            }
            await error;
            if (process.ExitCode != 0) { DiagnosticsLog.Warning("BatteryQuery.Exit", $"exitCode={process.ExitCode}"); return BatteryDiagnostics.Unknown("Windows WMI query failed."); }
            using var document = JsonDocument.Parse(await output);
            return Parse(document.RootElement);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or JsonException or System.IO.IOException)
        {
            DiagnosticsLog.Error("BatteryQuery.Failed", ex);
            return BatteryDiagnostics.Unknown("Windows WMI data is unavailable.");
        }
    }

    private static BatteryDiagnostics Parse(JsonElement root)
    {
        var win32 = Rows(root, "Win32");
        var groups = new[] { Rows(root, "Static"), Rows(root, "Full"), Rows(root, "Cycle"), Rows(root, "Dynamic"), Rows(root, "Temperature") };
        string? sourceWarning = root.TryGetProperty("QueryIssues", out var issues) && issues.ValueKind == JsonValueKind.Array
            ? string.Join("; ", issues.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString())) : null;
        if (win32.Length == 0 && groups.All(rows => rows.Length == 0))
            return BatteryDiagnostics.Unknown(string.IsNullOrWhiteSpace(sourceWarning) ? "No battery data detected." : "Battery data could not be read. " + sourceWarning);
        if (win32.Length > 1 || groups.Any(rows => rows.Length > 1))
            return BatteryDiagnostics.Unknown("Multiple batteries detected; per-battery readings are hidden.");

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in groups.SelectMany(rows => rows))
        {
            if (row.TryGetProperty("InstanceName", out var name) && name.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(name.GetString()))
                identities.Add(name.GetString()!);
        }
        if (identities.Count > 1)
            return BatteryDiagnostics.Unknown("Battery WMI identities disagree; per-battery readings are hidden.");
        var tags = groups.SelectMany(rows => rows).Select(row => Positive(row, "Tag")).Where(tag => tag is not null).Distinct().ToArray();
        if (tags.Length > 1)
            return BatteryDiagnostics.Unknown("Battery changed during the query; capture another reading.");

        uint? design = Positive(groups[0].FirstOrDefault(), "DesignedCapacity");
        uint? full = Positive(groups[1].FirstOrDefault(), "FullChargedCapacity");
        uint? cycles = Positive(groups[2].FirstOrDefault(), "CycleCount");
        string batteryId = BatteryIdentity.Create(
            Text(groups[0].FirstOrDefault(), "ManufactureName"),
            Text(win32.FirstOrDefault(), "Name"),
            Text(win32.FirstOrDefault(), "DeviceID"));
        var dynamic = groups[3].FirstOrDefault();
        uint? remaining = Nonnegative(dynamic, "RemainingCapacity");
        uint? designVoltage = Positive(groups[0].FirstOrDefault(), "DesignedVoltage");
        uint? reportedVoltage = Positive(dynamic, "Voltage");
        string? voltageWarning = reportedVoltage is uint rv && rv >= 100_000 ? $"Reported voltage {rv / 1000.0:0.00} V is invalid." :
            reportedVoltage is uint rv2 && designVoltage is uint dv && (rv2 < dv * 0.7 || rv2 > dv * 1.3)
                ? $"Reported voltage {rv2 / 1000.0:0.00} V differs greatly from design voltage {dv / 1000.0:0.00} V." : null;
        uint? voltage = voltageWarning is null ? reportedVoltage : null;
        uint? rawTemperature = Positive(groups[4].FirstOrDefault(), "Temperature");
        double? temperature = rawTemperature is uint t && t / 10.0 - 273.15 is double c && c >= -50 && c <= 150
            ? Math.Round(c, 1) : null;
        uint? chargingRate = Positive(dynamic, "ChargeRate");
        uint? dischargingRate = Positive(dynamic, "DischargeRate");
        bool Flag(string property) => dynamic.ValueKind == JsonValueKind.Object && dynamic.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;
        int? rate = Flag("Charging") && chargingRate is uint cr ? (int)Math.Min(cr, int.MaxValue) :
            Flag("Discharging") && dischargingRate is uint dr ? -(int)Math.Min(dr, int.MaxValue) : null;
        if (design is null || full is null)
            return new(null, null, "Unknown", design, full, cycles, null, batteryId, remaining, voltage, reportedVoltage, designVoltage, temperature, rate, voltageWarning, StatusText(dynamic)) { SourceWarning = sourceWarning };

        double percent = Math.Clamp((double)full.Value / design.Value * 100, 0, 100);
        double rounded = Math.Round(percent, 1);
        string label = BusinessPolicy.Classify(rounded);
        return new(rounded, Math.Round(100 - percent, 1), label, design, full, cycles, null, batteryId, remaining, voltage, reportedVoltage, designVoltage, temperature, rate, voltageWarning, StatusText(dynamic)) { SourceWarning = sourceWarning };
    }

    private static string StatusText(JsonElement row) => row.ValueKind != JsonValueKind.Object || !new[] { "Charging", "Discharging", "PowerOnline" }.All(p => row.TryGetProperty(p, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) ? "Unavailable" : new[] { "Charging", "Discharging", "PowerOnline" }.Where(p => row.GetProperty(p).ValueKind == JsonValueKind.True).DefaultIfEmpty("Idle / offline").Aggregate((a, b) => a + ", " + b);
    private static string? Text(JsonElement row, string property) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static JsonElement[] Rows(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value)) return [];
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToArray(),
            JsonValueKind.Object => [value],
            _ => []
        };
    }

    private static uint? Positive(JsonElement row, string property)
        => Nonnegative(row, property) is uint number && number > 0 ? number : null;

    private static uint? Nonnegative(JsonElement row, string property)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint number) && number != uint.MaxValue)
            return number;
        return null;
    }
}
