using System;
using System.IO;

namespace PSUM_Check_Interrogation_WinUI_3;

internal static class ModePolicy
{
    public static string Classify(double? health, bool business) => business
        ? BusinessPolicy.Classify(health)
        : health is null || !double.IsFinite(health.Value) || health < 0 ? "Unknown"
        : health >= 80 ? "Good" : health >= 60 ? "Service recommended" : health >= 30 ? "Poor" : "Critical";
}

internal sealed class ModeSettings
{
    private readonly string path;
    public ModeSettings(string? path = null) => this.path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSUM", "mode.txt");

    public bool Load()
    {
        if (!File.Exists(path)) return false;
        return File.ReadAllText(path).Trim() switch
        {
            "Standard" => false,
            "Business" => true,
            _ => throw new InvalidDataException("The saved mode is invalid. Select a mode to save a new preference.")
        };
    }

    public void Save(bool business)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, business ? "Business" : "Standard");
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
