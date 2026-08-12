using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

namespace AuraToolsExp.Dll.Features.DamageMeter.Storage;

internal sealed class DamageHistoryPage<T>
{
    internal DamageHistoryPage(IReadOnlyList<T> items, long nextCursor, bool hasMore, int totalCount)
    {
        Items = items;
        NextCursor = nextCursor;
        HasMore = hasMore;
        TotalCount = totalCount;
    }

    internal IReadOnlyList<T> Items { get; }

    internal long NextCursor { get; }

    internal bool HasMore { get; }

    internal int TotalCount { get; }
}

internal sealed class DamageHistoryDatabase
{
    internal const int DefaultPageSize = 30;
    private const int MaximumPageSize = 100;
    private readonly object gate = new();
    private readonly string databasePath;
    private bool initialized;

    internal DamageHistoryDatabase(string databasePath)
    {
        this.databasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
    }

    internal string DatabasePath => databasePath;

    internal void Initialize()
    {
        lock (gate)
        {
            EnsureInitialized();
        }
    }

    internal DamageFightRecord? AppendFight(string adventureId, DamageFightRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(adventureId) || string.IsNullOrWhiteSpace(record.SessionId))
        {
            return null;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var existing = FindFightSequence(connection, adventureId, record.SessionId);
                if (existing > 0)
                {
                    connection.Execute("COMMIT;");
                    return null;
                }

                var sequence = NextFightSequence(connection, adventureId);
                var stored = CloneFight(record);
                stored.Sequence = sequence;
                using (var insert = connection.Prepare(
                           "INSERT INTO fight_history(adventure_id, sequence, session_id, result, ended_utc, completed_rounds, total_damage, payload) "
                           + "VALUES(?, ?, ?, ?, ?, ?, ?, ?);"))
                {
                    insert.Bind(1, adventureId.Trim());
                    insert.Bind(2, sequence);
                    insert.Bind(3, stored.SessionId);
                    insert.Bind(4, stored.Result);
                    insert.Bind(5, stored.EndedUtc);
                    insert.Bind(6, Math.Max(0, stored.Snapshot?.CompletedRoundCount ?? 0));
                    insert.Bind(7, FightTotal(stored));
                    insert.Bind(8, DamageHistoryPayload.Encode(stored));
                    insert.Execute();
                }

                connection.Execute("COMMIT;");
                return stored;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal int ImportFights(string adventureId, IEnumerable<DamageFightRecord>? records)
    {
        var imported = 0;
        foreach (var record in records ?? Array.Empty<DamageFightRecord>())
        {
            if (AppendFight(adventureId, record) != null)
            {
                imported++;
            }
        }

        return imported;
    }

    internal void SaveRunState(string adventureId, DamageRunAggregateSnapshot snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(adventureId))
        {
            return;
        }

        lock (gate)
        {
            EnsureInitialized();
            snapshot.ProtocolVersion = DamageMeterProtocol.Version;
            using var connection = Open();
            using var statement = connection.Prepare(
                "INSERT OR REPLACE INTO run_state(adventure_id, started_utc, updated_utc, payload) VALUES(?, ?, ?, ?);");
            statement.Bind(1, adventureId.Trim());
            statement.Bind(2, snapshot.StartedUtc ?? "");
            statement.Bind(3, snapshot.UpdatedUtc ?? "");
            statement.Bind(4, DamageHistoryPayload.Encode(snapshot));
            statement.Execute();
        }
    }

    internal DamageRunAggregateSnapshot? LoadRunState(string adventureId)
    {
        if (string.IsNullOrWhiteSpace(adventureId))
        {
            return null;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var statement = connection.Prepare("SELECT payload FROM run_state WHERE adventure_id = ?;");
            statement.Bind(1, adventureId.Trim());
            if (!statement.Read())
            {
                return null;
            }

            var snapshot = DamageHistoryPayload.Decode<DamageRunAggregateSnapshot>(statement.Blob(0));
            if (snapshot != null)
            {
                snapshot.ProtocolVersion = DamageMeterProtocol.Version;
            }

            return snapshot;
        }
    }

