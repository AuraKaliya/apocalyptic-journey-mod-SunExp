using System;
using System.Collections;
using System.Collections.Generic;
using AuraCombatAi.Shared;
using AuraCombatAi.Shared.GameApi;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using UnityEngine;

namespace Terrias.Dll.Mechanics;

public sealed class ProjectionOtherObj : Partner
{
    private static readonly CombatDecisionEngine DecisionEngine = new();
    private CompanionBattleState? battleState;
    private ProjectionCardBattleState? cardState;

    public string RoleId { get; private set; } = "";

    public string OwnerStatusId { get; private set; } = "";

    public string OwnerPlayerId { get; private set; } = "";

    public string ExecutionRoutePlayerId { get; private set; } = "";

    internal CombatAutoTurnResult? LastAutoTurnResult { get; private set; }

    public override string Type => "Projection";

    public bool InitProjection(
        PolymorphRoleSpec role,
        string ownerStatusId,
        int slotIndex,
        CompanionStats stats,
        string statusId = "",
        string ownerPlayerId = "",
        string executionRoutePlayerId = "")
    {
        if (role == null)
        {
            return false;
        }

        RoleId = role.Id;
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = CompanionOwnershipService.ResolveSemanticOwnerPlayerId(
            OwnerStatusId,
            ownerPlayerId);
        ExecutionRoutePlayerId = string.IsNullOrWhiteSpace(executionRoutePlayerId)
            ? CompanionExecutionRouteApi.ResolveAuthoritativePlayerId(OwnerPlayerId)
            : executionRoutePlayerId.Trim();
        dataConfig = ProjectionLifecycle.Current.CreateDataConfig(role, stats);
        data = dataConfig.data;
        FightAction = new ObjectAction(this);
        ApplyEnemyMaterial();

        Attack = stats.Attack;
        Defend = stats.Armor;
        MaxHp = stats.MaxHp;
        CurHp = stats.MaxHp;
        MaxActionCount = 0;
        ActionCount = 0;
        InstanceId = string.IsNullOrWhiteSpace(statusId)
            ? ProjectionStateStore.NextStatusId()
            : statusId.Trim();
        battleState = CompanionBattleStateStore.Create(
            InstanceId,
            role.Id,
            OwnerStatusId,
            slotIndex,
            stats,
            OwnerPlayerId,
            ExecutionRoutePlayerId);
        gameObject.name = "TerriasProjection:" + RoleId + ":" + InstanceId;
        var status = transform.gameObject.AddComponent<StatusManager>().Init(this)
                     as StatusManager;
        if (status == null)
        {
            CompanionBattleStateStore.Remove(InstanceId);
            battleState = null;
            return false;
        }

        Status = status;
        ProjectionLifecycle.Current.Register(
            this,
            "ProjectionOtherObj.InitProjection");
        dataConfig.scriptExecutor.Self = Status;
        dataConfig.scriptExecutor.SetStatus("Self");
        AddCardList();
        status.animatedState = IStatusManager.AnimatedState.Idle;
        if (status.actionContent != null)
        {
            status.actionContent.SetActive(false);
        }
        if (GameApp.Instance.NowBackground != null
            && GameApp.Instance.NowBackground.name == "BalancedHolySee")
        {
            CanRef();
        }

        InitBound(null, true);
        return true;
    }

    public void ActivateAfterHydration(
        CompanionIntentPlan? authoritativePlan,
        string source)
    {
        if (!BattleLifecycleApi.AcceptsCompanionContinuation)
        {
            return;
        }
        if (Status is StatusManager status && status.actionContent != null)
        {
            status.actionContent.SetActive(false);
        }
    }

    public override IEnumerator DoAction()
    {
        LastAutoTurnResult = null;
        if (!BattleLifecycleApi.AcceptsCompanionContinuation)
        {
            yield break;
        }
        FightManager.Instance?.ChangeUnit(FightType.Partner);
        if (!CompanionAuthorityService.IsAuthoritative())
        {
            yield return WaitForAuthoritativeTurnCompletion();
            yield break;
        }

        if (Status == null || Status.state == IStatusManager.State.Dead)
        {
            yield break;
        }

        TriggerProjectionStartRound();
        yield return null;
        if (Status.state == IStatusManager.State.NoAction)
        {
            Status.ChangeState(IStatusManager.State.Default);
            CompleteSkippedTurn("NoAction");
            yield break;
        }
        if (cardState == null)
        {
            TerriasLog.Warn(
                "[ProjectionCards] actor turn skipped because card state is not hydrated: "
                + InstanceId);
            CompleteSkippedTurn("CardStateUnavailable");
            yield break;
        }

        cardState.PrepareTurn(Status);
        var runner = new CombatAutoTurnRunner(
            new CombatAgentDescriptor
            {
                OwnerModId = TerriasIds.ModId,
                ActorId = InstanceId,
                RuntimeId = ProjectionCardBattleState.RuntimeId(Status),
                ControlMode = CombatAgentControlMode.AutonomousRequired
            },
            ProjectionTurnProfile(),
            new CombatDecisionEngineSource(DecisionEngine),
            new ProjectionCombatAgentPort(this, cardState));
        while (runner.Result == null)
        {
            if (Status.state == IStatusManager.State.Dead
                || !BattleLifecycleApi.AcceptsCompanionContinuation)
            {
                break;
            }

            runner.Step(Time.unscaledTime);
            yield return null;
        }
        LastAutoTurnResult = runner.Result;

        if (Status.CurHp > 0
            && Status.state != IStatusManager.State.Dead
            && BattleLifecycleApi.AcceptsCompanionContinuation)
        {
            battleState?.AdvanceTurn();
            ProjectionLifecycle.Current.CompleteTurn(
                this,
                "ActorTurnAdvanced");
        }
    }

