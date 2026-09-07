using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class MoonHomecomingMechanics
{
    private static IStatusManager? powerOwner;
    private static int temporaryPower;
    private static readonly Queue<ScriptExecutor> Offerings = new();
    private static bool selectingOffering;
    private static int selectionGeneration;

    public static bool CanResolve(ScriptExecutor? self)
    {
        return MoonHomecomingCardApi.IsLocalPlayer(self?.Self)
            && StatusApi.IsAlive(self?.Self)
            && AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation;
    }

    public static bool HasOffering(ScriptExecutor self)
    {
        return MoonHomecomingCardApi.HandCards(self)
            .Any(card => !MoonHomecomingCardApi.SameCard(card, self.dataConfig));
    }

    public static void PrepareHomecoming(ScriptExecutor self)
    {
        if (!CanResolve(self)) return;
        var chronicles = MoonHomecomingRules.ReadChronicles(
            MoonHomecomingCardApi.HandCards(self).Select(CardConfigApi.Id));
        DictionaryUtil.Set(self.Vars, MoonHomecomingIds.HomecomingChroniclesKey, ((int)chronicles).ToString());
    }

    public static void UseFrostmoonNewGod(ScriptExecutor self)
    {
        FieldApi.ActivateField(self, TerriasFieldId.MoonDomain, 1,
            FieldActivationIntentCatalog.FrostmoonNewGodIntent);
        if (BuffApi.Level(self.Self, MoonHomecomingIds.FrostmoonMarrow) == 0)
            AddBuff(self, MoonHomecomingIds.FrostmoonMarrow, 1);
    }

    public static void UseFlowerSeaMoonNight(ScriptExecutor self)
    {
        var targets = TargetApi.OpposingSideTargets(self, self.Self).Where(StatusApi.IsAlive).ToArray();
        foreach (var target in targets)
            ElementalReactionService.Apply(self, target, ElementalType.Dendro, "MoonHomecoming.FlowerSea");
        StatusApi.TryAddShield(self.Self, MoonHomecomingRules.Shield(StatusApi.MaxHp(self.Self), 20));
    }

    public static void UseOffering(ScriptExecutor self)
    {
        // Repeated use scripts must choose from the hand left by the previous
        // offering, rather than queue multiple native dialogs with stale cards.
        Offerings.Enqueue(self);
        SelectNextOffering();
    }

    private static void SelectNextOffering()
    {
        if (selectingOffering) return;
        while (Offerings.Count > 0)
        {
            var self = Offerings.Dequeue();
            if (!CanResolve(self)) continue;
            var cards = MoonHomecomingCardApi.HandCards(self)
                .Where(card => !MoonHomecomingCardApi.SameCard(card, self.dataConfig)).ToArray();
            if (cards.Length == 0) continue;
            selectingOffering = true;
            if (OpenOfferingSelection(self, cards)) return;
            selectingOffering = false;
            TerriasLog.Warn("[MoonHomecoming] offering selection could not be opened.");
        }
    }

    private static bool OpenOfferingSelection(ScriptExecutor self, IDataConfig[] cards)
    {
        var generation = selectionGeneration;
        var battle = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        return CardSelectionApi.SelectOneFromCards(self, cards, _ => true, selected =>
        {
            try
            {
                if (generation != selectionGeneration || !CanResolve(self)
                    || battle != AuraBattleLifecycleRouter.CurrentBattleSessionId) return;
                var cost = CardConfigApi.CurrentCost(selected);
                if (!MoonHomecomingCardApi.TryBurnHandCard(self, selected)) return;
                var recovery = MoonHomecomingRules.OfferingRecovery(
                    StatusApi.MaxHp(self.Self), self.Self.CurHp, cost);
                StatusApi.TryHeal(self.Self, recovery);
            }
            finally { CompleteOffering(generation); }
        }, "选择1张手牌供奉：焚毁后按其当前费用恢复生命。",
        onCancelled: () => CompleteOffering(generation), interactionHint: new CombatInteractionHint
        {
            OwnerModId = TerriasIds.ModId,
            Purpose = "moon-homecoming-offering",
            Kind = CombatPromptKind.BurnCards,
            Zone = CombatPromptZone.Hand,
            Forced = true
        });
    }

    private static void CompleteOffering(int generation)
    {
        if (generation != selectionGeneration) return;
        selectingOffering = false;
        SelectNextOffering();
    }

    public static void UseKuutarMorningMist(ScriptExecutor self)
    {
        CombatCardApi.TryDrawPlayerCards(self, 1, "MoonHomecoming.Kuutar");
        AddBuff(self, TerriasIds.GravityRipple, 3);
    }

    public static void UseHomecomingNight(ScriptExecutor self)
    {
        // NativeStarted captures this once before any use script, including when
        // Reappear repeats the use script or the first reward draws another chronicle.
        if (!self.Vars.ContainsKey(MoonHomecomingIds.HomecomingChroniclesKey)) PrepareHomecoming(self);
        var reward = new MoonHomecomingReward((MoonChronicles)DictionaryUtil.GetInt(
            self.Vars, MoonHomecomingIds.HomecomingChroniclesKey));
        if (reward.Power > 0) PlayerPowerApi.TryGainPower(reward.Power);
        if (reward.Draw > 0) CombatCardApi.TryDrawPlayerCards(self, reward.Draw, "MoonHomecoming.Homecoming");
        AddBuff(self, TerriasIds.GravityRipple, reward.Ripples);
        if (reward.ExtraUses > 0)
        {
            self.SetStatus("Self");
            self.ChangeDynamicVar("UseCount", reward.ExtraUses.ToString());
        }
    }

    public static void UseNewMoonBlessing(ScriptExecutor self)
    {
        PlayerApi.AddMoney(30);
        TruthCurrencyApi.Refund(90);
    }

    public static void UseLuonnotar(ScriptExecutor self)
    {
        AddBuff(self, "buff_rebirth", 30);
        CardApi.AddCardToDiscardPile(self,
            MoonHomecomingRules.RandomChronicleId(UnityEngine.Random.Range(0, 3)));
    }

    public static void DrawFirstChronicle(ScriptExecutor self)
    {
        if (powerOwner != null && !ReferenceEquals(powerOwner, self.Self)) ClearTemporaryPower();
        if (PlayerPowerApi.TryChangeMaxPower(1))
        {
            powerOwner = self.Self;
            temporaryPower++;
        }
    }

    public static void DrawSecondChronicle(ScriptExecutor self)
    {
        GrowAdventureMaxHp(self.Self, 5, "MoonHomecoming.ChronicleII");
    }

    public static void DrawThirdChronicle(ScriptExecutor self)
    {
        StatusApi.TryAddShield(self.Self, MoonHomecomingRules.Shield(StatusApi.MaxHp(self.Self), 10));
    }

    public static void ResolveReactionGrowth(IStatusManager source, ElementalReactionType reaction)
    {
        if (!MoonHomecomingRules.IsMarrowReaction(reaction)
            || !MoonHomecomingCardApi.IsLocalPlayer(source)
            || !StatusApi.IsAlive(source)
            || BuffApi.Level(source, MoonHomecomingIds.FrostmoonMarrow) <= 0) return;
        GrowAdventureMaxHp(source, MoonHomecomingRules.MarrowGrowth(StatusApi.MaxHp(source)),
            "MoonHomecoming.FrostmoonMarrow");
    }

    public static void ClearTemporaryPower()
    {
        var owner = powerOwner;
        var amount = temporaryPower;
        powerOwner = null;
        temporaryPower = 0;
        var player = FightPlayer.Instance;
        if (amount <= 0 || player == null || !ReferenceEquals(owner, player.Status)) return;
        PlayerPowerApi.TryChangeMaxPower(-Math.Min(amount, player.MaxPowerCount));
        // The native maximum setter adjusts current power by the same delta but
        // does not clamp it. Clear only this pack's bonus, keeping other bonuses.
        if (player.CurPowerCount < 0) PlayerPowerApi.TrySetPower(0);
    }

    public static void EndBattle()
    {
        unchecked { selectionGeneration++; }
        Offerings.Clear();
        selectingOffering = false;
        ClearTemporaryPower();
    }

    private static void GrowAdventureMaxHp(IStatusManager source, int growth, string origin)
    {
        if (growth <= 0) return;
        PlayerMaxHpApi.TrySetNativeMaxHp(source,
            MoonHomecomingRules.AddMaximumHp(StatusApi.MaxHp(source), growth), persistRole: true, origin);
    }

    private static void AddBuff(ScriptExecutor self, string buffId, int amount)
    {
        if (amount <= 0) return;
        self.SetStatus("Self");
        self.AddBuff(buffId, amount.ToString());
    }
}
