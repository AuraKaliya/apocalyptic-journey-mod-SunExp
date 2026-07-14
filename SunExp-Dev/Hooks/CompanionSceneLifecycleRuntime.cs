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
        RegisterMenuExitBoundary(modConfig, "TopBarUI.ReturnToMenu");
        RegisterMenuExitBoundary(modConfig, "GameApp.ReturnToMenu");
        RegisterMenuExitBoundary(modConfig, "GameEntryUI.Init");
        SunExpBattleLifecycleRouter.Register("CompanionScene", new SunExpBattleLifecycleSubscription
        {
            FightInitializing = _ => CleanupAfterSceneBoundary("FightInitializing"),
            FightStarted = _ => CompanionSceneApi.TrackBattleScene(
                SceneManager.GetActiveScene(),
                "FightStarted"),
            FightEnded = _ => CleanupAfterSceneBoundary("FightEnded")
        });
        SunExpLog.Info("Companion scene lifecycle runtime initialized");
    }

    private static void RegisterMenuExitBoundary(ModConfig modConfig, string target)
    {
        SunExpHookRegistry.Before(
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
            var artifacts = CaptureArtifactSnapshot(source);
            var suppression = default(CompanionPresentationSuppression);
            RunCleanupStep(
                "SuppressPresentation",
                source,
                () => suppression = CompanionPresentationCleanup.SuppressAll(source));
            RunCleanupStep("InvalidateBattleEpoch", source, CompanionAuthorityService.InvalidateBattleEpoch);
            RunCleanupStep("Projection", source, () => ProjectionRuntime.ClearBattle(source));
            RunCleanupStep("Spirit", source, () => SpiritRuntime.ClearBattle(source));
            RunCleanupStep("CompanionState", source, CompanionBattleStateStore.Clear);
            RunCleanupStep("OrphanedObjects", source, DestroyOrphanedCompanionObjects);
            RunCleanupStep("ProjectionProxies", source, () => ProjectionAttachmentPresenter.ClearAll(source));
            RunCleanupStep("SpiritProxies", source, () => SpiritAttachmentPresenter.ClearAll(source));
            SunExpPerformanceCounters.Record("CompanionScene.Cleared");
            LogCleanupSummary(source, artifacts, suppression);
            SchedulePostCleanupAudit(source);
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
                        "SunExp_ProjectionVisualProxy:",
                        StringComparison.Ordinal)),
                CountSceneObjects<ProjectionVisualProxy>(component =>
                    component.gameObject.name.StartsWith(
                        "SunExp_SpiritVisualProxy:",
                        StringComparison.Ordinal)));
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[CompanionScene] artifact snapshot failed @ " + source + ": " + ex.Message);
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
        CompanionArtifactSnapshot artifacts,
        CompanionPresentationSuppression suppression)
    {
        var message = artifacts.Available
            ? "[CompanionScene] cleanup boundary processed: source=" + source
              + ", projectionRoots=" + artifacts.ProjectionRoots
              + ", spiritRoots=" + artifacts.SpiritRoots
              + ", turnAnchors=" + artifacts.TurnAnchors
              + ", projectionProxies=" + artifacts.ProjectionProxies
              + ", spiritProxies=" + artifacts.SpiritProxies
              + ", suppressedActorRoots=" + suppression.ActorRoots
              + ", suppressedProxyRoots=" + suppression.ProxyRoots
              + ", suppressedRenderers=" + suppression.Renderers
              + ", suppressedUi=" + suppression.UiObjects
            : "[CompanionScene] cleanup boundary processed: source=" + source
              + ", artifactCounts=unavailable";

        if (!artifacts.Available
            || artifacts.Total > 0
            || source.StartsWith("GameEntryUI.", StringComparison.Ordinal))
        {
            SunExpLog.InfoAlways(message);
            return;
        }

        SunExpLog.Info(message);
    }

    private static void SchedulePostCleanupAudit(string source)
    {
        if (!IsMenuExitBoundary(source))
        {
            return;
        }

        var frame = Time.frameCount;
        SunExpFrameDispatcher.RunOnceNextFrame(
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
                    SunExpLog.Warn(message);
                    return;
                }

                SunExpLog.InfoAlways(message);
            });
    }

    private static bool IsMenuExitBoundary(string source)
    {
        return string.Equals(source, "TopBarUI.ReturnToMenu", StringComparison.Ordinal)
               || string.Equals(source, "GameApp.ReturnToMenu", StringComparison.Ordinal)
               || string.Equals(source, "GameEntryUI.Init", StringComparison.Ordinal);
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
