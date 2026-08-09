using System;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SolarBlessingService
{
    public static void ApplyOwnScript(ScriptExecutor? self, string id)
    {
        if (self == null || Normalize(id) != TerriasIds.WhiteRadianceSaintBlessing)
        {
            return;
        }

        if (OriginCapService.TryIncreasePrimaryAndSecondaryCurrent(
                OriginCapService.FateStarIncrease,
                "Blessing.WhiteRadianceSaint",
                out var state))
        {
            OriginCapService.ShowPrimaryIncreaseCaption(state);
        }
    }

    public static void ApplyFightScript(ScriptExecutor? self, string id)
    {
        if (self == null)
        {
            return;
        }

        switch (Normalize(id))
        {
            case TerriasIds.SolarWitchBlessing:
                RegisterSolarWitch(self);
                break;
            case TerriasIds.SunPriestBlessing:
                RegisterSunPriest(self);
                break;
            case TerriasIds.ForgottenOneBlessing:
                RegisterForgottenOne(self);
                break;
        }
    }

    private static void RegisterSolarWitch(ScriptExecutor self)
    {
        const string tokenKey = "TerriasSolarWitchToken";
        var token = ExecutorApi.RegisterHook(self, "TerriasSolarWitchHook", tokenKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(
            self,
            "StartRound",
            tokenKey,
            token!,
            new Action(() => ResolveSolarWitch(self)),
            TerriasIds.SolarWitchBlessing);
    }

    private static void RegisterSunPriest(ScriptExecutor self)
    {
        const string tokenKey = "TerriasSunPriestToken";
        var token = ExecutorApi.RegisterHook(self, "TerriasSunPriestHook", tokenKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(
            self,
            "FightStart",
            tokenKey,
            token!,
            new Action(() =>
            {
                self.SetStatus("Self");
                self.AddBuff(TerriasIds.SolarRadiance, "3");
            }),
            TerriasIds.SunPriestBlessing);
    }

    private static void RegisterForgottenOne(ScriptExecutor self)
    {
        const string tokenKey = "TerriasForgottenOneToken";
        var token = ExecutorApi.RegisterHook(self, "TerriasForgottenOneHook", tokenKey);
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(
            self,
            "FightStart",
            tokenKey,
            token!,
            new Action(() => CardApi.AddCardToDiscardPile(self, TerriasIds.ForgottenCardId)),
            TerriasIds.ForgottenOneBlessing);
        ExecutorApi.TryAddTokenedEvent(
            self,
            "StartRound",
            tokenKey,
            token!,
            new Action(() =>
            {
                var buffId = UnityEngine.Random.Range(0, 2) == 0
                    ? TerriasIds.KeenEdge
                    : TerriasIds.Resilient;
                self.SetStatus("Self");
                self.AddBuff(buffId, "2");
            }),
            TerriasIds.ForgottenOneBlessing);
    }

    private static void ResolveSolarWitch(ScriptExecutor self)
    {
        var candidates = ExecutorApi.EnemyTargets(self)
            .Where(StatusApi.IsAlive)
            .Where(BuffApi.HasRemovablePositiveBuff)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        var removedLevel = BuffApi.RemoveRandomPositiveBuffAndGetLevel(self, target);
        if (removedLevel <= 0)
        {
            return;
        }

        var owner = FightPlayer.Instance?.Status ?? self.Self;
        if (owner == null)
        {
            return;
        }

        var nextMaxHp = (int)Math.Min(int.MaxValue, (long)Math.Max(1, owner.MaxHp) + removedLevel);
        if (PlayerMaxHpApi.TrySetNativeMaxHp(owner, nextMaxHp, true, "Blessing.SolarWitch"))
        {
            PlayerApi.ShowCaption("曜日魔女：生命上限 +" + removedLevel);
        }
    }

    private static string Normalize(string id)
    {
        return TerriasContentIdCompatibility.LocalId(id).TrimStart('*');
    }
}
