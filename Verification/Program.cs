using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using PSUM_Check_Interrogation_WinUI_3;

static void Check(bool condition, string description) { if (!condition) throw new Exception(description); Console.WriteLine("PASS: " + description); }
string testRoot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "verify", Guid.NewGuid().ToString("N")[..8]);
string modePath = Path.Combine(testRoot, "mode", "mode.txt");
var modeSettings = new ModeSettings(modePath);
Check(!modeSettings.Load(), "First launch defaults to Standard");
modeSettings.Save(true);
Check(new ModeSettings(modePath).Load(), "Business mode persists across launches");
modeSettings.Save(false);
Check(!new ModeSettings(modePath).Load(), "Switching back to Standard persists");
File.WriteAllText(modePath, "invalid");
try { modeSettings.Load(); throw new Exception("Invalid mode silently accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS: Invalid mode preference is reported"); }
Check(ModePolicy.Classify(80, false) == "Good" && ModePolicy.Classify(60, false) == "Service recommended" && ModePolicy.Classify(30, false) == "Poor" && ModePolicy.Classify(29.9, false) == "Critical", "Standard policy boundaries are preserved");
Check(ModePolicy.Classify(49.9, true) == "Critical" && ModePolicy.Classify(49.9, false) == "Poor", "Switching mode changes the applicable capacity policy");
Check(ModePolicy.Classify(null, false) == "Unknown" && ModePolicy.Classify(double.NaN, false) == "Unknown", "Standard unknown readings remain unknown");
Check(BusinessPolicy.Classify(null) == "Unknown", "Missing capacity is not healthy");
Check(BusinessPolicy.Classify(80) == "Healthy" && BusinessPolicy.Classify(79.9) == "Watch" && BusinessPolicy.Classify(70) == "Watch" && BusinessPolicy.Classify(69.9) == "Degraded" && BusinessPolicy.Classify(50) == "Degraded" && BusinessPolicy.Classify(49.9) == "Critical", "Health classification boundaries");
BatteryDiagnostics Parse(string json) { using var doc = JsonDocument.Parse(json); return (BatteryDiagnostics)typeof(BatteryDiagnosticsProvider).GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { doc.RootElement })!; }
Check(Parse("{}").Issue is not null, "Absent hardware stays unknown");
Check(Parse("{\"Win32\":[{},{}]}").Issue is not null, "Multiple batteries rejected");
Check(Parse("{\"Static\":[{\"InstanceName\":\"A\"}],\"Full\":[{\"InstanceName\":\"B\"}]}").Issue is not null, "Conflicting battery identities rejected");
var reading = Parse("{\"Static\":[{\"InstanceName\":\"A\",\"DesignedCapacity\":50000}],\"Full\":[{\"InstanceName\":\"A\",\"FullChargedCapacity\":40000}],\"Temperature\":[{\"InstanceName\":\"A\",\"Temperature\":2982}],\"Cycle\":[{\"InstanceName\":\"A\",\"CycleCount\":0}]}");
Check(reading.HealthPercent == 80 && reading.TemperatureC == 25.1 && reading.CycleCount is null, "Capacity ratio, Kelvin conversion, unavailable cycle handling");
var item = new BusinessEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,"tester","machine","=formula",reading.BatteryId,"Inspection",reading,"Present","Absent","Absent","Absent","Normal","Line one, \"quoted\"\nLine two",DateTimeOffset.UtcNow.AddDays(90),"maker","model","serial","fru");
Check(BusinessPolicy.Action(item, 100).StartsWith("Stop use"), "Physical hazards override healthy capacity");
string folder = Path.Combine(testRoot, "records");
var store = new BusinessStore(folder);
store.Append(item);
var restored = store.Read().Single();
Check(restored == item, "Audit record preserves all data through JSON");
try { store.Append(item); throw new Exception("Duplicate record overwrote history"); } catch (IOException) { Check(store.Read().Single() == item, "Duplicate ID cannot overwrite history"); }
Check(BusinessStore.Csv(new[] { item }).Contains("\"'=formula\"") && BusinessStore.Csv(new[] {item}).Contains("\"\"quoted\"\""), "CSV escapes quotes and neutralizes spreadsheet formulas");
File.WriteAllText(Path.Combine(folder, "corrupt.json"), "bad json");
try { store.Read(); throw new Exception("Corrupt history was hidden"); } catch (JsonException) { Console.WriteLine("PASS: Corrupt history surfaces an error"); }
try { store.Append(item with { Id = Guid.NewGuid() }); throw new Exception("Allowed writes despite corrupt history"); } catch (JsonException) { Console.WriteLine("PASS: Corrupt history blocks new writes"); }
var validStore = new BusinessStore(Path.Combine(testRoot, "valid"));
try { validStore.Append(item with { Swelling = "Unknown" }); throw new Exception("Incomplete inspection accepted"); } catch (InvalidDataException) { Console.WriteLine("PASS: Incomplete inspections rejected in storage"); }
Directory.CreateDirectory(validStore.Folder);
File.WriteAllText(Path.Combine(validStore.Folder, "invalid.json"), "{}");
try { validStore.Read(); throw new Exception("Empty object accepted as audit record"); } catch (InvalidDataException) { Console.WriteLine("PASS: Semantically invalid audit records rejected"); }
Check(BusinessPolicy.Classify(double.NaN) == "Unknown" && BusinessPolicy.Classify(double.PositiveInfinity) == "Unknown", "Nonfinite health is unknown");
Check(Parse("{\"Static\":[{\"InstanceName\":\"A\",\"Tag\":1}],\"Full\":[{\"InstanceName\":\"A\",\"Tag\":2}]}").Issue is not null, "Battery tag mismatch blocks mixed readings");
var zero = Parse("{\"Dynamic\":[{\"RemainingCapacity\":0,\"Discharging\":true,\"Charging\":false,\"PowerOnline\":false,\"ChargeRate\":100,\"DischargeRate\":200}]}");
Check(zero.RemainingCapacityMWh == 0 && zero.RateMw == -200 && zero.Status == "Discharging", "Empty capacity preserved and discharge rate uses active state");
Check(Parse("{\"Dynamic\":[{}]}").Status == "Unavailable", "Missing status flags are not presented as idle");
Check(reading.HealthLabel == BusinessPolicy.Classify(reading.HealthPercent), "Export and dashboard classifications agree");
Check(Parse("{\"QueryIssues\":[\"Access is denied\"]}").Issue!.Contains("could not be read"), "Denied WMI access is not mislabeled as absent hardware");
Check(Parse("{\"Win32\":[{}],\"QueryIssues\":[\"Optional temperature unavailable\"]}").SourceWarning!.Contains("temperature"), "Partial data warnings are preserved");
if (args.Contains("--hardware")) {
    var hardware = await BatteryDiagnosticsProvider.ReadAsync();
    Console.WriteLine($"Hardware query: {hardware.HealthLabel}; health {hardware.HealthPercent}; status {hardware.Status}; issue {hardware.Issue ?? "none"}");
    Check(hardware.Issue is null, "Live Windows battery query succeeds");
}
string logFolder = Path.Combine(testRoot, "logs");
var logger = new DiagnosticWriter(logFolder, 1024, 3);
Check(Enumerable.Range(0, 20).All(i => logger.Write("INFO", "Test.Write", new string('x', 400))), "Repeated diagnostic writes succeed");
var logFiles = Directory.GetFiles(logFolder, "diagnostic-*.log");
Check(logFiles.Length <= 3 && logFiles.All(f => new FileInfo(f).Length <= 1024), "Log rotation and retention bound storage");
Check(logger.Write("WARN", "Test.Newline", "one\r\nFAKE ENTRY"), "Diagnostic newline write succeeds");
Check(Directory.GetFiles(logFolder, "diagnostic-*.log").SelectMany(File.ReadAllLines).Any(line => line.Contains("one  FAKE ENTRY")), "Newlines cannot inject log entries");
string blockedPath = Path.Combine(AppContext.BaseDirectory, "blocked-log-" + Guid.NewGuid());
File.WriteAllText(blockedPath, "not a directory");
Check(!new DiagnosticWriter(blockedPath).Write("ERROR", "Test.Failure", ""), "Unwritable logging fails without throwing");
var diagnostic = DiagnosticsLog.Describe(new IOException("SECRET-ASSET-AND-NOTES", new Exception("SECRET-SERIAL")));
Check(!diagnostic.Contains("SECRET") && diagnostic.Contains("IOException") && diagnostic.Contains("HRESULT"), "Exception logs omit private messages but preserve exception type and code");
Check(DiagnosticsLog.BatteryIssue("BatteryStaticData: Access is denied") == "AccessDenied", "Hardware warnings use diagnostic categories");
Check(BusinessPolicy.Alerts(new[] {item}, "machine", item.At).Any(alert => alert.Contains("physical hazard")), "Business hazards appear in unified alerts");
Check(!BusinessPolicy.Alerts(new[] { item }, "other machine", item.At).Any(), "Business alerts filter by machine");
var replacementEvent = item with { Id = Guid.NewGuid(), Kind = "Replacement", At = item.At.AddSeconds(1), Swelling = null, Heat = null, Leakage = null, Odor = null, Visual = null, NextDue = null };
var resetAlerts = BusinessPolicy.Alerts(new[] { item, replacementEvent }, "machine", item.At.AddSeconds(2));
Check(!resetAlerts.Any(alert => alert.Contains("physical hazard")) && resetAlerts.Any(alert => alert.Contains("none completed")), "Replacement resets business hazard and inspection lifecycle in alerts");
Check(CheckInStorage.DefaultPath.EndsWith(Path.Combine("PSUM", "psum.sqlite3")), "Unified modes preserve the Standard telemetry database");
string telemetryFolder = Path.Combine(testRoot, "telemetry");
Directory.CreateDirectory(telemetryFolder);
var telemetry = new CheckInStorage(Path.Combine(telemetryFolder, "telemetry.sqlite3"));
var checkin = new BatteryCheckIn(0, DateTimeOffset.UtcNow, "test-battery", 80, "On AC", 90, 10, "=formula", Guid.NewGuid().ToString());
telemetry.Add(checkin);
Check(telemetry.Recent().Single().EvidenceId == checkin.EvidenceId, "Reused SQLite history preserves evidence ID");
Check(HistoryFeatures.RenderCsv(telemetry.Recent()).Contains("'=formula"), "Reused history export neutralizes spreadsheet formulas");
string backupPath = Path.Combine(telemetryFolder, "backup.sqlite3");
telemetry.BackupDatabase(backupPath);
telemetry.Add(checkin with { EvidenceId = Guid.NewGuid().ToString() });
string safetyPath = telemetry.RestoreDatabase(backupPath);
Check(telemetry.Recent().Count == 1 && File.Exists(safetyPath) && new CheckInStorage(safetyPath).Recent().Count == 2, "Reused backup and restore preserve a safety copy");
var storedInspection = new StoredBatteryInspection(0, "profile", DateTimeOffset.UtcNow, false, null, null, null, null, false, false, null, null, Guid.NewGuid().ToString());
Check(StoredBatteryRules.NextCheck(storedInspection, BatteryLifecycles.Stored, storedInspection.CheckedAt) == storedInspection.CheckedAt.AddDays(7), "Reused stored-battery incomplete inspection schedules seven-day followup");
Console.WriteLine("All verification checks passed.");
