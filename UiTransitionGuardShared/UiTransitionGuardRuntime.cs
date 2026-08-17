using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using UiRaycastSafetyShared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI;

namespace UiTransitionGuardShared;

public enum UiTransitionGuardLogLevel
{
    Normal = 0,
    Verbose = 1,
    Trace = 2
}

public sealed class UiTransitionGuardOptions
{
    public UiTransitionGuardLogLevel LogLevel { get; set; } = UiTransitionGuardLogLevel.Normal;

    public int MaxGuardFrames { get; set; } = 24;

    public int RegistryScrubFrames { get; set; } = 8;

    public int ScrubEveryFrames { get; set; } = 2;
}

public static class UiTransitionGuardRuntime
{
    private const string GlobalObjectName = "UiTransitionGuard.Global";
    private const string ComponentFullName = "UiTransitionGuardShared.UiTransitionGuardRuntime+UiTransitionGuardComponent";
    public const string CurrentBuildId = "ui-transition-guard-2026-08-16-v6";
    public const int CurrentProtocolVersion = 2;
    public const int MinimumSupportedProtocolVersion = 1;

    private static readonly HashSet<string> ReuseLogOwners = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> CompatibilityErrorsShown = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(ModConfig modConfig, string ownerModId, UiTransitionGuardOptions? options = null)
    {
        EnsureGuard(modConfig, ownerModId, options);
    }

    public static void BeginTransition(ModConfig? modConfig, string ownerModId, string source, int frames = 8)
    {
        var guard = EnsureGuard(modConfig, ownerModId, null);
        Invoke(guard, "BeginTransition", ownerModId, source, frames);
    }

    public static int DisableRaycasts(ModConfig? modConfig, string ownerModId, GameObject? root, string source)
    {
        if (root == null)
        {
            return 0;
        }

        var guard = EnsureGuard(modConfig, ownerModId, null);
        var result = Invoke(guard, "DisableRaycasts", ownerModId, root, source);
        return result is int count ? count : UiRaycastSafeDestroyRuntime.DisableRaycasts(root, ownerModId + ":" + source, Log);
    }

    public static void RunAfterGuard(ModConfig? modConfig, string ownerModId, string source, Action action, int extraFrames = 4)
    {
        var guard = EnsureGuard(modConfig, ownerModId, null);
        if (guard == null)
        {
            SafeInvoke(ownerModId + ":" + source, action);
            return;
        }

        Invoke(guard, "RunAfterGuard", ownerModId, source, action, extraFrames);
    }

    public static int ScrubNow(ModConfig? modConfig, string ownerModId, string source)
    {
        var guard = EnsureGuard(modConfig, ownerModId, null);
        var result = Invoke(guard, "ScrubNow", ownerModId, source);
        return result is int count ? count : UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(ownerModId + ":" + source, Log);
    }

    public static void RecoverNativeInput(
        ModConfig? modConfig,
        string ownerModId,
        string source,
        int frames = 12)
    {
        var guard = EnsureGuard(modConfig, ownerModId, null);
        if (guard == null)
        {
            RecoverNativeInputPass(ownerModId, source + ":fallback", terminal: true, Log);
            return;
        }

        Invoke(guard, "RecoverNativeInput", ownerModId, source, frames);
    }

