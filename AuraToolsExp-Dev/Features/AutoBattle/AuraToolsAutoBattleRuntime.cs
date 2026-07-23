using System;
using System.Collections.Generic;
using System.IO;
using AuraCombatAi.Shared;
using AuraCombatAi.Shared.GameApi;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using Michsky.MUIP;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.AutoBattle;

public static class AuraToolsAutoBattleRuntime
{
    private const string HandlerId = "AutoBattle";
    private static bool initialized;
    private static AuraToolsAutoBattleController? controller;
    private static IDisposable? lifecycleSubscription;
    private static IDisposable? trainingSinkRegistration;

    internal static bool ModuleEnabled =>
        AuraToolsConfigService.Root.MatchExperience.Enabled
        && AuraToolsConfigService.MatchExperience.AutoBattle.Enabled;

    public static bool Active => controller != null && controller.Active;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        EnsureController();
        AuraToolsHookRegistry.After(
            modConfig,
            "DeckUI.CreateDeckMenuForSelect",
            WitchCombatInteractionRuntime.ObserveDeckPrompt,
            HandlerId);
        AuraToolsHookRegistry.Before(
            modConfig,
            "FightUI.SelectCardToAction",
            WitchCombatInteractionRuntime.ObserveHandPrompt,
            HandlerId);
        lifecycleSubscription = AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            HandlerId,
            new AuraBattleLifecycleSubscription
            {
                FightStarting = _ => ResetForBattle(),
                FightStarted = _ => ResetForBattle(),
                FightEnding = _ => EndBattle(),
                FightEnded = _ => EndBattle()
            },
            AuraToolsLog.Info,
            AuraToolsLog.Warn);
        trainingSinkRegistration = CombatAiRegistry.RegisterTrainingSink(
            AuraToolsIds.ModId,
            "JsonLinesV1",
            new AuraToolsAutoBattleTrainingSink());
        AuraToolsConfigService.Changed += OnConfigurationChanged;
    }

    public static void SetActive(bool active)
    {
        EnsureController().SetActive(active);
    }

    private static void OnConfigurationChanged()
    {
        EnsureController().ApplyConfiguration();
    }

    private static void ResetForBattle()
    {
        WitchCombatInteractionRuntime.Reset();
        EnsureController().ResetForBattle(
            ModuleEnabled && AuraToolsConfigService.MatchExperience.AutoBattle.StartActive);
    }

    private static void EndBattle()
    {
        WitchCombatInteractionRuntime.Reset();
        controller?.EndBattle();
    }

    private static AuraToolsAutoBattleController EnsureController()
    {
        if (controller != null)
        {
            return controller;
        }

        var host = new GameObject("AuraToolsAutoBattleRuntime");
        Object.DontDestroyOnLoad(host);
        controller = host.AddComponent<AuraToolsAutoBattleController>();
        return controller;
    }
}

internal sealed class AuraToolsAutoBattleController : MonoBehaviour
{
    private const string ButtonName = "AuraToolsAutoBattleButton";
    private readonly WitchCombatRuntime runtime = new();
    private readonly CombatDecisionEngine decisionEngine = new();
    private GameObject? buttonRoot;
    private ButtonManager? buttonManager;
    private FightUI? buttonOwner;
    private bool waitingForSettlement;
    private float actionStartedAt;
    private float nextDecisionAt;
    private float nextUiProbeAt;
    private CombatStateObservation? beforeAction;
    private CombatDecision? pendingDecision;

    public bool Active { get; private set; }

    public void SetActive(bool active)
    {
        Active = active && AuraToolsAutoBattleRuntime.ModuleEnabled;
        waitingForSettlement = false;
        beforeAction = null;
        pendingDecision = null;
        nextDecisionAt = Time.unscaledTime + 0.15f;
        UpdateButtonLabel();
    }

    public void ResetForBattle(bool startActive)
    {
        SetActive(startActive);
        DestroyButton();
        nextUiProbeAt = 0f;
    }

    public void EndBattle()
    {
        Active = false;
        waitingForSettlement = false;
        beforeAction = null;
        pendingDecision = null;
        DestroyButton();
    }

    public void ApplyConfiguration()
    {
        if (!AuraToolsAutoBattleRuntime.ModuleEnabled)
        {
            SetActive(false);
            DestroyButton();
        }

        UpdateButtonLabel();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextUiProbeAt)
        {
            nextUiProbeAt = Time.unscaledTime + 0.5f;
            RefreshButton();
        }

