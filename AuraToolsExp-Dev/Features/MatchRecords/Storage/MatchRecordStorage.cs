using System;
using System.IO;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static class MatchRecordStorage
{
    private static readonly object Gate = new();
    private static MatchRecordDatabase? database;
    private static bool initializing;
    private static bool countsPending;
    private static bool countsDirty;
    internal static bool Ready { get; private set; }
    internal static string Status { get; private set; } = "等待初始化";
    internal static int AutoCount { get; private set; }
    internal static int FavoriteCount { get; private set; }
    internal static int AdventureCount { get; private set; }
    internal static long DatabaseBytes { get; private set; }

    internal static MatchRecordDatabase Database
    {
        get
        {
            lock (Gate)
            {
                if (database == null)
                {
                    // Construction is pure. Recovery and schema work have an
                    // explicit background lifecycle, never a property side effect.
                    database = new MatchRecordDatabase(DamageHistoryStorage.DatabasePath);
                }

                return database;
            }
        }
    }

    internal static void InitializeAsync()
    {
        if (Ready || initializing) return;
        initializing = true;
        Status = "正在恢复对局资料";
        var store = Database;
        var limit = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit;
        ReplayBackgroundWork.Storage.Enqueue("InitializeRecords", () =>
        {
            _ = DamageHistoryStorage.Database; // Preserve legacy import before opening the new tables.
            store.Initialize();
            var recovered = store.RecoverFinalizingCapturesV17();
            recovered += ReplayReplicaStoreV17.Recover(store, limit);
            foreach (var name in new[] { "Exports", "Imports", "Media", "Temporary" })
                Directory.CreateDirectory(Path.Combine(RootDirectory, name));
            return recovered;
        }, recovered =>
        {
            initializing = false; Ready = true;
            Status = recovered > 0 ? "已恢复 " + recovered + " 条对局" : "对局资料已就绪";
            countsDirty = true;
        }, ex =>
        {
            initializing = false; Status = "对局资料初始化失败：" + ex.Message;
            AuraToolsLog.Warn("[MatchRecords] " + Status);
        });
    }

    internal static void InvalidateCounts() => countsDirty = true;

    internal static void Pump()
    {
        if (!Ready || !countsDirty || countsPending) return;
        countsPending = true;
        if (!ReplayBackgroundWork.Storage.TryEnqueue("RecordCounts", () =>
        {
            var store = Database;
            return (store.Count(MatchRecordCollections.Auto), store.Count(MatchRecordCollections.Favorite),
                File.Exists(store.DatabasePath) ? new FileInfo(store.DatabasePath).Length : 0L,
                DamageHistoryStorage.Database.CountAdventures());
        }, counts =>
        {
            AutoCount = counts.Item1; FavoriteCount = counts.Item2; DatabaseBytes = counts.Item3;
            AdventureCount = counts.Item4;
            countsPending = false;
        }, ex => { countsPending = false; AuraToolsLog.Warn("[MatchRecords] count refresh failed: " + ex.Message); }))
            countsPending = false;
        else countsDirty = false;
    }

    internal static string RootDirectory => Path.GetDirectoryName(Database.DatabasePath) ?? ".";

    internal static string ExportsDirectory => Ensure("Exports");

    internal static string ImportsDirectory => Ensure("Imports");

    internal static string MediaDirectory => Ensure("Media");

    internal static string TemporaryDirectory => Ensure("Temporary");

    private static string Ensure(string name)
    {
        var path = Path.Combine(RootDirectory, name);
        return path;
    }
}
