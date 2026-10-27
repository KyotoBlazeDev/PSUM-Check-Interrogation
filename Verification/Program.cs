using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
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
foreach (bool business in new[] { false, true })
{
    var boundaries = business
        ? new[] { (80d, "Healthy", "Watch"), (70d, "Watch", "Degraded"), (50d, "Degraded", "Critical") }
        : new[] { (80d, "Good", "Service recommended"), (60d, "Service recommended", "Poor"), (30d, "Poor", "Critical") };
    foreach (var (boundary, at, below) in boundaries)
    {
        Check(ModePolicy.Classify(Math.BitDecrement(boundary), business) == below &&
              ModePolicy.Classify(boundary, business) == at &&
              ModePolicy.Classify(Math.BitIncrement(boundary), business) == at,
              $"{(business ? "Business" : "Standard")} exact boundary {boundary}%");
        uint fullCapacity = (uint)(boundary * 1000 - 40);
        var nearBoundary = Parse($$"""{"Static":[{"InstanceName":"A","DesignedCapacity":100000}],"Full":[{"InstanceName":"A","FullChargedCapacity":{{fullCapacity}}}]}""");
        Check(nearBoundary.HealthPercent < boundary && ModePolicy.Classify(nearBoundary.HealthPercent, business) == below &&
              nearBoundary.HealthLabel == BusinessPolicy.Classify(nearBoundary.HealthPercent),
              $"Capacity acquisition preserves classification below {boundary}% in {(business ? "Business" : "Standard")}");
    }
    foreach (double? invalid in new double?[] { null, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        Check(ModePolicy.Classify(invalid, business) == "Unknown", $"Invalid health {invalid} stays Unknown (business={business})");
    Check(ModePolicy.Classify(0, business) == "Critical", $"Valid zero health is Critical (business={business})");
}
foreach (string capacity in new[] { "0", "-1", "4294967295", "null" })
{
    var invalidCapacity = Parse($$"""{"Static":[{"DesignedCapacity":{{capacity}}}],"Full":[{"FullChargedCapacity":40000}]}""");
    Check(invalidCapacity.HealthPercent is null && invalidCapacity.HealthLabel == "Unknown", $"Invalid design capacity {capacity} stays Unknown");
}
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
Check(PowerStatusPolicy.State(null, null, false, null) == "Unknown" && PowerStatusPolicy.State(null, false, false, true) == "Unknown", "Failed or unavailable power readings are not absent batteries");
Check(PowerStatusPolicy.State(null, true, false, true) == "No battery", "Only confirmed battery absence is saved as No battery");
Check(PowerStatusPolicy.State(80, false, true, true) == "Charging" && PowerStatusPolicy.State(100, false, false, true) == "Full" && PowerStatusPolicy.State(80, false, false, false) == "Discharging", "Known power states are preserved");
var now = DateTimeOffset.UtcNow;
var offline = BusinessPolicy.InspectionReading(BatteryDiagnostics.Unknown("Access denied"), now, now, item.BatteryId, "");
Check(offline.HealthPercent is null && offline.BatteryId == item.BatteryId && offline.Issue!.Contains("Access denied"), "Offline observation preserves identity and failure without reusing capacity");
var offlineHazard = item with { Id = Guid.NewGuid(), Reading = offline };
var offlineStore = new BusinessStore(Path.Combine(testRoot, "offline"));
offlineStore.Append(offlineHazard);
Check(offlineStore.Read().Single() == offlineHazard && BusinessPolicy.Alerts(offlineStore.Read(), "machine", now).Any(a => a.Contains("physical hazard")), "Physical hazards persist and alert without telemetry");
var stale = BusinessPolicy.InspectionReading(reading, now.AddMinutes(-6), now, item.BatteryId, "");
Check(stale.HealthPercent is null && stale.Issue is not null, "Stale capacity is not saved as current physical inspection telemetry");
var labelOnly = BusinessPolicy.InspectionReading(null, default, now, null, "inventory-123");
Check(labelOnly.BatteryId == BatteryIdentity.Create("label", "inventory", "inventory-123") && labelOnly.HealthPercent is null, "New offline battery identity has label provenance");
try { BusinessPolicy.InspectionReading(null, default, now, null, ""); throw new Exception("Unidentified inspection accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS: Unidentified offline inspection rejected"); }
Check(BusinessPolicy.InspectionReading(reading, now, now, null, "") == reading, "Fresh valid telemetry remains attached to inspections");
var otherAsset = item with { Id = Guid.NewGuid(), Asset = "other-asset", At = item.At.AddSeconds(3) };
var afterReplacement = item with { Id = Guid.NewGuid(), At = item.At.AddSeconds(4), Swelling = "Absent" };
var lifecycle = BusinessPolicy.Lifecycle(new[] { item, replacementEvent, otherAsset, afterReplacement }, "machine", item.Asset, item.BatteryId);
Check(lifecycle.SequenceEqual(new[] { replacementEvent, afterReplacement }), "Workspace lifecycle excludes other assets and inspections before replacement");
Check(!BusinessPolicy.Lifecycle(new[] { item }, "machine", "new-asset", item.BatteryId).Any(), "New asset cannot inherit another asset's inspection or trend");
var provenanceHistory = new[] { item, otherAsset, item with { Id = Guid.NewGuid(), Machine = "other-machine", At = item.At.AddDays(1), Serial = "unrelated" } };
Check(BusinessPolicy.LatestAssetRecord(provenanceHistory, "machine", "  " + item.Asset + "  ") == item, "Asset lookup trims form whitespace and excludes other machines and assets");
Check(BusinessPolicy.LatestAssetRecord(provenanceHistory, "machine", "new asset") is null, "New asset has no inherited label provenance");
var relabeled = BusinessPolicy.AssetInspectionReading(provenanceHistory, "machine", item.Asset, "new-serial", reading, now, now);
Check(relabeled.HealthPercent is null && relabeled.Issue is not null && relabeled.BatteryId == BatteryIdentity.Create("label", "inventory", "new-serial"), "Changed serial cannot inherit fresh telemetry from the previous battery");
Check(BusinessPolicy.AssetInspectionReading(provenanceHistory, "machine", item.Asset, "  SERIAL  ", reading, now, now) == reading, "Unchanged label comparison ignores case and surrounding whitespace");
var knownOffline = BusinessPolicy.AssetInspectionReading(provenanceHistory, "machine", item.Asset, item.Serial, null, default, now);
Check(knownOffline.BatteryId == item.BatteryId && knownOffline.HealthPercent is null, "Offline inspection retains only the selected asset's known battery identity");
var newOffline = BusinessPolicy.AssetInspectionReading(provenanceHistory, "machine", "new asset", "new-label", null, default, now);
Check(newOffline.BatteryId == BatteryIdentity.Create("label", "inventory", "new-label") && newOffline.HealthPercent is null, "New offline asset uses explicit label identity without invented telemetry");
try { BusinessPolicy.AssetInspectionReading(provenanceHistory, "machine", "  ", "serial", reading, now, now); throw new Exception("Blank asset accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS: Inspection policy rejects blank asset tags"); }
Check(BusinessPolicy.InspectionReading(reading, now.AddMinutes(1), now, item.BatteryId, "").HealthPercent is null, "Future-dated capture is not fresh inspection evidence");
Check(BusinessPolicy.InspectionReading(null, default, now, "  ", "label-id").BatteryId == BatteryIdentity.Create("label", "inventory", "label-id"), "Blank known identity falls back to explicit label provenance");
var provenanceStore = new BusinessStore(Path.Combine(testRoot, "provenance"));
try { provenanceStore.Append(item with { BatteryId = "different-battery" }); throw new Exception("Mismatched battery evidence accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS: Audit store rejects mismatched event and telemetry identities"); }
try { provenanceStore.Append(item with { Kind = "Reading", Asset = "  " }); throw new Exception("Unassigned reading accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS: New audit readings require an asset tag"); }
Directory.CreateDirectory(provenanceStore.Folder);
File.WriteAllText(Path.Combine(provenanceStore.Folder, "legacy.json"), JsonSerializer.Serialize(item with { Kind = "Reading", Asset = "" }));
Check(provenanceStore.Read().Single().Asset == "", "Legacy unassigned readings stay readable without rewriting history");
provenanceStore.Append(item with { Id = Guid.NewGuid(), Reading = relabeled, BatteryId = relabeled.BatteryId, Serial = "new-serial" });
var savedLabel = provenanceStore.Read().Single(e => e.Serial == "new-serial");
Check(savedLabel.Reading == relabeled && BusinessStore.Csv(new[] { savedLabel }).Contains("Unknown"), "Label-only provenance survives JSON persistence and exports as Unknown health");
Check(BusinessPolicy.AssetInspectionReading(new[] { savedLabel }, "machine", item.Asset, "new-serial", reading, now, now).HealthPercent is null,
    "Later live telemetry cannot silently convert a label-only battery into a connected battery");
var relabeledCapture = item with { Id = Guid.NewGuid(), Kind = "Reading", At = item.At.AddSeconds(1), Serial = "new-serial" };
Check(BusinessPolicy.AssetInspectionReading(new[] { item, relabeledCapture }, "machine", item.Asset, "new-serial", reading, now, now).HealthPercent is null,
    "An intervening capture cannot overwrite established inspection label provenance");
int captureCalls = 0, captureNotifications = 0;
var captureSource = new TaskCompletionSource<BatteryDiagnostics>();
var captureSession = new BatteryCaptureSession(() => { captureCalls++; return captureSource.Task; });
captureSession.Captured += _ => captureNotifications++;
var dashboardCapture = captureSession.ReadAsync();
var inspectionCapture = captureSession.ReadAsync();
Check(ReferenceEquals(dashboardCapture, inspectionCapture) && captureCalls == 1, "Dashboard and inspection share one in-flight battery query");
captureSource.SetResult(reading);
Check(await dashboardCapture == reading && await inspectionCapture == reading && captureNotifications == 1, "Shared capture publishes one sample to both consumers");
captureSource = new TaskCompletionSource<BatteryDiagnostics>();
var unavailableCapture = captureSession.ReadAsync();
captureSource.SetResult(BatteryDiagnostics.Unknown("Access denied"));
Check((await unavailableCapture).HealthPercent is null && captureNotifications == 2 && captureCalls == 2, "A later capture can publish unavailable telemetry without reusing old health");
captureSource = new TaskCompletionSource<BatteryDiagnostics>();
var failedCapture = captureSession.ReadAsync();
captureSource.SetException(new IOException("simulated query failure"));
try { await failedCapture; throw new Exception("Query failure was hidden"); }
catch (IOException) { Check(captureNotifications == 2, "Failed queries do not publish a successful capture"); }
captureSource = new TaskCompletionSource<BatteryDiagnostics>();
var recoveredCapture = captureSession.ReadAsync();
captureSource.SetResult(reading);
Check(await recoveredCapture == reading && captureCalls == 4 && captureNotifications == 3, "Battery capture recovers after a failed query");
var synchronousSession = new BatteryCaptureSession(() => Task.FromResult(reading));
Check(await synchronousSession.ReadAsync() == reading && await synchronousSession.ReadAsync() == reading, "Immediately completed capture does not leave a stuck in-flight request");
void WithSqlite(string path, Action<IntPtr> action)
{
    using var connection = (IDisposable)typeof(CheckInStorage).GetMethod("OpenPath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { path, 6 })!;
    action((IntPtr)connection.GetType().GetProperty("Handle")!.GetValue(connection)!);
}
void Sql(string path, params string[] commands) => WithSqlite(path, db =>
{
    foreach (string sql in commands) typeof(CheckInStorage).GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { db, sql });
});
string Scalar(string path, string sql)
{
    string result = "";
    WithSqlite(path, db =>
    {
        using var statement = (IDisposable)typeof(CheckInStorage).GetMethod("Prepare", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { db, sql })!;
        var handle = (IntPtr)statement.GetType().GetProperty("Handle")!.GetValue(statement)!;
        if ((int)typeof(CheckInStorage).GetMethod("sqlite3_step", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { handle })! != 100) throw new Exception("Fixture query returned no row");
        result = (string)typeof(CheckInStorage).GetMethod("ReadText", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { handle, 0 })!;
    });
    return result;
}
string legacyPath = Path.Combine(telemetryFolder, "legacy.sqlite3");
telemetry.BackupDatabase(legacyPath);
Sql(legacyPath, "DROP INDEX idx_checkins_evidence_id", "ALTER TABLE checkins DROP COLUMN evidence_id",
    "DROP INDEX idx_stored_battery_inspections_evidence_id", "ALTER TABLE stored_battery_inspections DROP COLUMN evidence_id",
    "DROP INDEX idx_lenovo_snapshot_day_identity", "ALTER TABLE lenovo_battery_snapshots DROP COLUMN snapshot_day",
    "ALTER TABLE lenovo_battery_snapshots DROP COLUMN identity_key", "DROP TABLE app_settings", "DROP TABLE lenovo_battery_snapshot_revisions");
byte[] originalLegacy = File.ReadAllBytes(legacyPath);
telemetry.ValidateBackup(legacyPath);
Check(File.ReadAllBytes(legacyPath).SequenceEqual(originalLegacy), "Backup validation upgrades only staging and leaves the legacy source unchanged");
Check(!Directory.GetFiles(telemetryFolder, ".psum-restore-*").Any(), "Validation removes its staging database");
var upgraded = new CheckInStorage(legacyPath);
Check(upgraded.MigrationBackupPath is string migrationCopy && File.Exists(migrationCopy) &&
    Scalar(migrationCopy, "SELECT count(*) FROM pragma_table_info('checkins') WHERE name='evidence_id'") == "0", "Legacy startup saves the pre-migration schema before any upgrade");
string upgradedEvidence = upgraded.Recent().Single().EvidenceId;
Check(Guid.TryParse(upgradedEvidence, out _) && upgraded.Recent().Single().Note == checkin.Note, "Transactional migration retains legacy data and adds evidence identity");
Check(new CheckInStorage(legacyPath).MigrationBackupPath is null && upgraded.Recent().Single().EvidenceId == upgradedEvidence, "Reopening an upgraded database preserves evidence IDs without another migration backup");
string restoreLegacy = Path.Combine(telemetryFolder, "restore-legacy.sqlite3");
File.WriteAllBytes(restoreLegacy, originalLegacy);
string restoreSafety = telemetry.RestoreDatabase(restoreLegacy);
Check(File.ReadAllBytes(restoreLegacy).SequenceEqual(originalLegacy) && File.Exists(restoreSafety), "Legacy restore preserves its source and saves the active database");
telemetry.SetSetting("after-restore", "works");
Check(telemetry.GetSetting("after-restore") == "works" && Guid.TryParse(telemetry.Recent().Single().EvidenceId, out _), "Restored legacy schema is upgraded before settings and history are used");
string failedMigration = Path.Combine(telemetryFolder, "failed-migration.sqlite3");
telemetry.BackupDatabase(failedMigration);
Sql(failedMigration, "DROP TABLE app_settings", "DROP INDEX idx_checkins_evidence_id", "UPDATE checkins SET evidence_id='duplicate'",
    "INSERT INTO checkins(recorded_at,battery_id,state,note,evidence_id) VALUES('2026-10-27','fixture','Unknown','preserve','duplicate')");
try { _ = new CheckInStorage(failedMigration); throw new Exception("Invalid migration succeeded"); }
catch (IOException) { Console.WriteLine("PASS: Migration rejects conflicting evidence identities"); }
Check(Scalar(failedMigration, "SELECT count(*) FROM sqlite_master WHERE name='app_settings'") == "0" && Scalar(failedMigration, "SELECT count(*) FROM checkins") == "2", "Failed migration rolls back schema edits and preserves all records");
string beforeFailedRestore = telemetry.GetSetting("after-restore")!;
try { telemetry.RestoreDatabase(failedMigration); throw new Exception("Invalid restore succeeded"); }
catch (IOException) { Console.WriteLine("PASS: Restore rejects a candidate that cannot migrate"); }
Check(telemetry.GetSetting("after-restore") == beforeFailedRestore && telemetry.Recent().Count == 1, "Failed staged migration leaves active history and settings untouched");
string corruptBackup = Path.Combine(telemetryFolder, "corrupt.sqlite3");
File.WriteAllText(corruptBackup, "not SQLite");
try { telemetry.RestoreDatabase(corruptBackup); throw new Exception("Corrupt restore succeeded"); }
catch (IOException) { Console.WriteLine("PASS: Corrupt backup rejected before restore"); }
Check(telemetry.GetSetting("after-restore") == "works" && !Directory.GetFiles(telemetryFolder, ".psum-restore-*").Any(), "Rejected restores retain live settings and leave no staging files");
string malformedSettings = Path.Combine(telemetryFolder, "malformed-settings.sqlite3");
telemetry.BackupDatabase(malformedSettings);
Sql(malformedSettings, "ALTER TABLE app_settings RENAME COLUMN value TO incompatible_value");
try { telemetry.RestoreDatabase(malformedSettings); throw new Exception("Incompatible settings restored"); }
catch (InvalidDataException) { Console.WriteLine("PASS: Staged restore checks settings schema as well as telemetry tables"); }
Check(telemetry.GetSetting("after-restore") == "works", "Incompatible auxiliary schema cannot replace active settings");
string duplicateSnapshots = Path.Combine(telemetryFolder, "legacy-snapshots.sqlite3");
File.WriteAllBytes(duplicateSnapshots, originalLegacy);
Sql(duplicateSnapshots,
    "INSERT INTO lenovo_battery_snapshots(capture_id,captured_at,battery_id,barcode,data_json) VALUES('first','2026-10-20T10:00:00','battery','barcode','{}')",
    "INSERT INTO lenovo_battery_snapshots(capture_id,captured_at,battery_id,barcode,data_json) VALUES('second','2026-10-20T11:00:00','battery','barcode','{}')");
var consolidated = new CheckInStorage(duplicateSnapshots);
Check(consolidated.RecentLenovoSnapshots().Count == 1 && Scalar(duplicateSnapshots, "SELECT count(*) FROM lenovo_battery_snapshot_revisions") == "1", "Transactional migration archives duplicate snapshots instead of losing them");
Check(Scalar(consolidated.MigrationBackupPath!, "SELECT count(*) FROM lenovo_battery_snapshots") == "2", "Pre-migration safety copy retains both original snapshots");
try { telemetry.BackupDatabase(Path.Combine(telemetryFolder, ".", "telemetry.sqlite3")); throw new Exception("Self-backup accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: Backup rejects an alternate spelling of the active path"); }
try { telemetry.RestoreDatabase(Path.Combine(telemetryFolder, ".", "telemetry.sqlite3")); throw new Exception("Self-restore accepted"); }
catch (ArgumentException) { Console.WriteLine("PASS: Restore rejects an alternate spelling of the active path"); }
string blockedBackup = Path.Combine(telemetryFolder, "blocked-destination");
Directory.CreateDirectory(blockedBackup);
File.WriteAllText(Path.Combine(blockedBackup, "keep.txt"), "preserve");
try { telemetry.BackupDatabase(blockedBackup); throw new Exception("Backup replaced a directory"); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine("PASS: Backup handles a blocked destination"); }
Check(File.ReadAllText(Path.Combine(blockedBackup, "keep.txt")) == "preserve" &&
    !Directory.GetFiles(telemetryFolder, "blocked-destination.staging-*").Any() && telemetry.GetSetting("after-restore") == "works",
    "Failed backup preserves the destination and active database and cleans staging");
Console.WriteLine("All verification checks passed.");
