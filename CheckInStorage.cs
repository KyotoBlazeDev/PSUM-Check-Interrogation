using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PSUM_Check_Interrogation_WinUI_3;

internal sealed record BatteryCheckIn(
    long Id, DateTimeOffset RecordedAt, string BatteryId, int? ChargePercent,
    string State, double? HealthPercent, int? CycleCount, string Note, string EvidenceId,
    string? SavedTimestamp = null);

internal sealed record LenovoSnapshotRow(long Id, string CapturedAt, string BatteryId, string Barcode, string DataJson);

internal sealed class CheckInStorage
{
    private const int Row = 100;
    private const int Done = 101;
    private const int Null = 5;
    private const int OpenReadOnly = 0x00000001;
    private const int OpenReadWriteCreate = 0x00000006;
    private readonly string _path;
    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSUM", "psum.sqlite3");

    public CheckInStorage(string? path = null)
    {
        _path = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var db = Open();
        Execute(db.Handle, """
            CREATE TABLE IF NOT EXISTS checkins (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                recorded_at TEXT NOT NULL,
                battery_id TEXT NOT NULL,
                charge_percent INTEGER,
                state TEXT NOT NULL,
                health_percent REAL,
                cycle_count INTEGER,
                note TEXT NOT NULL DEFAULT '',
                evidence_id TEXT
            )
            """);
        Execute(db.Handle, "CREATE TABLE IF NOT EXISTS battery_profile_state (profile_id TEXT PRIMARY KEY, lifecycle_state TEXT NOT NULL, updated_at TEXT NOT NULL)");
        Execute(db.Handle, """
            CREATE TABLE IF NOT EXISTS stored_battery_inspections (
                id INTEGER PRIMARY KEY AUTOINCREMENT, profile_id TEXT NOT NULL, checked_at TEXT NOT NULL,
                physically_inspected INTEGER NOT NULL, swelling INTEGER, leakage INTEGER,
                unusual_odor INTEGER, unexpected_warmth INTEGER, charge_measured INTEGER NOT NULL,
                voltage_measured INTEGER NOT NULL, charge_percent INTEGER, voltage REAL, evidence_id TEXT
            )
            """);
        Execute(db.Handle, "CREATE INDEX IF NOT EXISTS idx_stored_inspection_profile_time ON stored_battery_inspections(profile_id, checked_at DESC)");
        Execute(db.Handle, """
            CREATE TABLE IF NOT EXISTS lenovo_battery_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT, capture_id TEXT NOT NULL,
                captured_at TEXT NOT NULL, battery_id TEXT, barcode TEXT, data_json TEXT NOT NULL
            )
            """);
        Execute(db.Handle, "CREATE TABLE IF NOT EXISTS app_settings (key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        Execute(db.Handle, """
            CREATE TABLE IF NOT EXISTS lenovo_battery_snapshot_revisions (
                id INTEGER PRIMARY KEY AUTOINCREMENT, original_id INTEGER NOT NULL,
                capture_id TEXT NOT NULL, captured_at TEXT NOT NULL, battery_id TEXT,
                barcode TEXT, data_json TEXT NOT NULL, archived_at TEXT NOT NULL
            )
            """);
        var snapshotColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var columns = Prepare(db.Handle, "PRAGMA table_info(lenovo_battery_snapshots)"))
            while (sqlite3_step(columns.Handle) == Row) snapshotColumns.Add(ReadText(columns.Handle, 1));
        if (!snapshotColumns.Contains("snapshot_day")) Execute(db.Handle, "ALTER TABLE lenovo_battery_snapshots ADD COLUMN snapshot_day TEXT");
        if (!snapshotColumns.Contains("identity_key")) Execute(db.Handle, "ALTER TABLE lenovo_battery_snapshots ADD COLUMN identity_key TEXT");
        ConsolidateLegacySnapshots(db.Handle);
        Execute(db.Handle, "CREATE UNIQUE INDEX IF NOT EXISTS idx_lenovo_snapshot_day_identity ON lenovo_battery_snapshots(snapshot_day, identity_key)");
        MigrateEvidenceIds(db.Handle, "checkins");
        MigrateEvidenceIds(db.Handle, "stored_battery_inspections");
    }

    public void BackupDatabase(string destination)
    {
        if (SamePath(_path, destination)) throw new ArgumentException("Choose a destination different from the active database.");
        string staged = destination + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            ValidateDatabase(_path);
            BackupCopy(_path, staged);
            ValidateDatabase(staged);
            File.Move(staged, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }

    public void ValidateBackup(string path) => ValidateDatabase(path);

    public string RestoreDatabase(string selected)
    {
        if (SamePath(_path, selected)) throw new ArgumentException("Select a backup file, not the active database.");
        if (!File.Exists(selected)) throw new FileNotFoundException("Backup file not found.", selected);
        ValidateDatabase(selected);
        string safety = Path.Combine(Path.GetDirectoryName(_path)!,
            $"{Path.GetFileNameWithoutExtension(_path)}-before-restore-{DateTime.Now:yyyyMMdd-HHmmssffffff}.sqlite3");
        BackupDatabase(safety);
        string staged = Path.Combine(Path.GetDirectoryName(_path)!, ".psum-restore-" + Guid.NewGuid().ToString("N") + ".sqlite3");
        try
        {
            BackupCopy(selected, staged);
            ValidateDatabase(staged);
            BackupCopy(staged, _path);
            ValidateDatabase(_path);
            return safety;
        }
        catch (Exception ex)
        {
            throw new IOException($"Restore failed. The safety copy is at {safety}. {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static void BackupCopy(string source, string destination)
    {
        using var original = OpenPath(source, OpenReadOnly);
        using var copy = OpenPath(destination, OpenReadWriteCreate);
        IntPtr backup = sqlite3_backup_init(copy.Handle, "main", original.Handle, "main");
        if (backup == IntPtr.Zero) throw Error(copy.Handle);
        int step = sqlite3_backup_step(backup, -1);
        int finish = sqlite3_backup_finish(backup);
        if (step != Done || finish != 0) throw Error(copy.Handle);
    }

    private static void ValidateDatabase(string path)
    {
        using var db = OpenPath(path, OpenReadOnly);
        using (var check = Prepare(db.Handle, "PRAGMA quick_check"))
        {
            if (sqlite3_step(check.Handle) != Row || ReadText(check.Handle, 0) != "ok")
                throw new InvalidDataException("The SQLite database failed its integrity check.");
        }
        var required = new Dictionary<string, string[]>
        {
            ["checkins"] = ["id", "recorded_at", "battery_id", "charge_percent", "state", "health_percent", "cycle_count", "note"],
            ["battery_profile_state"] = ["profile_id", "lifecycle_state", "updated_at"],
            ["stored_battery_inspections"] = ["id", "profile_id", "checked_at", "physically_inspected", "swelling", "leakage", "unusual_odor", "unexpected_warmth", "charge_measured", "voltage_measured", "charge_percent", "voltage"],
            ["lenovo_battery_snapshots"] = ["id", "capture_id", "captured_at", "battery_id", "barcode", "data_json"]
        };
        foreach (var (table, columns) in required)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var statement = Prepare(db.Handle, $"PRAGMA table_info({table})");
            int step;
            while ((step = sqlite3_step(statement.Handle)) == Row)
                found.Add(ReadText(statement.Handle, 1));
            if (step != Done || columns.Any(column => !found.Contains(column)))
                throw new InvalidDataException($"The backup has an incompatible {table} table.");
        }
    }

    private static void ConsolidateLegacySnapshots(IntPtr db)
    {
        var rows = new List<(long id, string time, string battery, string barcode)>();
        using (var statement = Prepare(db, "SELECT id,captured_at,battery_id,barcode FROM lenovo_battery_snapshots WHERE snapshot_day IS NULL OR identity_key IS NULL ORDER BY captured_at DESC,id DESC"))
        {
            int step;
            while ((step = sqlite3_step(statement.Handle)) == Row)
                rows.Add((sqlite3_column_int64(statement.Handle, 0), ReadText(statement.Handle, 1), ReadText(statement.Handle, 2), ReadText(statement.Handle, 3)));
            if (step != Done) throw Error(db);
        }
        if (rows.Count == 0) return;
        Execute(db, "BEGIN IMMEDIATE");
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                string day = row.time.Length >= 10 ? row.time[..10] : row.time;
                string identity = !string.IsNullOrWhiteSpace(row.barcode) ? "barcode:" + row.barcode.Trim().ToLowerInvariant() :
                    !string.IsNullOrWhiteSpace(row.battery) ? "battery-id:" + row.battery.Trim().ToLowerInvariant() : $"unknown:{row.id}";
                if (!seen.Add(day + "|" + identity))
                {
                    using (var archive = Prepare(db, "INSERT INTO lenovo_battery_snapshot_revisions(original_id,capture_id,captured_at,battery_id,barcode,data_json,archived_at) SELECT id,capture_id,captured_at,battery_id,barcode,data_json,? FROM lenovo_battery_snapshots WHERE id=?"))
                    {
                        BindText(archive.Handle, 1, DateTimeOffset.Now.ToString("o"));
                        Check(db, sqlite3_bind_int64(archive.Handle, 2, row.id));
                        if (sqlite3_step(archive.Handle) != Done) throw Error(db);
                    }
                    using var remove = Prepare(db, "DELETE FROM lenovo_battery_snapshots WHERE id=?");
                    Check(db, sqlite3_bind_int64(remove.Handle, 1, row.id));
                    if (sqlite3_step(remove.Handle) != Done) throw Error(db);
                }
                else
                {
                    using var update = Prepare(db, "UPDATE lenovo_battery_snapshots SET snapshot_day=?,identity_key=? WHERE id=?");
                    BindText(update.Handle, 1, day); BindText(update.Handle, 2, identity);
                    Check(db, sqlite3_bind_int64(update.Handle, 3, row.id));
                    if (sqlite3_step(update.Handle) != Done) throw Error(db);
                }
            }
            Execute(db, "COMMIT");
        }
        catch { Execute(db, "ROLLBACK"); throw; }
    }

    public string? GetSetting(string key)
    {
        using var db = Open();
        using var statement = Prepare(db.Handle, "SELECT value FROM app_settings WHERE key=?");
        BindText(statement.Handle, 1, key);
        return sqlite3_step(statement.Handle) == Row ? ReadText(statement.Handle, 0) : null;
    }

    public void SetSetting(string key, string value)
    {
        using var db = Open();
        using var statement = Prepare(db.Handle, "INSERT INTO app_settings(key,value) VALUES (?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value");
        BindText(statement.Handle, 1, key);
        BindText(statement.Handle, 2, value);
        if (sqlite3_step(statement.Handle) != Done) throw Error(db.Handle);
    }

    public int AddLenovoSnapshotBatch(IReadOnlyList<LenovoBatteryReading> readings, DateTimeOffset capturedAt)
    {
        if (readings.Count == 0) return 0;
        using var db = Open();
        string time = capturedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
        string day = capturedAt.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string captureId = Guid.NewGuid().ToString("N");
        Execute(db.Handle, "BEGIN IMMEDIATE");
        try
        {
            for (int i = 0; i < readings.Count; i++)
            {
                var reading = readings[i];
                string barcode = reading.Barcode?.Trim() ?? "";
                string batteryId = reading.BatteryId?.Trim() ?? "";
                string identity = barcode.Length > 0 ? "barcode:" + barcode.ToLowerInvariant() :
                    batteryId.Length > 0 ? "battery-id:" + batteryId.ToLowerInvariant() : $"unknown:{i}";
                long? previous = null;
                using (var find = Prepare(db.Handle, "SELECT id FROM lenovo_battery_snapshots WHERE snapshot_day=? AND identity_key=?"))
                {
                    BindText(find.Handle, 1, day); BindText(find.Handle, 2, identity);
                    if (sqlite3_step(find.Handle) == Row) previous = sqlite3_column_int64(find.Handle, 0);
                }
                if (previous is long id)
                {
                    using (var archive = Prepare(db.Handle, "INSERT INTO lenovo_battery_snapshot_revisions(original_id,capture_id,captured_at,battery_id,barcode,data_json,archived_at) SELECT id,capture_id,captured_at,battery_id,barcode,data_json,? FROM lenovo_battery_snapshots WHERE id=?"))
                    {
                        BindText(archive.Handle, 1, DateTimeOffset.Now.ToString("o"));
                        Check(db.Handle, sqlite3_bind_int64(archive.Handle, 2, id));
                        if (sqlite3_step(archive.Handle) != Done) throw Error(db.Handle);
                    }
                    using var update = Prepare(db.Handle, "UPDATE lenovo_battery_snapshots SET capture_id=?,captured_at=?,battery_id=?,barcode=?,data_json=? WHERE id=?");
                    BindText(update.Handle, 1, captureId); BindText(update.Handle, 2, time); BindText(update.Handle, 3, batteryId);
                    BindText(update.Handle, 4, barcode); BindText(update.Handle, 5, reading.DataJson);
                    Check(db.Handle, sqlite3_bind_int64(update.Handle, 6, id));
                    if (sqlite3_step(update.Handle) != Done) throw Error(db.Handle);
                }
                else
                {
                    using var insert = Prepare(db.Handle, "INSERT INTO lenovo_battery_snapshots(capture_id,captured_at,battery_id,barcode,data_json,snapshot_day,identity_key) VALUES (?,?,?,?,?,?,?)");
                    BindText(insert.Handle, 1, captureId); BindText(insert.Handle, 2, time); BindText(insert.Handle, 3, batteryId);
                    BindText(insert.Handle, 4, barcode); BindText(insert.Handle, 5, reading.DataJson);
                    BindText(insert.Handle, 6, day); BindText(insert.Handle, 7, identity);
                    if (sqlite3_step(insert.Handle) != Done) throw Error(db.Handle);
                }
            }
            Execute(db.Handle, "COMMIT");
            return readings.Count;
        }
        catch { Execute(db.Handle, "ROLLBACK"); throw; }
    }

    public IReadOnlyList<LenovoSnapshotRow> RecentLenovoSnapshots(int limit = 500)
    {
        var rows = new List<LenovoSnapshotRow>();
        using var db = Open();
        using var statement = Prepare(db.Handle, "SELECT id,captured_at,battery_id,barcode,data_json FROM lenovo_battery_snapshots ORDER BY captured_at DESC,id DESC LIMIT ?");
        Check(db.Handle, sqlite3_bind_int(statement.Handle, 1, limit));
        int step;
        while ((step = sqlite3_step(statement.Handle)) == Row)
            rows.Add(new(sqlite3_column_int64(statement.Handle, 0), ReadText(statement.Handle, 1), ReadText(statement.Handle, 2), ReadText(statement.Handle, 3), ReadText(statement.Handle, 4)));
        if (step != Done) throw Error(db.Handle);
        return rows;
    }

    public void Add(BatteryCheckIn checkIn)
    {
        if (string.IsNullOrWhiteSpace(checkIn.BatteryId)) throw new ArgumentException("Battery identity is required.");
        if (checkIn.ChargePercent is < 0 or > 100) throw new ArgumentException("Charge must be between 0 and 100 percent.");
        if (checkIn.HealthPercent is < 0 or > 100) throw new ArgumentException("Health must be between 0 and 100 percent.");
        if (checkIn.Note.Length > 500) throw new ArgumentException("Note must be 500 characters or fewer.");

        using var db = Open();
        using var statement = Prepare(db.Handle, """
            INSERT INTO checkins
                (recorded_at, battery_id, charge_percent, state, health_percent, cycle_count, note, evidence_id)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """);
        BindText(statement.Handle, 1, checkIn.RecordedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture));
        BindText(statement.Handle, 2, checkIn.BatteryId);
        BindIntOrNull(statement.Handle, 3, checkIn.ChargePercent);
        BindText(statement.Handle, 4, checkIn.State);
        BindDoubleOrNull(statement.Handle, 5, checkIn.HealthPercent);
        BindIntOrNull(statement.Handle, 6, checkIn.CycleCount);
        BindText(statement.Handle, 7, checkIn.Note.Trim());
        BindText(statement.Handle, 8, checkIn.EvidenceId);
        if (sqlite3_step(statement.Handle) != Done) throw Error(db.Handle);
    }

    public IReadOnlyList<BatteryCheckIn> Recent(int limit = 100)
    {
        var result = new List<BatteryCheckIn>();
        if (limit < 1) return result;
        using var db = Open();
        using var statement = Prepare(db.Handle, """
            SELECT id, recorded_at, battery_id, charge_percent, state,
                   health_percent, cycle_count, note, evidence_id
            FROM checkins ORDER BY recorded_at DESC, id DESC LIMIT ?
            """);
        Check(db.Handle, sqlite3_bind_int(statement.Handle, 1, limit));
        int step;
        while ((step = sqlite3_step(statement.Handle)) == Row)
        {
            string timestampText = ReadText(statement.Handle, 1);
            var timestamp = DateTimeOffset.Parse(timestampText, CultureInfo.InvariantCulture);
            result.Add(new BatteryCheckIn(
                sqlite3_column_int64(statement.Handle, 0), timestamp,
                ReadText(statement.Handle, 2), ReadNullableInt(statement.Handle, 3),
                ReadText(statement.Handle, 4), ReadNullableDouble(statement.Handle, 5),
                ReadNullableInt(statement.Handle, 6), ReadText(statement.Handle, 7),
                ReadText(statement.Handle, 8), timestampText));
        }
        if (step != Done) throw Error(db.Handle);
        return result;
    }

    public bool DeleteCheckIn(long id)
    {
        using var db = Open();
        using var statement = Prepare(db.Handle, "DELETE FROM checkins WHERE id=?");
        Check(db.Handle, sqlite3_bind_int64(statement.Handle, 1, id));
        if (sqlite3_step(statement.Handle) != Done) throw Error(db.Handle);
        return sqlite3_changes(db.Handle) > 0;
    }

    public string GetLifecycle(string profileId, string fallback)
    {
        using var db = Open();
        using var statement = Prepare(db.Handle, "SELECT lifecycle_state FROM battery_profile_state WHERE profile_id = ?");
        BindText(statement.Handle, 1, profileId);
        int step = sqlite3_step(statement.Handle);
        if (step == Done) return fallback;
        if (step != Row) throw Error(db.Handle);
        string value = ReadText(statement.Handle, 0);
        return BatteryLifecycles.All.Contains(value) ? value : fallback;
    }

    public IReadOnlyList<string> KnownProfileIds()
    {
        var result = new List<string>();
        using var db = Open();
        using var statement = Prepare(db.Handle, """
            SELECT profile_id FROM battery_profile_state
            UNION SELECT profile_id FROM stored_battery_inspections
            ORDER BY profile_id
            """);
        int step;
        while ((step = sqlite3_step(statement.Handle)) == Row)
            result.Add(ReadText(statement.Handle, 0));
        if (step != Done) throw Error(db.Handle);
        return result;
    }

    public void SetLifecycle(string profileId, string lifecycle)
    {
        if (string.IsNullOrWhiteSpace(profileId)) throw new ArgumentException("Battery profile is required.");
        if (!BatteryLifecycles.All.Contains(lifecycle)) throw new ArgumentException("Choose a valid lifecycle state.");
        var latest = StoredInspections(profileId, 1).FirstOrDefault();
        if (latest?.Result == "STOP CHECK" && lifecycle is not (BatteryLifecycles.AttentionRequired or BatteryLifecycles.Retired or BatteryLifecycles.Recycled))
            throw new ArgumentException("The latest inspection reported a physical hazard. Record a new safe inspection before returning this battery to an active lifecycle state.");
        using var db = Open();
        SaveLifecycle(db.Handle, profileId, lifecycle, DateTimeOffset.Now);
    }

    public IReadOnlyList<StoredBatteryInspection> StoredInspections(string profileId, int limit = 100)
    {
        var result = new List<StoredBatteryInspection>();
        if (limit < 1) return result;
        using var db = Open();
        using var statement = Prepare(db.Handle, """
            SELECT id, profile_id, checked_at, physically_inspected, swelling, leakage,
                   unusual_odor, unexpected_warmth, charge_measured, voltage_measured,
                   charge_percent, voltage, evidence_id
            FROM stored_battery_inspections WHERE profile_id = ?
            ORDER BY checked_at DESC, id DESC LIMIT ?
            """);
        BindText(statement.Handle, 1, profileId);
        Check(db.Handle, sqlite3_bind_int(statement.Handle, 2, limit));
        int step;
        while ((step = sqlite3_step(statement.Handle)) == Row)
        {
            result.Add(new StoredBatteryInspection(
                sqlite3_column_int64(statement.Handle, 0), ReadText(statement.Handle, 1),
                DateTimeOffset.Parse(ReadText(statement.Handle, 2), CultureInfo.InvariantCulture),
                sqlite3_column_int(statement.Handle, 3) != 0,
                ReadNullableBool(statement.Handle, 4), ReadNullableBool(statement.Handle, 5),
                ReadNullableBool(statement.Handle, 6), ReadNullableBool(statement.Handle, 7),
                sqlite3_column_int(statement.Handle, 8) != 0, sqlite3_column_int(statement.Handle, 9) != 0,
                ReadNullableInt(statement.Handle, 10), ReadNullableDouble(statement.Handle, 11),
                ReadText(statement.Handle, 12)));
        }
        if (step != Done) throw Error(db.Handle);
        return result;
    }

    public void AddStoredInspection(StoredBatteryInspection inspection)
    {
        StoredBatteryRules.Validate(inspection);
        using var db = Open();
        Execute(db.Handle, "BEGIN IMMEDIATE");
        try
        {
            using (var statement = Prepare(db.Handle, """
                INSERT INTO stored_battery_inspections
                    (profile_id, checked_at, physically_inspected, swelling, leakage,
                     unusual_odor, unexpected_warmth, charge_measured, voltage_measured,
                     charge_percent, voltage, evidence_id)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """))
            {
                BindText(statement.Handle, 1, inspection.ProfileId);
                BindText(statement.Handle, 2, inspection.CheckedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture));
                Check(db.Handle, sqlite3_bind_int(statement.Handle, 3, inspection.PhysicallyInspected ? 1 : 0));
                BindBoolOrNull(statement.Handle, 4, inspection.Swelling);
                BindBoolOrNull(statement.Handle, 5, inspection.Leakage);
                BindBoolOrNull(statement.Handle, 6, inspection.UnusualOdor);
                BindBoolOrNull(statement.Handle, 7, inspection.UnexpectedWarmth);
                Check(db.Handle, sqlite3_bind_int(statement.Handle, 8, inspection.ChargeMeasured ? 1 : 0));
                Check(db.Handle, sqlite3_bind_int(statement.Handle, 9, inspection.VoltageMeasured ? 1 : 0));
                BindIntOrNull(statement.Handle, 10, inspection.ChargePercent);
                BindDoubleOrNull(statement.Handle, 11, inspection.Voltage);
                BindText(statement.Handle, 12, inspection.EvidenceId);
                if (sqlite3_step(statement.Handle) != Done) throw Error(db.Handle);
            }
            if (inspection.Result == "STOP CHECK")
                SaveLifecycle(db.Handle, inspection.ProfileId, BatteryLifecycles.AttentionRequired, inspection.CheckedAt);
            else if (inspection.PhysicallyInspected && !LifecycleExists(db.Handle, inspection.ProfileId))
                SaveLifecycle(db.Handle, inspection.ProfileId, BatteryLifecycles.Stored, inspection.CheckedAt);
            Execute(db.Handle, "COMMIT");
        }
        catch
        {
            Execute(db.Handle, "ROLLBACK");
            throw;
        }
    }

    private static bool LifecycleExists(IntPtr db, string profileId)
    {
        using var statement = Prepare(db, "SELECT 1 FROM battery_profile_state WHERE profile_id = ?");
        BindText(statement.Handle, 1, profileId);
        return sqlite3_step(statement.Handle) == Row;
    }

    private static void SaveLifecycle(IntPtr db, string profileId, string lifecycle, DateTimeOffset timestamp)
    {
        using var statement = Prepare(db, """
            INSERT INTO battery_profile_state(profile_id, lifecycle_state, updated_at)
            VALUES (?, ?, ?) ON CONFLICT(profile_id) DO UPDATE SET
                lifecycle_state = excluded.lifecycle_state, updated_at = excluded.updated_at
            """);
        BindText(statement.Handle, 1, profileId);
        BindText(statement.Handle, 2, lifecycle);
        BindText(statement.Handle, 3, timestamp.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture));
        if (sqlite3_step(statement.Handle) != Done) throw Error(db);
    }

    private void MigrateEvidenceIds(IntPtr db, string table)
    {
        bool found = false;
        using (var columns = Prepare(db, $"PRAGMA table_info({table})"))
        {
            while (sqlite3_step(columns.Handle) == Row)
                found |= ReadText(columns.Handle, 1) == "evidence_id";
        }
        if (!found) Execute(db, $"ALTER TABLE {table} ADD COLUMN evidence_id TEXT");

        var ids = new List<long>();
        using (var missing = Prepare(db, $"SELECT id FROM {table} WHERE evidence_id IS NULL OR evidence_id = ''"))
        {
            while (sqlite3_step(missing.Handle) == Row)
                ids.Add(sqlite3_column_int64(missing.Handle, 0));
        }
        foreach (long id in ids)
        {
            using var update = Prepare(db, $"UPDATE {table} SET evidence_id = ? WHERE id = ?");
            BindText(update.Handle, 1, Guid.NewGuid().ToString());
            Check(db, sqlite3_bind_int64(update.Handle, 2, id));
            if (sqlite3_step(update.Handle) != Done) throw Error(db);
        }
        Execute(db, $"CREATE UNIQUE INDEX IF NOT EXISTS idx_{table}_evidence_id ON {table}(evidence_id)");
    }

    private Connection Open() => OpenPath(_path, OpenReadWriteCreate);

    private static Connection OpenPath(string path, int flags)
    {
        int result = sqlite3_open_v2(path, out var handle, flags, IntPtr.Zero);
        if (result != 0)
        {
            string message = handle == IntPtr.Zero ? "Could not open the check-in database." : ReadUtf8(sqlite3_errmsg(handle));
            if (handle != IntPtr.Zero) sqlite3_close(handle);
            throw new IOException(message);
        }
        sqlite3_busy_timeout(handle, 5000);
        return new Connection(handle);
    }

    private static void Execute(IntPtr db, string sql)
    {
        using var statement = Prepare(db, sql);
        if (sqlite3_step(statement.Handle) != Done) throw Error(db);
    }

    private static Statement Prepare(IntPtr db, string sql)
    {
        int result = sqlite3_prepare_v2(db, sql, -1, out var handle, IntPtr.Zero);
        Check(db, result);
        return new Statement(handle);
    }

    private static void BindText(IntPtr statement, int index, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        int result = sqlite3_bind_text(statement, index, bytes, bytes.Length, new IntPtr(-1));
        if (result != 0) throw new IOException($"SQLite bind failed ({result}).");
    }

    private static void BindIntOrNull(IntPtr statement, int index, int? value)
    {
        int result = value.HasValue ? sqlite3_bind_int(statement, index, value.Value) : sqlite3_bind_null(statement, index);
        if (result != 0) throw new IOException($"SQLite bind failed ({result}).");
    }

    private static void BindDoubleOrNull(IntPtr statement, int index, double? value)
    {
        int result = value.HasValue ? sqlite3_bind_double(statement, index, value.Value) : sqlite3_bind_null(statement, index);
        if (result != 0) throw new IOException($"SQLite bind failed ({result}).");
    }

    private static int? ReadNullableInt(IntPtr statement, int column) =>
        sqlite3_column_type(statement, column) == Null ? null : sqlite3_column_int(statement, column);

    private static double? ReadNullableDouble(IntPtr statement, int column) =>
        sqlite3_column_type(statement, column) == Null ? null : sqlite3_column_double(statement, column);

    private static bool? ReadNullableBool(IntPtr statement, int column) =>
        sqlite3_column_type(statement, column) == Null ? null : sqlite3_column_int(statement, column) != 0;

    private static void BindBoolOrNull(IntPtr statement, int index, bool? value)
    {
        int result = value.HasValue ? sqlite3_bind_int(statement, index, value.Value ? 1 : 0) : sqlite3_bind_null(statement, index);
        if (result != 0) throw new IOException($"SQLite bind failed ({result}).");
    }

    private static string ReadText(IntPtr statement, int column)
    {
        IntPtr pointer = sqlite3_column_text(statement, column);
        int length = sqlite3_column_bytes(statement, column);
        if (pointer == IntPtr.Zero || length == 0) return string.Empty;
        byte[] bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string ReadUtf8(IntPtr pointer) => Marshal.PtrToStringUTF8(pointer) ?? "SQLite error";
    private static IOException Error(IntPtr db) => new(ReadUtf8(sqlite3_errmsg(db)));
    private static void Check(IntPtr db, int result)
    {
        if (result != 0) throw Error(db);
    }

    private sealed class Connection(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() => sqlite3_close(Handle);
    }

    private sealed class Statement(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() => sqlite3_finalize(Handle);
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] text, int length, IntPtr destructor);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int(IntPtr statement, int index, int value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_double(IntPtr statement, int index, double value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_null(IntPtr statement, int index);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_int(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern double sqlite3_column_double(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_changes(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_backup_init(IntPtr destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, IntPtr source, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_backup_step(IntPtr backup, int pages);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_backup_finish(IntPtr backup);
}
