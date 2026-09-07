using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
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

    public static int AutoRecordCount => MatchRecordStorage.AutoCount;

    public static int FavoriteRecordCount => MatchRecordStorage.FavoriteCount;
    public static bool StorageReady => MatchRecordStorage.Ready;
    public static string StorageStatus => MatchRecordStorage.Status;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        EnsureDriver();
        MatchRecordStorage.InitializeAsync();
        AuraToolsDamageMeterRuntime.Initialize(modConfig);
        MatchReplayHookAdapter.Initialize(modConfig);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.DamageStatistics,
            OnConfigChanged);
        AuraToolsConfigService.SubscribeModule(
            AuraToolModuleIds.BattleReplay,
            OnConfigChanged);
        ApplyModuleActivation();
        EnsureDriver();
        AuraToolsLog.Info("[MatchRecords] runtime initialized; replay protocol v"
                          + ReplayProtocolV17.DocumentVersion + ".");
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
        if (!Enabled && MatchReplayPlayer.IsActive)
        {
            MatchReplayPlayer.StopForModuleDisabled();
        }
    }

    internal static void ApplyModuleActivation()
    {
        if (!initialized) return;
        if (Enabled && !MatchRecordStorage.Ready) MatchRecordStorage.InitializeAsync();
        MatchReplayHookAdapter.EnsureHooksMatchConfig();
        if (ReplayEnabled && !mediaInitialized)
        {
            mediaInitialized = true;
            Media.MatchReplayVideoExporter.Initialize();
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
    private void Update()
    {
        ReplayBackgroundWork.Pump();
        MatchReplayRecorder.PumpPersistence();
        MatchRecordStorage.Pump();
        MatchRecordLibraryPresenter.PumpQuery();
        ReplayV17.Network.ReplayNetworkAuthorityV17.PumpIncoming();
        MatchReplayPlayer.Tick();
    }
}
