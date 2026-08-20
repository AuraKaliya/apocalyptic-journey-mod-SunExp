using System;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class MorningStarRelicService
{
    public static void RegisterBlackSunCross(ScriptExecutor self)
    {
        var ownerId = self?.Self?.InstanceId ?? "";
        var registrationKey = "TerriasBlackSunCrossRegistered_" + ownerId;
        if (self?.Self == null
            || string.IsNullOrWhiteSpace(ownerId)
            || ExecutorApi.CombatIntGet(registrationKey) > 0)
        {
            return;
        }

        ExecutorApi.CombatIntSet(registrationKey, 1);
        using var scope = ScriptEventApi.BeginFightScope(self, "Relic.BlackSunCross");
        if (scope == null)
        {
            ExecutorApi.CombatIntSet(registrationKey, 0);
            return;
        }

        void Cleanup()
        {
            ExecutorApi.CombatIntSet(registrationKey, 0);
            scope.Invalidate();
        }

        scope.AddRequired(
            "StartRoundEnd",
            new Action(() =>
            {
                var count = MorningStarCurseService.HandCurses(self).Count;
                if (count <= 0)
                {
                    return;
                }

                self.SetStatus("Self");
                self.AddBuff(TerriasIds.VowPower, count.ToString());
                self.UpdateRelicShow();
            }),
            TerriasIds.BlackSunCrossRelic);
        scope.AddRequired(
            "EndRound",
            new Action(() =>
            {
                var owner = self.Self;
                var recovery = MorningStarCurseFormula.BlackSunCrossRecovery(
                    owner?.MaxHp ?? 0,
                    owner?.CurHp ?? 0,
                    ExecutorApi.SelfBuffLevel(self, TerriasIds.VowPower));
                if (recovery > 0 && StatusApi.TryHeal(owner, recovery))
                {
                    self.UpdateRelicShow();
                }
            }),
            TerriasIds.BlackSunCrossRelic);
        scope.AddRequired("Win", new Action(Cleanup), TerriasIds.BlackSunCrossRelic);
        scope.AddRequired("Escape", new Action(Cleanup), TerriasIds.BlackSunCrossRelic);
        if (!scope.Commit())
        {
            ExecutorApi.CombatIntSet(registrationKey, 0);
        }
    }

    public static void RegisterTimelessClock(ScriptExecutor self)
    {
        const string countKey = "TerriasTimelessClockActionCount";
        using var scope = ScriptEventApi.BeginFightScope(self, "Relic.TimelessClock");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired(
            "FightStart",
            new Action(() =>
            {
                ExecutorApi.SetVar(self, countKey, 0);
                self.UpdateRelicShow();
            }),
            TerriasIds.TimelessClockRelic);
        TerriasActionPassiveRegistry.Register(
            self,
            "Relic.TimelessClock",
            AuraShared.Core.AuraCardActionPhase.NativeStarted,
            _ =>
            {
                var count = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, countKey, "0")) + 1;
                ExecutorApi.SetVar(self, countKey, count);
                if (count % 3 == 0)
                {
                    MakeRandomHandCardFree(self);
                }

                self.UpdateRelicShow();
            });
        if (!scope.Commit())
        {
            TerriasActionPassiveRegistry.Unregister(self, "Relic.TimelessClock");
        }
    }

    public static void RegisterLoneerStarStonePouch(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Relic.LoneerStarStonePouch");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired(
            "FightStart",
            new Action(() =>
            {
                StarStonePouchService.GrantRelicInitial(self);
                self.UpdateRelicShow();
            }),
            TerriasIds.LoneerStarStonePouchRelic);
        scope.AddRequired(
            "Win",
            new Action(() => StarStonePouchService.RemoveRelicState(self?.Self)),
            TerriasIds.LoneerStarStonePouchRelic);
        scope.AddRequired(
            "Escape",
            new Action(() => StarStonePouchService.RemoveRelicState(self?.Self)),
            TerriasIds.LoneerStarStonePouchRelic);
        scope.Commit();
    }

    public static void RegisterFoxWomanHarp(ScriptExecutor self)
    {
        const string countKey = "TerriasFoxWomanHarpApplicationCount";
        using var scope = ScriptEventApi.BeginFightScope(self, "Relic.FoxWomanHarp");
        if (scope == null)
        {
            return;
        }

        ExecutorApi.SetVar(self, countKey, 0);
        scope.AddRequired<AddBuffData>(
            "AddBuff",
            data =>
            {
                var ownerId = self?.Self?.InstanceId ?? "";
                var targetIsEnemy = ExecutorApi.EnemyTargets(self)
                    .Any(target => string.Equals(target.InstanceId, data.toId, StringComparison.Ordinal));
                if (!MorningStarRelicFormula.ShouldCountNegativeBuffApplication(
                        ownerId,
                        data.fromId,
                        targetIsEnemy,
                        BuffApi.IsNegativeBuffId(data.dataId)))
                {
                    return;
                }

                var count = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, countKey, "0")) + 1;
                if (count >= 2)
                {
                    count -= 2;
                    BuffApi.RemoveRandomNegativeBuff(self!, self?.Self);
                }

                ExecutorApi.SetVar(self, countKey, count);
                self!.UpdateRelicShow();
            },
            TerriasIds.FoxWomanHarpRelic);
        scope.Commit();
    }

    public static void RegisterDimStarStone(ScriptExecutor self)
    {
        using var scope = ScriptEventApi.BeginFightScope(self, "Relic.DimStarStone");
        if (scope == null)
        {
            return;
        }

        scope.AddRequired(
            "StartRound",
            new Action(() =>
            {
                self.SetStatus("Self");
                self.RandomAddGoodBuff("1");
                self.UpdateRelicShow();
            }),
            TerriasIds.DimStarStoneRelic);
        scope.Commit();
    }

    private static void MakeRandomHandCardFree(ScriptExecutor self)
    {
        var candidates = self?.HandCard?
            .Where(card => MorningStarRelicFormula.IsTimelessClockCandidate(card?.dataConfig))
            .ToList();
        if (candidates == null || candidates.Count == 0)
        {
            return;
        }

        var card = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        if (!MorningStarRelicFormula.MakeTimelessClockFree(card.dataConfig))
        {
            return;
        }

        TerriasCardRefreshQueue.RequestFullRefresh(card, "Relic.TimelessClock");
    }
}
