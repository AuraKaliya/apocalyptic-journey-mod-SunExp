using System;
using System.Collections;
using AuraGameData.Shared.GameApi;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;

namespace SunExp.Dll.Mechanics;

public sealed class ProjectionOtherObj : OtherObj
{
    private CompanionBattleState? battleState;
    public string RoleId { get; private set; } = "";

    public string OwnerStatusId { get; private set; } = "";

    public string OwnerPlayerId { get; private set; } = "";

    public override string Type => "Projection";

    public bool InitProjection(PolymorphRoleSpec role, string ownerStatusId, int slotIndex, CompanionStats stats, string statusId = "", string ownerPlayerId = "")
    {
        if (role == null)
        {
            return false;
        }

        RoleId = role.Id;
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(OwnerStatusId, ownerPlayerId);
        dataConfig = ProjectionSummonService.CreateProjectionDataConfig(role, stats);
        data = dataConfig.data;
        FightAction = new ObjectAction(this);
        ApplyEnemyMaterial();

        Attack = stats.Attack;
        Defend = 0;
        MaxHp = 1;
        CurHp = 1;
        MaxActionCount = 1;
        ActionCount = MaxActionCount;
        InstanceId = string.IsNullOrWhiteSpace(statusId) ? ProjectionStateStore.NextStatusId() : statusId.Trim();
        battleState = CompanionBattleStateStore.Create(InstanceId, role.Id, OwnerStatusId, slotIndex, stats, OwnerPlayerId);
        gameObject.name = data.Localize("Name") + InstanceId;
        var status = transform.gameObject.AddComponent<StatusManager>().Init(this) as StatusManager;
        if (status == null)
        {
            return false;
        }

        Status = status;
        EnsureActionIcons();
        ProjectionSummonService.RegisterFightState(this, "ProjectionOtherObj.InitProjection");
        dataConfig.scriptExecutor.Self = Status;
        dataConfig.scriptExecutor.SetStatus("Self");
        AddCardList();
        status.animatedState = IStatusManager.AnimatedState.Idle;
        if (GameApp.Instance.NowBackground != null && GameApp.Instance.NowBackground.name == "BalancedHolySee")
        {
            CanRef();
        }

        InitBound(null, true);
        return true;
    }

    public void ActivateAfterHydration(CompanionIntentPlan? authoritativePlan, string source)
    {
        var state = battleState ?? CompanionBattleStateStore.Find(InstanceId);
        if (state == null)
        {
            return;
        }

        if (authoritativePlan != null)
        {
            state.CurrentPlan = authoritativePlan.Snapshot();
            state.CurrentIntentId = authoritativePlan.IntentId;
            RebuildProjectionAction(authoritativePlan.EnemyCardId, authoritativePlan.Priority);
            ShowCommittedPlan();
            return;
        }

        RefreshProjectionIntent(source);
    }

    public override IEnumerator DoAction()
    {
        if (!CompanionAuthorityService.IsAuthoritative())
        {
            yield break;
        }

        if (!ProjectionEffectContextService.IsOwnerAvailable(battleState))
        {
            yield break;
        }

        FightManager.Instance?.ChangeUnit(FightType.Partner);
        if (Status.state == IStatusManager.State.Dead)
        {
            yield break;
        }

        TriggerProjectionStartRound();
        yield return new WaitForSeconds(0.5f);
        if (Status.state == IStatusManager.State.Dead)
        {
            yield break;
        }

        if (Status.state == IStatusManager.State.NoAction)
        {
            Status.ChangeState(IStatusManager.State.Default);
            yield break;
        }

        EnsureProjectionIntentForTurn();
        if (battleState?.CurrentPlan?.IsWait == true)
        {
            HideAction();
        }
        for (var index = 0; ActionCards != null && index < ActionCards.Count; index++)
        {
            if (battleState?.CurrentPlan?.IsWait == true)
            {
                break;
            }

            if (!ExecuteProjectionAction(index))
            {
                yield break;
            }

            if (Status.state == IStatusManager.State.NoAction || Status.state == IStatusManager.State.Dead)
            {
                yield break;
            }

            yield return new WaitForSeconds(1f);
        }

        if (Status.CurHp > 0
            && Status.state != IStatusManager.State.Dead
            && FightManager.Instance != null
            && FightManager.Instance.fightType != FightType.Loss)
        {
            battleState?.Stats.RecoverMagic(1);
            battleState?.AdvanceTurn();
            RefreshProjectionIntent("DoAction.CardUpdate");
        }
    }

    public override void AddCardList()
    {
        RebuildProjectionAction(SunExpIds.ProjectionActionStaffTapCardId, 1);
    }

    private void RebuildProjectionAction(string cardId, int priority)
    {
        FightAction = new ObjectAction(this);
        var objectCard = new ObjectCard
        {
            status = Status as StatusManager
        };
        var actionConfig = AuraGameDataHostApi.Materialize(DataType.EnemyCard, cardId).Instance as DataConfig;
        if (actionConfig == null)
        {
            return;
        }
        NormalizeProjectionActionConfig(actionConfig, priority);
        objectCard.Init(actionConfig);
        NormalizeProjectionActionConfig(objectCard.dataConfig, priority);
        FightAction.AddCard(objectCard);
    }

