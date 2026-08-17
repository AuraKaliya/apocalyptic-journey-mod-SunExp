using System;
using AuraToolsExp.Dll.GameApi;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Owns the real native ChatUI needed by RpcSendChat while making it invisible
/// and non-interactive inside the replay-only local host.
/// </summary>
internal static class MatchReplayChatUiLeaseRuntime
{
    private static ChatUI? leasedChatUi;
    private static bool quarantined;
    private static bool preservedAfterReplay;
    private static bool childStateCaptured;
    private static bool inputWasActive;
    private static bool outputWasActive;
    private static GameObject? closingChatRoot;

    internal static bool IsNativeChatReady => ResolveNativeChatUi() != null;

    internal static bool IsClosing
    {
        get
        {
            PruneClosingRoot();
            return closingChatRoot != null;
        }
    }

    internal static GameObject? ClosingRoot
    {
        get
        {
            PruneClosingRoot();
            return closingChatRoot;
        }
    }

    internal static GameObject? TrackedRoot
    {
        get
        {
            PruneClosingRoot();
            if (closingChatRoot != null)
            {
                return closingChatRoot;
            }

            var chatUi = leasedChatUi ?? ResolveNativeChatUi();
            return chatUi == null ? null : chatUi.gameObject;
        }
    }

    internal static void BeginReplay(string source)
    {
        PruneClosingRoot();
        var existing = ResolveNativeChatUi();
        if (existing == null)
        {
            leasedChatUi = null;
            quarantined = false;
            preservedAfterReplay = false;
            AuraToolsLog.Debug("[MatchRecords] replay ChatUI lease awaiting native panel: source="
                               + source + ".");
            return;
        }

        AttachAndQuarantine(existing, source);
    }

    internal static void OnNativeChatPanelReady(string source)
    {
        var chatUi = ResolveNativeChatUi();
        if (MatchReplaySessionState.IsPlayback || MatchReplayLocalHostRuntime.OwnsHost)
        {
            if (chatUi == null)
            {
                AuraToolsLog.Warn("[MatchRecords] native ChatUI was not created before local lobby join: source="
                                  + source + ".");
                return;
            }

            AttachAndQuarantine(chatUi, source);
            return;
        }

        if (preservedAfterReplay && chatUi != null)
        {
            RestoreForNativeSession(chatUi, source);
        }
    }

    internal static void ReassertQuarantine(string source)
    {
        if (!quarantined || leasedChatUi == null)
        {
            return;
        }

        EnforceQuarantine(leasedChatUi, source, log: false);
    }

    internal static void FinalizeAfterNetworkStop(string source)
    {
        var chatUi = leasedChatUi ?? ResolveNativeChatUi();
        if (chatUi == null)
        {
            ResetLease();
            AuraToolsLog.Debug("[MatchRecords] replay ChatUI lease finalized: source="
                               + source + ", mode=absent.");
            return;
        }

        var networkStopped = MatchReplayLocalHostRuntime.IsStopped;
        var callbacksDetached = false;
        var detachDetail = "not-attempted";
        if (networkStopped)
        {
            callbacksDetached = ChatUiLifecycleApi.TryDetachInputCallbacks(chatUi, out detachDetail);
        }

        var mode = MatchReplayChatUiLifecyclePolicy.ResolveFinalization(
            networkStopped,
            callbacksDetached);
        if (mode == MatchReplayChatUiFinalizationModes.WaitForNetwork)
        {
            AttachAndQuarantine(chatUi, source + " deferred");
            AuraToolsLog.Warn("[MatchRecords] replay ChatUI finalization deferred until network stop: source="
                              + source + ".");
            return;
        }

        if (mode == MatchReplayChatUiFinalizationModes.PreserveQuarantined)
        {
            leasedChatUi = chatUi;
            preservedAfterReplay = true;
            quarantined = true;
            EnforceQuarantine(chatUi, source + " preserved", log: false);
            AuraToolsLog.Warn("[MatchRecords] replay ChatUI preserved in non-blocking quarantine because input callback detachment was unavailable: source="
                              + source + ", detail=" + detachDetail + ".");
            return;
        }

        var root = chatUi.gameObject;
        closingChatRoot = root;
        MatchReplayUiLifecycle.RequestCloseNativeUi(chatUi, source);
        if (ReferenceEquals(ChatUI.Instance, chatUi))
        {
            ChatUI.Instance = null;
        }

        ResetLease();
        AuraToolsLog.Debug("[MatchRecords] replay ChatUI lease finalized: source=" + source
                           + ", mode=native-close-requested, instance=" + root.GetInstanceID()
                           + ", detail=" + detachDetail + ".");
    }

    internal static void ForceFinalizeAfterTimeout(string source)
    {
        PruneClosingRoot();
        var chatUi = leasedChatUi ?? ResolveNativeChatUi();
        var root = closingChatRoot ?? chatUi?.gameObject;
        if (chatUi != null)
        {
            ChatUiLifecycleApi.TryDetachInputCallbacks(chatUi, out _);
            if (ReferenceEquals(ChatUI.Instance, chatUi))
            {
                ChatUI.Instance = null;
            }
        }

        ResetLease();
        if (root == null)
        {
            closingChatRoot = null;
            return;
        }

        closingChatRoot = root;
        MatchReplayUiLifecycle.ForceDestroyRoot(root, source);
    }