    public static bool IsGuardActive(ModConfig? modConfig, string ownerModId)
    {
        var guard = EnsureGuard(modConfig, ownerModId, null);
        if (guard == null)
        {
            return false;
        }

        try
        {
            return guard.GetType()
                .GetProperty("IsGuardActive", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(guard) is true;
        }
        catch
        {
            return false;
        }
    }

    private static object? EnsureGuard(ModConfig? modConfig, string ownerModId, UiTransitionGuardOptions? options)
    {
        var gameObject = GameObject.Find(GlobalObjectName);
        if (gameObject != null)
        {
            var existing = FindGuardComponent(gameObject);
            if (existing != null)
            {
                if (!ValidateExistingGuard(existing, ownerModId))
                {
                    return null;
                }

                if (ReuseLogOwners.Add(ownerModId))
                {
                    Log("Reusing global guard for " + ownerModId
                        + ", ownerType=" + existing.GetType().Assembly.GetName().Name
                        + ", protocol=" + ReadIntProperty(existing, "ProtocolVersion", 0)
                        + ", buildId=" + ReadStringProperty(existing, "BuildId"));
                }

                TryInitializeExisting(existing, modConfig, ownerModId, options);
                return existing;
            }
        }

        if (modConfig == null)
        {
            return null;
        }

        if (gameObject == null)
        {
            gameObject = new GameObject(GlobalObjectName);
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        var component = gameObject.AddComponent<UiTransitionGuardComponent>();
        component.InitializeOwner(modConfig, ownerModId, options);
        Log("Created global guard, owner=" + ownerModId);
        return component;
    }

    private static bool ValidateExistingGuard(object existing, string ownerModId)
    {
        var type = existing.GetType();
        var protocolVersion = ReadIntProperty(existing, "ProtocolVersion", 0);
        var minimumSupported = ReadIntProperty(existing, "MinimumSupportedProtocolVersion", int.MaxValue);
        var buildId = ReadStringProperty(existing, "BuildId");
        var methodsPresent = new[]
            {
                "InitializeOwner", "BeginTransition", "DisableRaycasts", "RunAfterGuard", "ScrubNow",
                "RecoverNativeInput"
            }
            .All(name => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public) != null);
        var compatible = protocolVersion >= MinimumSupportedProtocolVersion
            && minimumSupported <= CurrentProtocolVersion
            && string.Equals(buildId, CurrentBuildId, StringComparison.Ordinal)
            && methodsPresent;

        if (!compatible && CompatibilityErrorsShown.Add(ownerModId + ":" + type.AssemblyQualifiedName))
        {
            Debug.LogError("[UiTransitionGuard] Incompatible global guard; UI transition guard disabled for "
                           + ownerModId
                           + ". existingAssembly=" + type.Assembly.GetName().Name
                           + ", protocol=" + protocolVersion
                           + ", minSupported=" + minimumSupported
                           + ", buildId=" + (string.IsNullOrWhiteSpace(buildId) ? "<missing>" : buildId)
                           + ", requiredBuildId=" + CurrentBuildId
                           + ", methodsPresent=" + methodsPresent);
        }

        return compatible;
    }

    private static object? FindGuardComponent(GameObject gameObject)
    {
        foreach (var component in gameObject.GetComponents<MonoBehaviour>())
        {
            if (component != null && component.GetType().FullName == ComponentFullName)
            {
                return component;
            }
        }

        return null;
    }

    private static void TryInitializeExisting(object existing, ModConfig? modConfig, string ownerModId, UiTransitionGuardOptions? options)
    {
        if (modConfig == null)
        {
            return;
        }

        try
        {
            existing.GetType()
                .GetMethod("InitializeOwner", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(existing, new object?[] { modConfig, ownerModId, options });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UiTransitionGuard] Existing guard initialize failed for " + ownerModId + ": " + ex.Message);
        }
    }

    private static object? Invoke(object? target, string methodName, params object?[] args)
    {
        if (target == null)
        {
            return null;
        }

        try
        {
            return target.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(target, args);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UiTransitionGuard] Call failed: " + methodName + " -> " + UnwrapMessage(ex));
            return null;
        }
    }

