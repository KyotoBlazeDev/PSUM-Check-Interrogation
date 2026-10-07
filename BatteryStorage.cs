using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PSUM_Check_Interrogation_WinUI_3;

internal sealed record RegistryBatteryProfile(
    string ProfileId, bool IsCurrent, string? Slot,
    int? DesignCapacityMWh, int? FullChargeBeforeResetMWh,
    int? ChargeStartPercent, int? ChargeStopPercent,
    bool? ChargeStartEnabled, bool? ChargeStopEnabled, int? ErrorCode)
{
    public double? CachedRatio => DesignCapacityMWh > 0 && FullChargeBeforeResetMWh > 0
        ? Math.Round((double)FullChargeBeforeResetMWh.Value / DesignCapacityMWh.Value * 100, 1)
        : null;
}

internal static class LenovoProfileReader
{
    private const string ProfilePath = @"SOFTWARE\WOW6432Node\Lenovo\PWRMGRV\ConfKeys\Data";

    public static IReadOnlyList<RegistryBatteryProfile> Read()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var root = machine.OpenSubKey(ProfilePath, writable: false);
            if (root is null) return [];
            string[] children = root.GetSubKeyNames();
            var slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string child in children.Where(name => name.StartsWith("Battery", StringComparison.OrdinalIgnoreCase)))
            {
                using var key = root.OpenSubKey(child, writable: false);
                string? barcode = key?.GetValue("Barcode Number")?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(barcode)) slots[barcode] = child;
            }

            var profiles = new List<RegistryBatteryProfile>();
            foreach (string child in children)
            {
                using var key = root.OpenSubKey(child, writable: false);
                if (key is null) continue;
                int? design = Positive(key.GetValue("DesignCapacity"));
                int? full = Positive(key.GetValue("FullChargeCapacityBeforeGaugeReset"));
                if (design is null && full is null) continue;
                string? slot = slots.GetValueOrDefault(child);
                profiles.Add(new RegistryBatteryProfile(
                    child, slot is not null, slot, design, full,
                    Percent(key.GetValue("ChargeStartPercentage")),
                    Percent(key.GetValue("ChargeStopPercentage")),
                    Flag(key.GetValue("ChargeStartControl")),
                    Flag(key.GetValue("ChargeStopControl")),
                    Nonnegative(key.GetValue("ErrorCode"))));
            }
            return profiles.OrderBy(profile => !profile.IsCurrent)
                .ThenBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException or ArgumentException)
        {
            return [];
        }
    }

    private static int? Nonnegative(object? value) =>
        int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int number) && number >= 0 ? number : null;
    private static int? Positive(object? value) => Nonnegative(value) is int number && number > 0 ? number : null;
    private static int? Percent(object? value) => Nonnegative(value) is int number && number <= 100 ? number : null;
    private static bool? Flag(object? value) => Nonnegative(value) is int number ? number == 1 : null;
}

internal static class BatteryLifecycles
{
    public const string Installed = "Installed";
    public const string Stored = "Stored";
    public const string AttentionRequired = "Attention required";
    public const string Retired = "Retired";
    public const string Recycled = "Recycled";
    public const string UnknownHistorical = "Unknown historical profile";
    public static readonly string[] All = [Installed, Stored, AttentionRequired, Retired, Recycled, UnknownHistorical];
}

internal sealed record StoredBatteryInspection(
    long Id, string ProfileId, DateTimeOffset CheckedAt, bool PhysicallyInspected,
    bool? Swelling, bool? Leakage, bool? UnusualOdor, bool? UnexpectedWarmth,
    bool ChargeMeasured, bool VoltageMeasured, int? ChargePercent, double? Voltage,
    string EvidenceId)
{
    public string Result => !PhysicallyInspected ? "CHECK INCOMPLETE" :
        Swelling == true || Leakage == true || UnusualOdor == true || UnexpectedWarmth == true
            ? "STOP CHECK" : "CHECK COMPLETE";
}

internal static class StoredBatteryRules
{
    public static void Validate(StoredBatteryInspection check)
    {
        if (string.IsNullOrWhiteSpace(check.ProfileId)) throw new ArgumentException("Battery profile is required.");
        if (check.PhysicallyInspected &&
            (check.Swelling is null || check.Leakage is null || check.UnusualOdor is null || check.UnexpectedWarmth is null))
            throw new ArgumentException("Complete every physical condition question.");
        if (check.ChargeMeasured && check.ChargePercent is null) throw new ArgumentException("Enter the measured charge percentage.");
        if (check.ChargePercent is < 0 or > 100) throw new ArgumentException("Charge must be between 0 and 100 percent.");
        if (check.VoltageMeasured && check.Voltage is null) throw new ArgumentException("Enter the measured voltage.");
        if (check.Voltage is <= 0) throw new ArgumentException("Voltage must be greater than zero.");
        if (check.Result == "STOP CHECK" &&
            (check.ChargeMeasured || check.VoltageMeasured || check.ChargePercent is not null || check.Voltage is not null))
            throw new ArgumentException("Stop the check after a safety concern; do not record measurements.");
    }

    public static string DefaultLifecycle(StoredBatteryInspection? latest) =>
        latest?.Result == "STOP CHECK" ? BatteryLifecycles.AttentionRequired :
        latest?.PhysicallyInspected == true ? BatteryLifecycles.Stored : BatteryLifecycles.UnknownHistorical;

    public static DateTimeOffset? NextCheck(StoredBatteryInspection? latest, string lifecycle, DateTimeOffset now)
    {
        if (lifecycle != BatteryLifecycles.Stored || latest?.Result == "STOP CHECK") return null;
        if (latest is null) return now;
        return latest.Result == "CHECK INCOMPLETE" ? latest.CheckedAt.AddDays(7) : latest.CheckedAt.AddMonths(4);
    }

    public static string Action(StoredBatteryInspection? latest, string lifecycle, DateTimeOffset now)
    {
        if (lifecycle == BatteryLifecycles.Recycled) return "Recycled · closed";
        if (latest?.Result == "STOP CHECK") return "STOP CHECK · safety concern";
        if (lifecycle == BatteryLifecycles.AttentionRequired) return "Attention required";
        if (lifecycle == BatteryLifecycles.Retired) return "Retired · no check scheduled";
        if (lifecycle == BatteryLifecycles.Installed) return "Installed · use live screen";
        if (lifecycle == BatteryLifecycles.UnknownHistorical) return "Historical · status unverified";
        if (latest is null) return "CHECK DUE";
        var due = NextCheck(latest, lifecycle, now);
        return due?.LocalDateTime.Date <= now.LocalDateTime.Date ? "CHECK OVERDUE" :
            $"Next check {due?.LocalDateTime:yyyy-MM-dd}";
    }
}