    internal static string Describe()
    {
        PruneClosingRoot();
        var chatUi = leasedChatUi;
        if (chatUi == null)
        {
            return closingChatRoot == null
                ? "none"
                : "closing:#" + closingChatRoot.GetInstanceID();
        }

        var group = chatUi.GetComponent<CanvasGroup>();
        return "instance=" + chatUi.gameObject.GetInstanceID()
               + ", quarantined=" + quarantined
               + ", preserved=" + preservedAfterReplay
               + ", active=" + chatUi.gameObject.activeInHierarchy
               + ", blocksRaycasts=" + (group != null && group.blocksRaycasts)
               + ", selected=" + IsSelectionWithin(chatUi.gameObject);
    }

    internal static bool ShouldEnforce(ChatUI chatUi)
    {
        return quarantined && leasedChatUi != null && leasedChatUi == chatUi;
    }

    private static void AttachAndQuarantine(ChatUI chatUi, string source)
    {
        if (leasedChatUi == null || leasedChatUi != chatUi || !childStateCaptured)
        {
            var input = chatUi.transform.Find("Input");
            var output = chatUi.transform.Find("Output");
            inputWasActive = input != null && input.gameObject.activeSelf;
            outputWasActive = output != null && output.gameObject.activeSelf;
            childStateCaptured = true;
        }

        leasedChatUi = chatUi;
        quarantined = true;
        preservedAfterReplay = false;
        var driver = chatUi.GetComponent<MatchReplayChatUiQuarantineDriver>()
                     ?? chatUi.gameObject.AddComponent<MatchReplayChatUiQuarantineDriver>();
        driver.Bind(chatUi);
        EnforceQuarantine(chatUi, source, log: true);
    }

    private static void RestoreForNativeSession(ChatUI chatUi, string source)
    {
        quarantined = false;
        preservedAfterReplay = false;
        var driver = chatUi.GetComponent<MatchReplayChatUiQuarantineDriver>();
        if (driver != null)
        {
            driver.Release();
        }

        try
        {
            chatUi.gameObject.SetActive(true);
            var group = chatUi.GetComponent<CanvasGroup>()
                        ?? chatUi.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
            chatUi.isOpen = false;
            chatUi.Show();
            var input = chatUi.transform.Find("Input");
            if (input != null)
            {
                input.gameObject.SetActive(inputWasActive);
            }

            var output = chatUi.transform.Find("Output");
            if (output != null)
            {
                output.gameObject.SetActive(outputWasActive);
            }

            AuraToolsLog.Info("[MatchRecords] preserved ChatUI restored for a native session: source="
                              + source + ".");
        }
        catch (Exception ex)
        {
            leasedChatUi = chatUi;
            quarantined = true;
            preservedAfterReplay = true;
            EnforceQuarantine(chatUi, source + " restore failed", log: false);
            AuraToolsLog.Warn("[MatchRecords] preserved ChatUI restore degraded: " + ex.Message);
            return;
        }

        leasedChatUi = null;
    }

    private static void EnforceQuarantine(ChatUI chatUi, string source, bool log)
    {
        if (chatUi == null || chatUi.gameObject == null)
        {
            return;
        }

        var root = chatUi.gameObject;
        if (!root.activeSelf)
        {
            root.SetActive(true);
        }

        var group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        chatUi.isOpen = false;

        var input = chatUi.transform.Find("Input")?.GetComponent<TMP_InputField>();
        if (input != null)
        {
            input.text = "";
            input.gameObject.SetActive(false);
        }

        var output = chatUi.transform.Find("Output");
        if (output != null)
        {
            output.gameObject.SetActive(false);
        }

        ClearSelectionWithin(root);
        if (log)
        {
            AuraToolsLog.Debug("[MatchRecords] native ChatUI leased in non-blocking quarantine: source="
                               + source + ", instance=" + root.GetInstanceID() + ".");
        }
    }

    private static ChatUI? ResolveNativeChatUi()
    {
        var managed = WitchUiManager.Instance?.GetUI<ChatUI>("ChatUI");
        if (managed != null && managed.gameObject != null)
        {
            return managed;
        }

        return ChatUI.Instance != null && ChatUI.Instance.gameObject != null
            ? ChatUI.Instance
            : null;
    }

    private static void ResetLease()
    {
        leasedChatUi = null;
        quarantined = false;
        preservedAfterReplay = false;
        childStateCaptured = false;
        inputWasActive = false;
        outputWasActive = false;
    }

    private static void PruneClosingRoot()
    {
        if (closingChatRoot == null)
        {
            closingChatRoot = null;
        }
    }

    private static void ClearSelectionWithin(GameObject root)
    {
        var eventSystem = EventSystem.current;
        var selected = eventSystem?.currentSelectedGameObject;
        if (eventSystem == null || selected == null)
        {
            return;
        }

        if (selected == root || selected.transform.IsChildOf(root.transform))
        {
            eventSystem.SetSelectedGameObject(null);
        }
    }

    private static bool IsSelectionWithin(GameObject root)
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        return selected != null
               && (selected == root || selected.transform.IsChildOf(root.transform));
    }
}

internal sealed class MatchReplayChatUiQuarantineDriver : MonoBehaviour
{
    private ChatUI? chatUi;

    internal void Bind(ChatUI target)
    {
        chatUi = target;
        enabled = true;
    }

    internal void Release()
    {
        enabled = false;
        chatUi = null;
        Destroy(this);
    }

    private void LateUpdate()
    {
        if (chatUi == null || !MatchReplayChatUiLeaseRuntime.ShouldEnforce(chatUi))
        {
            return;
        }

        MatchReplayChatUiLeaseRuntime.ReassertQuarantine("late-update");
    }
}
