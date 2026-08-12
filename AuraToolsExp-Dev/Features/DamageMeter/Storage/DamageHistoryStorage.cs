using System;
using System.IO;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.DamageMeter.Storage;

internal static class DamageHistoryStorage
{
    private const string SystemName = "MatchRecords";
    private const string DatabaseFileName = "MatchRecords.sqlite3";
    private const string LegacySystemName = "DamageMeter";
    private const string LegacyDatabaseFileName = "DamageHistory.sqlite3";
    private const string LegacyAdventureMigrationKey = "legacy_adventure_history_imported";
    private static readonly object Gate = new();
    private static DamageHistoryDatabase? database;
    private static bool legacyOutOfRunMigrationChecked;

    internal static DamageHistoryDatabase Database
    {
        get
        {
            lock (Gate)
            {
                if (database == null)
                {
                    var directory = AuraSharedPaths.OwnerSystemDataDirectory(AuraToolsIds.ModId, SystemName);
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, DatabaseFileName);
                    ImportLegacyDatabaseFile(path);
                    database = new DamageHistoryDatabase(path);
                    database.Initialize();
                }

                return database;
            }
        }
    }

    private static void ImportLegacyDatabaseFile(string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        var legacyDirectory = AuraSharedPaths.OwnerSystemDataDirectory(AuraToolsIds.ModId, LegacySystemName);
        var legacyPath = Path.Combine(legacyDirectory, LegacyDatabaseFileName);
        if (!File.Exists(legacyPath))
        {
            return;
        }

        try
        {
            File.Copy(legacyPath, destinationPath, overwrite: false);
            AuraToolsLog.Info("[MatchRecords] copied the legacy DPS history database into the match-record store.");
        }
        catch (IOException) when (File.Exists(destinationPath))
        {
        }
    }

    internal static void ImportLegacyAdventureHistoryOnce()
    {
        var store = Database;
        if (store.HasMeta(LegacyAdventureMigrationKey))
        {
            return;
        }

        var legacy = LegacyOutOfRunDamageHistoryPersistence.LoadLegacyFile();
        var imported = store.ImportAdventures(legacy.Records);
        store.SetMeta(LegacyAdventureMigrationKey, DateTime.UtcNow.ToString("O"));
        if (imported > 0)
        {
            AuraToolsLog.Info("[DamageMeter] migrated " + imported + " legacy adventure history records to SQLite.");
        }
    }

    internal static void EnsureLegacyMigrations()
    {
        lock (Gate)
        {
            if (legacyOutOfRunMigrationChecked)
            {
                return;
            }

            legacyOutOfRunMigrationChecked = true;
            try
            {
                ImportLegacyAdventureHistoryOnce();
            }
            catch (Exception ex)
            {
                legacyOutOfRunMigrationChecked = false;
                AuraToolsLog.Warn("[DamageMeter] legacy history migration failed: " + ex.Message);
            }
        }
    }
}
