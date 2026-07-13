using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class CompanionSceneLifecycleRuntime
{
    private static readonly object CleanupGate = new();
    private static bool initialized;
    private static bool cleanupInProgress;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        SunExpBattleLifecycleRouter.Register("CompanionScene", new SunExpBattleLifecycleSubscription
        {
            FightStarted = _ => CompanionSceneApi.TrackBattleScene(
                SceneManager.GetActiveScene(),
                "FightStarted"),
            FightEnding = _ => CompanionSceneApi.ClearTrackedScenes("FightEnding")
        });
        SunExpLog.Info("Companion scene lifecycle runtime initialized");
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        if (!CompanionSceneApi.IsTracked(scene))
        {
            return;
        }

        CleanupAfterSceneBoundary("SceneUnloaded:" + scene.name + ":" + scene.handle);
    }

    private static void OnActiveSceneChanged(Scene previous, Scene next)
    {
        if (!CompanionSceneApi.IsTracked(previous))
        {
            return;
        }

        var previousHandle = previous.handle;
        SunExpFrameDispatcher.RunOnceNextFrame(
            "CompanionSceneBoundary.ActiveChanged." + previousHandle,
            () =>
            {
                if (CompanionSceneApi.IsTracked(previousHandle)
                    && !CompanionSceneApi.IsSceneLoaded(previousHandle))
                {
                    CleanupAfterSceneBoundary(
                        "ActiveSceneChanged:"
                        + previous.name
                        + ":"
                        + previousHandle
                        + "->"
                        + next.name
                        + ":"
                        + next.handle);
                }
            });
    }

    internal static void CleanupAfterSceneBoundary(string source)
    {
        lock (CleanupGate)
        {
            if (cleanupInProgress)
            {
                return;
            }

            cleanupInProgress = true;
        }

        try
        {
            RunCleanupStep("InvalidateBattleEpoch", source, CompanionAuthorityService.InvalidateBattleEpoch);
            RunCleanupStep("Projection", source, () => ProjectionRuntime.ClearBattle(source));
            RunCleanupStep("Spirit", source, () => SpiritRuntime.ClearBattle(source));
            RunCleanupStep("CompanionState", source, CompanionBattleStateStore.Clear);
            RunCleanupStep("OrphanedObjects", source, DestroyOrphanedCompanionObjects);
            RunCleanupStep("ProjectionProxies", source, () => ProjectionAttachmentPresenter.ClearAll(source));
            RunCleanupStep("SpiritProxies", source, () => SpiritAttachmentPresenter.ClearAll(source));
            SunExpPerformanceCounters.Record("CompanionScene.Cleared");
            SunExpLog.Info("[CompanionScene] battle state cleared from " + source);
        }
        finally
        {
            CompanionSceneApi.ClearTrackedScenes(source);
            lock (CleanupGate)
            {
                cleanupInProgress = false;
            }
        }
    }

    private static void DestroyOrphanedCompanionObjects()
    {
        DestroyAll<ProjectionOtherObj>();
        DestroyAll<SpiritOtherObj>();
        DestroyAll<ProjectionTurnAnchorObj>();
    }

    private static void DestroyAll<T>()
        where T : UnityEngine.Component
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component == null
                || component.gameObject == null
                || !component.gameObject.scene.IsValid())
            {
                continue;
            }

            component.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(component.gameObject);
        }
    }

    private static void RunCleanupStep(string step, string source, Action action)
    {
        try
        {
            action();
            SunExpPerformanceCounters.Record("CompanionScene.CleanupStep." + step);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Companion scene cleanup step failed: " + step + " @ " + source, ex);
        }
    }
}
