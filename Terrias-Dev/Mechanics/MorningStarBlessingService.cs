using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class MorningStarBlessingService
{
    private static readonly Dictionary<string, Action<ScriptExecutor>> FightHandlers = new(StringComparer.Ordinal)
    {
        [TerriasIds.DreamTalkerBlessing] = RegisterDreamTalker,
        [TerriasIds.DeliriousTalkerBlessing] = RegisterDeliriousTalker,
        [TerriasIds.WisherBlessing] = RegisterWisher,
        [TerriasIds.UnspeakableOneBlessing] = RegisterUnspeakableOne,
        [TerriasIds.WitheredOneBlessing] = RegisterWitheredOne,
        [TerriasIds.BlindOneBlessing] = RegisterBlindOne
    };

    public static void ApplyFightScript(ScriptExecutor? self, string id)
    {
        if (self == null)
        {
            return;
        }

        var localId = TerriasContentIdCompatibility.LocalId(id).TrimStart('*');
        if (FightHandlers.TryGetValue(localId, out var handler))
        {
            handler(self);
        }
    }

    private static void RegisterDreamTalker(ScriptExecutor self)
    {
        Register(
            self,
            TerriasIds.DreamTalkerBlessing,
            new[] { TerriasIds.DreamCardId },
            () => AddSelfBuff(self, TerriasIds.Evergreen, 1));
    }

    private static void RegisterDeliriousTalker(ScriptExecutor self)
    {
        Register(
            self,
            TerriasIds.DeliriousTalkerBlessing,
            new[] { TerriasIds.ThoughtDisorderCardId },
            () => AddSelfBuff(self, TerriasIds.KeenEdge, 2));
    }

    private static void RegisterWisher(ScriptExecutor self)
    {
        Register(
            self,
            TerriasIds.WisherBlessing,
            new[] { TerriasIds.PhantomPainCardId },
            () => AddSelfBuff(self, TerriasIds.VowPower, 2));
    }

    private static void RegisterUnspeakableOne(ScriptExecutor self)
    {
        Register(
            self,
            TerriasIds.UnspeakableOneBlessing,
            new[] { TerriasIds.HiddenIllnessCardId, TerriasIds.AbyssDeficitCardId },
            () =>
            {
                self.SetStatus("Self");
                self.ChangeHp("-5");
                self.SetStatus("Self");
                self.ChangePower("1");
            });
    }

    private static void RegisterWitheredOne(ScriptExecutor self)
    {
        Register(
            self,
            TerriasIds.WitheredOneBlessing,
            new[] { TerriasIds.DecayCardId },
            () =>
            {
                var owner = self?.Self;
                if (owner == null)
                {
                    return;
                }

                var recovery = MorningStarBlessingFormula.MissingHealthRecovery(
                    owner.MaxHp,
                    owner.CurHp);
                if (recovery <= 0)
                {
                    return;
                }

                self!.SetStatus("Self");
                self.ChangeHp(recovery.ToString());
            });
    }

    private static void RegisterBlindOne(ScriptExecutor self)
    {
        Register(
            self,
            TerriasIds.BlindOneBlessing,
            new[] { TerriasIds.FearCardId },
            () => CombatCardApi.TryDrawPlayerCards(1, "Blessing.BlindOne"));
    }

    private static void Register(
        ScriptExecutor self,
        string blessingId,
        IReadOnlyList<string> combatStartCards,
        Action roundStartEffect)
    {
        var tokenKey = "TerriasMorningStarBlessingToken_" + blessingId;
        var token = ExecutorApi.RegisterHook(self, "TerriasMorningStarBlessingHook_" + blessingId, tokenKey);
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
                foreach (var cardId in combatStartCards)
                {
                    CardApi.AddCardToDiscardPile(self, cardId);
                }
            }),
            blessingId);
        ExecutorApi.TryAddTokenedEvent(
            self,
            "StartRound",
            tokenKey,
            token,
            roundStartEffect,
            blessingId);
    }

    private static void AddSelfBuff(ScriptExecutor self, string buffId, int amount)
    {
        self.SetStatus("Self");
        self.AddBuff(buffId, Math.Max(1, amount).ToString());
    }
}
