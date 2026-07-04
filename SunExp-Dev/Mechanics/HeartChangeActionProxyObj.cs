using System;
using System.Collections;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public sealed class HeartChangeActionProxyObj : OtherObj
{
    private const float ActionStepDelaySeconds = 0.6f;

    private int proxyIntentCount = 1;
    private bool proxyActionsResolved;

    public override string Type => "Projection";

    public int IntentCount => proxyIntentCount;

    public void Configure(Enemy source)
    {
        if (source == null)
        {
            return;
        }

        Status = source.Status;
        InstanceId = source.InstanceId;
        dataConfig = source.dataConfig;
        data = source.data;
        Attack = source.Attack;
        Defend = source.Defend;
        MaxHp = source.MaxHp;
        CurHp = source.CurHp;
        proxyIntentCount = ResolveIntentCount(source);
        proxyActionsResolved = false;
        MaxActionCount = proxyIntentCount;
        ActionCount = proxyIntentCount;

        FightAction = new ObjectAction(this);
        for (var i = 0; i < proxyIntentCount; i++)
        {
            FightAction.AddCard(CreateProxyActionCard(source.Status as StatusManager));
        }

        SunExpLog.Info("[HeartChange] configured proxy action: status="
            + InstanceId
            + ", intentCount="
            + proxyIntentCount);
        RefreshIntent("Configure");
    }

    public void RefreshIntent(string source)
    {
        try
        {
            if (FightAction == null || Status == null || !IsAlive(Status))
            {
                return;
            }

            ActionCount = Math.Max(1, proxyIntentCount);
            SetAction();
            ShowAction();
            SunExpPerformanceCounters.Record("HeartChange.ProxyIntentRefreshed");
        }
        catch (System.Exception ex)
        {
            SunExpLog.Warn("[HeartChange] proxy intent refresh failed from " + source + ": " + ex.Message);
        }
    }

    public override IEnumerator DoAction()
    {
        try
        {
            FightManager.Instance?.ChangeUnit(FightType.Partner);
        }
        catch
        {
            // Cosmetic only.
        }

        if (Status == null || !IsAlive(Status))
        {
            HeartChangeControlService.CompleteProxyAction(Status, "ProxyAction.NotAlive");
            yield break;
        }

        if (Status.state == IStatusManager.State.NoAction)
        {
            Status.ChangeState(IStatusManager.State.Default);
            HeartChangeControlService.CompleteProxyAction(Status, "ProxyAction.NoAction");
            yield break;
        }

        if (FightAction == null || ActionCards == null || ActionCards.Count == 0)
        {
            RefreshIntent("DoAction.MissingIntent");
        }

        SunExpLog.Info("[HeartChange] proxy action begin: status="
            + Status.InstanceId
            + ", intentCount="
            + proxyIntentCount
            + ", source=DoAction");

        var resolved = ResolveNow("DoAction");
        if (resolved && IsAlive(Status))
        {
            yield return new WaitForSeconds(ActionStepDelaySeconds);
        }

        HeartChangeControlService.CompleteProxyAction(Status, "ProxyAction.Complete");
        yield break;
    }

    public bool ResolveNow(string source)
    {
        if (proxyActionsResolved)
        {
            SunExpLog.Info("[HeartChange] proxy action already resolved: status="
                + InstanceId
                + ", source="
                + source);
            return false;
        }

        proxyActionsResolved = true;
        var executed = 0;
        try
        {
            EnsureIntentForExecution(source);
            HideAction();
            var cards = SnapshotActionCards();
            SunExpLog.Info("[HeartChange] resolving proxy action: status="
                + InstanceId
                + ", count="
                + cards.Count
                + ", source="
                + source);

            for (var index = 0; index < cards.Count; index++)
            {
                if (!CanResolveAction())
                {
                    break;
                }

                if (ExecuteProxyCard(cards[index], index, source))
                {
                    executed++;
                }
            }

            SunExpLog.Info("[HeartChange] proxy action complete: status="
                + InstanceId
                + ", executed="
                + executed
                + ", source="
                + source);
            SunExpPerformanceCounters.Record("HeartChange.ProxyActionExecuted");
            return executed > 0;
        }
        catch (System.Exception ex)
        {
            SunExpLog.Warn("[HeartChange] proxy action failed from " + source + ": " + ex.Message);
            return executed > 0;
        }
    }

    private bool ExecuteProxyCard(ObjectCard card, int index, string source)
    {
        var actor = Status as StatusManager;
        var target = HeartChangeIntentService.SelectStrikeTarget(Status);
        if (actor == null || target is not StatusManager targetStatus)
        {
            SunExpLog.Info("[HeartChange] proxy card skipped: status="
                + InstanceId
                + ", index="
                + index
                + ", reason=NoTarget"
                + ", source="
                + source);
            SunExpPerformanceCounters.Record("HeartChange.ProxyActionNoTarget");
            return false;
        }

        card.status = actor;
        card.nowCD = 0;
        card.UseCard(targetStatus);
        CallActionAnimation(card);
        SunExpLog.Info("[HeartChange] proxy card executed: status="
            + InstanceId
            + ", index="
            + index
            + ", target="
            + targetStatus.InstanceId
            + ", damagePreview="
            + HeartChangeIntentService.StrikeDamage(Status)
            + ", source="
            + source);
        return true;
    }

    private void EnsureIntentForExecution(string source)
    {
        if (FightAction == null)
        {
            FightAction = new ObjectAction(this);
        }

        if (ActionCards == null || ActionCards.Count == 0)
        {
            RefreshIntent(source + ".MissingIntent");
        }
    }

    private List<ObjectCard> SnapshotActionCards()
    {
        if (ActionCards == null || ActionCards.Count == 0)
        {
            return new List<ObjectCard>();
        }

        return new List<ObjectCard>(ActionCards);
    }

    private bool CanResolveAction()
    {
        return Status != null
            && Status.CurHp > 0
            && Status.state != IStatusManager.State.NoAction
            && Status.state != IStatusManager.State.Dead
            && FightManager.Instance != null
            && FightManager.Instance.fightType != FightType.Loss;
    }

    private static ObjectCard CreateProxyActionCard(StatusManager? status)
    {
        var actionCard = new ObjectCard
        {
            status = status,
            isIgnored = false,
            nowCD = 0
        };
        actionCard.Init(new DataConfig(SunExpIds.HeartChangeActionStrikeCardId, DataType.EnemyCard));
        return actionCard;
    }

    private static int ResolveIntentCount(Enemy source)
    {
        var count = source.ActionCards?.Count ?? 0;
        if (count <= 0)
        {
            count = source.ActionCount;
        }

        if (count <= 0)
        {
            count = source.MaxActionCount;
        }

        return Math.Max(1, count);
    }

    private static void CallActionAnimation(ObjectCard card)
    {
        try
        {
            var executor = card.dataConfig?.scriptExecutor;
            if (executor == null)
            {
                return;
            }

            UIManager.Instance?.GetUI<FightUI>("FightUI")?.CallActionAnimation(executor);
        }
        catch (System.Exception ex)
        {
            SunExpLog.Debug("[HeartChange] proxy action animation skipped: " + ex.Message);
        }
    }

    private static bool IsAlive(IStatusManager? status)
    {
        return status != null && status.CurHp > 0 && status.state != IStatusManager.State.Dead;
    }
}
