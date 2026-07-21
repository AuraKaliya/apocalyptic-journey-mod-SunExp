using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class CompanionSceneLifecycleRuntime
{
    private static readonly object CleanupGate = new();
    private static bool initialized;
    private static bool cleanupInProgress;
    private static bool cleanupPending = true;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        RegisterMenuExitBoundary(modConfig, "TopBarUI.ReturnToMenu");
        RegisterMenuExitBoundary(modConfig, "GameApp.ReturnToMenu");
        RegisterMenuExitBoundary(modConfig, "GameEntryUI.Init");
        TerriasBattleLifecycleRouter.Register("CompanionScene", new TerriasBattleLifecycleSubscription
        {
            FightInitializing = _ => CleanupAfterSceneBoundary("FightInitializing"),
            FightStarted = _ => CompanionSceneApi.TrackBattleScene(
                SceneManager.GetActiveScene(),
                "FightStarted"),
            FightEnded = _ => CleanupAfterSceneBoundary("FightEnded")
        });
        TerriasLog.Info("Companion scene lifecycle runtime initialized");
    }

    private static void RegisterMenuExitBoundary(ModConfig modConfig, string target)
    {
        TerriasHookRegistry.Before(
            modConfig,
            target,
            _ => CleanupAfterSceneBoundary(target),
            "CompanionScene");
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
        TerriasFrameDispatcher.RunOnceNextFrame(
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
        var hasTrackedScenes = CompanionSceneApi.HasTrackedScenes();
        lock (CleanupGate)
        {
            if (cleanupInProgress)
            {
                return;
            }

            if (!cleanupPending && !hasTrackedScenes)
            {
                TerriasPerformanceCounters.Record("CompanionScene.CleanupDeduplicated");
                TerriasLog.Debug("[CompanionScene] duplicate cleanup boundary skipped: source=" + source);
                return;
            }

            cleanupInProgress = true;
        }

        var cleanupStarted = TerriasPerformanceCounters.Timestamp();
        try
        {
            var suppression = default(CompanionPresentationSuppression);
            var cleanupSucceeded = RunCleanupStep(
                "SuppressPresentation",
                source,
                () => suppression = CompanionPresentationCleanup.SuppressAll(source));
            cleanupSucceeded &= RunCleanupStep("InvalidateBattleEpoch", source, CompanionAuthorityService.InvalidateBattleEpoch);
            cleanupSucceeded &= RunCleanupStep("Projection", source, () => ProjectionRuntime.ClearBattle(source, sweepVisualOrphans: false));
            cleanupSucceeded &= RunCleanupStep("Spirit", source, () => SpiritRuntime.ClearBattle(source, sweepVisualOrphans: false));
            cleanupSucceeded &= RunCleanupStep("CompanionState", source, CompanionBattleStateStore.Clear);

            var needsOrphanSweep = !suppression.Available || suppression.Total > 0 || !cleanupSucceeded;
            if (needsOrphanSweep)
            {
                cleanupSucceeded &= RunCleanupStep("OrphanedObjects", source, DestroyOrphanedCompanionObjects);
                cleanupSucceeded &= RunCleanupStep("ProjectionProxies", source, () => ProjectionAttachmentPresenter.ClearAll(source));
                cleanupSucceeded &= RunCleanupStep("SpiritProxies", source, () => SpiritAttachmentPresenter.ClearAll(source));
            }

            TerriasPerformanceCounters.Record("CompanionScene.Cleared");
            LogCleanupSummary(source, suppression, cleanupSucceeded, needsOrphanSweep);
            SchedulePostCleanupAudit(source, suppression.Total > 0 || !cleanupSucceeded);
        }
        finally
        {
            CompanionSceneApi.ClearTrackedScenes(source);
            lock (CleanupGate)
            {
                cleanupPending = false;
                cleanupInProgress = false;
            }

            TerriasPerformanceCounters.RecordHotspot(
                "CompanionScene.Cleanup",
                cleanupStarted,
                "source=" + source,
                slowWarningMilliseconds: 8.0);
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

    private static CompanionArtifactSnapshot CaptureArtifactSnapshot(string source)
    {
        try
        {
            return new CompanionArtifactSnapshot(
                CountSceneObjects<ProjectionOtherObj>(),
                CountSceneObjects<SpiritOtherObj>(),
                CountSceneObjects<ProjectionTurnAnchorObj>(),
                CountSceneObjects<ProjectionVisualProxy>(component =>
                    component.gameObject.name.StartsWith(
                        "Terrias_ProjectionVisualProxy:",
                        StringComparison.Ordinal)),
                CountSceneObjects<ProjectionVisualProxy>(component =>
                    component.gameObject.name.StartsWith(
                        "Terrias_SpiritVisualProxy:",
                        StringComparison.Ordinal)));
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CompanionScene] artifact snapshot failed @ " + source + ": " + ex.Message);
            return CompanionArtifactSnapshot.Unavailable;
        }
    }

    private static int CountSceneObjects<T>(Func<T, bool>? predicate = null)
        where T : UnityEngine.Component
    {
        var count = 0;
        foreach (var component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component == null
                || component.gameObject == null
                || !component.gameObject.scene.IsValid()
                || (predicate != null && !predicate(component)))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static void LogCleanupSummary(
        string source,
        CompanionPresentationSuppression suppression,
        bool cleanupSucceeded,
        bool orphanSweep)
    {
        var message = suppression.Available
            ? "[CompanionScene] cleanup boundary processed: source=" + source
              + ", projectionRoots=" + suppression.ProjectionRoots
              + ", spiritRoots=" + suppression.SpiritRoots
              + ", turnAnchors=" + suppression.TurnAnchors
              + ", projectionProxies=" + suppression.ProjectionProxies
              + ", spiritProxies=" + suppression.SpiritProxies
              + ", suppressedActorRoots=" + suppression.ActorRoots
              + ", suppressedProxyRoots=" + suppression.ProxyRoots
              + ", suppressedRenderers=" + suppression.Renderers
              + ", suppressedUi=" + suppression.UiObjects
              + ", orphanSweep=" + orphanSweep
              + ", success=" + cleanupSucceeded
            : "[CompanionScene] cleanup boundary processed: source=" + source
              + ", artifactCounts=unavailable"
              + ", orphanSweep=" + orphanSweep
              + ", success=" + cleanupSucceeded;

        if (!suppression.Available || suppression.Total > 0 || !cleanupSucceeded)
        {
            TerriasLog.InfoAlways(message);
            return;
        }

        TerriasLog.Info(message);
    }

    private static void SchedulePostCleanupAudit(string source, bool required)
    {
        if (!required || !IsMenuExitBoundary(source))
        {
            return;
        }

        var frame = Time.frameCount;
        TerriasFrameDispatcher.RunOnceNextFrame(
            "CompanionScene.PostCleanupAudit." + frame,
            () =>
            {
                var remaining = CaptureArtifactSnapshot(source + ":post-frame");
                var message = remaining.Available
                    ? "[CompanionScene] post-cleanup audit: source=" + source
                      + ", remaining=" + remaining.Total
                      + ", projectionRoots=" + remaining.ProjectionRoots
                      + ", spiritRoots=" + remaining.SpiritRoots
                      + ", turnAnchors=" + remaining.TurnAnchors
                      + ", projectionProxies=" + remaining.ProjectionProxies
                      + ", spiritProxies=" + remaining.SpiritProxies
                    : "[CompanionScene] post-cleanup audit: source=" + source
                      + ", remaining=unavailable";

                if (!remaining.Available || remaining.Total > 0)
                {
                    TerriasLog.Warn(message);
                    MarkCleanupPending();
                    if (remaining.Available && remaining.Total > 0)
                    {
                        CleanupAfterSceneBoundary(source + ":post-audit-recovery");
                    }
                    return;
                }

                TerriasLog.InfoAlways(message);
            });
    }

    private static void MarkCleanupPending()
    {
        lock (CleanupGate)
        {
            cleanupPending = true;
        }
    }

    private static bool IsMenuExitBoundary(string source)
    {
        return string.Equals(source, "TopBarUI.ReturnToMenu", StringComparison.Ordinal)
               || string.Equals(source, "GameApp.ReturnToMenu", StringComparison.Ordinal)
               || string.Equals(source, "GameEntryUI.Init", StringComparison.Ordinal);
    }

    private static bool RunCleanupStep(string step, string source, Action action)
    {
        try
        {
            action();
            TerriasPerformanceCounters.Record("CompanionScene.CleanupStep." + step);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Companion scene cleanup step failed: " + step + " @ " + source, ex);
            return false;
        }
    }

    private readonly struct CompanionArtifactSnapshot
    {
        public static readonly CompanionArtifactSnapshot Unavailable = new(false, 0, 0, 0, 0, 0);

        public CompanionArtifactSnapshot(
            int projectionRoots,
            int spiritRoots,
            int turnAnchors,
            int projectionProxies,
            int spiritProxies)
            : this(
                true,
                projectionRoots,
                spiritRoots,
                turnAnchors,
                projectionProxies,
                spiritProxies)
        {
        }

        private CompanionArtifactSnapshot(
            bool available,
            int projectionRoots,
            int spiritRoots,
            int turnAnchors,
            int projectionProxies,
            int spiritProxies)
        {
            Available = available;
            ProjectionRoots = projectionRoots;
            SpiritRoots = spiritRoots;
            TurnAnchors = turnAnchors;
            ProjectionProxies = projectionProxies;
            SpiritProxies = spiritProxies;
        }

        public bool Available { get; }

        public int ProjectionRoots { get; }

        public int SpiritRoots { get; }

        public int TurnAnchors { get; }

        public int ProjectionProxies { get; }

        public int SpiritProxies { get; }

        public int Total => ProjectionRoots
                            + SpiritRoots
                            + TurnAnchors
                            + ProjectionProxies
                            + SpiritProxies;
    }
}
