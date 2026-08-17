using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Mirror;
using UiTransitionGuardShared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Witch.UI.Window;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Provides replay-owned lifecycle barriers. Shared UI safety remains semantic-free;
/// this runner knows which replay roots and network state must be gone before handoff.
/// </summary>
internal sealed class MatchReplayLifecycleRunner : MonoBehaviour
{
    private const string RunnerName = "AuraToolsMatchReplayLifecycleRunner";
    private const float UiCloseTimeoutMilliseconds = 2600f;
    private const float NetworkForceStopMilliseconds = 2000f;
    private const float NetworkDegradedHandoffMilliseconds = 8000f;
    private const float NetworkDiagnosticIntervalMilliseconds = 2000f;
    private const float MenuMinimumSettleMilliseconds = 650f;
    private const float MenuRestoreTimeoutMilliseconds = 6000f;
    private const float MenuCacheHideTimeoutMilliseconds = 1800f;
    private const int MenuCacheRebuildAttempts = 2;
    private static MatchReplayLifecycleRunner? instance;
    private Coroutine? launchRoutine;
    private Coroutine? stopRoutine;
    private int generation;
    private MatchReplayExitPhases phase = MatchReplayExitPhases.Idle;

    internal static bool IsStopping => instance != null && instance.stopRoutine != null;

    internal static MatchReplayExitPhases Phase => instance?.phase ?? MatchReplayExitPhases.Idle;

    internal static void BeginLaunch(IReadOnlyList<GameObject>? trackedRoots, Action ready)
    {
        var runner = Ensure();
        if (runner.launchRoutine != null)
        {
            runner.StopCoroutine(runner.launchRoutine);
        }

        if (runner.stopRoutine != null)
        {
            throw new InvalidOperationException("A replay cannot launch while the previous replay is returning to the menu.");
        }

        var token = ++runner.generation;
        runner.launchRoutine = runner.StartCoroutine(runner.WaitForLaunch(trackedRoots, ready, token));
    }

    internal static void BeginStop(IReadOnlyList<GameObject>? trackedRoots, Action completed)
    {
        var runner = Ensure();
        if (runner.launchRoutine != null)
        {
            runner.StopCoroutine(runner.launchRoutine);
            runner.launchRoutine = null;
        }

        if (runner.stopRoutine != null)
        {
            runner.StopCoroutine(runner.stopRoutine);
        }

        var token = ++runner.generation;
        runner.stopRoutine = runner.StartCoroutine(runner.WaitForStop(trackedRoots, completed, token));
    }

    private static MatchReplayLifecycleRunner Ensure()
    {
        if (instance != null)
        {
            return instance;
        }

        var existing = GameObject.Find(RunnerName);
        if (existing != null)
        {
            instance = existing.GetComponent<MatchReplayLifecycleRunner>()
                       ?? existing.AddComponent<MatchReplayLifecycleRunner>();
            return instance;
        }

        var root = new GameObject(RunnerName);
        Object.DontDestroyOnLoad(root);
        instance = root.AddComponent<MatchReplayLifecycleRunner>();
        return instance;
    }

    private IEnumerator WaitForLaunch(IReadOnlyList<GameObject>? roots, Action ready, int token)
    {
        yield return WaitForRoots(roots, "launch");
        if (token != generation)
        {
            yield break;
        }

        yield return null;
        yield return new WaitForEndOfFrame();
        LogTerminalState("launch-ready");
        launchRoutine = null;
        ready();
    }

