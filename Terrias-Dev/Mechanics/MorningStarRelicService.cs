using System;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class MorningStarRelicService
{
    public static void RegisterTimelessClock(ScriptExecutor self)
    {
        const string countKey = "TerriasTimelessClockActionCount";
        const string tokenKey = "TerriasTimelessClockToken";
        var token = ExecutorApi.RegisterHook(self, "TerriasTimelessClockHook", tokenKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(
            self,
            "FightStart",
            tokenKey,
            token,
            new Action(() =>
            {
                ExecutorApi.SetVar(self, countKey, 0);
                self.UpdateRelicShow();
            }),
            TerriasIds.TimelessClockRelic);
        ExecutorApi.TryAddTokenedEvent(
            self,
            "Action",
            tokenKey,
            token,
            new Action(() =>
            {
                var count = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, countKey, "0")) + 1;
                ExecutorApi.SetVar(self, countKey, count);
                if (count % 3 == 0)
                {
                    MakeRandomHandCardFree(self);
                }

                self.UpdateRelicShow();
            }),
            TerriasIds.TimelessClockRelic);
    }

    public static void RegisterLoneerStarStonePouch(ScriptExecutor self)
    {
        const string tokenKey = "TerriasLoneerStarStonePouchRelicToken";
        var token = ExecutorApi.RegisterHook(self, "TerriasLoneerStarStonePouchRelicHook", tokenKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(
            self,
            "FightStart",
            tokenKey,
            token,
            new Action(() =>
            {
                StarStonePouchService.GrantRelicInitial(self);
                self.UpdateRelicShow();
            }),
            TerriasIds.LoneerStarStonePouchRelic);
        ExecutorApi.TryAddTokenedEvent(
            self,
            "Win",
            tokenKey,
            token,
            new Action(() => StarStonePouchService.RemoveRelicState(self?.Self)),
            TerriasIds.LoneerStarStonePouchRelic);
        ExecutorApi.TryAddTokenedEvent(
            self,
            "Escape",
            tokenKey,
            token,
            new Action(() => StarStonePouchService.RemoveRelicState(self?.Self)),
            TerriasIds.LoneerStarStonePouchRelic);
    }

    public static void RegisterFoxWomanHarp(ScriptExecutor self)
    {
        const string countKey = "TerriasFoxWomanHarpApplicationCount";
        const string tokenKey = "TerriasFoxWomanHarpToken";
        var token = ExecutorApi.RegisterHook(self, "TerriasFoxWomanHarpHook", tokenKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        ExecutorApi.SetVar(self, countKey, 0);
        ExecutorApi.TryAddTokenedEvent<AddBuffData>(
            self,
            "AddBuff",
            tokenKey,
            token,
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
    }

    public static void RegisterDimStarStone(ScriptExecutor self)
    {
        const string tokenKey = "TerriasDimStarStoneToken";
        var token = ExecutorApi.RegisterHook(self, "TerriasDimStarStoneHook", tokenKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(
            self,
            "StartRound",
            tokenKey,
            token,
            new Action(() =>
            {
                self.SetStatus("Self");
                self.RandomAddGoodBuff("1");
                self.UpdateRelicShow();
            }),
            TerriasIds.DimStarStoneRelic);
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

        try
        {
            card.DataUpdate();
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Timeless Clock card refresh skipped: " + ex.Message);
        }
    }
}