    private static void NormalizeProjectionActionConfig(DataConfig? config, int priority)
    {
        if (config?.Vars == null)
        {
            return;
        }

        DictionaryUtil.Set(config.Vars, "CD", "0");
        DictionaryUtil.Set(config.Vars, "priority", Math.Max(1, priority).ToString());
    }

    private bool ExecuteProjectionAction(int index)
    {
        if (!CanProjectionAct())
        {
            return false;
        }

        try
        {
            var action = ActionCards != null && index < ActionCards.Count ? ActionCards[index] : null;
            if (action == null)
            {
                RefreshProjectionIntent("DoAction.MissingAction");
            }

            var plan = battleState?.CurrentPlan;
            if (plan == null || plan.IsWait)
            {
                return false;
            }

            if (!CompanionIntentExecutor.CanExecute(plan))
            {
                SunExpLog.Debug("[Projection] committed plan is no longer executable: " + plan.PlanId);
                return true;
            }

            HideAction();
            ProjectionStateStore.NotifyActionPresented(InstanceId);
            return battleState != null && ProjectionActionExecutor.Execute(this, battleState, action);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("[Projection] action execution failed: status=" + InstanceId + ", index=" + index, ex);
            return false;
        }
    }

    private void RefreshProjectionIntent(string source)
    {
        try
        {
            if (FightAction == null || Status == null || Status.state == IStatusManager.State.Dead)
            {
                return;
            }

            var state = battleState ?? CompanionBattleStateStore.Find(InstanceId);
            if (state != null)
            {
                var plan = CompanionIntentPlanner.Create(this, state);
                CompanionIntentPlanner.Commit(state, plan);
                RebuildProjectionAction(plan.EnemyCardId, plan.Priority);
            }

            ShowCommittedPlan();
            ProjectionSummonService.BroadcastRuntimeState(this, source);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Projection] intent refresh failed from " + source + ": " + ex.Message);
        }
    }

    public void RefreshCommittedIntentValues(string source)
    {
        try
        {
            var state = battleState ?? CompanionBattleStateStore.Find(InstanceId);
            if (state?.CurrentPlan == null || state.CurrentPlan.IsWait)
            {
                return;
            }

            state.CurrentPlan = ProjectionEffectContextService.RefreshLockedPlan(this, state, state.CurrentPlan);
            var intent = CompanionIntentResolver.Find(state, state.CurrentPlan.IntentId);
            if (intent != null)
            {
                var repeatCount = state.CurrentPlan.ResolvedEffects.Count == 0
                    ? 1
                    : state.CurrentPlan.ResolvedEffects[0].RepeatCount;
                CompanionThreatService.SetPreview(
                    state,
                    intent,
                    state.CurrentPlan.ResolvedValue,
                    repeatCount);
            }
            RebuildProjectionAction(state.CurrentPlan.EnemyCardId, state.CurrentPlan.Priority);
            ShowCommittedPlan();
            ProjectionSummonService.BroadcastRuntimeState(this, source);
            SunExpPerformanceCounters.Record("ProjectionIntent.OwnerModifierRefresh");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Projection] committed intent refresh failed from " + source + ": " + ex.Message);
        }
    }

    private void ShowCommittedPlan()
    {
        ActionCount = Math.Max(1, MaxActionCount);
        SetAction();
        var state = battleState ?? CompanionBattleStateStore.Find(InstanceId);
        ProjectionStateStore.NotifyIntentPresented(InstanceId, state?.CurrentPlan);
        ShowAction();
    }

    private void EnsureProjectionIntentForTurn()
    {
        if (ActionCards == null)
        {
            RefreshProjectionIntent("DoAction.MissingIntent");
            return;
        }

        try
        {
            ShowAction();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Projection] intent show failed at turn start: " + ex.Message);
        }
    }

    private void TriggerProjectionStartRound()
    {
        try
        {
            if (!CompanionAuthorityService.IsAuthoritative())
            {
                return;
            }

            CompanionThreatService.DecayForTurn(battleState);
            EventCenter.Instance.EventTrigger("StartRound" + Status.InstanceId, false);
            EventCenter.Instance.EventTrigger("StartRoundEnd" + Status.InstanceId, false);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Projection] start-round trigger failed: " + ex.Message);
        }
    }

    private bool CanProjectionAct()
    {
        return Status != null
            && ProjectionEffectContextService.IsOwnerAvailable(battleState)
            && Status.state != IStatusManager.State.NoAction
            && Status.state != IStatusManager.State.Dead
            && Status.CurHp > 0
            && FightManager.Instance != null
            && FightManager.Instance.fightType != FightType.Loss;
    }

    private void ApplyEnemyMaterial()
    {
        try
        {
            var body = transform.Find("body");
            var renderer = body?.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                var material = SunExpResourceCache.Load<Material>("Material/EnemyMaterial", true);
                if (material != null)
                {
                    renderer.material = UnityEngine.Object.Instantiate(material);
                }

                renderer.color = new Color(0.82f, 0.9f, 1f, 0.88f);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[Projection] material fallback used: " + ex.Message);
        }
    }

    private void EnsureActionIcons()
    {
        var status = Status as StatusManager;
        if (status?.actionContent == null)
        {
            return;
        }

        var content = status.actionContent.transform.Find("content");
        if (content == null)
        {
            return;
        }

        try
        {
            var layout = content.GetComponent<AnimatedHorizontalLayout>();
            if (layout != null)
            {
                layout.spacing = 24f;
            }
        }
        catch
        {
            // Layout tuning is cosmetic.
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
