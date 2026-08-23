using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static class MatchRecordsDatabaseMigrator
{
    internal const int CurrentVersion = 4;

    internal static void BackupBeforeUpgrade(string databasePath)
    {
        if (!File.Exists(databasePath) || new FileInfo(databasePath).Length == 0) return;
        using var connection = new WinSqliteConnection(databasePath);
        var version = UserVersion(connection);
        if (version >= CurrentVersion) return;
        var backup = databasePath + ".backup-v" + version + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        File.Copy(databasePath, backup, overwrite: false);
        var stale = Directory.GetFiles(Path.GetDirectoryName(databasePath) ?? ".", Path.GetFileName(databasePath) + ".backup-v*")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Skip(3);
        foreach (var path in stale)
        {
            try { File.Delete(path); } catch { }
        }
    }

    internal static void Apply(WinSqliteConnection connection)
    {
        connection.Execute("PRAGMA foreign_keys=ON;");
        var version = UserVersion(connection);
        connection.Execute("BEGIN IMMEDIATE;");
        try
        {
            if (version < 1) connection.Execute("PRAGMA user_version=1;");
            if (version < 2) connection.Execute("PRAGMA user_version=2;");
            if (TableExists(connection, "battle_records") && !ColumnExists(connection, "battle_records", "metadata_payload"))
            {
                connection.Execute("ALTER TABLE battle_records ADD COLUMN metadata_payload BLOB NOT NULL DEFAULT X'';");
            }

            connection.Execute("PRAGMA user_version=" + CurrentVersion + ";");
            connection.Execute("COMMIT;");
        }
        catch
        {
            try { connection.Execute("ROLLBACK;"); } catch { }
            throw;
        }
    }

    internal static void Validate(WinSqliteConnection connection)
    {
        using (var check = connection.Prepare("PRAGMA quick_check;"))
        {
            var result = check.Read() ? check.Text(0) : "unknown";
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Match record database integrity check failed: " + result);
            }
        }

        if (!TableExists(connection, "battle_records")) return;
        VerifyNoOrphans(connection, "replay_chunks");
        VerifyNoOrphans(connection, "match_analysis");
        VerifyNoOrphans(connection, "replay_media");
        VerifyNoOrphans(connection, "replay_documents");
        VerifyNoOrphans(connection, "replay_timeline_chunks");
        VerifyNoOrphans(connection, "replay_asset_refs");
        VerifyNoOrphans(connection, "replay_export_jobs");
    }

    internal static int UserVersion(WinSqliteConnection connection)
    {
        using var query = connection.Prepare("PRAGMA user_version;");
        return query.Read() ? (int)query.Int64(0) : 0;
    }

    internal static string IntegrityCheck(string databasePath)
    {
        using var connection = new WinSqliteConnection(databasePath);
        using var query = connection.Prepare("PRAGMA quick_check;");
        return query.Read() ? query.Text(0) : "unknown";
    }

    private static bool TableExists(WinSqliteConnection connection, string table)
    {
        using var query = connection.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;");
        query.Bind(1, table);
        return query.Read();
    }

    private static bool ColumnExists(WinSqliteConnection connection, string table, string column)
    {
        using var query = connection.Prepare("PRAGMA table_info(" + table + ");");
        while (query.Read())
        {
            if (string.Equals(query.Text(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static void VerifyNoOrphans(WinSqliteConnection connection, string childTable)
    {
        if (!TableExists(connection, childTable)) return;
        using var query = connection.Prepare(
            "SELECT 1 FROM " + childTable + " child LEFT JOIN battle_records parent ON parent.record_id = child.record_id "
            + "WHERE parent.record_id IS NULL LIMIT 1;");
        if (query.Read()) throw new InvalidDataException("Match record database contains orphan rows in " + childTable + ".");
    }
}

internal sealed class MatchRecordMetadata
{
    public string BattleTitle { get; set; } = "";
    public bool IsFavorite { get; set; }
    public string Origin { get; set; } = Model.MatchRecordOrigins.Auto;
    public string Tags { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> RequiredCapabilities { get; set; } = new();
    public List<string> OptionalCapabilities { get; set; } = new();
    public List<string> ContentDependencies { get; set; } = new();
    public string ContentSha256 { get; set; } = "";
    public List<string> CaptureDiagnostics { get; set; } = new();
}
