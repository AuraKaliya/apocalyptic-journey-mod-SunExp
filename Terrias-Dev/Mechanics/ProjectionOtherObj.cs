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

    public override string Type => "Projection";

    public bool InitProjection(
        PolymorphRoleSpec role,
        string ownerStatusId,
        int slotIndex,
        CompanionStats stats,
        string statusId = "",
        string ownerPlayerId = "")
    {
        if (role == null)
        {
            return false;
        }

        RoleId = role.Id;
        OwnerStatusId = ownerStatusId ?? "";
        OwnerPlayerId = CompanionOwnershipService.ResolveOwnerPlayerId(
            OwnerStatusId,
            ownerPlayerId);
        dataConfig = ProjectionSummonService.CreateProjectionDataConfig(role, stats);
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
            OwnerPlayerId);
        gameObject.name = data.Localize("Name") + InstanceId;
        var status = transform.gameObject.AddComponent<StatusManager>().Init(this)
                     as StatusManager;
        if (status == null)
        {
            return false;
        }

        Status = status;
        ProjectionSummonService.RegisterFightState(
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
        if (Status is StatusManager status && status.actionContent != null)
        {
            status.actionContent.SetActive(false);
        }
    }

    public override IEnumerator DoAction()
    {
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

        cardState.PrepareTurn();
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
                || FightManager.Instance == null
                || FightManager.Instance.fightType is FightType.None
                    or FightType.Win
                    or FightType.Loss
                    or FightType.Escape)
            {
                break;
            }

            runner.Step(Time.unscaledTime);
            yield return null;
        }

        if (Status.CurHp > 0
            && Status.state != IStatusManager.State.Dead
            && FightManager.Instance?.fightType != FightType.Loss)
        {
            battleState?.AdvanceTurn();
            ProjectionSummonService.BroadcastRuntimeState(
                this,
                "ActorTurnAdvanced");
        }
    }

    public override void AddCardList()
    {
        FightAction ??= new ObjectAction(this);
        ActionCards = new List<ObjectCard>();
    }

    public void HydrateOwnerCombatState(ProjectionOwnerCombatSnapshot? snapshot)
    {
        if (snapshot == null || Status == null)
        {
            return;
        }

        foreach (var existing in Status.GetBuffs() ?? Array.Empty<IBuffItem>())
        {
            var id = existing?.buffConfig?.BuffId ?? "";
            if (id.Length > 0)
            {
                Status.RemoveBuff(id);
            }
        }
        foreach (var buff in snapshot.Buffs ?? new List<ProjectionBuffSnapshot>())
        {
            var id = buff?.BuffId ?? "";
            var level = buff?.Level ?? 0;
            if (id.Length == 0 || level <= 0)
            {
                continue;
            }
            try
            {
                Status.AddBuff(id, level);
            }
            catch (Exception ex)
            {
                TerriasLog.Debug(
                    "[Projection] owner buff copy skipped: "
                    + id
                    + ", "
                    + ex.Message);
            }
        }

        // Buff initialization can mutate stats and dynamic variables. Restore
        // the captured owner values last so the projection starts identically.
        MaxHp = Math.Max(1, snapshot.MaxHp);
        CurHp = Math.Max(1, Math.Min(MaxHp, snapshot.CurrentHp));
        Defend = Math.Max(0, snapshot.Defend);
        Attack = Math.Max(0, snapshot.Attack);
        if (Status.dynamicVariables != null)
        {
            Status.dynamicVariables.Clear();
            foreach (var entry in snapshot.DynamicVariables
                         ?? new Dictionary<string, float>())
            {
                Status.dynamicVariables[entry.Key] = entry.Value;
            }
        }
        Status.UpdateStatus(true);
    }

    public void HydrateCardState(
        CombatActorCardStateSnapshot? snapshot,
        string source)
    {
        if (snapshot != null
            && (!string.Equals(
                    snapshot.OwnerModId,
                    TerriasIds.ModId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    snapshot.ActorId,
                    InstanceId,
                    StringComparison.Ordinal)))
        {
            TerriasLog.Warn(
                "[ProjectionCards] snapshot identity mismatch from "
                + source);
            return;
        }
        var hydrated = ProjectionCardBattleState.Hydrate(snapshot, out var reason);
        if (hydrated == null)
        {
            if (snapshot != null)
            {
                TerriasLog.Warn(
                    "[ProjectionCards] snapshot hydrate failed from "
                    + source
                    + ": "
                    + reason);
            }
            return;
        }
        if (cardState != null && hydrated.Revision < cardState.Revision)
        {
            TerriasLog.Debug(
                "[ProjectionCards] ignored stale snapshot from "
                + source
                + ": remote="
                + hydrated.Revision
                + ", local="
                + cardState.Revision);
            return;
        }
        cardState = hydrated;
    }

    public CombatActorCardStateSnapshot? ExportCardState()
    {
        return cardState?.Export(
            TerriasIds.ModId,
            InstanceId,
            AuraShared.Core.AuraBattleLifecycleRouter.CurrentBattleSessionId);
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
        var expectedTurn = (battleState?.TurnIndex ?? 0) + 1;
        var deadline = Time.unscaledTime + 50f;
        while (Time.unscaledTime < deadline
               && (battleState?.TurnIndex ?? 0) < expectedTurn
               && Status != null
               && Status.state != IStatusManager.State.Dead
               && FightManager.Instance != null
               && FightManager.Instance.fightType is not (FightType.None
                   or FightType.Win
                   or FightType.Loss
                   or FightType.Escape))
        {
            yield return null;
        }
        if ((battleState?.TurnIndex ?? 0) < expectedTurn)
        {
            TerriasLog.Warn(
                "[ProjectionCards] remote turn wait timed out: "
                + InstanceId);
        }
    }

    private void CompleteSkippedTurn(string source)
    {
        battleState?.AdvanceTurn();
        ProjectionSummonService.BroadcastRuntimeState(
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
