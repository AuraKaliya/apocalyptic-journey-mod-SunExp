using System;
using System.Linq;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SunExp.Dll.Hooks.Visual;

public static class ProjectionIntentPresenter
{
    private static bool initialized;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        ProjectionStateStore.IntentPresented += BindCommittedPlan;
        SpiritStateStore.IntentPresented += BindCommittedPlan;
    }

    private static void BindCommittedPlan(ProjectionState projectionState, CompanionIntentPlan plan)
    {
        BindCommittedPlan(projectionState?.StatusId ?? "", projectionState?.Projection, plan);
    }

    private static void BindCommittedPlan(SpiritState spiritState, CompanionIntentPlan plan)
    {
        BindCommittedPlan(spiritState?.StatusId ?? "", spiritState?.Spirit, plan);
    }

    private static void BindCommittedPlan(string statusId, OtherObj? actor, CompanionIntentPlan plan)
    {
        try
        {
            if (actor == null)
            {
                return;
            }

            var status = actor.Status as StatusManager;
            if (status?.actionObj == null)
            {
                return;
            }

            ResetAllLines(status);
            if (plan == null || plan.IsWait)
            {
                return;
            }

            var battleState = CompanionBattleStateStore.Find(statusId);
            var intent = CompanionIntentResolver.Find(battleState, plan.IntentId);
            if (intent == null || battleState == null)
            {
                return;
            }

            var target = ResolveLineTarget(battleState, intent, plan);
            var targetStatus = target as StatusManager;
            var targetUi = targetStatus?.selfUI;
            var line = LineFor(status, 0);
            if (targetUi == null || line == null)
            {
                return;
            }

            line.SetStartPos(Vector3.zero);
            line.curvature = 0.3f;
            var hoverLine = status.actionObj[0].GetComponent<ProjectionIntentHoverLine>()
                ?? status.actionObj[0].AddComponent<ProjectionIntentHoverLine>();
            hoverLine.Configure(line, targetUi.transform);
            SunExpPerformanceCounters.Record("CompanionIntent.PresentationBound");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[CompanionIntent] presentation bind failed: " + ex.Message);
        }
    }

    private static IStatusManager? ResolveLineTarget(
        CompanionBattleState battleState,
        CompanionIntentDefinition intent,
        CompanionIntentPlan plan)
    {
        var effects = plan.ResolvedEffects ?? new System.Collections.Generic.List<CompanionResolvedEffect>();
        var specs = CompanionIntentEffects.Expand(intent);
        var candidates = new System.Collections.Generic.List<(int Rank, IStatusManager Target)>();
        for (var index = 0; index < effects.Count && index < specs.Count; index++)
        {
            var effectIntent = CompanionIntentEffects.AsDefinition(intent, specs[index]);
            if (string.Equals(effectIntent.Target.Scope, "Self", StringComparison.Ordinal)
                || string.Equals(effectIntent.Target.Mode, "All", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var candidate in CompanionTargetPolicyRegistry.Alive(effects[index].TargetIds))
            {
                if (CompanionTargetPolicyRegistry.IsValidCommittedTarget(battleState, effectIntent, candidate))
                {
                    var rank = string.Equals(effectIntent.Target.Scope, "Enemy", StringComparison.Ordinal) ? 0 : 1;
                    candidates.Add((rank, candidate));
                }
            }
        }

        return candidates.OrderBy(candidate => candidate.Rank).Select(candidate => candidate.Target).FirstOrDefault();
    }

    private static void ResetAllLines(StatusManager status)
    {
        for (var index = 0; index < status.actionObj.Length; index++)
        {
            var line = LineFor(status, index);
            if (line == null)
            {
                continue;
            }

            line.show = false;
            line.Combine(null);
            line.gameObject.SetActive(false);
            status.actionObj[index].GetComponent<ProjectionIntentHoverLine>()?.Configure(line, null);
        }
    }

    private static FightLine? LineFor(StatusManager status, int index)
    {
        if (index < 0 || index >= status.actionObj.Length || status.actionObj[index] == null)
        {
            return null;
        }

        return status.actionObj[index].transform.Find("LineUI")?.GetComponent<FightLine>();
    }
}

internal sealed class ProjectionIntentHoverLine : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private FightLine? line;
    private Transform? target;

    public void Configure(FightLine nextLine, Transform? nextTarget)
    {
        Hide();
        line = nextLine;
        target = nextTarget;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (line == null || target == null || !isActiveAndEnabled)
        {
            return;
        }

        line.Combine(target);
        line.show = true;
        line.gameObject.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Hide();
    }

    private void OnDisable()
    {
        Hide();
    }

    private void Hide()
    {
        if (line == null)
        {
            return;
        }

        line.show = false;
        line.Combine(null);
        line.gameObject.SetActive(false);
    }
}
