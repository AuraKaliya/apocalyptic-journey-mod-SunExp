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
                           "INSERT OR IGNORE INTO adventure_archives(adventure_id, started_utc, ended_utc, status, result, mode_id, role_id, game_build, tool_build, mod_fingerprint, latest_stage, event_count, snapshot_count) "
                           + "VALUES(?, ?, '', 'in-progress', '', ?, ?, ?, ?, ?, ?, 0, 0);"))
                {
                    insert.Bind(1, record.AdventureId);
                    insert.Bind(2, record.StartedUtc);
                    insert.Bind(3, record.ModeId);
                    insert.Bind(4, record.RoleId);
                    insert.Bind(5, record.GameBuild);
                    insert.Bind(6, record.ToolBuild);
                    insert.Bind(7, record.ModFingerprint);
                    insert.Bind(8, record.LatestStage);
                    insert.Execute();
                }

                using (var update = connection.Prepare(
                           "UPDATE adventure_archives SET mode_id=?, role_id=?, game_build=?, tool_build=?, mod_fingerprint=?, latest_stage=? WHERE adventure_id=?;"))
                {
                    update.Bind(1, record.ModeId);
                    update.Bind(2, record.RoleId);
                    update.Bind(3, record.GameBuild);
                    update.Bind(4, record.ToolBuild);
                    update.Bind(5, record.ModFingerprint);
                    update.Bind(6, record.LatestStage);
                    update.Bind(7, record.AdventureId);
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
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                item.Sequence = NextSequence(connection, "adventure_archive_events", adventureId);
                using (var insert = connection.Prepare(
                           "INSERT INTO adventure_archive_events(adventure_id, sequence, occurred_utc, kind, title, detail, payload_json) VALUES(?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, adventureId);
                    insert.Bind(2, item.Sequence);
                    insert.Bind(3, item.OccurredUtc);
                    insert.Bind(4, item.Kind);
                    insert.Bind(5, item.Title);
                    insert.Bind(6, item.Detail);
                    insert.Bind(7, item.PayloadJson);
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
                           "INSERT INTO adventure_archive_snapshots(adventure_id, sequence, occurred_utc, reason, stage, role_id, cards_json, relics_json, state_json) VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?);"))
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
                      + " FROM adventure_archives a WHERE a.adventure_id=? LIMIT 1;";
            using (var query = connection.Prepare(sql))
            {
                query.Bind(1, adventureId);
                if (query.Read()) record = ReadRecord(query);
            }
            if (record == null) return null;

            var result = new AdventureArchiveDetails { Record = record };
            using (var events = connection.Prepare(
                       "SELECT sequence, occurred_utc, kind, title, detail, payload_json FROM adventure_archive_events WHERE adventure_id=? ORDER BY sequence;"))
            {
                events.Bind(1, adventureId);
                while (events.Read())
                {
                    result.Events.Add(new AdventureArchiveEvent
                    {
                        Sequence = (int)events.Int64(0), OccurredUtc = events.Text(1), Kind = events.Text(2),
                        Title = events.Text(3), Detail = events.Text(4), PayloadJson = events.Text(5)
                    });
                }
            }
            using (var snapshots = connection.Prepare(
                       "SELECT sequence, occurred_utc, reason, stage, role_id, cards_json, relics_json, state_json FROM adventure_archive_snapshots WHERE adventure_id=? ORDER BY sequence;"))
            {
                snapshots.Bind(1, adventureId);
                while (snapshots.Read())
                {
                    result.Snapshots.Add(new AdventureArchiveSnapshot
                    {
                        Sequence = (int)snapshots.Int64(0), OccurredUtc = snapshots.Text(1), Reason = snapshots.Text(2),
                        Stage = snapshots.Text(3), RoleId = snapshots.Text(4), CardsJson = snapshots.Text(5),
                        RelicsJson = snapshots.Text(6), StateJson = snapshots.Text(7)
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
        connection.Execute("CREATE TABLE IF NOT EXISTS adventure_archives(adventure_id TEXT PRIMARY KEY, started_utc TEXT NOT NULL, ended_utc TEXT NOT NULL, status TEXT NOT NULL, result TEXT NOT NULL, mode_id TEXT NOT NULL, role_id TEXT NOT NULL, game_build TEXT NOT NULL, tool_build TEXT NOT NULL, mod_fingerprint TEXT NOT NULL, latest_stage TEXT NOT NULL, event_count INTEGER NOT NULL, snapshot_count INTEGER NOT NULL);");
        connection.Execute("CREATE TABLE IF NOT EXISTS adventure_archive_events(adventure_id TEXT NOT NULL, sequence INTEGER NOT NULL, occurred_utc TEXT NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL, detail TEXT NOT NULL, payload_json TEXT NOT NULL, PRIMARY KEY(adventure_id, sequence), FOREIGN KEY(adventure_id) REFERENCES adventure_archives(adventure_id) ON DELETE CASCADE);");
        connection.Execute("CREATE TABLE IF NOT EXISTS adventure_archive_snapshots(adventure_id TEXT NOT NULL, sequence INTEGER NOT NULL, occurred_utc TEXT NOT NULL, reason TEXT NOT NULL, stage TEXT NOT NULL, role_id TEXT NOT NULL, cards_json TEXT NOT NULL, relics_json TEXT NOT NULL, state_json TEXT NOT NULL, PRIMARY KEY(adventure_id, sequence), FOREIGN KEY(adventure_id) REFERENCES adventure_archives(adventure_id) ON DELETE CASCADE);");
        connection.Execute("CREATE INDEX IF NOT EXISTS idx_adventure_archives_started ON adventure_archives(started_utc DESC);");
        connection.Execute("CREATE INDEX IF NOT EXISTS idx_adventure_events_kind ON adventure_archive_events(adventure_id, kind);");
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

    private static AdventureArchiveRecord ReadRecord(WinSqliteConnection.WinSqliteStatement query)
    {
        return new AdventureArchiveRecord
        {
            AdventureId = query.Text(0), StartedUtc = query.Text(1), EndedUtc = query.Text(2), Status = query.Text(3),
            Result = query.Text(4), ModeId = query.Text(5), RoleId = query.Text(6), GameBuild = query.Text(7),
            ToolBuild = query.Text(8), ModFingerprint = query.Text(9), LatestStage = query.Text(10),
            EventCount = (int)query.Int64(11), SnapshotCount = (int)query.Int64(12), BattleCount = (int)query.Int64(13)
        };
    }

    private static void TryRollback(WinSqliteConnection connection)
    {
        try { connection.Execute("ROLLBACK;"); } catch { }
    }
}