    public override void AddCardList()
    {
        FightAction ??= new ObjectAction(this);
        ActionCards = new List<ObjectCard>();
    }

    public void HydrateOwnerCoreStats(IStatusManager? owner)
    {
        if (owner == null || Status == null)
        {
            return;
        }

        MaxHp = Math.Max(1, owner.MaxHp);
        CurHp = Math.Max(1, Math.Min(MaxHp, owner.CurHp));
        Defend = Math.Max(0, owner.Defend);
        Attack = Math.Max(0, (owner.fatherObject as OtherObj)?.Attack ?? Attack);
        Status.UpdateStatus(true);
    }

    public bool InitializeRoleDeck(
        ProjectionDeckRecipe? recipe,
        string source)
    {
        var initialized = ProjectionCardBattleState.CreateFresh(
            recipe,
            InstanceId,
            out var reason);
        if (initialized == null)
        {
            TerriasLog.Warn(
                "[ProjectionCards] role deck initialization failed from "
                + source
                + ": "
                + reason);
            return false;
        }
        cardState = initialized;
        cardState.InitializeLifecycle(Status);
        return true;
    }

    private static CombatAutoTurnProfile ProjectionTurnProfile()
    {
        return new CombatAutoTurnProfile
        {
            MaxConsecutiveFailures = 3,
            MaxCommittedActions = 32,
            MaxRepeatedStateObservations = 4,
            DecisionTimeoutSeconds = 3d,
            ActionTimeoutSeconds = 8d,
            TurnTimeoutSeconds = 45d,
            RequireDeclaredHeadlessActions = true,
            DecisionProfile = new CombatDecisionProfile
            {
                Id = "terrias-projection",
                SearchQuality = "fast",
                SearchBudgetMode = "fixed",
                SearchSimulationBudget = 64,
                SearchMinimumSimulations = 32,
                SearchTimeBudgetMilliseconds = 150
            }
        };
    }

    private IEnumerator WaitForAuthoritativeTurnCompletion()
    {
        var state = ProjectionStateStore.Find(InstanceId);
        if (state == null)
        {
            yield break;
        }
        // If the completion frame arrived before Partner.DoAction, consume it
        // immediately instead of waiting for a turn that does not exist.
        var expectedTurn = state.RemoteTurnGate.BeginInvocation();
        var startedAt = Time.unscaledTimeAsDouble;
        while (!state.RemoteTurnGate.IsSatisfied(expectedTurn)
               && Status != null
               && Status.state != IStatusManager.State.Dead
               && FightManager.Instance != null
               && FightManager.Instance.fightType is not (FightType.None
                   or FightType.Win
                   or FightType.Loss
                   or FightType.Escape))
        {
            var now = Time.unscaledTimeAsDouble;
            if (state.RemoteTurnGate.ShouldQuery(now, idleSeconds: 2d, minimumIntervalSeconds: 1.5d))
            {
                state.RemoteTurnGate.MarkQuery(now);
                ProjectionLifecycle.Current.RequestState(this, "RemoteTurnWait");
            }
            if (now - startedAt >= 12d)
            {
                TerriasLog.Warn(
                    "[ProjectionCards] remote turn released after state-query grace period: "
                    + InstanceId
                    + ", expected="
                    + expectedTurn
                    + ", completed="
                    + state.RemoteTurnGate.Completed);
                state.RemoteTurnGate.Release(expectedTurn);
                yield break;
            }
            yield return null;
        }
        if (state.RemoteTurnGate.IsSatisfied(expectedTurn))
        {
            state.RemoteTurnGate.Consume(expectedTurn);
        }
    }

    private void CompleteSkippedTurn(string source)
    {
        LastAutoTurnResult = new CombatAutoTurnResult
        {
            Reason = string.Equals(source, "NoAction", StringComparison.Ordinal)
                ? CombatAgentCompletionReason.NoLegalAction
                : CombatAgentCompletionReason.FatalExecutionFailure,
            Forced = true,
            Message = "projection turn skipped: " + source
        };
        cardState?.CompleteTurn(Status);
        battleState?.AdvanceTurn();
        ProjectionLifecycle.Current.CompleteTurn(
            this,
            "ActorTurnSkipped." + source);
    }

    private void TriggerProjectionStartRound()
    {
        EventCenter.Instance.EventTrigger("StartRound" + Status.InstanceId, false);
        EventCenter.Instance.EventTrigger("StartRoundEnd" + Status.InstanceId, false);
    }

    private void ApplyEnemyMaterial()
    {
        try
        {
            var renderer = transform.Find("body")?.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                return;
            }
            var material = TerriasResourceCache.Load<Material>(
                "Material/EnemyMaterial",
                true);
            if (material != null)
            {
                renderer.material = UnityEngine.Object.Instantiate(material);
            }
            renderer.color = new Color(0.82f, 0.9f, 1f, 0.88f);
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[Projection] material fallback used: " + ex.Message);
        }
    }
}
