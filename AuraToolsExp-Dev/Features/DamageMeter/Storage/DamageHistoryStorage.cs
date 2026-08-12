using System;
using System.IO;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.DamageMeter.Storage;

internal static class DamageHistoryStorage
{
    private const string SystemName = "DamageMeter";
    private const string DatabaseFileName = "DamageHistory.sqlite3";
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
                    database = new DamageHistoryDatabase(Path.Combine(directory, DatabaseFileName));
                    database.Initialize();
                }

                return database;
            }
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