    private IEnumerator WaitForStop(IReadOnlyList<GameObject>? roots, Action completed, int token)
    {
        SetPhase(MatchReplayExitPhases.ClosingTransientUi);
        var presentationIds = new HashSet<int>(
            MatchReplayUiLifecycle.SnapshotReplayOwnedPresentationRoots()
                .Where(root => root != null)
                .Select(root => root.GetInstanceID()));
        var preNetworkRoots = (roots ?? Array.Empty<GameObject>())
            .Where(root => root != null)
            .Where(root => MatchReplayExitPolicy.CanWaitForUiBeforeNetworkStop(
                presentationIds.Contains(root.GetInstanceID())))
            .Distinct()
            .ToList();
        var rejectedPresentationRoots = (roots ?? Array.Empty<GameObject>())
            .Where(root => root != null && presentationIds.Contains(root.GetInstanceID()))
            .Select(root => root.name)
            .Distinct()
            .ToArray();
        if (rejectedPresentationRoots.Length != 0)
        {
            AuraToolsLog.Warn("[MatchRecords] replay native presentation roots were removed from the pre-network close barrier: "
                              + string.Join("|", rejectedPresentationRoots) + ".");
        }

        yield return WaitForRoots(preNetworkRoots, "pre-network");
        if (token != generation)
        {
            yield break;
        }

        yield return null;
        yield return new WaitForEndOfFrame();
        SetPhase(MatchReplayExitPhases.StoppingNetwork);
        var diagnosticClock = 0f;
        var networkClock = 0f;
        var forced = false;
        var allowTransportOnly = false;
        while (!MatchReplayLocalHostRuntime.IsStopped)
        {
            var elapsed = FrameMilliseconds();
            diagnosticClock += elapsed;
            networkClock += elapsed;
            if (!forced && networkClock >= NetworkForceStopMilliseconds)
            {
                forced = true;
                MatchReplayLocalHostRuntime.ForceStop();
            }

            if (diagnosticClock >= NetworkDiagnosticIntervalMilliseconds)
            {
                diagnosticClock = 0f;
                AuraToolsLog.Warn("[MatchRecords] replay network teardown still pending: elapsedMs="
                                  + (int)networkClock + ", state="
                                  + MatchReplayLocalHostRuntime.DescribeTeardownState() + ".");
                try
                {
                    MatchReplayLocalHostRuntime.Stop();
                }
                catch (Exception ex)
                {
                    AuraToolsLog.Warn("[MatchRecords] replay host stop retry failed: " + ex.Message);
                }
            }

            if (forced
                && networkClock >= NetworkDegradedHandoffMilliseconds
                && MatchReplayLocalHostRuntime.IsTransportQuiescent)
            {
                allowTransportOnly = true;
                AuraToolsLog.Warn("[MatchRecords] replay network object settlement exceeded timeout; "
                                  + "continuing after transport quiescence: state="
                                  + MatchReplayLocalHostRuntime.DescribeTeardownState() + ".");
                break;
            }

            yield return null;
        }

        try
        {
            MatchReplayLocalHostRuntime.CompleteStop(allowTransportOnly);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[MatchRecords] replay host finalization failed.", ex);
        }

        // Mirror destroys local-host identities at end of frame. Give those native
        // OnDisable/OnDestroy callbacks two complete frames before closing UI or
        // asking GameApp to rebuild the house.
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return null;
        yield return new WaitForEndOfFrame();

        var menuRoots = MatchReplayUiLifecycle.SnapshotReplayOwnedPresentationRoots();
        var chatRoot = MatchReplayChatUiLeaseRuntime.TrackedRoot;
        if (chatRoot != null)
        {
            menuRoots.Add(chatRoot);
        }

        menuRoots = menuRoots.Where(root => root != null).Distinct().ToList();
        SetPhase(MatchReplayExitPhases.ReturningToMenu);
        var nativeReturnRequested = false;
        try
        {
            // ReturnToMenu synchronously performs its unmanaged-UI sweep before
            // scheduling StartHouse. Invoke it while native replay UI is still
            // registered so those objects are not reclassified and hard-destroyed.
            nativeReturnRequested = MatchReplayEnvironmentScope.BeginNativeMenuReturn();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[MatchRecords] native replay menu return failed to start.", ex);
        }

        try
        {
            if (allowTransportOnly)
            {
                MatchReplayChatUiLeaseRuntime.ForceFinalizeAfterTimeout(
                    "Match replay degraded network terminal");
            }
            else
            {
                MatchReplayChatUiLeaseRuntime.FinalizeAfterNetworkStop(
                    "Match replay network terminal");
            }

            var closingChatRoot = MatchReplayChatUiLeaseRuntime.ClosingRoot;
            if (closingChatRoot != null)
            {
                menuRoots.Add(closingChatRoot);
            }

            MatchReplayUiLifecycle.RequestCloseReplayOwnedPresentationUis(
                "Match replay native menu return");
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[MatchRecords] replay post-network UI close request failed.", ex);
        }

        menuRoots = menuRoots.Where(root => root != null).Distinct().ToList();

        if (nativeReturnRequested)
        {
            UiTransitionGuardRuntime.BeginTransition(
                null,
                AuraToolsIds.ModId,
                "Match replay native menu return",
                16);
            UiTransitionGuardRuntime.RecoverNativeInput(
                null,
                AuraToolsIds.ModId,
                "Match replay native menu return",
                16);
            SetPhase(MatchReplayExitPhases.VerifyingMenu);
            yield return WaitForNativeMenu(menuRoots);
        }

        try
        {
            if (nativeReturnRequested)
            {
                MatchReplayEnvironmentScope.CompleteNativeMenuReturn();
            }
            else
            {
                MatchReplayEnvironmentScope.Restore();
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[MatchRecords] replay environment restoration failed.", ex);
        }

        yield return null;
        yield return new WaitForEndOfFrame();
        UiTransitionGuardRuntime.ScrubNow(null, AuraToolsIds.ModId, "Match replay stop terminal");
        UiTransitionGuardRuntime.RecoverNativeInput(
            null,
            AuraToolsIds.ModId,
            "Match replay stop terminal",
            12);
        yield return null;
        yield return new WaitForEndOfFrame();

        var menuCacheReady = false;
        SetPhase(MatchReplayExitPhases.RebuildingMenuCaches);
        yield return RebuildNativeMenuCaches(ready => menuCacheReady = ready);
        if (token != generation)
        {
            yield break;
        }

        SetPhase(menuCacheReady
            ? MatchReplayExitPhases.Ready
            : MatchReplayExitPhases.MenuCacheFailed);
        try
        {
            completed();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[MatchRecords] replay terminal callback failed.", ex);
        }

        if (token == generation)
        {
            LogTerminalState(menuCacheReady ? "stop-ready" : "stop-menu-cache-failed");
            stopRoutine = null;
        }
    }

    private IEnumerator RebuildNativeMenuCaches(Action<bool> completed)
    {
        for (var attempt = 1; attempt <= MenuCacheRebuildAttempts; attempt++)
        {
            var before = CaptureMenuCacheState();
            if (before.SettingUiCount != 0 || before.Registered)
            {
                AuraToolsLog.Warn("[MatchRecords] native menu cache prewarm found residual SettingUI state: attempt="
                                  + attempt + ", state=" + DescribeMenuCacheState(before) + ".");
                MatchReplayUiLifecycle.ForceCloseOriginUi(
                    "Match replay SettingUI cache residual cleanup");
                yield return null;
                yield return new WaitForEndOfFrame();
            }

            var requested = false;
            try
            {
                MatchReplayUiLifecycle.PrewarmNativeSettingUiCache(
                    "Match replay menu cache rebuild attempt " + attempt);
                requested = true;
            }
            catch (Exception ex)
            {
                AuraToolsLog.Error("[MatchRecords] native SettingUI cache prewarm failed: attempt="
                                   + attempt + ".", ex);
            }

            if (requested)
            {
                var elapsed = 0f;
                while (elapsed < MenuCacheHideTimeoutMilliseconds)
                {
                    var state = CaptureMenuCacheState();
                    if (MatchReplayExitPolicy.IsMenuCacheReady(state))
                    {
                        AuraToolsLog.Info("[MatchRecords] native SettingUI cache verified: attempt="
                                          + attempt + ", elapsedMs=" + (int)elapsed
                                          + ", state=" + DescribeMenuCacheState(state) + ".");
                        completed(true);
                        yield break;
                    }

                    elapsed += FrameMilliseconds();
                    yield return null;
                }

                AuraToolsLog.Warn("[MatchRecords] native SettingUI cache did not settle: attempt="
                                  + attempt + ", state="
                                  + DescribeMenuCacheState(CaptureMenuCacheState()) + ".");
            }

            if (attempt < MenuCacheRebuildAttempts)
            {
                MatchReplayUiLifecycle.ForceCloseOriginUi(
                    "Match replay SettingUI cache retry cleanup");
                yield return null;
                yield return new WaitForEndOfFrame();
            }
        }

        // The native Hide tween is the only expected source of a late state.
        // Normalize that one registered instance without fabricating or keeping
        // duplicate caches, then verify the same invariant once more.
        MatchReplayUiLifecycle.ForceNormalizeNativeSettingUiCache(
            "Match replay SettingUI cache terminal fallback");
        yield return null;
        yield return new WaitForEndOfFrame();
        var terminal = CaptureMenuCacheState();
        var ready = MatchReplayExitPolicy.IsMenuCacheReady(terminal);
        if (!ready)
        {
            AuraToolsLog.Error("[MatchRecords] native SettingUI cache rebuild failed terminal verification: state="
                               + DescribeMenuCacheState(terminal) + ".");
        }

        completed(ready);
    }

    private IEnumerator WaitForNativeMenu(IReadOnlyList<GameObject> roots)
    {
        var tracked = (roots ?? Array.Empty<GameObject>())
            .Where(root => root != null)
            .Distinct()
            .ToList();
        var elapsed = 0f;
        var diagnosticClock = 0f;
        while (elapsed < MenuRestoreTimeoutMilliseconds)
        {
            var frame = FrameMilliseconds();
            elapsed += frame;
            diagnosticClock += frame;
            if (elapsed >= MenuMinimumSettleMilliseconds)
            {
                var state = CaptureMenuRestorationState(tracked);
                if (MatchReplayExitPolicy.IsMenuRestorationReady(state))
                {
                    AuraToolsLog.Info("[MatchRecords] native menu return verified: elapsedMs="
                                      + (int)elapsed + ", state=" + DescribeMenuState(state) + ".");
                    yield break;
                }

                if (diagnosticClock >= NetworkDiagnosticIntervalMilliseconds)
                {
                    diagnosticClock = 0f;
                    AuraToolsLog.Warn("[MatchRecords] native menu return still settling: elapsedMs="
                                      + (int)elapsed + ", state=" + DescribeMenuState(state) + ".");
                }
            }

            yield return null;
        }

        var terminal = CaptureMenuRestorationState(tracked);
        AuraToolsLog.Warn("[MatchRecords] native menu return exceeded timeout; forcing replay residuals: state="
                          + DescribeMenuState(terminal) + ".");
        MatchReplayChatUiLeaseRuntime.ForceFinalizeAfterTimeout(
            "Match replay menu-return timeout");
        MatchReplayUiLifecycle.ForceCloseOriginUi("Match replay menu-return timeout");
        MatchReplayUiLifecycle.ForceCloseReplayOwnedPresentationUis(
            "Match replay menu-return timeout");
        foreach (var root in tracked.Where(root => root != null))
        {
            MatchReplayUiLifecycle.ForceDestroyRoot(root, "Match replay menu-return timeout");
        }

        yield return null;
        yield return new WaitForEndOfFrame();
        UiTransitionGuardRuntime.RecoverNativeInput(
            null,
            AuraToolsIds.ModId,
            "Match replay menu-return timeout",
            12);
    }

    private static MatchReplayMenuRestorationState CaptureMenuRestorationState(
        IReadOnlyList<GameObject> trackedRoots)
    {
        var house = GameApp.Instance?.HouseItem;
        return new MatchReplayMenuRestorationState
        {
            NativeReturnRequested = MatchReplayEnvironmentScope.NativeMenuReturnRequested,
            ExpectedHouseActive = MatchReplayEnvironmentScope.ExpectedHouseActive,
            HouseActive = house != null && house.activeInHierarchy,
            ReplayBackgroundAlive = MatchReplayEnvironmentScope.IsReplayBackgroundAlive,
            ResidualReplayUiCount = (trackedRoots ?? Array.Empty<GameObject>())
                .Count(root => root != null),
            SettingUiCount = MatchReplayUiLifecycle.SettingUiCount,
            ChatUiClosing = MatchReplayChatUiLeaseRuntime.IsClosing,
            InputInfrastructureReady = IsMenuInputInfrastructureReady()
        };
    }

    private static string DescribeMenuState(MatchReplayMenuRestorationState state)
    {
        return "requested=" + state.NativeReturnRequested
               + ",expectedHouse=" + state.ExpectedHouseActive
               + ",house=" + state.HouseActive
               + ",replayBackground=" + state.ReplayBackgroundAlive
               + ",residualReplayUi=" + state.ResidualReplayUiCount
               + ",settings=" + state.SettingUiCount
               + ",chatClosing=" + state.ChatUiClosing
               + ",inputInfrastructure=" + state.InputInfrastructureReady;
    }

    private static MatchReplayMenuCacheState CaptureMenuCacheState()
    {
        return MatchReplayUiLifecycle.CaptureNativeSettingUiCacheState(
            IsMenuInputInfrastructureReady());
    }

    private static string DescribeMenuCacheState(MatchReplayMenuCacheState state)
    {
        return "settings=" + state.SettingUiCount
               + ",registered=" + state.Registered
               + ",registeredMatches=" + state.RegisteredMatchesOnlyInstance
               + ",activeSelf=" + state.ActiveSelf
               + ",blocksRaycasts=" + state.BlocksRaycasts
               + ",mainCanvas=" + state.ParentIsMainCanvas
               + ",inputInfrastructure=" + state.InputInfrastructureReady;
    }

    private void SetPhase(MatchReplayExitPhases next)
    {
        if (phase == next)
        {
            return;
        }

        phase = next;
        AuraToolsLog.Debug("[MatchRecords] replay exit phase: generation="
                           + generation + ", phase=" + phase + ".");
    }

    private static IEnumerator WaitForRoots(IReadOnlyList<GameObject>? roots, string phase)
    {
        var tracked = (roots ?? Array.Empty<GameObject>())
            .Where(root => root != null)
            .Distinct()
            .ToList();
        var elapsed = 0f;
        while (tracked.Any(root => root != null) && elapsed < UiCloseTimeoutMilliseconds)
        {
            elapsed += FrameMilliseconds();
            yield return null;
        }

        var residual = tracked.Where(root => root != null).ToList();
        if (residual.Count == 0)
        {
            yield break;
        }

        AuraToolsLog.Warn("[MatchRecords] replay " + phase
                          + " UI close exceeded lifecycle timeout; forcing tracked roots="
                          + string.Join("|", residual.Select(root => root.name)) + ".");
        foreach (var root in residual)
        {
            if (root == null)
            {
                continue;
            }

            MatchReplayUiLifecycle.ForceDestroyRoot(
                root,
                "Match replay " + phase + " lifecycle timeout");
        }

        yield return null;
        yield return new WaitForEndOfFrame();
    }

    private static float FrameMilliseconds()
    {
        return Math.Max(1f, Time.unscaledDeltaTime * 1000f);
    }

    private static void LogTerminalState(string source)
    {
        var top = "none";
        try
        {
            var eventSystem = EventSystem.current;
            var mouse = Mouse.current;
            if (eventSystem != null && mouse != null)
            {
                var hits = new List<RaycastResult>();
                eventSystem.RaycastAll(
                    new PointerEventData(eventSystem) { position = mouse.position.ReadValue() },
                    hits);
                var first = hits.FirstOrDefault(item => item.gameObject != null);
                if (first.gameObject != null)
                {
                    top = Path(first.gameObject.transform);
                }
            }
        }
        catch (Exception ex)
        {
            top = "diagnostic-failed:" + ex.Message;
        }

        AuraToolsLog.Debug("[MatchRecords] replay lifecycle terminal: source=" + source
                           + ", exitPhase=" + Phase
                           + ", server=" + NetworkServer.active
                           + ", client=" + NetworkClient.active
                           + ", connected=" + NetworkClient.isConnected
                           + ", controls=" + (GameObject.Find("AuraToolsMatchReplayControls") != null)
                           + ", managedUi=" + ManagedUiSummary()
                           + ", chatLease=" + MatchReplayChatUiLeaseRuntime.Describe()
                           + ", upperChildren=" + UpperChildrenSummary()
                           + ", rootCanvasGroups=" + RootCanvasGroupSummary()
                           + ", houseInput=" + HouseInputSummary()
                           + ", eventSystems=" + EventSystemSummary()
                           + ", cameraInput=" + CameraInputSummary()
                           + ", settings=" + SettingUiSummary()
                           + ", inputInfrastructureReady=" + IsMenuInputInfrastructureReady()
                           + ", networkTeardown=" + MatchReplayLocalHostRuntime.DescribeTeardownState()
                           + ", selected=" + SelectedObjectSummary()
                           + ", topRaycast=" + top + ".");
    }

    private static string RootCanvasGroupSummary()
    {
        try
        {
            var groups = SceneManager.GetActiveScene()
                .GetRootGameObjects()
                .Where(root => root != null
                               && !string.Equals(root.name, "Upper Canvas", StringComparison.Ordinal))
                .Select(root => new { Root = root, Group = root.GetComponent<CanvasGroup>() })
                .Where(item => item.Group != null)
                .Select(item => item.Root.name + ":blocks=" + item.Group.blocksRaycasts
                                                      + ":interactable=" + item.Group.interactable)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            return groups.Length == 0 ? "none" : string.Join("|", groups);
        }
        catch (Exception ex)
        {
            return "diagnostic-failed:" + ex.Message;
        }
    }

    private static string HouseInputSummary()
    {
        try
        {
            var house = GameApp.Instance?.HouseItem;
            if (house == null)
            {
                return "none";
            }

            return "active=" + house.activeInHierarchy
                   + ",mainHudRaycaster=" + RaycasterState(house.transform.Find("UIRoot/MainHudCanvas"))
                   + ",windowRaycaster=" + RaycasterState(house.transform.Find("UIRoot/WindowCanvas"));
        }
        catch (Exception ex)
        {
            return "diagnostic-failed:" + ex.Message;
        }
    }

    private static string RaycasterState(Transform? root)
    {
        if (root == null)
        {
            return "missing";
        }

        var raycaster = root.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        return raycaster == null ? "missing" : raycaster.enabled.ToString();
    }

    private static bool IsMenuInputInfrastructureReady()
    {
        try
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null
                || !eventSystem.enabled
                || eventSystem.currentInputModule == null
                || !eventSystem.currentInputModule.enabled)
            {
                return false;
            }

            var upper = Witch.UI.UIManager.Instance?.upperCanvasTf;
            var activeUpperCount = upper == null
                ? 0
                : upper.Cast<Transform>().Count(child => child != null && child.gameObject.activeInHierarchy);
            var expectedNativeRaycasters = activeUpperCount == 0;
            var mainRaycaster = GameObject.Find("Canvas")?.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (mainRaycaster != null && mainRaycaster.enabled != expectedNativeRaycasters)
            {
                return false;
            }

            var house = GameApp.Instance?.HouseItem;
            if (house != null && house.activeInHierarchy)
            {
                if (!RaycasterMatches(house.transform.Find("UIRoot/MainHudCanvas"), expectedNativeRaycasters)
                    || !RaycasterMatches(house.transform.Find("UIRoot/WindowCanvas"), expectedNativeRaycasters))
                {
                    return false;
                }
            }

            var camera = Camera.main;
            if (camera != null)
            {
                var physics = camera.GetComponent<PhysicsRaycaster>();
                var physics2D = camera.GetComponent<Physics2DRaycaster>();
                if ((physics != null && physics.enabled != expectedNativeRaycasters)
                    || (physics2D != null && physics2D.enabled != expectedNativeRaycasters))
                {
                    return false;
                }
            }

            return SceneManager.GetActiveScene()
                .GetRootGameObjects()
                .Where(root => root != null
                               && !string.Equals(root.name, "Upper Canvas", StringComparison.Ordinal))
                .Select(root => root.GetComponent<CanvasGroup>())
                .Where(group => group != null)
                .All(group => group.blocksRaycasts == expectedNativeRaycasters
                              && (!expectedNativeRaycasters || group.interactable));
        }
        catch
        {
            return false;
        }
    }

    private static bool RaycasterMatches(Transform? root, bool expected)
    {
        var raycaster = root?.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        return raycaster == null || raycaster.enabled == expected;
    }

    private static string EventSystemSummary()
    {
        try
        {
            var systems = Resources.FindObjectsOfTypeAll<EventSystem>();
            return "total=" + systems.Length
                   + ":active=" + systems.Count(system => system != null && system.isActiveAndEnabled)
                   + ":current=" + (EventSystem.current == null
                       ? "none"
                       : EventSystem.current.GetInstanceID().ToString());
        }
        catch (Exception ex)
        {
            return "diagnostic-failed:" + ex.Message;
        }
    }

    private static string CameraInputSummary()
    {
        try
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return "none";
            }

            var physics = camera.GetComponent<PhysicsRaycaster>();
            var physics2D = camera.GetComponent<Physics2DRaycaster>();
            return camera.name + "#" + camera.GetInstanceID()
                   + ":physics=" + (physics == null ? "missing" : physics.enabled.ToString())
                   + ":physics2D=" + (physics2D == null ? "missing" : physics2D.enabled.ToString());
        }
        catch (Exception ex)
        {
            return "diagnostic-failed:" + ex.Message;
        }
    }

    private static string SettingUiSummary()
    {
        try
        {
            var settings = Object.FindObjectsByType<SettingUI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            var registered = Witch.UI.UIManager.Instance?.GetUI<SettingUI>("SettingUI");
            return "instances=" + settings.Length
                   + ":registered=" + (registered == null ? "none" : registered.GetInstanceID().ToString())
                   + ":details=" + (settings.Length == 0
                       ? "none"
                       : string.Join("|", settings.Where(item => item != null).Select(DescribeSettingUi)));
        }
        catch (Exception ex)
        {
            return "diagnostic-failed:" + ex.Message;
        }
    }

    private static string DescribeSettingUi(SettingUI setting)
    {
        var group = setting.GetComponent<CanvasGroup>();
        return "#" + setting.GetInstanceID()
               + ":active=" + setting.gameObject.activeInHierarchy
               + ":alpha=" + (group == null ? "missing" : group.alpha.ToString("0.###"))
               + ":blocks=" + (group != null && group.blocksRaycasts)
               + ":interactable=" + (group != null && group.interactable);
    }

    private static string SelectedObjectSummary()
    {
        try
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected == null ? "none" : Path(selected.transform);
        }
        catch (Exception ex)
        {
            return "diagnostic-failed:" + ex.Message;
        }
    }

    private static string ManagedUiSummary()
    {
        try
        {
            var manager = Witch.UI.UIManager.Instance;
            var names = manager?.GetAllUI()
                .Where(ui => ui != null && ui.gameObject != null)
                .Select(ui => ui.gameObject.name + ":" + (ui.gameObject.activeInHierarchy ? "active" : "inactive"))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            return names == null || names.Length == 0 ? "none" : string.Join("|", names);
        }
        catch (Exception ex)
        {
            return "diagnostic-failed:" + ex.Message;
        }
    }

    private static string UpperChildrenSummary()
    {
        try
        {
            var upper = Witch.UI.UIManager.Instance?.upperCanvasTf;
            if (upper == null)
            {
                return "none";
            }

            var names = upper.Cast<Transform>()
                .Where(child => child != null && child.gameObject.activeInHierarchy)
                .Select(child => child.name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            return names.Length == 0 ? "none" : string.Join("|", names);
        }
        catch (Exception ex)
        {
            return "diagnostic-failed:" + ex.Message;
        }
    }

    private static string Path(Transform transform)
    {
        var names = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }
}