    internal DamageHistoryPage<DamageFightRecord> LoadFightPage(string adventureId, long beforeSequence = 0, int pageSize = DefaultPageSize)
    {
        var normalizedAdventureId = adventureId ?? "";
        lock (gate)
        {
            EnsureInitialized();
            var normalizedPageSize = NormalizePageSize(pageSize);
            var items = new List<DamageFightRecord>(normalizedPageSize);
            using var connection = Open();
            using (var statement = connection.Prepare(
                       "SELECT sequence, payload FROM fight_history "
                       + "WHERE adventure_id = ? AND (? <= 0 OR sequence < ?) "
                       + "ORDER BY sequence DESC LIMIT ?;"))
            {
                statement.Bind(1, normalizedAdventureId);
                statement.Bind(2, beforeSequence);
                statement.Bind(3, beforeSequence);
                statement.Bind(4, normalizedPageSize + 1L);
                while (statement.Read())
                {
                    var record = DamageHistoryPayload.Decode<DamageFightRecord>(statement.Blob(1));
                    if (record == null)
                    {
                        continue;
                    }

                    record.Sequence = (int)Math.Max(0, Math.Min(int.MaxValue, statement.Int64(0)));
                    NormalizeFight(record);
                    items.Add(record);
                }
            }

            var hasMore = items.Count > normalizedPageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            var nextCursor = items.Count == 0 ? 0 : items[items.Count - 1].Sequence;
            return new DamageHistoryPage<DamageFightRecord>(items, nextCursor, hasMore, CountFights(connection, normalizedAdventureId));
        }
    }

