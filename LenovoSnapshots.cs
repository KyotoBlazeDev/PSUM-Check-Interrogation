using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PSUM_Check_Interrogation_WinUI_3;

internal sealed record LenovoBatteryReading(string? BatteryId, string? Barcode, string DataJson);

internal static class LenovoSnapshots
{
    public static readonly string[] Fields = ["Adapter", "BarCode", "BatteryHealth", "BatteryID", "ChargeCompletionTime", "Condition", "CycleCount", "DesignCapacity", "DesignVoltage", "DeviceChemistry", "FirmwareVersion", "FirstUseDate", "FRUPartNumber", "FullChargeCapacity", "ManufactureDate", "Manufacturer", "RemainingCapacity", "RemainingPercentage", "RemainingTime", "Status", "Temperature", "Voltage", "Wattage"];
    private static readonly string Query = "$ErrorActionPreference = 'Stop'; Get-CimInstance -Namespace 'root/Lenovo' -ClassName 'Lenovo_Battery' -ErrorAction Stop | Select-Object -Property " + string.Join(',', Fields) + " | ConvertTo-Json -Compress -Depth 4";

    public static async Task<IReadOnlyList<LenovoBatteryReading>> ReadAsync()
    {
        using var process = new Process { StartInfo = new ProcessStartInfo {
            FileName = "powershell.exe", Arguments = "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(Query)),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        if (!process.Start()) throw new InvalidOperationException("Lenovo WMI query could not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException("Lenovo WMI query timed out."); }
        string stderr = await error;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(stderr.Contains("0x80041003", StringComparison.OrdinalIgnoreCase) || stderr.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
                ? "Access to Lenovo Battery WMI was denied." : "Lenovo Battery WMI is unavailable: " + stderr.Trim());
        string json = await output;
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = JsonDocument.Parse(json);
        var elements = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToArray() : [document.RootElement];
        return elements.Where(element => element.ValueKind == JsonValueKind.Object).Select(element =>
            new LenovoBatteryReading(Value(element, "BatteryID"), Value(element, "BarCode"), element.GetRawText())).ToArray();
    }

    private static string? Value(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
}
