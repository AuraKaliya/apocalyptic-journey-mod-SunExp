using System;
using System.Collections.Generic;
using System.IO;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;

namespace AuraToolsExp.Dll.Features.AdventureArchive;

internal sealed class AdventureArchiveDatabase
{
    private readonly object gate = new();
    private readonly string databasePath;
    private bool initialized;

    internal AdventureArchiveDatabase(string databasePath)
    {
        this.databasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
    }

    internal string DatabasePath => databasePath;

    internal void Initialize()
    {
        lock (gate) EnsureInitialized();
    }

    internal void Begin(AdventureArchiveRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.AdventureId)) return;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                using (var insert = connection.Prepare(
                           "INSERT OR IGNORE INTO adventure_archives(adventure_id, started_utc, ended_utc, status, result, mode_id, role_id, game_build, tool_build, mod_fingerprint, latest_stage, event_count, snapshot_count, schema_version, data_completeness, role_name, mode_name) "
                           + "VALUES(?, ?, '', 'in-progress', '', ?, ?, ?, ?, ?, ?, 0, 0, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, record.AdventureId);
                    insert.Bind(2, record.StartedUtc);
                    insert.Bind(3, record.ModeId);
                    insert.Bind(4, record.RoleId);
                    insert.Bind(5, record.GameBuild);
                    insert.Bind(6, record.ToolBuild);
                    insert.Bind(7, record.ModFingerprint);
                    insert.Bind(8, record.LatestStage);
                    insert.Bind(9, AdventureArchiveSchema.CurrentVersion);
                    insert.Bind(10, AdventureArchiveSchema.Rich);
                    insert.Bind(11, record.RoleName);
                    insert.Bind(12, record.ModeName);
                    insert.Execute();
                }

                using (var update = connection.Prepare(
                           "UPDATE adventure_archives SET mode_id=?, role_id=?, game_build=?, tool_build=?, mod_fingerprint=?, latest_stage=?, schema_version=?, data_completeness=CASE WHEN data_completeness='summary-only' THEN 'partial' ELSE ? END, role_name=?, mode_name=? WHERE adventure_id=?;"))
                {
                    update.Bind(1, record.ModeId);
                    update.Bind(2, record.RoleId);
                    update.Bind(3, record.GameBuild);
                    update.Bind(4, record.ToolBuild);
                    update.Bind(5, record.ModFingerprint);
                    update.Bind(6, record.LatestStage);
                    update.Bind(7, AdventureArchiveSchema.CurrentVersion);
                    update.Bind(8, AdventureArchiveSchema.Rich);
                    update.Bind(9, record.RoleName);
                    update.Bind(10, record.ModeName);
                    update.Bind(11, record.AdventureId);
                    update.Execute();
                }
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal void AppendEvent(string adventureId, AdventureArchiveEvent item)
    {
        if (string.IsNullOrWhiteSpace(adventureId) || item == null) return;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            if (!string.IsNullOrWhiteSpace(item.DedupeKey)
                && EventExists(connection, adventureId, item.DedupeKey))
            {
                return;
            }
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                item.Sequence = NextSequence(connection, "adventure_archive_events", adventureId);
                using (var insert = connection.Prepare(
                           "INSERT INTO adventure_archive_events(adventure_id, sequence, occurred_utc, kind, title, detail, payload_json, dedupe_key) VALUES(?, ?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, adventureId);
                    insert.Bind(2, item.Sequence);
                    insert.Bind(3, item.OccurredUtc);
                    insert.Bind(4, item.Kind);
                    insert.Bind(5, item.Title);
                    insert.Bind(6, item.Detail);
                    insert.Bind(7, item.PayloadJson);
                    insert.Bind(8, item.DedupeKey);
                    insert.Execute();
                }
                UpdateCounts(connection, adventureId);
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal void AppendSnapshot(string adventureId, AdventureArchiveSnapshot item)
    {
        if (string.IsNullOrWhiteSpace(adventureId) || item == null) return;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                item.Sequence = NextSequence(connection, "adventure_archive_snapshots", adventureId);
                using (var insert = connection.Prepare(
                           "INSERT INTO adventure_archive_snapshots(adventure_id, sequence, occurred_utc, reason, stage, role_id, cards_json, relics_json, state_json, blessings_json) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, adventureId);
                    insert.Bind(2, item.Sequence);
                    insert.Bind(3, item.OccurredUtc);
                    insert.Bind(4, item.Reason);
                    insert.Bind(5, item.Stage);
                    insert.Bind(6, item.RoleId);
                    insert.Bind(7, item.CardsJson);
                    insert.Bind(8, item.RelicsJson);
                    insert.Bind(9, item.StateJson);
                    insert.Bind(10, item.BlessingsJson);
                    insert.Execute();
                }
                using (var update = connection.Prepare("UPDATE adventure_archives SET latest_stage=? WHERE adventure_id=?;"))
                {
                    update.Bind(1, item.Stage);
                    update.Bind(2, adventureId);
                    update.Execute();
                }
                UpdateCounts(connection, adventureId);
                connection.Execute("COMMIT;");
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal void Complete(string adventureId, string result)
    {
        if (string.IsNullOrWhiteSpace(adventureId)) return;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var update = connection.Prepare(
                "UPDATE adventure_archives SET ended_utc=?, status='complete', result=? WHERE adventure_id=?;");
            update.Bind(1, DateTime.UtcNow.ToString("O"));
            update.Bind(2, string.IsNullOrWhiteSpace(result) ? "Ended" : result.Trim());
            update.Bind(3, adventureId);
            update.Execute();
        }
    }

    internal IReadOnlyList<AdventureArchiveRecord> List(int maximum)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            var battleTable = TableExists(connection, "battle_records");
            var sql = "SELECT a.adventure_id, a.started_utc, a.ended_utc, a.status, a.result, a.mode_id, a.role_id, a.game_build, a.tool_build, a.mod_fingerprint, a.latest_stage, a.event_count, a.snapshot_count, "
                      + (battleTable ? "(SELECT COUNT(*) FROM battle_records b WHERE b.adventure_id=a.adventure_id)" : "0")
                      + ", a.schema_version, a.data_completeness, a.role_name, a.mode_name"
                      + " FROM adventure_archives a ORDER BY a.started_utc DESC LIMIT ?;";
            using var query = connection.Prepare(sql);
            query.Bind(1, Math.Max(1, Math.Min(2000, maximum)));
            var rows = new List<AdventureArchiveRecord>();
            while (query.Read()) rows.Add(ReadRecord(query));
            return rows;
        }
    }

    internal AdventureArchiveDetails? Load(string adventureId)
    {
        if (string.IsNullOrWhiteSpace(adventureId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            AdventureArchiveRecord? record = null;
            var battleTable = TableExists(connection, "battle_records");
            var sql = "SELECT a.adventure_id, a.started_utc, a.ended_utc, a.status, a.result, a.mode_id, a.role_id, a.game_build, a.tool_build, a.mod_fingerprint, a.latest_stage, a.event_count, a.snapshot_count, "
                      + (battleTable ? "(SELECT COUNT(*) FROM battle_records b WHERE b.adventure_id=a.adventure_id)" : "0")
                      + ", a.schema_version, a.data_completeness, a.role_name, a.mode_name"
                      + " FROM adventure_archives a WHERE a.adventure_id=? LIMIT 1;";
            using (var query = connection.Prepare(sql))
            {
                query.Bind(1, adventureId);
                if (query.Read()) record = ReadRecord(query);
            }
            if (record == null) return null;

            var result = new AdventureArchiveDetails { Record = record };
            using (var events = connection.Prepare(
                       "SELECT sequence, occurred_utc, kind, title, detail, payload_json, dedupe_key FROM adventure_archive_events WHERE adventure_id=? ORDER BY sequence;"))
            {
                events.Bind(1, adventureId);
                while (events.Read())
                {
                    result.Events.Add(new AdventureArchiveEvent
                    {
                        Sequence = (int)events.Int64(0), OccurredUtc = events.Text(1), Kind = events.Text(2),
                        Title = events.Text(3), Detail = events.Text(4), PayloadJson = events.Text(5),
                        DedupeKey = events.Text(6)
                    });
                }
            }
            using (var snapshots = connection.Prepare(
                       "SELECT sequence, occurred_utc, reason, stage, role_id, cards_json, relics_json, state_json, blessings_json FROM adventure_archive_snapshots WHERE adventure_id=? ORDER BY sequence;"))
            {
                snapshots.Bind(1, adventureId);
                while (snapshots.Read())
                {
                    result.Snapshots.Add(new AdventureArchiveSnapshot
                    {
                        Sequence = (int)snapshots.Int64(0), OccurredUtc = snapshots.Text(1), Reason = snapshots.Text(2),
                        Stage = snapshots.Text(3), RoleId = snapshots.Text(4), CardsJson = snapshots.Text(5),
                        RelicsJson = snapshots.Text(6), StateJson = snapshots.Text(7),
                        BlessingsJson = snapshots.Text(8)
                    });
                }
            }
            if (battleTable)
            {
                using var battles = connection.Prepare("SELECT record_id FROM battle_records WHERE adventure_id=? ORDER BY started_utc;");
                battles.Bind(1, adventureId);
                while (battles.Read()) result.BattleRecordIds.Add(battles.Text(0));
            }
            return result;
        }
    }

    internal bool Delete(string adventureId)
    {
        if (string.IsNullOrWhiteSpace(adventureId)) return false;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                DeleteChildren(connection, adventureId);
                using var delete = connection.Prepare("DELETE FROM adventure_archives WHERE adventure_id=?;");
                delete.Bind(1, adventureId);
                delete.Execute();
                var changed = connection.Changes > 0;
                connection.Execute("COMMIT;");
                return changed;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal void Prune(int maximum)
    {
        maximum = Math.Max(10, Math.Min(2000, maximum));
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            var ids = new List<string>();
            using (var query = connection.Prepare(
                       "SELECT adventure_id FROM adventure_archives ORDER BY started_utc DESC LIMIT -1 OFFSET ?;"))
            {
                query.Bind(1, maximum);
                while (query.Read()) ids.Add(query.Text(0));
            }
            foreach (var id in ids) Delete(id);
        }
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        using var connection = Open();
        connection.Execute("PRAGMA foreign_keys=ON;");
        connection.Execute("CREATE TABLE IF NOT EXISTS adventure_archives(adventure_id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, ended_utc TEXT NOT NULL, status TEXT NOT NULL, result TEXT NOT NULL, mode_id TEXT NOT NULL, role_id TEXT NOT NULL, game_build TEXT NOT NULL, tool_build TEXT NOT NULL, mod_fingerprint TEXT NOT NULL, latest_stage TEXT NOT NULL, event_count INTEGER NOT NULL, snapshot_count INTEGER NOT NULL, schema_version INTEGER NOT NULL DEFAULT 2, data_completeness TEXT NOT NULL DEFAULT 'rich', role_name TEXT NOT NULL DEFAULT '', mode_name TEXT NOT NULL DEFAULT '');");
        connection.Execute("CREATE TABLE IF NOT EXISTS adventure_archive_events(adventure_id TEXT NOT NULL, sequence INTEGER NOT NULL, occurred_utc TEXT NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL, detail TEXT NOT NULL, payload_json TEXT NOT NULL, dedupe_key TEXT NOT NULL DEFAULT '', PRIMARY KEY(adventure_id, sequence), FOREIGN KEY(adventure_id) REFERENCES adventure_archives(adventure_id) ON DELETE CASCADE);");
        connection.Execute("CREATE TABLE IF NOT EXISTS adventure_archive_snapshots(adventure_id TEXT NOT NULL, sequence INTEGER NOT NULL, occurred_utc TEXT NOT NULL, reason TEXT NOT NULL, stage TEXT NOT NULL, role_id TEXT NOT NULL, cards_json TEXT NOT NULL, relics_json TEXT NOT NULL, state_json TEXT NOT NULL, blessings_json TEXT NOT NULL DEFAULT '[]', PRIMARY KEY(adventure_id, sequence), FOREIGN KEY(adventure_id) REFERENCES adventure_archives(adventure_id) ON DELETE CASCADE);");
        EnsureColumn(connection, "adventure_archives", "schema_version", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "adventure_archives", "data_completeness", "TEXT NOT NULL DEFAULT 'summary-only'");
        EnsureColumn(connection, "adventure_archives", "role_name", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "adventure_archives", "mode_name", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "adventure_archive_events", "dedupe_key", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "adventure_archive_snapshots", "blessings_json", "TEXT NOT NULL DEFAULT '[]'");
        MigrateLegacyRows(connection);
        connection.Execute("CREATE INDEX IF NOT EXISTS idx_adventure_archives_started ON adventure_archives(started_utc DESC);");
        connection.Execute("CREATE INDEX IF NOT EXISTS idx_adventure_events_kind ON adventure_archive_events(adventure_id, kind);");
        connection.Execute("CREATE UNIQUE INDEX IF NOT EXISTS idx_adventure_events_dedupe ON adventure_archive_events(adventure_id, dedupe_key) WHERE dedupe_key<>'';");
        initialized = true;
    }

    private WinSqliteConnection Open() => new(databasePath);

    private static int NextSequence(WinSqliteConnection connection, string table, string adventureId)
    {
        using var query = connection.Prepare("SELECT COALESCE(MAX(sequence), 0) + 1 FROM " + table + " WHERE adventure_id=?;");
        query.Bind(1, adventureId);
        return query.Read() ? (int)query.Int64(0) : 1;
    }

    private static void UpdateCounts(WinSqliteConnection connection, string adventureId)
    {
        using var update = connection.Prepare(
            "UPDATE adventure_archives SET event_count=(SELECT COUNT(*) FROM adventure_archive_events WHERE adventure_id=?), snapshot_count=(SELECT COUNT(*) FROM adventure_archive_snapshots WHERE adventure_id=?) WHERE adventure_id=?;");
        update.Bind(1, adventureId);
        update.Bind(2, adventureId);
        update.Bind(3, adventureId);
        update.Execute();
    }

    private static void DeleteChildren(WinSqliteConnection connection, string adventureId)
    {
        using (var events = connection.Prepare("DELETE FROM adventure_archive_events WHERE adventure_id=?;"))
        {
            events.Bind(1, adventureId);
            events.Execute();
        }
        using var snapshots = connection.Prepare("DELETE FROM adventure_archive_snapshots WHERE adventure_id=?;");
        snapshots.Bind(1, adventureId);
        snapshots.Execute();
    }

    private static bool TableExists(WinSqliteConnection connection, string table)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, table);
        return query.Read();
    }

    private static bool EventExists(WinSqliteConnection connection, string adventureId, string dedupeKey)
    {
        using var query = connection.Prepare(
            "SELECT 1 FROM adventure_archive_events WHERE adventure_id=? AND dedupe_key=? LIMIT 1;");
        query.Bind(1, adventureId);
        query.Bind(2, dedupeKey);
        return query.Read();
    }

    private static void EnsureColumn(
        WinSqliteConnection connection,
        string table,
        string column,
        string declaration)
    {
        var exists = false;
        using (var query = connection.Prepare("PRAGMA table_info(" + table + ");"))
        {
            while (query.Read())
            {
                if (!string.Equals(query.Text(1), column, StringComparison.OrdinalIgnoreCase)) continue;
                exists = true;
                break;
            }
        }
        if (exists) return;
        connection.Execute("ALTER TABLE " + table + " ADD COLUMN " + column + " " + declaration + ";");
    }

    private static void MigrateLegacyRows(WinSqliteConnection connection)
    {
        var snapshots = new List<(string AdventureId, int Sequence, string Cards, string Relics)>();
        using (var query = connection.Prepare(
                   "SELECT adventure_id, sequence, cards_json, relics_json FROM adventure_archive_snapshots "
                   + "WHERE adventure_id IN (SELECT adventure_id FROM adventure_archives WHERE schema_version<2);"))
        {
            while (query.Read())
            {
                snapshots.Add((query.Text(0), (int)query.Int64(1), query.Text(2), query.Text(3)));
            }
        }
        foreach (var snapshot in snapshots)
        {
            using var update = connection.Prepare(
                "UPDATE adventure_archive_snapshots SET cards_json=?, relics_json=?, blessings_json='[]' WHERE adventure_id=? AND sequence=?;");
            update.Bind(1, AdventureArchiveProjection.MigrateLegacyArray(snapshot.Cards, "牌组"));
            update.Bind(2, AdventureArchiveProjection.MigrateLegacyArray(snapshot.Relics, "遗物"));
            update.Bind(3, snapshot.AdventureId);
            update.Bind(4, snapshot.Sequence);
            update.Execute();
        }
        connection.Execute(
            "UPDATE adventure_archive_events SET dedupe_key='legacy-' || sequence, "
            + "payload_json=CASE WHEN payload_json='' OR payload_json='{}' THEN '{\"legacy\":true}' ELSE payload_json END "
            + "WHERE adventure_id IN (SELECT adventure_id FROM adventure_archives WHERE schema_version<2);");
        connection.Execute(
            "UPDATE adventure_archives SET schema_version=2, data_completeness='summary-only' WHERE schema_version<2;");
    }

    private static AdventureArchiveRecord ReadRecord(WinSqliteConnection.WinSqliteStatement query)
    {
        return new AdventureArchiveRecord
        {
            AdventureId = query.Text(0), StartedUtc = query.Text(1), EndedUtc = query.Text(2), Status = query.Text(3),
            Result = query.Text(4), ModeId = query.Text(5), RoleId = query.Text(6), GameBuild = query.Text(7),
            ToolBuild = query.Text(8), ModFingerprint = query.Text(9), LatestStage = query.Text(10),
            EventCount = (int)query.Int64(11), SnapshotCount = (int)query.Int64(12), BattleCount = (int)query.Int64(13),
            SchemaVersion = (int)query.Int64(14), DataCompleteness = query.Text(15),
            RoleName = query.Text(16), ModeName = query.Text(17)
        };
    }

    private static void TryRollback(WinSqliteConnection connection)
    {
        try { connection.Execute("ROLLBACK;"); } catch { }
    }
}
