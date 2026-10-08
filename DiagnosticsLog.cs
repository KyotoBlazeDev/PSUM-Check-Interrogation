using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PSUM_Check_Interrogation_WinUI_3;

internal static class DiagnosticsLog
{
    private static readonly DiagnosticWriter Writer = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSUM", "BusinessEdition", "logs"));
    public static string Folder => Writer.Folder;
    public static void Info(string operation, string detail = "") => Writer.Write("INFO", operation, detail);
    public static void Warning(string operation, string detail = "") => Writer.Write("WARN", operation, detail);
    public static void Error(string operation, Exception exception)
        => Writer.Write("ERROR", operation, Describe(exception));
    internal static string Describe(Exception exception)
    {
        // Exception messages/file paths may contain user inputs or account names.
        // Keep exception types, HRESULTs and method frames, without those values.
        var chain = new StringBuilder();
        for (Exception? current = exception; current is not null && chain.Length < 8000; current = current.InnerException)
        {
            chain.Append($"{current.GetType().FullName} HRESULT=0x{current.HResult:X8}; ");
            foreach (var frame in new StackTrace(current, false).GetFrames() ?? Array.Empty<StackFrame>())
            {
                var method = frame.GetMethod();
                chain.Append($"{method?.DeclaringType?.FullName}.{method?.Name} -> ");
                if (chain.Length >= 8000) break;
            }
        }
        return chain.ToString();
    }
    public static string BatteryIssue(string issue) => issue.ToLowerInvariant() switch
    {
        var text when text.Contains("access") || text.Contains("denied") => "AccessDenied",
        var text when text.Contains("timed out") => "Timeout",
        var text when text.Contains("multiple batteries") => "MultipleBatteries",
        var text when text.Contains("identities disagree") => "IdentityMismatch",
        var text when text.Contains("changed during") => "TagMismatch",
        var text when text.Contains("no battery") => "NoBatteryData",
        _ => "SourceUnavailable"
    };
}

internal sealed class DiagnosticWriter
{
    private readonly object gate = new();
    private readonly long maxBytes;
    private readonly int maxFiles;
    private readonly string session = Guid.NewGuid().ToString("N")[..12];
    private int segment;
    private string? currentPath;
    public string Folder { get; }
    public DiagnosticWriter(string folder, long maxBytes = 2 * 1024 * 1024, int maxFiles = 20)
    {
        Folder = folder;
        this.maxBytes = Math.Max(256, maxBytes);
        this.maxFiles = Math.Max(1, maxFiles);
    }
    public bool Write(string level, string operation, string detail)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Folder);
                string Clean(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
                string line = $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} session={session} {Clean(level)} {Clean(operation)} {Clean(detail)}";
                if (line.Length > 12000) line = line[..12000];
                line += Environment.NewLine;
                if (currentPath is null || (File.Exists(currentPath) && new FileInfo(currentPath).Length + Encoding.UTF8.GetByteCount(line) > maxBytes))
                    currentPath = Path.Combine(Folder, $"diagnostic-{DateTime.UtcNow:yyyyMMdd}-{Environment.ProcessId}-{session}-{segment++:D3}.log");
                File.AppendAllText(currentPath, line, new UTF8Encoding(false));
                var oldFiles = new DirectoryInfo(Folder).EnumerateFiles("diagnostic-*.log")
                    .Where(file => !string.Equals(file.FullName, currentPath, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => file.LastWriteTimeUtc).Skip(maxFiles - 1);
                foreach (var file in oldFiles) file.Delete();
                return true;
            }
        }
        catch (Exception) { return false; } // Diagnostic failures must not break the app or audit writes.
    }
}