    internal int CountFights(string adventureId)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            return CountFights(connection, adventureId);
        }
    }

    internal bool DeleteFight(string adventureId, int sequence)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var statement = connection.Prepare("DELETE FROM fight_history WHERE adventure_id = ? AND sequence = ?;");
            statement.Bind(1, adventureId ?? "");
            statement.Bind(2, sequence);
            statement.Execute();
            return connection.Changes > 0;
        }
    }

    internal int ClearFights(string adventureId)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var statement = connection.Prepare("DELETE FROM fight_history WHERE adventure_id = ?;");
            statement.Bind(1, adventureId ?? "");
            statement.Execute();
            return connection.Changes;
        }
    }

    internal bool AppendAdventure(OutOfRunDamageHistoryRecord record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.AdventureId))
        {
            return false;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                if (AdventureExists(connection, record.AdventureId))
                {
                    connection.Execute("COMMIT;");
                    return false;
                }

                StoreAvatars(connection, record);
                var stored = CloneAdventureWithoutAvatarPayload(record);
                using var insert = connection.Prepare(
                    "INSERT OR IGNORE INTO adventure_history(adventure_id, mode_id, mode_name, status, ended_utc, team_total_damage, total_rounds, team_dpt, payload) "
                    + "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?);");
                insert.Bind(1, stored.AdventureId);
                insert.Bind(2, stored.ModeId);
                insert.Bind(3, stored.ModeDisplayName);
                insert.Bind(4, stored.Status);
                insert.Bind(5, stored.EndedUtc);
                insert.Bind(6, stored.TeamTotalDamage);
                insert.Bind(7, stored.TotalRounds);
                insert.Bind(8, stored.TeamDps);
                insert.Bind(9, DamageHistoryPayload.Encode(stored));
                insert.Execute();
                var added = connection.Changes > 0;
                connection.Execute("COMMIT;");
                return added;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal int ImportAdventures(IEnumerable<OutOfRunDamageHistoryRecord>? records)
    {
        var imported = 0;
        foreach (var record in records ?? Array.Empty<OutOfRunDamageHistoryRecord>())
        {
            if (AppendAdventure(record))
            {
                imported++;
            }
        }

        return imported;
    }

    internal DamageHistoryPage<OutOfRunDamageHistoryRecord> LoadAdventurePage(long beforeSequence = 0, int pageSize = DefaultPageSize)
    {
        lock (gate)
        {
            EnsureInitialized();
            var normalizedPageSize = NormalizePageSize(pageSize);
            var items = new List<OutOfRunDamageHistoryRecord>(normalizedPageSize);
            using var connection = Open();
            using (var statement = connection.Prepare(
                       "SELECT sequence, payload FROM adventure_history "
                       + "WHERE (? <= 0 OR sequence < ?) ORDER BY sequence DESC LIMIT ?;"))
            {
                statement.Bind(1, beforeSequence);
                statement.Bind(2, beforeSequence);
                statement.Bind(3, normalizedPageSize + 1L);
                while (statement.Read())
                {
                    var record = DamageHistoryPayload.Decode<OutOfRunDamageHistoryRecord>(statement.Blob(1));
                    if (record == null)
                    {
                        continue;
                    }

                    record.Sequence = (int)Math.Max(0, Math.Min(int.MaxValue, statement.Int64(0)));
                    HydrateAvatars(connection, record);
                    items.Add(record);
                }
            }

            var hasMore = items.Count > normalizedPageSize;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            var nextCursor = items.Count == 0 ? 0 : items[items.Count - 1].Sequence;
            return new DamageHistoryPage<OutOfRunDamageHistoryRecord>(items, nextCursor, hasMore, CountAdventures(connection));
        }
    }

    internal int CountAdventures()
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            return CountAdventures(connection);
        }
    }

    internal bool DeleteAdventure(int sequence)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            var adventureId = "";
            using (var lookup = connection.Prepare("SELECT adventure_id FROM adventure_history WHERE sequence = ?;"))
            {
                lookup.Bind(1, sequence);
                if (lookup.Read())
                {
                    adventureId = lookup.Text(0);
                }
            }

            if (string.IsNullOrWhiteSpace(adventureId))
            {
                return false;
            }

            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                DeleteAdventureData(connection, adventureId);
                connection.Execute("COMMIT;");
                connection.Execute("VACUUM;");
                return true;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal int ClearAdventures()
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            var count = CountAdventures(connection);
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                connection.Execute("DELETE FROM fight_history WHERE adventure_id IN (SELECT adventure_id FROM adventure_history);");
                connection.Execute("DELETE FROM run_state WHERE adventure_id IN (SELECT adventure_id FROM adventure_history);");
                connection.Execute("DELETE FROM adventure_history;");
                RemoveUnusedAvatars(connection);
                connection.Execute("COMMIT;");
                connection.Execute("VACUUM;");
                return count;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal int DeleteAdventuresBefore(string endedUtcExclusive)
    {
        if (string.IsNullOrWhiteSpace(endedUtcExclusive))
        {
            return 0;
        }

        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            var ids = new List<string>();
            using (var query = connection.Prepare("SELECT adventure_id FROM adventure_history WHERE ended_utc < ?;"))
            {
                query.Bind(1, endedUtcExclusive.Trim());
                while (query.Read())
                {
                    ids.Add(query.Text(0));
                }
            }

            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                foreach (var id in ids)
                {
                    DeleteAdventureData(connection, id, removeUnusedAvatars: false);
                }

                RemoveUnusedAvatars(connection);
                connection.Execute("COMMIT;");
                connection.Execute("VACUUM;");
                return ids.Count;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal bool HasMeta(string key)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var statement = connection.Prepare("SELECT 1 FROM meta WHERE key = ? LIMIT 1;");
            statement.Bind(1, key ?? "");
            return statement.Read();
        }
    }

    internal void SetMeta(string key, string value)
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var statement = connection.Prepare("INSERT OR REPLACE INTO meta(key, value) VALUES(?, ?);");
            statement.Bind(1, key ?? "");
            statement.Bind(2, value ?? "");
            statement.Execute();
        }
    }

    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        MatchRecordsDatabaseMigrator.BackupBeforeUpgrade(databasePath);
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode=DELETE;");
        connection.Execute("PRAGMA synchronous=NORMAL;");
        connection.Execute("CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);");
        connection.Execute("CREATE TABLE IF NOT EXISTS fight_history("
                           + "adventure_id TEXT NOT NULL, sequence INTEGER NOT NULL, session_id TEXT NOT NULL, result TEXT NOT NULL, "
                           + "ended_utc TEXT NOT NULL, completed_rounds INTEGER NOT NULL, total_damage INTEGER NOT NULL, payload BLOB NOT NULL, "
                           + "PRIMARY KEY(adventure_id, sequence), UNIQUE(adventure_id, session_id));");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_fight_history_recent ON fight_history(adventure_id, sequence DESC);");
        connection.Execute("CREATE TABLE IF NOT EXISTS run_state("
                           + "adventure_id TEXT PRIMARY KEY NOT NULL, started_utc TEXT NOT NULL, updated_utc TEXT NOT NULL, payload BLOB NOT NULL);");
        connection.Execute("CREATE TABLE IF NOT EXISTS adventure_history("
                           + "sequence INTEGER PRIMARY KEY AUTOINCREMENT, adventure_id TEXT UNIQUE NOT NULL, mode_id TEXT NOT NULL, mode_name TEXT NOT NULL, "
                           + "status TEXT NOT NULL, ended_utc TEXT NOT NULL, team_total_damage INTEGER NOT NULL, total_rounds INTEGER NOT NULL, "
                           + "team_dpt REAL NOT NULL, payload BLOB NOT NULL);");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_adventure_history_recent ON adventure_history(sequence DESC);");
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_adventure_history_ended ON adventure_history(ended_utc);");
        connection.Execute("CREATE TABLE IF NOT EXISTS avatars(sha256 TEXT PRIMARY KEY NOT NULL, png BLOB NOT NULL);");
        MatchRecordsDatabaseMigrator.Apply(connection);
        MatchRecordsDatabaseMigrator.Validate(connection);
        initialized = true;
    }

    private WinSqliteConnection Open()
    {
        return new WinSqliteConnection(databasePath);
    }

    private static int NextFightSequence(WinSqliteConnection connection, string adventureId)
    {
        using var statement = connection.Prepare("SELECT COALESCE(MAX(sequence), 0) + 1 FROM fight_history WHERE adventure_id = ?;");
        statement.Bind(1, adventureId);
        return statement.Read() ? (int)Math.Min(int.MaxValue, statement.Int64(0)) : 1;
    }

    private static int FindFightSequence(WinSqliteConnection connection, string adventureId, string sessionId)
    {
        using var statement = connection.Prepare("SELECT sequence FROM fight_history WHERE adventure_id = ? AND session_id = ?;");
        statement.Bind(1, adventureId);
        statement.Bind(2, sessionId);
        return statement.Read() ? (int)Math.Min(int.MaxValue, statement.Int64(0)) : 0;
    }

    private static int CountFights(WinSqliteConnection connection, string adventureId)
    {
        using var statement = connection.Prepare("SELECT COUNT(*) FROM fight_history WHERE adventure_id = ?;");
        statement.Bind(1, adventureId ?? "");
        return statement.Read() ? (int)Math.Min(int.MaxValue, statement.Int64(0)) : 0;
    }

    private static int CountAdventures(WinSqliteConnection connection)
    {
        using var statement = connection.Prepare("SELECT COUNT(*) FROM adventure_history;");
        return statement.Read() ? (int)Math.Min(int.MaxValue, statement.Int64(0)) : 0;
    }

    private static bool AdventureExists(WinSqliteConnection connection, string adventureId)
    {
        using var statement = connection.Prepare("SELECT 1 FROM adventure_history WHERE adventure_id = ? LIMIT 1;");
        statement.Bind(1, adventureId);
        return statement.Read();
    }

    private static long FightTotal(DamageFightRecord record)
    {
        return (record.Snapshot?.Combatants ?? new List<CombatantDamageStat>())
            .Where(stat => stat != null)
            .Sum(stat => Math.Max(0, stat.TotalHpDamage) + Math.Max(0, stat.TotalShieldDamage));
    }

    private static void StoreAvatars(WinSqliteConnection connection, OutOfRunDamageHistoryRecord record)
    {
        foreach (var member in record.TeamMembers ?? new List<OutOfRunTeamMemberSnapshot>())
        {
            if (member == null || string.IsNullOrWhiteSpace(member.AvatarPngBase64))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(member.AvatarPngBase64);
            }
            catch
            {
                continue;
            }

            if (bytes.Length == 0)
            {
                continue;
            }

            var sha = string.IsNullOrWhiteSpace(member.AvatarSha256)
                ? Sha256Hex(bytes)
                : member.AvatarSha256.Trim().ToLowerInvariant();
            member.AvatarSha256 = sha;
            using var insert = connection.Prepare("INSERT OR IGNORE INTO avatars(sha256, png) VALUES(?, ?);");
            insert.Bind(1, sha);
            insert.Bind(2, bytes);
            insert.Execute();
        }
    }

    private static void HydrateAvatars(WinSqliteConnection connection, OutOfRunDamageHistoryRecord record)
    {
        foreach (var member in record.TeamMembers ?? new List<OutOfRunTeamMemberSnapshot>())
        {
            if (member == null || string.IsNullOrWhiteSpace(member.AvatarSha256))
            {
                continue;
            }

            using var query = connection.Prepare("SELECT png FROM avatars WHERE sha256 = ?;");
            query.Bind(1, member.AvatarSha256);
            if (query.Read())
            {
                var bytes = query.Blob(0);
                member.AvatarPngBase64 = bytes.Length == 0 ? "" : Convert.ToBase64String(bytes);
            }
        }
    }

    private static void DeleteAdventureData(
        WinSqliteConnection connection,
        string adventureId,
        bool removeUnusedAvatars = true)
    {
        using (var fights = connection.Prepare("DELETE FROM fight_history WHERE adventure_id = ?;"))
        {
            fights.Bind(1, adventureId);
            fights.Execute();
        }

        using (var state = connection.Prepare("DELETE FROM run_state WHERE adventure_id = ?;"))
        {
            state.Bind(1, adventureId);
            state.Execute();
        }

        using (var adventure = connection.Prepare("DELETE FROM adventure_history WHERE adventure_id = ?;"))
        {
            adventure.Bind(1, adventureId);
            adventure.Execute();
        }

        if (removeUnusedAvatars)
        {
            RemoveUnusedAvatars(connection);
        }
    }

    private static void RemoveUnusedAvatars(WinSqliteConnection connection)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var query = connection.Prepare("SELECT payload FROM adventure_history;"))
        {
            while (query.Read())
            {
                var record = DamageHistoryPayload.Decode<OutOfRunDamageHistoryRecord>(query.Blob(0));
                foreach (var member in record?.TeamMembers ?? new List<OutOfRunTeamMemberSnapshot>())
                {
                    if (!string.IsNullOrWhiteSpace(member?.AvatarSha256))
                    {
                        used.Add(member!.AvatarSha256);
                    }
                }
            }
        }

        if (used.Count == 0)
        {
            connection.Execute("DELETE FROM avatars;");
            return;
        }

        var stale = new List<string>();
        using (var query = connection.Prepare("SELECT sha256 FROM avatars;"))
        {
            while (query.Read())
            {
                var sha = query.Text(0);
                if (!used.Contains(sha))
                {
                    stale.Add(sha);
                }
            }
        }

        foreach (var sha in stale)
        {
            using var delete = connection.Prepare("DELETE FROM avatars WHERE sha256 = ?;");
            delete.Bind(1, sha);
            delete.Execute();
        }
    }

    private static OutOfRunDamageHistoryRecord CloneAdventureWithoutAvatarPayload(OutOfRunDamageHistoryRecord source)
    {
        var clone = DamageHistoryPayload.Decode<OutOfRunDamageHistoryRecord>(DamageHistoryPayload.Encode(source))
                    ?? new OutOfRunDamageHistoryRecord();
        foreach (var member in clone.TeamMembers ?? new List<OutOfRunTeamMemberSnapshot>())
        {
            member.AvatarPngBase64 = "";
        }

        return clone;
    }

    private static DamageFightRecord CloneFight(DamageFightRecord source)
    {
        var clone = DamageHistoryPayload.Decode<DamageFightRecord>(DamageHistoryPayload.Encode(source))
                    ?? new DamageFightRecord();
        NormalizeFight(clone);
        return clone;
    }

    private static void NormalizeFight(DamageFightRecord record)
    {
        record.SessionId ??= "";
        record.Result ??= "";
        record.EndedUtc ??= "";
        record.Snapshot ??= new DamageMeterSnapshot();
        record.Snapshot.ProtocolVersion = DamageMeterProtocol.Version;
        record.Snapshot.RunAggregate = null;
    }

    private static int NormalizePageSize(int pageSize)
    {
        return Math.Max(1, Math.Min(MaximumPageSize, pageSize <= 0 ? DefaultPageSize : pageSize));
    }

    private static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private static void TryRollback(WinSqliteConnection connection)
    {
        try
        {
            connection.Execute("ROLLBACK;");
        }
        catch
        {
        }
    }
}

internal static class DamageHistoryPayload
{
    internal static byte[] Encode<T>(T value)
    {
        var json = AuraSharedJson.SerializeCompact(value);
        var input = Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(input, 0, input.Length);
        }

        return output.ToArray();
    }

    internal static T? Decode<T>(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return default;
        }

        using var input = new MemoryStream(payload, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return AuraSharedJson.Deserialize<T>(reader.ReadToEnd());
    }
}
