using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Presentation;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules;
using System.Collections;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Features.MatchRecords;

public static class AuraToolsMatchRecordsRuntime
{
    private static bool initialized;
    private static bool mediaInitialized;
    private static GameObject? driverRoot;

    public static bool Enabled => AuraToolsConfigService.MatchExperience.MatchRecords.Enabled;

    public static bool ReplayEnabled => Enabled
                                        && AuraToolsConfigService.MatchExperience.MatchRecords.Replay.Enabled;

    public static int AutoRecordCount => SafeCount(Model.MatchRecordCollections.Auto);

    public static int FavoriteRecordCount => SafeCount(Model.MatchRecordCollections.Favorite);

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraToolsDamageMeterRuntime.Initialize(modConfig);
        MatchReplayHookAdapter.Initialize(modConfig);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.DamageStatistics,
            OnConfigChanged);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.BattleReplay,
            OnConfigChanged);
        ApplyModuleActivation();
        AuraToolsLog.Info("[MatchRecords] runtime initialized; replay protocol v"
                          + ReplayProtocolV10.DocumentVersion + ".");
    }

    internal static Coroutine? StartRuntimeCoroutine(IEnumerator routine)
    {
        EnsureDriver();
        return driverRoot?.GetComponent<AuraToolsMatchRecordsDriver>()?.StartCoroutine(routine);
    }

    internal static void ReleaseRuntimeDriver()
    {
        if (driverRoot == null) return;
        Object.Destroy(driverRoot);
        driverRoot = null;
    }

    public static void OpenLibrary(Transform parent)
    {
        MatchRecordLibraryPresenter.Show(parent);
    }

    private static void OnConfigChanged()
    {
        ApplyModuleActivation();
        if (!Enabled && ReplaySceneRuntime.IsActive)
        {
            ReplaySceneRuntime.Stop();
        }
    }

    internal static void ApplyModuleActivation()
    {
        if (!initialized) return;
        MatchReplayHookAdapter.EnsureHooksMatchConfig();
        if (ReplayEnabled && !mediaInitialized)
        {
            mediaInitialized = true;
            Media.MatchReplayVideoExporter.Initialize();
        }
    }

    private static int SafeCount(string collection)
    {
        try
        {
            return MatchRecordStorage.Database.Count(collection);
        }
        catch
        {
            return 0;
        }
    }

    private static void EnsureDriver()
    {
        if (driverRoot != null)
        {
            return;
        }

        driverRoot = new GameObject("AuraToolsMatchRecordsRuntime");
        Object.DontDestroyOnLoad(driverRoot);
        driverRoot.AddComponent<AuraToolsMatchRecordsDriver>();
    }
}

internal sealed class AuraToolsMatchRecordsDriver : MonoBehaviour
{
}
