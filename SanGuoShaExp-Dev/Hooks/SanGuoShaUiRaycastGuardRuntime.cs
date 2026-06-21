using System;
using System.Collections.Generic;
using SanGuoShaExp.Dll.Infrastructure;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI;

namespace SanGuoShaExp.Dll.Hooks;

public static class SanGuoShaUiRaycastGuardRuntime
{
    private const int TransitionGuardFrames = 8;
    private const int RegistryScrubFrames = 36;
    private const int MaxRaycasterDetailsPerPass = 12;
    private const string RunnerName = "SanGuoShaExp.UiTransitionGuard";

    private static bool initialized;
    private static int guardUntilFrame = -1;
    private static int lastEventSystemScrubFrame = -1;
    private static int lastGlobalRaycastScrubFrame = -1;
    private static int lastSuspendFrame = -1;
    private static int traceBudget;
    private static string guardSource = "";
    private static TransitionGuardRunner? runner;

    public static bool IsTransitionGuardActive => IsGuardActive();

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        RegisterBefore(modConfig, "GraphicRaycaster.Raycast", BeforeGraphicRaycasterRaycast);
        RegisterBefore(modConfig, "EventSystem.Update", BeforeEventSystemUpdate);
        RegisterBefore(modConfig, "UIManager.CloseUI", BeforeUiManagerCloseUi);
        RegisterAfter(modConfig, "UIManager.CloseUI", AfterUiLifecycle);
        RegisterBefore(modConfig, "UIBase.Close", BeforeUiBaseClose);
        RegisterAfter(modConfig, "UIBase.Close", AfterUiLifecycle);
        RegisterBefore(modConfig, "UIBase.OnDestroy", BeforeUiLifecycle);
        RegisterAfter(modConfig, "UIBase.OnDestroy", AfterUiLifecycle);
        RegisterBefore(modConfig, "DialogueManager.ShowDialogue", BeforeDialogueTransition);
        RegisterAfter(modConfig, "DialogueManager.ShowDialogue", AfterUiLifecycle);
        RegisterBefore(modConfig, "DialogueManager.InternalShowDialogue", BeforeDialogueTransition);
        RegisterAfter(modConfig, "DialogueManager.InternalShowDialogue", AfterUiLifecycle);
        RegisterBefore(modConfig, "DialogueUI.ShowDialogue", BeforeDialogueTransition);
        RegisterAfter(modConfig, "DialogueUI.ShowDialogue", AfterUiLifecycle);
        RegisterBefore(modConfig, "UpperCanvasController.RefreshRaycasterState", BeforeUiLifecycle);
        RegisterAfter(modConfig, "UpperCanvasController.RefreshRaycasterState", AfterUpperCanvasRaycasterState);
        RegisterBefore(modConfig, "UpperCanvasController.ChangeRaycaster", BeforeUiLifecycle);
        RegisterAfter(modConfig, "UpperCanvasController.ChangeRaycaster", AfterUpperCanvasRaycasterState);
        RegisterBefore(modConfig, "UpperCanvasController.OnTransformChildrenChanged", BeforeUiLifecycle);
        RegisterAfter(modConfig, "UpperCanvasController.OnTransformChildrenChanged", AfterUpperCanvasRaycasterState);
    }

    public static void BeginTransitionGuard(string source)
    {
        guardSource = source;
        guardUntilFrame = Math.Max(guardUntilFrame, Time.frameCount + TransitionGuardFrames);
        lastEventSystemScrubFrame = -1;
        lastGlobalRaycastScrubFrame = -1;
        lastSuspendFrame = -1;
        traceBudget = 24;
        SanGuoShaExpLog.Info(
            "UI raycast transition guard armed. source=" + source
            + ", frame=" + Time.frameCount
            + ", untilFrame=" + guardUntilFrame);
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(source + ":guard-arm", SanGuoShaExpLog.Info);
        EnsureRunner()?.EnsureActive();
        SuspendRaycasters(source + ":guard-arm");
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(
            RegistryScrubFrames,
            source + ":guard-registry",
            SanGuoShaExpLog.Debug);
    }

    public static void RunAfterGuard(string source, Action action, int extraFrames = 4)
    {
        var owner = EnsureRunner();
        if (owner == null)
        {
            SafeInvoke(source, action);
            return;
        }

        var dueFrame = Math.Max(Time.frameCount, guardUntilFrame) + Math.Max(1, extraFrames);
        owner.EnqueueDeferredAction(source, dueFrame, action);
    }

    private static void BeforeEventSystemUpdate(ModHookContext context)
    {
        if (!IsGuardActive())
        {
            return;
        }

        if (lastEventSystemScrubFrame == Time.frameCount)
        {
            return;
        }

        lastEventSystemScrubFrame = Time.frameCount;
        SuspendRaycasters(guardSource + ":before-event-system-update");
        var removed = UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(
            guardSource + ":before-event-system-update:frame" + Time.frameCount,
            SanGuoShaExpLog.Info);
        Trace(
            "UI raycast guard EventSystem.Update pre-scrub. frame=" + Time.frameCount
            + ", removed=" + removed
            + ", target=" + TargetName(context));
    }

    private static void BeforeGraphicRaycasterRaycast(ModHookContext context)
    {
        if (!IsGuardActive())
        {
            return;
        }

        if (context.Target is GraphicRaycaster raycaster)
        {
            var removed = UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForRaycaster(
                raycaster,
                guardSource + ":before-graphic-raycaster:frame" + Time.frameCount,
                SanGuoShaExpLog.Info);
            Trace(
                "UI raycast guard GraphicRaycaster.Raycast pre-scrub. frame=" + Time.frameCount
                + ", removed=" + removed
                + ", raycaster=" + RaycasterName(raycaster));
            return;
        }

        var canvas = context.Arguments != null && context.Arguments.Length > 0
            ? context.Arguments[0] as Canvas
            : null;
        if (canvas != null)
        {
            var removed = UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForCanvas(
                canvas,
                guardSource + ":before-static-graphic-raycaster:frame" + Time.frameCount,
                SanGuoShaExpLog.Info);
            Trace(
                "UI raycast guard static GraphicRaycaster.Raycast pre-scrub. frame=" + Time.frameCount
                + ", removed=" + removed
                + ", canvas=" + CanvasName(canvas));
            return;
        }

        if (lastGlobalRaycastScrubFrame == Time.frameCount)
        {
            return;
        }

        lastGlobalRaycastScrubFrame = Time.frameCount;
        var globalRemoved = UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(
            guardSource + ":before-unknown-raycaster:frame" + Time.frameCount,
            SanGuoShaExpLog.Info);
        Trace(
            "UI raycast guard unknown raycaster pre-scrub. frame=" + Time.frameCount
            + ", removed=" + globalRemoved
            + ", target=" + TargetName(context));
    }

    private static void BeforeUiManagerCloseUi(ModHookContext context)
    {
        var uiName = ArgumentString(context, 0);
        if (!IsWatchedUiName(uiName) && !IsGuardActive())
        {
            return;
        }

        BeginTransitionGuard("UIManager.CloseUI.before:" + EmptyAsUnknown(uiName));
        var root = FindUiRoot(uiName);
        if (root != null)
        {
            UiRaycastSafeDestroyRuntime.DisableRaycasts(
                root,
                "UIManager.CloseUI.before:" + EmptyAsUnknown(uiName),
                SanGuoShaExpLog.Info);
        }
    }

    private static void BeforeUiBaseClose(ModHookContext context)
    {
        var root = TargetGameObject(context);
        if (root == null)
        {
            return;
        }

        var watched = IsWatchedUiName(root.name) || IsWatchedTransform(root.transform);
        if (!watched && !IsGuardActive())
        {
            return;
        }

        BeginTransitionGuard("UIBase.Close.before:" + SafeObjectName(root));
        UiRaycastSafeDestroyRuntime.DisableRaycasts(
            root,
            "UIBase.Close.before:" + SafeObjectName(root),
            SanGuoShaExpLog.Info);
    }

    private static void BeforeDialogueTransition(ModHookContext context)
    {
        BeginTransitionGuard(TargetName(context) + ".before-dialogue");
    }

    private static void BeforeUiLifecycle(ModHookContext context)
    {
        if (!IsGuardActive())
        {
            return;
        }

        SuspendRaycasters(TargetName(context) + ":before-ui-lifecycle");
        UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(
            TargetName(context) + ":before-ui-lifecycle",
            SanGuoShaExpLog.Debug);
    }

    private static void AfterUiLifecycle(ModHookContext context)
    {
        if (!IsGuardActive())
        {
            return;
        }

        SuspendRaycasters(TargetName(context) + ":after-ui-lifecycle");
        var removed = UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(
            TargetName(context) + ":after-ui-lifecycle",
            SanGuoShaExpLog.Debug);
        Trace(
            "UI transition lifecycle scrub. target=" + TargetName(context)
            + ", removed=" + removed
            + ", frame=" + Time.frameCount);
    }

    private static void AfterUpperCanvasRaycasterState(ModHookContext context)
    {
        if (!IsGuardActive())
        {
            return;
        }

        SuspendRaycasters(TargetName(context) + ":after-upper-canvas");
        var removed = UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(
            TargetName(context) + ":after-upper-canvas",
            SanGuoShaExpLog.Debug);
        Trace(
            "Upper canvas raycaster state scrubbed. target=" + TargetName(context)
            + ", removed=" + removed
            + ", frame=" + Time.frameCount);
    }

    private static void MaintainTransitionGuard()
    {
        if (IsGuardActive())
        {
            SuspendRaycasters(guardSource + ":runner-update");
            if (lastEventSystemScrubFrame != Time.frameCount)
            {
                lastEventSystemScrubFrame = Time.frameCount;
                UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(
                    guardSource + ":runner-update:frame" + Time.frameCount,
                    SanGuoShaExpLog.Debug);
            }

            return;
        }

        RestoreSuspendedRaycasters("guard-expired");
    }

    private static void SuspendRaycasters(string source)
    {
        if (lastSuspendFrame == Time.frameCount && source.EndsWith(":runner-update", StringComparison.Ordinal))
        {
            return;
        }

        lastSuspendFrame = Time.frameCount;
        EnsureRunner()?.SuspendRaycasters(source);
    }

    private static void RestoreSuspendedRaycasters(string source)
    {
        EnsureRunner()?.RestoreRaycasters(source);
    }

    private static bool IsGuardActive()
    {
        return guardUntilFrame >= 0 && Time.frameCount <= guardUntilFrame;
    }

    private static void Trace(string message)
    {
        if (traceBudget <= 0)
        {
            return;
        }

        traceBudget--;
        SanGuoShaExpLog.Info(message + ", guardUntilFrame=" + guardUntilFrame);
    }

    private static string TargetName(ModHookContext context)
    {
        try
        {
            var target = context.Target;
            return target == null ? "<null>" : target.GetType().FullName ?? target.GetType().Name;
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static string ArgumentString(ModHookContext context, int index)
    {
        try
        {
            if (context.Arguments == null || context.Arguments.Length <= index)
            {
                return "";
            }

            return context.Arguments[index]?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static GameObject? TargetGameObject(ModHookContext context)
    {
        try
        {
            return context.Target is UnityEngine.Component component ? component.gameObject : null;
        }
        catch
        {
            return null;
        }
    }

    private static GameObject? FindUiRoot(string uiName)
    {
        if (string.IsNullOrWhiteSpace(uiName))
        {
            return null;
        }

        try
        {
            var manager = UIManager.Instance;
            if (manager == null)
            {
                return null;
            }

            return FindChild(manager.canvasTf, uiName)
                   ?? FindChild(manager.upperCanvasTf, uiName);
        }
        catch
        {
            return null;
        }
    }

    private static GameObject? FindChild(Transform? root, string uiName)
    {
        if (root == null)
        {
            return null;
        }

        var child = root.Find(uiName) ?? root.Find(uiName + "(Clone)");
        return child == null ? null : child.gameObject;
    }

    private static bool IsWatchedTransform(Transform transform)
    {
        try
        {
            var current = transform;
            while (current != null)
            {
                if (IsWatchedUiName(current.name))
                {
                    return true;
                }

                current = current.parent;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsWatchedUiName(string? uiName)
    {
        if (string.IsNullOrWhiteSpace(uiName))
        {
            return false;
        }

        var name = uiName!.Replace("(Clone)", "").Trim();
        return string.Equals(name, "FightUI", StringComparison.Ordinal)
               || string.Equals(name, "PopUpTextUI", StringComparison.Ordinal)
               || string.Equals(name, "BattleRewardsUI", StringComparison.Ordinal)
               || string.Equals(name, "DialogueUI", StringComparison.Ordinal)
               || string.Equals(name, "InkTurnUI", StringComparison.Ordinal)
               || string.Equals(name, "SceneTurnUI", StringComparison.Ordinal)
               || string.Equals(name, "CurtainTurnUI", StringComparison.Ordinal)
               || string.Equals(name, "HouseUI", StringComparison.Ordinal)
               || string.Equals(name, "StatusBarUI", StringComparison.Ordinal)
               || string.Equals(name, "BuffBarUI", StringComparison.Ordinal);
    }

    private static string RaycasterName(GraphicRaycaster raycaster)
    {
        try
        {
            var canvas = raycaster.GetComponent<Canvas>() ?? raycaster.GetComponentInParent<Canvas>();
            return raycaster.name + ", canvas=" + CanvasName(canvas);
        }
        catch
        {
            return raycaster.name;
        }
    }

    private static string CanvasName(Canvas? canvas)
    {
        if (canvas == null)
        {
            return "<null>";
        }

        try
        {
            return canvas.name
                   + ", active=" + canvas.isActiveAndEnabled
                   + ", renderMode=" + canvas.renderMode
                   + ", order=" + canvas.sortingOrder;
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static string TransformPath(Transform? transform)
    {
        if (transform == null)
        {
            return "<null>";
        }

        try
        {
            var parts = new List<string>();
            var current = transform;
            while (current != null && parts.Count < 32)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static string SafeObjectName(UnityEngine.Object? target)
    {
        try
        {
            return target == null ? "<null>" : target.name;
        }
        catch
        {
            return "<unreadable>";
        }
    }

    private static string EmptyAsUnknown(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<unknown>" : value;
    }

    private static bool IsRuntimeSceneObject(UnityEngine.Component component)
    {
        try
        {
            return component.gameObject.scene.IsValid();
        }
        catch
        {
            return false;
        }
    }

    private static TransitionGuardRunner? EnsureRunner()
    {
        if (runner != null)
        {
            return runner;
        }

        try
        {
            var existing = GameObject.Find(RunnerName);
            var gameObject = existing != null ? existing : new GameObject(RunnerName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.GetComponent<TransitionGuardRunner>() ?? gameObject.AddComponent<TransitionGuardRunner>();
            return runner;
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("UI transition guard runner unavailable: " + ex.Message);
            return null;
        }
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookBefore(target, context => SafeInvoke(target, () => action(context)));
            SanGuoShaExpLog.Info("UI raycast guard hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("UI raycast guard hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, context => SafeInvoke(target, () => action(context)));
            SanGuoShaExpLog.Info("UI raycast guard hook after registered: " + target);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("UI raycast guard hook after failed: " + target + " -> " + ex.Message);
        }
    }

    private static void SafeInvoke(string target, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("UI raycast guard hook failed: " + target + " -> " + ex.Message);
        }
    }

    [DefaultExecutionOrder(-32000)]
    private sealed class TransitionGuardRunner : MonoBehaviour
    {
        private readonly Dictionary<int, SuspendedRaycaster> suspendedRaycasters = new();
        private readonly List<DeferredAction> deferredActions = new();

        public void EnsureActive()
        {
            enabled = true;
        }

        public void SuspendRaycasters(string source)
        {
            GraphicRaycaster[] raycasters;
            try
            {
                raycasters = Resources.FindObjectsOfTypeAll<GraphicRaycaster>();
            }
            catch (Exception ex)
            {
                SanGuoShaExpLog.Warn("UI transition guard failed to enumerate raycasters: " + ex.Message);
                return;
            }

            var suspended = 0;
            var details = 0;
            foreach (var raycaster in raycasters)
            {
                if (raycaster == null || !IsRuntimeSceneObject(raycaster))
                {
                    continue;
                }

                int id;
                try
                {
                    id = raycaster.GetInstanceID();
                }
                catch
                {
                    continue;
                }

                if (!suspendedRaycasters.ContainsKey(id))
                {
                    suspendedRaycasters[id] = new SuspendedRaycaster(
                        raycaster,
                        raycaster.enabled,
                        RaycasterName(raycaster),
                        TransformPath(raycaster.transform));
                }

                if (!raycaster.enabled)
                {
                    continue;
                }

                try
                {
                    raycaster.enabled = false;
                    suspended++;
                    if (details < MaxRaycasterDetailsPerPass)
                    {
                        details++;
                        SanGuoShaExpLog.Info(
                            "UI transition guard suspended raycaster. source=" + source
                            + ", frame=" + Time.frameCount
                            + ", raycaster=" + RaycasterName(raycaster)
                            + ", path=" + TransformPath(raycaster.transform));
                    }
                }
                catch (Exception ex)
                {
                    SanGuoShaExpLog.Warn(
                        "UI transition guard failed to suspend raycaster. source=" + source
                        + ", raycaster=" + RaycasterName(raycaster)
                        + " -> " + ex.Message);
                }
            }

            if (suspended > 0)
            {
                SanGuoShaExpLog.Info(
                    "UI transition guard suspended raycasters. source=" + source
                    + ", frame=" + Time.frameCount
                    + ", count=" + suspended
                    + ", tracked=" + suspendedRaycasters.Count
                    + ", untilFrame=" + guardUntilFrame);
            }
        }

        public void RestoreRaycasters(string source)
        {
            if (suspendedRaycasters.Count == 0)
            {
                return;
            }

            var restored = 0;
            var skipped = 0;
            foreach (var item in suspendedRaycasters.Values)
            {
                var raycaster = item.Raycaster;
                if (raycaster == null)
                {
                    skipped++;
                    continue;
                }

                if (!item.OriginalEnabled)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    raycaster.enabled = true;
                    restored++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    SanGuoShaExpLog.Warn(
                        "UI transition guard failed to restore raycaster. source=" + source
                        + ", raycaster=" + item.Name
                        + " -> " + ex.Message);
                }
            }

            SanGuoShaExpLog.Info(
                "UI transition guard restored raycasters. source=" + source
                + ", frame=" + Time.frameCount
                + ", restored=" + restored
                + ", skipped=" + skipped
                + ", tracked=" + suspendedRaycasters.Count);
            suspendedRaycasters.Clear();
            UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(source + ":after-restore", SanGuoShaExpLog.Debug);
        }

        public void EnqueueDeferredAction(string source, int dueFrame, Action action)
        {
            deferredActions.Add(new DeferredAction(source, dueFrame, action));
            SanGuoShaExpLog.Debug(
                "UI transition guard deferred action queued. source=" + source
                + ", frame=" + Time.frameCount
                + ", dueFrame=" + dueFrame);
        }

        private void Update()
        {
            MaintainTransitionGuard();
            RunDueDeferredActions();
        }

        private void LateUpdate()
        {
            if (IsGuardActive())
            {
                SuspendRaycasters(guardSource + ":runner-late-update");
            }
        }

        private void RunDueDeferredActions()
        {
            if (deferredActions.Count == 0)
            {
                return;
            }

            for (var i = deferredActions.Count - 1; i >= 0; i--)
            {
                var item = deferredActions[i];
                if (Time.frameCount < item.DueFrame)
                {
                    continue;
                }

                deferredActions.RemoveAt(i);
                SafeInvoke(item.Source, item.Action);
            }
        }
    }

    private sealed class SuspendedRaycaster
    {
        public SuspendedRaycaster(GraphicRaycaster raycaster, bool originalEnabled, string name, string path)
        {
            Raycaster = raycaster;
            OriginalEnabled = originalEnabled;
            Name = name;
            Path = path;
        }

        public GraphicRaycaster Raycaster { get; }

        public bool OriginalEnabled { get; }

        public string Name { get; }

        public string Path { get; }
    }

    private sealed class DeferredAction
    {
        public DeferredAction(string source, int dueFrame, Action action)
        {
            Source = source;
            DueFrame = dueFrame;
            Action = action;
        }

        public string Source { get; }

        public int DueFrame { get; }

        public Action Action { get; }
    }
}
