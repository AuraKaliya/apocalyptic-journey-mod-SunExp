using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using SunExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;

namespace SunExp.Dll.Mechanics;

public sealed class SpiritOtherObj : OtherObj
{
    private static readonly object PresentationCacheLock = new();
    private static readonly Dictionary<string, Dictionary<string, string>> PresentationTemplates =
        new(StringComparer.Ordinal);
    private static Dictionary<string, string>? presentationAdapterData;

    private CompanionBattleState? battleState;

    public CapturedEnemySnapshot Snapshot { get; private set; } = new();

    public string OwnerStatusId { get; private set; } = "";

    public string OwnerPlayerId { get; private set; } = "";

    public override string Type => "Spirit";

    public bool InitSpirit(
        CapturedEnemySnapshot snapshot,
        string ownerStatusId,
        int slotIndex,
        CompanionStats stats,
        string ownerPlayerId = "",
        string statusId = "")
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.EnemyId))
        {
            return false;
        }

        Snapshot = snapshot;
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(OwnerStatusId, ownerPlayerId);
        var config = SpiritSummonService.CreateSpiritDataConfig(snapshot, stats);
        base.Init(config, 0f, Math.Max(0, slotIndex));
        Attack = stats.Attack;
        Defend = 0;
        MaxHp = 1;
        CurHp = 1;
        MaxActionCount = 1;
        ActionCount = 1;
        InstanceId = string.IsNullOrWhiteSpace(statusId) ? SpiritStateStore.NextStatusId() : statusId.Trim();
        gameObject.name = "SunExpSpirit:" + snapshot.DisplayName + ":" + InstanceId;
        var status = transform.gameObject.AddComponent<StatusManager>().Init(this) as StatusManager;
        if (status == null)
        {
            return false;
        }

        Status = status;
        battleState = CompanionBattleStateStore.Create(
            InstanceId,
            snapshot.ProfileKey,
            OwnerStatusId,
            slotIndex,
            stats,
            OwnerPlayerId,
            "SpiritAttachment");
        EnsureActionIcons();
        SpiritSummonService.RegisterFightState(this);
        dataConfig.scriptExecutor.Self = Status;
        dataConfig.scriptExecutor.SetStatus("Self");
        AddCardList();
        status.animatedState = IStatusManager.AnimatedState.Idle;
        InitBound(null, true);
        return true;
    }

    public void Activate(string source)
    {
        RefreshIntent(source);
    }

    public void ActivateAfterHydration(CompanionIntentPlan? authoritativePlan, string source)
    {
        var state = battleState ?? CompanionBattleStateStore.Find(InstanceId);
        if (state == null)
        {
            return;
        }

        if (authoritativePlan == null)
        {
            RefreshIntent(source);
            return;
        }

        state.CurrentPlan = authoritativePlan.Snapshot();
        state.CurrentIntentId = authoritativePlan.IntentId;
        RebuildAction(authoritativePlan.EnemyCardId, authoritativePlan.Priority);
        ActionCount = 1;
        SetAction();
        SpiritStateStore.NotifyIntentPresented(InstanceId, authoritativePlan);
        ShowAction();
    }

    public override IEnumerator DoAction()
    {
        if (!CompanionAuthorityService.IsAuthoritative() || !ProjectionEffectContextService.IsOwnerAvailable(battleState))
        {
            yield break;
        }

        FightManager.Instance?.ChangeUnit(FightType.Partner);
        if (Status.state == IStatusManager.State.Dead)
        {
            yield break;
        }

        CompanionThreatService.DecayForTurn(battleState);
        EventCenter.Instance.EventTrigger("StartRound" + Status.InstanceId, false);
        EventCenter.Instance.EventTrigger("StartRoundEnd" + Status.InstanceId, false);
        yield return new WaitForSeconds(0.5f);

        var plan = battleState?.CurrentPlan;
        if (plan == null || plan.IsWait || !CompanionIntentExecutor.CanExecute(plan))
        {
            RefreshIntent("DoAction.NoExecutableIntent");
            yield break;
        }

        HideAction();
        SpiritStateStore.NotifyActionPresented(InstanceId);
        var action = ActionCards != null && ActionCards.Count > 0 ? ActionCards[0] : null;
        ProjectionActionExecutor.Execute(this, battleState!, action);
        yield return new WaitForSeconds(1f);

        if (Status.CurHp > 0 && Status.state != IStatusManager.State.Dead && FightManager.Instance?.fightType != FightType.Loss)
        {
            battleState?.Stats.RecoverMagic(1);
            battleState?.AdvanceTurn();
            RefreshIntent("DoAction.CardUpdate");
        }
    }

    public override void AddCardList()
    {
        RebuildAction(SunExpIds.ProjectionActionStaffTapCardId, 1);
    }

    private void RefreshIntent(string source)
    {
        try
        {
            var state = battleState ?? CompanionBattleStateStore.Find(InstanceId);
            if (state == null || Status == null || Status.state == IStatusManager.State.Dead)
            {
                return;
            }

            var plan = CompanionIntentPlanner.Create(this, state);
            CompanionIntentPlanner.Commit(state, plan);
            RebuildAction(plan.EnemyCardId, plan.Priority);
            ActionCount = 1;
            SetAction();
            SpiritStateStore.NotifyIntentPresented(InstanceId, plan);
            ShowAction();
            SunExpLog.Debug("[Spirit] intent refreshed from " + source + ": " + plan.IntentId);
            SpiritSummonService.BroadcastRuntimeState(this, source);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Spirit] intent refresh failed from " + source + ": " + ex.Message);
        }
    }

    private void RebuildAction(string cardId, int priority)
    {
        var started = SunExpPerformanceCounters.Timestamp();
        FightAction = new ObjectAction(this);
        var card = new ObjectCard { status = Status as StatusManager };
        var sourceCardId = string.IsNullOrWhiteSpace(cardId)
            ? SunExpIds.ProjectionActionWaitCardId
            : cardId.Trim();
        var presentationData = PresentationDataFor(sourceCardId);
        var adapterHandle = AuraGameDataHostApi.ResolveHandle(DataType.EnemyCard, SunExpIds.SpiritIntentAdapterCardId)
            ?? throw new InvalidOperationException("Spirit intent adapter definition is not registered.");
        var materialized = AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
        {
            Definition = adapterHandle,
            DataOverrides = presentationData
                .Where(pair => !string.Equals(pair.Key, "Id", StringComparison.Ordinal)
                    && pair.Key.IndexOf("Script", StringComparison.OrdinalIgnoreCase) < 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Vars = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SunExpIds.SpiritIntentSourceCardVar] = sourceCardId,
                ["CD"] = "0",
                ["priority"] = Math.Max(1, priority).ToString()
            }
        });
        var config = materialized.Instance as DataConfig
            ?? throw new InvalidOperationException("Spirit intent materialization failed: " + materialized.Message);
        card.Init(config);
        VerifyPresentationBinding(config, sourceCardId);
        FightAction.AddCard(card);
        SunExpPerformanceCounters.RecordHotspot(
            "Spirit.Intent.PresentationBuild",
            started,
            "card=" + cardId + ", intent=" + (battleState?.CurrentPlan?.IntentId ?? "<none>"),
            logFirstSample: true);
    }

    private static Dictionary<string, string> PresentationDataFor(string cardId)
    {
        var key = string.IsNullOrWhiteSpace(cardId) ? SunExpIds.ProjectionActionWaitCardId : cardId.Trim();
        lock (PresentationCacheLock)
        {
            if (!PresentationTemplates.TryGetValue(key, out var template))
            {
                var source = AuraGameDataHostApi.CopyRow(DataType.EnemyCard, key)
                    ?? throw new InvalidOperationException("Spirit source intent definition is not registered: " + key);
                presentationAdapterData ??= AuraGameDataHostApi.CopyRow(
                    DataType.EnemyCard,
                    SunExpIds.SpiritIntentAdapterCardId)
                    ?? throw new InvalidOperationException("Spirit intent adapter definition is not registered.");
                template = SpiritIntentPresentationDataComposer.Compose(source, presentationAdapterData);
                PresentationTemplates[key] = template;
            }

            return new Dictionary<string, string>(template);
        }
    }

    private void VerifyPresentationBinding(DataConfig config, string sourceCardId)
    {
        var expectedPlanId = battleState?.CurrentPlan?.PlanId;
        if (string.IsNullOrWhiteSpace(expectedPlanId))
        {
            return;
        }

        var presentedPlanId = DictionaryUtil.Get(config.Vars, CompanionIntentExecutor.PresentedPlanVar);
        if (string.Equals(presentedPlanId, expectedPlanId, StringComparison.Ordinal))
        {
            return;
        }

        SunExpLog.Warn("[SpiritIntentPresentationAdapter] binding failed: status=" + InstanceId
            + ", sourceCard=" + sourceCardId
            + ", runtimeCard=" + DictionaryUtil.Get(config.Vars, "Id")
            + ", expectedPlan=" + expectedPlanId
            + ", presentedPlan=" + (string.IsNullOrWhiteSpace(presentedPlanId) ? "<none>" : presentedPlanId));
    }

    private void EnsureActionIcons()
    {
        var status = Status as StatusManager;
        var content = status?.actionContent?.transform.Find("content");
        if (status == null || content == null)
        {
            return;
        }

        for (var i = 0; i < 4; i++)
        {
            if (status.actionObj[i] != null && status.actionText[i] != null)
            {
                continue;
            }

            var icon = UIManager.Instance.CreateActionIcon();
            icon.transform.SetParent(content);
            icon.transform.localScale = Vector3.one;
            icon.transform.localPosition = Vector3.zero;
            icon.transform.Find("Icon").GetComponent<Image>().color = Color.white;
            var keyword = icon.AddComponent<KeywordDisplay>();
            keyword.type = "Action";
            icon.SetActive(false);
            status.actionObj[i] = icon;
            status.actionText[i] = keyword;
            var valueText = icon.transform.Find("Icon/val")?.GetComponent<TMP_Text>();
            if (valueText != null)
            {
                valueText.text = "";
            }
        }
    }
}