        var interaction = WitchCombatInteractionRuntime.TryResolve(Active);
        if (interaction == WitchInteractionResolveResult.Pending)
        {
            return;
        }

        if (!Active || !AuraToolsAutoBattleRuntime.ModuleEnabled)
        {
            return;
        }

        if (waitingForSettlement)
        {
            ObserveSettlement();
            return;
        }

        if (Time.unscaledTime < nextDecisionAt)
        {
            return;
        }

        DecideAndExecute();
    }

    private void DecideAndExecute()
    {
        if (!runtime.TryCapture(out var state, out _)
            || !state.IsPlayerActionWindow
            || state.UiBusy)
        {
            nextDecisionAt = Time.unscaledTime + 0.2f;
            return;
        }

        var decision = decisionEngine.Choose(state, BuildProfile());
        if (!decision.HasAction || decision.Action == null)
        {
            StopWithReason("没有可执行的合法动作");
            return;
        }

        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        if (string.Equals(settings.UnknownActionPolicy, "handoff", StringComparison.OrdinalIgnoreCase)
            && decision.Action.Semantics.Uncertainty >= 1.5d)
        {
            StopWithReason("遇到未识别动作，已交还玩家");
            return;
        }

        var execution = runtime.Execute(decision.Action);
        if (!execution.Accepted)
        {
            StopWithReason(execution.Message);
            return;
        }

        beforeAction = state;
        pendingDecision = decision;
        waitingForSettlement = true;
        actionStartedAt = Time.unscaledTime;
        nextDecisionAt = Time.unscaledTime
                         + AuraToolsConfigService.MatchExperience.AutoBattle.DecisionIntervalMs / 1000f;
        AuraToolsLog.Debug(
            "[AutoBattle] " + decision.Action.CandidateId
            + " score=" + decision.Score.ToString("0.00")
            + " reason=" + decision.Reason);
    }

    private void ObserveSettlement()
    {
        var elapsed = Time.unscaledTime - actionStartedAt;
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var timeout = pendingDecision?.Action?.Kind == CombatActionKind.EndTurn
            ? Math.Max(60f, settings.ActionTimeoutSeconds)
            : settings.ActionTimeoutSeconds;
        if (elapsed > timeout)
        {
            StopWithReason("动作等待超时");
            return;
        }

        if (elapsed < settings.DecisionIntervalMs / 1000f
            || WitchCombatInteractionRuntime.HasActivePrompt)
        {
            return;
        }

        if (!runtime.TryCapture(out var after, out _)
            || after.UiBusy
            || !after.IsPlayerActionWindow)
        {
            return;
        }

        if (beforeAction != null && pendingDecision?.Action != null)
        {
            RecordTrainingSample(beforeAction, after, pendingDecision);
        }

        waitingForSettlement = false;
        beforeAction = null;
        pendingDecision = null;
        nextDecisionAt = Time.unscaledTime + settings.DecisionIntervalMs / 1000f;
    }

    private static void RecordTrainingSample(
        CombatStateObservation before,
        CombatStateObservation after,
        CombatDecision decision)
    {
        if (!AuraToolsConfigService.MatchExperience.AutoBattle.CaptureTrainingSamples
            || decision.Action == null)
        {
            return;
        }

        var beforeEnemyHp = SumEnemyHp(before);
        var afterEnemyHp = SumEnemyHp(after);
        var reward = (double)(beforeEnemyHp - afterEnemyHp);
        reward += (after.Player.CurrentHp - before.Player.CurrentHp) * 1.2d;
        reward += (after.Player.Defend - before.Player.Defend) * 0.25d;
        reward += (after.CurrentPower - before.CurrentPower) * 0.2d;
        CombatAiRegistry.RecordTrainingSample(new CombatTrainingSample
        {
            BattleSessionId = before.BattleSessionId,
            Sequence = before.Sequence,
            StateFingerprint = before.Fingerprint,
            CandidateId = decision.Action.CandidateId,
            Features = new Dictionary<string, double>(decision.Action.Features, StringComparer.OrdinalIgnoreCase),
            PredictedScore = decision.Score,
            Reward = reward,
            Terminal = after.Enemies.Count == 0
        });
    }

    private static int SumEnemyHp(CombatStateObservation state)
    {
        var total = 0;
        for (var i = 0; i < state.Enemies.Count; i++)
        {
            total += Math.Max(0, state.Enemies[i].CurrentHp);
        }

        return total;
    }

    private CombatDecisionProfile BuildProfile()
    {
        var settings = AuraToolsConfigService.MatchExperience.AutoBattle;
        var profile = new CombatDecisionProfile();
        switch (settings.Profile)
        {
            case "aggressive":
                profile.Id = "aggressive";
                profile.Weights.Lethal = 2.1d;
                profile.Weights.Tempo = 1.25d;
                profile.Weights.Survival = 0.85d;
                break;

            case "defensive":
                profile.Id = "defensive";
                profile.Weights.Survival = 1.9d;
                profile.Weights.Risk = -1.6d;
                profile.Weights.Lethal = 1.15d;
                break;
        }

        if (string.Equals(settings.UnknownActionPolicy, "allow", StringComparison.OrdinalIgnoreCase))
        {
            profile.UnknownActionPenalty = 0.35d;
        }

        return profile;
    }

    private void RefreshButton()
    {
        if (!AuraToolsAutoBattleRuntime.ModuleEnabled)
        {
            DestroyButton();
            return;
        }

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null || !fightUi.gameObject.activeInHierarchy || fightUi.turnButton == null)
        {
            DestroyButton();
            return;
        }

        if (buttonRoot != null && buttonOwner == fightUi)
        {
            buttonRoot.SetActive(true);
            return;
        }

        DestroyButton();
        var native = fightUi.turnButton;
        var result = AuraUiNativeButtonCloneAdapter.TryClone(new AuraUiNativeButtonCloneRequest
        {
            Template = native,
            Parent = native.transform.parent,
            CloneName = ButtonName,
            Label = ButtonLabel(),
            TextSizeOverride = 18f,
            MinimumTextSizeOverride = 12f,
            OnClick = ToggleActive
        });
        if (!result.Success || result.Root == null)
        {
            AuraToolsLog.Warn("[AutoBattle] failed to create battle button: " + result.FailureReason);
            return;
        }

        buttonRoot = result.Root;
        buttonManager = result.Manager as ButtonManager;
        buttonOwner = fightUi;
        PositionButton(native.transform, buttonRoot.transform);
        buttonRoot.SetActive(true);
    }

    private void ToggleActive()
    {
        SetActive(!Active);
    }

    private void UpdateButtonLabel()
    {
        if (buttonManager == null)
        {
            return;
        }

        buttonManager.SetText(ButtonLabel());
        buttonManager.UpdateUI();
        buttonRoot?.GetComponent<AuraUiNativeButtonLabelOwner>()?.Configure(
            buttonManager,
            ButtonLabel(),
            18f,
            12f);
    }

    private string ButtonLabel()
    {
        return Active ? "自动战斗：开" : "自动战斗：关";
    }

    private static void PositionButton(Transform native, Transform clone)
    {
        clone.SetSiblingIndex(native.GetSiblingIndex() + 1);
        if (native is not RectTransform nativeRect || clone is not RectTransform cloneRect)
        {
            return;
        }

        var width = Mathf.Max(120f, Mathf.Abs(nativeRect.rect.width), Mathf.Abs(nativeRect.sizeDelta.x));
        cloneRect.anchorMin = nativeRect.anchorMin;
        cloneRect.anchorMax = nativeRect.anchorMax;
        cloneRect.pivot = nativeRect.pivot;
        cloneRect.sizeDelta = nativeRect.sizeDelta;
        cloneRect.anchoredPosition = nativeRect.anchoredPosition + Vector2.left * (width + 12f);
    }

    private void StopWithReason(string reason)
    {
        AuraToolsLog.Warn("[AutoBattle] stopped: " + reason);
        SetActive(false);
    }

    private void DestroyButton()
    {
        if (buttonRoot != null)
        {
            buttonRoot.SetActive(false);
            Object.Destroy(buttonRoot);
        }

        buttonRoot = null;
        buttonManager = null;
        buttonOwner = null;
    }
}

internal sealed class AuraToolsAutoBattleTrainingSink : ICombatTrainingSampleSink
{
    private readonly object gate = new();

    public void Record(CombatTrainingSample sample)
    {
        if (!AuraToolsConfigService.MatchExperience.AutoBattle.CaptureTrainingSamples)
        {
            return;
        }

        try
        {
            var path = AuraSharedLogStore.OwnerLogPath(
                AuraToolsIds.ModId,
                "auto-battle-training-v1.jsonl");
            var line = AuraSharedJson.Serialize(sample);
            lock (gate)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[AutoBattle] failed to record training sample: " + ex.Message);
        }
    }
}