    private static int ReadIntProperty(object source, string propertyName, int fallback)
    {
        try
        {
            return source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) is int value
                ? value
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static string ReadStringProperty(object source, string propertyName)
    {
        try
        {
            return source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void SafeInvoke(string source, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UiTransitionGuard] Deferred action failed. source=" + source + " -> " + ex.Message);
        }
    }

    private static string UnwrapMessage(Exception ex)
    {
        return ex is TargetInvocationException { InnerException: not null }
            ? ex.InnerException.Message
            : ex.Message;
    }

    private static void Log(string message)
    {
        Debug.Log("[UiTransitionGuard] " + message);
    }

    private static void RecoverNativeInputPass(
        string ownerModId,
        string source,
        bool terminal,
        Action<string> log)
    {
        try
        {
            var manager = UIManager.Instance;
            var upper = manager?.upperCanvasTf;
            var activeUpperChildren = ActiveChildNames(upper);
            var controller = upper?.GetComponent<UpperCanvasController>();
            controller?.RefreshRaycasterState();

            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.enabled = true;
                if (eventSystem.currentInputModule != null)
                {
                    eventSystem.currentInputModule.enabled = true;
                }
            }

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            var mainRaycaster = manager?.canvasTf?.GetComponent<GraphicRaycaster>();
            if (controller == null && mainRaycaster != null)
            {
                mainRaycaster.enabled = activeUpperChildren.Count == 0;
            }

            // UIBase.UpperBlock disables CanvasGroup.blocksRaycasts on every active-scene
            // root except Upper Canvas. The native inverse lives in UIBase.OnDisable, so a
            // force-destroyed or already-disabled upper UI can skip it. RefreshRaycasterState
            // only restores Graphic/Physics raycasters and cannot repair this parent blocker.
            // Match the native CancelUpperBlock contract once no upper modal remains.
            var rootCanvasGroups = RecoverRootCanvasGroups(activeUpperChildren.Count == 0);

            var upperRaycasters = upper == null
                ? Array.Empty<GraphicRaycaster>()
                : upper.GetComponents<GraphicRaycaster>();
            UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(
                ownerModId + ":" + source,
                terminal ? log : null);
            if (!terminal)
            {
                return;
            }

            var expectedMainEnabled = activeUpperChildren.Count == 0;
            var mainEnabled = mainRaycaster != null && mainRaycaster.enabled;
            log("Native input recovery terminal state. owner=" + ownerModId
                + ", source=" + source
                + ", eventSystem=" + (eventSystem != null)
                + ", eventSystemEnabled=" + (eventSystem != null && eventSystem.enabled)
                + ", inputModule=" + (eventSystem?.currentInputModule != null)
                + ", inputModuleEnabled=" + (eventSystem?.currentInputModule != null
                                               && eventSystem.currentInputModule.enabled)
                + ", activeUpperChildren=" + activeUpperChildren.Count
                + ", upperChildren=" + (activeUpperChildren.Count == 0
                    ? "none"
                    : string.Join("|", activeUpperChildren))
                + ", upperRaycasters=" + upperRaycasters.Length
                + ", upperRaycastersEnabled=" + upperRaycasters.Count(item => item != null && item.enabled)
                + ", mainRaycaster=" + (mainRaycaster != null)
                + ", mainRaycasterEnabled=" + mainEnabled
                + ", expectedMainRaycasterEnabled=" + expectedMainEnabled
                + ", rootCanvasGroups=" + rootCanvasGroups.Total
                + ", rootCanvasGroupsRecovered=" + rootCanvasGroups.Recovered
                + ", rootCanvasGroupsBlocked=" + rootCanvasGroups.Blocked
                + ", blockedRootCanvasGroupNames=" + (rootCanvasGroups.BlockedNames.Count == 0
                    ? "none"
                    : string.Join("|", rootCanvasGroups.BlockedNames))
                + ", ownershipConsistent=" + (mainRaycaster == null || mainEnabled == expectedMainEnabled)
                + ", nativeInputReady=" + (activeUpperChildren.Count != 0
                    || ((mainRaycaster == null || mainEnabled) && rootCanvasGroups.Blocked == 0)));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UiTransitionGuard] Native input recovery failed. owner="
                             + ownerModId + ", source=" + source + " -> " + ex.Message);
        }
    }

    private static RootCanvasGroupRecovery RecoverRootCanvasGroups(bool shouldRecover)
    {
        var result = new RootCanvasGroupRecovery();
        try
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null
                    || string.Equals(root.name, "Upper Canvas", StringComparison.Ordinal)
                    || !root.TryGetComponent<CanvasGroup>(out var canvasGroup)
                    || canvasGroup == null)
                {
                    continue;
                }

                result.Total++;
                if (!canvasGroup.blocksRaycasts && shouldRecover)
                {
                    canvasGroup.blocksRaycasts = true;
                    result.Recovered++;
                }

                if (!canvasGroup.blocksRaycasts)
                {
                    result.Blocked++;
                    if (result.BlockedNames.Count < 12)
                    {
                        result.BlockedNames.Add(string.IsNullOrWhiteSpace(root.name)
                            ? "<unnamed>"
                            : root.name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.BlockedNames.Add("diagnostic-failed:" + ex.Message);
        }

        return result;
    }

    private static List<string> ActiveChildNames(Transform? root)
    {
        var result = new List<string>();
        if (root == null)
        {
            return result;
        }

        try
        {
            foreach (Transform child in root)
            {
                if (child != null && child.gameObject.activeInHierarchy)
                {
                    result.Add(child.name ?? "<unnamed>");
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private sealed class RootCanvasGroupRecovery
    {
        public int Total { get; set; }

        public int Recovered { get; set; }

        public int Blocked { get; set; }

        public List<string> BlockedNames { get; } = new();
    }

    [DefaultExecutionOrder(-32000)]
    public sealed class UiTransitionGuardComponent : MonoBehaviour
    {
        private const int DefaultGuardFrames = 8;
        private const int DefaultMaxGuardFrames = 24;
        private const int DefaultRegistryScrubFrames = 8;
        private const int DefaultScrubEveryFrames = 2;
        private const int MaxAllowedGuardFrames = 120;
        private const int MaxRaycasterDetailsPerPass = 12;

        private readonly Dictionary<int, SuspendedRaycaster> suspendedRaycasters = new();
        private readonly Dictionary<int, int> lastRaycastDisableFrameByRoot = new();
        private readonly Dictionary<int, int> lastRaycasterLeaseFrameByRoot = new();
        private readonly Dictionary<int, int> lastRootScrubFrameByRoot = new();
        private readonly Dictionary<string, int> lastGuardFrameBySource = new(StringComparer.Ordinal);
        private readonly List<DeferredAction> deferredActions = new();
        private readonly HashSet<string> owners = new(StringComparer.OrdinalIgnoreCase);
        private int guardUntilFrame = -1;
        private int lastScrubFrame = -1;
        private int lastGlobalScrubFrame = -1;
        private int nativeInputRecoveryTicket;
        private string guardSource = "";
        private string primaryOwner = "";
        private bool hooksRegistered;
        private UiTransitionGuardLogLevel logLevel = UiTransitionGuardLogLevel.Normal;
        private int maxGuardFrames = DefaultMaxGuardFrames;
        private int registryScrubFrames = DefaultRegistryScrubFrames;
        private int scrubEveryFrames = DefaultScrubEveryFrames;

        public int ProtocolVersion => CurrentProtocolVersion;

        public int MinimumSupportedProtocolVersion => UiTransitionGuardRuntime.MinimumSupportedProtocolVersion;

        public string BuildId => CurrentBuildId;

        public bool IsGuardActive => IsActive();

        public void InitializeOwner(ModConfig modConfig, string ownerModId, object? options = null)
        {
            if (string.IsNullOrWhiteSpace(primaryOwner))
            {
                primaryOwner = ownerModId;
            }

            owners.Add(ownerModId);
            var requestedLogLevel = ReadLogLevel(options);
            if (requestedLogLevel > logLevel)
            {
                logLevel = requestedLogLevel;
            }

            ApplyOptions(options);

            if (hooksRegistered)
            {
                return;
            }

            hooksRegistered = true;
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
            RegisterAfter(modConfig, "GameEntryUI.Init", ctx =>
                RecoverNativeInput(primaryOwner, "GameEntryUI.Init", 12));
            RegisterBefore(modConfig, "Fight_Win.ResetStates", ctx => BeginTransition(primaryOwner, "Fight_Win.ResetStates.before", DefaultGuardFrames));
            RegisterAfter(modConfig, "Fight_Win.ResetStates", ctx => BeginTransition(primaryOwner, "Fight_Win.ResetStates.after", DefaultGuardFrames));
            RegisterBefore(modConfig, "Fight_Escape.ResetStates", ctx => BeginTransition(primaryOwner, "Fight_Escape.ResetStates.before", DefaultGuardFrames));
            RegisterAfter(modConfig, "Fight_Escape.ResetStates", ctx => BeginTransition(primaryOwner, "Fight_Escape.ResetStates.after", DefaultGuardFrames));
            RegisterBefore(modConfig, "Fight_Loss.Init", ctx => BeginTransition(primaryOwner, "Fight_Loss.Init.before", DefaultGuardFrames));
            RegisterAfter(modConfig, "Fight_Loss.Init", ctx => BeginTransition(primaryOwner, "Fight_Loss.Init.after", DefaultGuardFrames));
            Info("Hooks registered by owner=" + primaryOwner);
        }

        public void BeginTransition(string ownerModId, string source, int frames = DefaultGuardFrames)
        {
            var effectiveFrames = ClampGuardFrames(frames);
            var frame = Time.frameCount;
            guardSource = ownerModId + ":" + source;
            guardUntilFrame = Math.Max(guardUntilFrame, frame + effectiveFrames);
            if (lastGuardFrameBySource.TryGetValue(guardSource, out var lastGuardFrame)
                && lastGuardFrame == frame)
            {
                Trace("Duplicate guard arm skipped. source=" + guardSource
                      + ", frame=" + frame
                      + ", untilFrame=" + guardUntilFrame);
                return;
            }

            lastGuardFrameBySource[guardSource] = frame;
            lastScrubFrame = -1;
            Verbose("Guard armed. source=" + guardSource
                    + ", frame=" + frame
                    + ", untilFrame=" + guardUntilFrame);
            QueueGlobalScrub(ownerModId, source + ":guard-arm", 1);
            if (registryScrubFrames > 0)
            {
                UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(
                    Math.Min(registryScrubFrames, effectiveFrames),
                    guardSource + ":registry",
                    Trace);
            }
        }

        public int DisableRaycasts(string ownerModId, GameObject? root, string source)
        {
            if (!ShouldProcessRootThisFrame(root, lastRaycastDisableFrameByRoot))
            {
                return 0;
            }

            return UiRaycastSafeDestroyRuntime.DisableRaycasts(root, ownerModId + ":" + source, Verbose);
        }

        public void RunAfterGuard(string ownerModId, string source, Action action, int extraFrames = 4)
        {
            var dueFrame = Math.Max(Time.frameCount, guardUntilFrame) + Math.Max(1, extraFrames);
            deferredActions.Add(new DeferredAction(ownerModId + ":" + source, dueFrame, action));
            Trace("Deferred action queued. source=" + ownerModId + ":" + source
                  + ", frame=" + Time.frameCount
                  + ", dueFrame=" + dueFrame);
        }

        public int ScrubNow(string ownerModId, string source)
        {
            return UiRaycastSafeDestroyRuntime.ScrubGraphicRegistry(ownerModId + ":" + source, Trace);
        }

        public void RecoverNativeInput(string ownerModId, string source, int frames = 12)
        {
            var maximumOffset = Math.Max(1, Math.Min(maxGuardFrames, frames));
            var baseFrame = Math.Max(Time.frameCount, guardUntilFrame);
            var ticket = ++nativeInputRecoveryTicket;
            var offsets = new[] { 1, 2, 4, 8, maximumOffset }
                .Where(offset => offset <= maximumOffset)
                .Distinct()
                .OrderBy(offset => offset)
                .ToArray();
            foreach (var offset in offsets)
            {
                var terminal = offset == offsets[offsets.Length - 1];
                deferredActions.Add(new DeferredAction(
                    ownerModId + ":" + source + ":recover:" + offset,
                    baseFrame + offset,
                    () =>
                    {
                        if (ticket != nativeInputRecoveryTicket)
                        {
                            return;
                        }

                        RecoverNativeInputPass(
                            ownerModId,
                            source + ":frame+" + offset,
                            terminal,
                            terminal ? Info : Trace);
                    }));
            }

            Trace("Native input recovery queued. source=" + ownerModId + ":" + source
                  + ", baseFrame=" + baseFrame
                  + ", passes=" + string.Join(",", offsets));
        }

        private void BeforeUiManagerCloseUi(ModHookContext context)
        {
            var uiName = ArgumentString(context, 0);
            if (!IsWatchedUiName(uiName) && !IsActive())
            {
                return;
            }

            BeginTransition(primaryOwner, "UIManager.CloseUI.before:" + EmptyAsUnknown(uiName), DefaultGuardFrames);
            var root = FindUiRoot(uiName);
            if (root != null)
            {
                LeaseRaycasters(root, "UIManager.CloseUI.before:" + EmptyAsUnknown(uiName));
                ScrubRoot(root, "UIManager.CloseUI.before:" + EmptyAsUnknown(uiName));
            }
        }

        private void BeforeUiBaseClose(ModHookContext context)
        {
            var root = TargetGameObject(context);
            if (root == null)
            {
                return;
            }

            var watched = IsWatchedUiName(root.name) || IsWatchedTransform(root.transform);
            if (!watched && !IsActive())
            {
                return;
            }

            BeginTransition(primaryOwner, "UIBase.Close.before:" + SafeObjectName(root), DefaultGuardFrames);
            LeaseRaycasters(root, "UIBase.Close.before:" + SafeObjectName(root));
            ScrubRoot(root, "UIBase.Close.before:" + SafeObjectName(root));
        }

        private void BeforeDialogueTransition(ModHookContext context)
        {
            BeginTransition(primaryOwner, TargetName(context) + ".before-dialogue", DefaultGuardFrames);
        }

        private void BeforeUiLifecycle(ModHookContext context)
        {
            if (!IsActive())
            {
                return;
            }

            QueueGlobalScrub(primaryOwner, TargetName(context) + ":before-ui-lifecycle", 1);
        }

        private void AfterUiLifecycle(ModHookContext context)
        {
            if (!IsActive())
            {
                return;
            }

            QueueGlobalScrub(primaryOwner, TargetName(context) + ":after-ui-lifecycle", 1);
            RecoverNativeInput(primaryOwner, TargetName(context) + ":after-ui-lifecycle", 12);
            Verbose("Lifecycle scrub queued. target=" + TargetName(context)
                    + ", frame=" + Time.frameCount);
        }

        private void AfterUpperCanvasRaycasterState(ModHookContext context)
        {
            if (!IsActive())
            {
                return;
            }

            var root = UpperCanvasRoot();
            var removed = ScrubRoot(root, TargetName(context) + ":after-upper-canvas");
            Verbose("Upper canvas registry state scrubbed without leasing native raycasters. target=" + TargetName(context)
                    + ", removed=" + removed
                    + ", frame=" + Time.frameCount);
        }

        private void Update()
        {
            MaintainGuard();
            RunDueDeferredActions();
        }

        private void LateUpdate()
        {
            if (!IsActive())
            {
                RestoreRaycasters("late-update-expired");
            }
        }

        private void MaintainGuard()
        {
            PruneFrameMaps();
            if (IsActive())
            {
                if (lastScrubFrame < 0 || Time.frameCount - lastScrubFrame >= scrubEveryFrames)
                {
                    lastScrubFrame = Time.frameCount;
                    QueueGlobalScrub(primaryOwner, guardSource + ":runner-update:frame" + Time.frameCount, 1);
                }

                return;
            }

            RestoreRaycasters("guard-expired");
        }

        private void OnDisable()
        {
            RestoreRaycasters("component-disabled");
        }

        private void OnDestroy()
        {
            RestoreRaycasters("component-destroyed");
            deferredActions.Clear();
        }

        private GameObject? UpperCanvasRoot()
        {
            try
            {
                var manager = UIManager.Instance;
                return manager?.upperCanvasTf == null ? null : manager.upperCanvasTf.gameObject;
            }
            catch
            {
                return null;
            }
        }

        private int LeaseRaycasters(GameObject? root, string source)
        {
            if (root == null)
            {
                return 0;
            }

            if (!IsRuntimeSceneObject(root.transform))
            {
                return 0;
            }

            if (!ShouldProcessRootThisFrame(root, lastRaycasterLeaseFrameByRoot))
            {
                return 0;
            }

            GraphicRaycaster[] raycasters;
            try
            {
                raycasters = root.GetComponentsInChildren<GraphicRaycaster>(true);
            }
            catch (Exception ex)
            {
                Warn("Failed to enumerate raycasters under root. source=" + source
                     + ", root=" + SafeObjectName(root)
                     + " -> " + ex.Message);
                return 0;
            }

            var suspended = 0;
            var details = 0;
            foreach (var raycaster in raycasters)
            {
                suspended += LeaseRaycaster(raycaster, source, ref details);
            }

            if (suspended > 0)
            {
                Verbose("Leased raycasters. source=" + source
                        + ", frame=" + Time.frameCount
                        + ", count=" + suspended
                        + ", tracked=" + suspendedRaycasters.Count
                        + ", untilFrame=" + guardUntilFrame);
            }

            return suspended;
        }

        private int LeaseRaycaster(GraphicRaycaster? raycaster, string source, ref int details)
        {
            if (raycaster == null || !IsRuntimeSceneObject(raycaster))
            {
                return 0;
            }

            int id;
            try
            {
                id = raycaster.GetInstanceID();
            }
            catch
            {
                return 0;
            }

            if (!suspendedRaycasters.ContainsKey(id))
            {
                suspendedRaycasters[id] = new SuspendedRaycaster(
                    raycaster,
                    raycaster.enabled,
                    RaycasterName(raycaster),
                    TransformPath(raycaster.transform),
                    source,
                    Time.frameCount);
            }

            if (!raycaster.enabled)
            {
                return 0;
            }

            try
            {
                raycaster.enabled = false;
                if (logLevel >= UiTransitionGuardLogLevel.Verbose && details < MaxRaycasterDetailsPerPass)
                {
                    details++;
                    Verbose("Leased raycaster. source=" + source
                            + ", frame=" + Time.frameCount
                            + ", raycaster=" + RaycasterName(raycaster)
                            + ", path=" + TransformPath(raycaster.transform));
                }

                return 1;
            }
            catch (Exception ex)
            {
                Warn("Failed to lease raycaster. source=" + source
                     + ", raycaster=" + RaycasterName(raycaster)
                     + " -> " + ex.Message);
                return 0;
            }
        }

        private void RestoreRaycasters(string source)
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
                if (raycaster == null || !item.OriginalEnabled)
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
                    Warn("Failed to restore raycaster. source=" + source
                         + ", raycaster=" + item.Name
                         + " -> " + ex.Message);
                }
            }

            Verbose("Restored raycasters. source=" + source
                    + ", frame=" + Time.frameCount
                    + ", restored=" + restored
                    + ", skipped=" + skipped
                    + ", tracked=" + suspendedRaycasters.Count);
            suspendedRaycasters.Clear();
            QueueGlobalScrub(primaryOwner, source + ":after-restore", 1);
        }

        private void QueueGlobalScrub(string ownerModId, string source, int frames)
        {
            var frame = Time.frameCount;
            if (lastGlobalScrubFrame == frame)
            {
                Trace("Duplicate global scrub skipped. source=" + source
                      + ", frame=" + frame);
                return;
            }

            lastGlobalScrubFrame = frame;
            AuraSharedFrameScheduler.Enqueue(new AuraSharedFrameEnqueueRequest
            {
                OwnerId = ownerModId,
                Source = "UiTransitionGuard.Scrub:" + source,
                Action = () =>
                {
                    UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForFrames(
                        Math.Max(1, frames),
                        ownerModId + ":" + source,
                        Trace);
                }
            });
        }

        private int ScrubRoot(GameObject? root, string source)
        {
            if (!ShouldProcessRootThisFrame(root, lastRootScrubFrameByRoot))
            {
                return 0;
            }

            return UiRaycastSafeDestroyRuntime.ScrubGraphicRegistryForRoot(
                root,
                primaryOwner + ":" + source,
                Trace);
        }

        private static bool ShouldProcessRootThisFrame(GameObject? root, Dictionary<int, int> lastFrameByRoot)
        {
            if (!TryRootId(root, out var rootId))
            {
                return false;
            }

            var frame = Time.frameCount;
            if (lastFrameByRoot.TryGetValue(rootId, out var lastFrame) && lastFrame == frame)
            {
                return false;
            }

            lastFrameByRoot[rootId] = frame;
            return true;
        }

        private static bool TryRootId(GameObject? root, out int rootId)
        {
            rootId = 0;
            if (root == null)
            {
                return false;
            }

            try
            {
                rootId = root.GetInstanceID();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void PruneFrameMaps()
        {
            var frame = Time.frameCount;
            PruneFrameMap(lastRaycastDisableFrameByRoot, frame);
            PruneFrameMap(lastRaycasterLeaseFrameByRoot, frame);
            PruneFrameMap(lastRootScrubFrameByRoot, frame);
            if (lastGuardFrameBySource.Count > 256)
            {
                var expired = lastGuardFrameBySource
                    .Where(pair => frame - pair.Value > 60)
                    .Select(pair => pair.Key)
                    .ToList();
                foreach (var key in expired)
                {
                    lastGuardFrameBySource.Remove(key);
                }
            }
        }

        private static void PruneFrameMap(Dictionary<int, int> map, int frame)
        {
            if (map.Count <= 256)
            {
                return;
            }

            var expired = map
                .Where(pair => frame - pair.Value > 60)
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in expired)
            {
                map.Remove(key);
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

        private bool IsActive()
        {
            return guardUntilFrame >= 0 && Time.frameCount <= guardUntilFrame;
        }

        private void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
        {
            try
            {
                config.AddMethodHookBefore(target, context => SafeHook(target, () => action(context)));
                Trace("Hook before registered: " + target);
            }
            catch (Exception ex)
            {
                Warn("Hook before failed: " + target + " -> " + ex.Message);
            }
        }

        private void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
        {
            try
            {
                config.AddMethodHookAfter(target, context => SafeHook(target, () => action(context)));
                Trace("Hook after registered: " + target);
            }
            catch (Exception ex)
            {
                Warn("Hook after failed: " + target + " -> " + ex.Message);
            }
        }

        private void SafeHook(string target, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Warn("Hook failed: " + target + " -> " + ex.Message);
            }
        }

        private string ArgumentString(ModHookContext context, int index)
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

        private GameObject? TargetGameObject(ModHookContext context)
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

        private GameObject? FindUiRoot(string uiName)
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

        private bool IsWatchedTransform(Transform transform)
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

        private bool IsWatchedUiName(string? uiName)
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

        private string TargetName(ModHookContext context)
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

        private string RaycasterName(GraphicRaycaster raycaster)
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

        private string CanvasName(Canvas? canvas)
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

        private void Info(string message)
        {
            Debug.Log("[UiTransitionGuard] " + message);
        }

        private void Verbose(string message)
        {
            if (logLevel >= UiTransitionGuardLogLevel.Verbose)
            {
                Debug.Log("[UiTransitionGuard] " + message);
            }
        }

        private void Trace(string message)
        {
            if (logLevel >= UiTransitionGuardLogLevel.Trace)
            {
                Debug.Log("[UiTransitionGuard] " + message);
            }
        }

        private void Warn(string message)
        {
            Debug.LogWarning("[UiTransitionGuard] " + message);
        }

        private void ApplyOptions(object? options)
        {
            maxGuardFrames = ReadIntOption(
                options,
                nameof(UiTransitionGuardOptions.MaxGuardFrames),
                DefaultMaxGuardFrames,
                1,
                MaxAllowedGuardFrames);
            registryScrubFrames = ReadIntOption(
                options,
                nameof(UiTransitionGuardOptions.RegistryScrubFrames),
                DefaultRegistryScrubFrames,
                0,
                MaxAllowedGuardFrames);
            scrubEveryFrames = ReadIntOption(
                options,
                nameof(UiTransitionGuardOptions.ScrubEveryFrames),
                DefaultScrubEveryFrames,
                1,
                Math.Max(1, maxGuardFrames));
        }

        private int ClampGuardFrames(int frames)
        {
            return Math.Max(1, Math.Min(maxGuardFrames, frames));
        }

        private static int ReadIntOption(object? options, string propertyName, int fallback, int min, int max)
        {
            if (options == null)
            {
                return fallback;
            }

            try
            {
                object? value;
                if (options is UiTransitionGuardOptions typed)
                {
                    value = propertyName switch
                    {
                        nameof(UiTransitionGuardOptions.MaxGuardFrames) => typed.MaxGuardFrames,
                        nameof(UiTransitionGuardOptions.RegistryScrubFrames) => typed.RegistryScrubFrames,
                        nameof(UiTransitionGuardOptions.ScrubEveryFrames) => typed.ScrubEveryFrames,
                        _ => fallback
                    };
                }
                else
                {
                    value = options.GetType()
                        .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                        ?.GetValue(options);
                }

                var number = value switch
                {
                    int intValue => intValue,
                    long longValue => longValue > int.MaxValue ? int.MaxValue : (int)longValue,
                    short shortValue => shortValue,
                    byte byteValue => byteValue,
                    string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
                    _ => fallback
                };
                return Math.Max(min, Math.Min(max, number));
            }
            catch
            {
                return fallback;
            }
        }

        private static UiTransitionGuardLogLevel ReadLogLevel(object? options)
        {
            if (options == null)
            {
                return UiTransitionGuardLogLevel.Normal;
            }

            if (options is UiTransitionGuardOptions typed)
            {
                return typed.LogLevel;
            }

            try
            {
                var value = options.GetType()
                    .GetProperty("LogLevel", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(options);
                if (value == null)
                {
                    return UiTransitionGuardLogLevel.Normal;
                }

                return Enum.TryParse(value.ToString(), out UiTransitionGuardLogLevel parsed)
                    ? parsed
                    : UiTransitionGuardLogLevel.Normal;
            }
            catch
            {
                return UiTransitionGuardLogLevel.Normal;
            }
        }
    }

    private sealed class SuspendedRaycaster
    {
        public SuspendedRaycaster(GraphicRaycaster raycaster, bool originalEnabled, string name, string path, string source, int frame)
        {
            Raycaster = raycaster;
            OriginalEnabled = originalEnabled;
            Name = name;
            Path = path;
            Source = source;
            Frame = frame;
        }

        public GraphicRaycaster Raycaster { get; }

        public bool OriginalEnabled { get; }

        public string Name { get; }

        public string Path { get; }

        public string Source { get; }

        public int Frame { get; }
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
