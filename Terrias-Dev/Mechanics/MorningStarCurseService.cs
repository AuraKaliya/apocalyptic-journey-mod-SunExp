using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class MorningStarCurseService
{
    private static int elegySequence;

    public static void InitCard(ScriptExecutor self, string id)
    {
        var normalized = Normalize(id);
        if (normalized == TerriasIds.OmenTransferCardShortId)
        {
            ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: true);
        }
        else
        {
            ExecutorApi.SetBaseScript(self, "CommonCardItem");
        }

        if (normalized == TerriasIds.ReverseFormulaCardShortId
            || normalized == TerriasIds.OmenTransferCardShortId)
        {
            DictionaryUtil.Set(self?.Vars, "Usable", HandCurses(self).Count > 0 ? "1" : "0");
        }
    }

    public static void UseReverseFormula(ScriptExecutor self)
    {
        if (!CanResolveLocal(self))
        {
            return;
        }

        var ordered = HandCurses(self).ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        var selected = new List<IDataConfig>();
        SelectNextReverseFormulaCard(self, ordered, selected);
    }

    public static void UseMorningStarAfterglow(ScriptExecutor self)
    {
        if (!CanResolveLocal(self))
        {
            return;
        }

        var current = self.dataConfig;
        var cards = MorningStarCurseCardApi.AllOwnedCards(self)
            .Where(MorningStarCurseCatalog.IsCurse)
            .Where(card => !SameCard(card, current))
            .ToList();
        var burned = BurnCards(self, cards, rewardReversal: false, out _);
        if (burned <= 0)
        {
            return;
        }

        AddSelfBuff(self, TerriasIds.VowPower, burned);
        StarScoreService.AddStarlight(self, burned);
    }

    public static void UseOmenTransfer(ScriptExecutor self)
    {
        if (!CanResolveLocal(self))
        {
            return;
        }

        var target = self.Target ?? ExecutorApi.PrimaryTargetIncludingSelf(self);
        var curses = HandCurses(self).ToList();
        if (target == null || curses.Count == 0)
        {
            return;
        }

        var opened = CardSelectionApi.SelectOneFromCards(
            self,
            curses,
            _ => true,
            selected => ResolveOmenTransfer(self, selected, target),
            "选择1张手牌诅咒进行转移。",
            interactionHint: new CombatInteractionHint
            {
                OwnerModId = TerriasIds.ModId,
                Purpose = "morning-star-omen-transfer",
                Kind = CombatPromptKind.BurnCards,
                Zone = CombatPromptZone.Hand,
                Forced = true,
                PreferLowestValue = true
            });
        if (!opened)
        {
            ResolveOmenTransfer(self, curses[0], target);
        }
    }

    public static void UseAllBeingsAspect(ScriptExecutor self)
    {
        if (!CanResolveLocal(self))
        {
            return;
        }

        var owned = AdventureBlessingApi.OwnedBlessingIds();
        var missing = MorningStarCurseCatalog.MissingAllBeingsBlessings(owned);
        if (missing.Count == 0)
        {
            AddSelfBuff(self, TerriasIds.VowPower, 3);
            return;
        }

        var selected = missing[UnityEngine.Random.Range(0, missing.Count)];
        if (AdventureBlessingApi.TryGrantLocalAdventureBlessing(self, selected, "MorningStar.AllBeingsAspect"))
        {
            PlayerApi.ShowCaption("众生相：获得【" + selected + "】。");
        }
    }

    public static void UseAllBeingsWish(ScriptExecutor self)
    {
        if (!CanResolveLocal(self))
        {
            return;
        }

        var count = MorningStarCurseCatalog.CountAllBeingsBlessings(AdventureBlessingApi.OwnedBlessingIds());
        AddSelfBuff(self, TerriasIds.VowPower, count);
    }

    public static void UseAllBeingsFerry(ScriptExecutor self)
    {
        if (!CanResolveLocal(self))
        {
            return;
        }

        var cards = HandCurses(self).ToList();
        var burned = BurnCards(self, cards, rewardReversal: false, out _);
        if (burned <= 0)
        {
            return;
        }

        CombatCardApi.TryDrawPlayerCards(burned, "MorningStar.AllBeingsFerry");
        self.SetStatus("Self");
        self.ChangePower(burned.ToString());
    }

    public static void UseMorningStarElegy(ScriptExecutor self)
    {
        if (!CanResolveLocal(self) || self.Self == null)
        {
            return;
        }

        var before = Math.Max(0, self.Self.CurHp);
        var requestedLoss = MorningStarCurseFormula.ElegyHealthLoss(before);
        if (requestedLoss <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.ChangeHp((-requestedLoss).ToString());
        var actualLoss = Math.Max(0, before - Math.Max(0, self.Self.CurHp));
        var count = MorningStarCurseFormula.ElegyTriggerCount(actualLoss, self.Self.MaxHp);
        if (count <= 0)
        {
            return;
        }

        var sequence = elegySequence == int.MaxValue ? elegySequence = 1 : ++elegySequence;
        ResolveElegyDraw(self, count, sequence, 0);
    }

    public static IReadOnlyList<IDataConfig> HandCurses(ScriptExecutor? self)
    {
        return MorningStarCurseCardApi.HandCards(self)
            .Where(MorningStarCurseCatalog.IsCurse)
            .ToList();
    }

    private static void SelectNextReverseFormulaCard(
        ScriptExecutor self,
        IReadOnlyList<IDataConfig> ordered,
        List<IDataConfig> selected)
    {
        var remaining = ordered.Where(card => !selected.Any(item => SameCard(item, card))).ToList();
        if (remaining.Count == 0)
        {
            ResolveReverseFormula(self, ordered, selected);
            return;
        }

        var finish = MorningStarCurseCardApi.CreateFinishSelectionCard(self, selected.Count);
        if (finish == null)
        {
            ResolveReverseFormula(self, ordered, ordered.ToList());
            return;
        }

        var source = new List<IDataConfig>(remaining) { finish };
        var opened = CardSelectionApi.SelectOneFromCards(
            self,
            source,
            _ => true,
            card =>
            {
                if (SameCard(card, finish))
                {
                    if (selected.Count > 0)
                    {
                        ResolveReverseFormula(self, ordered, selected);
                    }

                    return;
                }

                selected.Add(card);
                SelectNextReverseFormulaCard(self, ordered, selected);
            },
            selected.Count == 0
                ? "选择诅咒；选择【完成选择】可取消。"
                : "已选择" + selected.Count + "张诅咒；继续选择或完成。",
            () => ResolveReverseFormula(self, ordered, selected),
            new CombatInteractionHint
            {
                OwnerModId = TerriasIds.ModId,
                Purpose = "morning-star-reverse-formula",
                Kind = CombatPromptKind.BurnCards,
                Zone = CombatPromptZone.Hand,
                Forced = false,
                PreferLowestValue = true
            });
        if (!opened)
        {
            ResolveReverseFormula(self, ordered, ordered.ToList());
        }
    }

    private static void ResolveReverseFormula(
        ScriptExecutor self,
        IReadOnlyList<IDataConfig> ordered,
        IReadOnlyList<IDataConfig> selected)
    {
        if (selected.Count == 0)
        {
            return;
        }

        var selectedIds = new HashSet<string>(selected.Select(InstanceId), StringComparer.Ordinal);
        var stableOrder = ordered
            .Where(card => selectedIds.Contains(InstanceId(card)))
            .ToList();
        BurnCards(self, stableOrder, rewardReversal: true, out var reward);
        ApplyReward(self, reward);
    }

    private static int BurnCards(
        ScriptExecutor self,
        IEnumerable<IDataConfig> cards,
        bool rewardReversal,
        out MorningStarCurseReward reward)
    {
        reward = new MorningStarCurseReward();
        var burned = 0;
        foreach (var card in cards)
        {
            if (!MorningStarCurseCatalog.IsCurse(card) || !MorningStarCurseCardApi.TryBurnCard(self, card))
            {
                continue;
            }

            burned++;
            if (rewardReversal)
            {
                reward.Add(MorningStarCurseReversalRegistry.Resolve(
                    MorningStarCurseCatalog.CardId(card),
                    MorningStarCurseCatalog.Rarity(card)));
            }
        }

        return burned;
    }

    private static void ResolveOmenTransfer(
        ScriptExecutor self,
        IDataConfig selected,
        IStatusManager target)
    {
        if (!StatusApi.IsAlive(target) || !MorningStarCurseCardApi.TryBurnCard(self, selected))
        {
            return;
        }

        ExecutorApi.AddStatusBuff(self, target, TerriasIds.Weak, 2, "Target");
        ExecutorApi.AddStatusBuff(self, target, TerriasIds.Vulnerability, 2, "Target");
    }

    private static void ResolveElegyDraw(
        ScriptExecutor self,
        int remaining,
        int sequence,
        int index)
    {
        if (remaining <= 0 || self.Self == null || !StatusApi.IsAlive(self.Self))
        {
            return;
        }

        var cardId = MorningStarCurseCatalog.RandomCurseCardId();
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        var result = CardApi.GrantCardToHand(
            self,
            CardGrantRequest.ToHand(cardId).WithSource("morning-star-elegy"));
        if (!result.Success)
        {
            return;
        }

        StarScoreService.AddStarlight(self, 2);
        if (remaining <= 1)
        {
            return;
        }

        TerriasFrameDispatcher.RunOnceAfterFrames(
            "MorningStarElegy." + sequence + "." + index,
            2,
            () => ResolveElegyDraw(self, remaining - 1, sequence, index + 1));
    }

    private static void ApplyReward(ScriptExecutor self, MorningStarCurseReward reward)
    {
        AddSelfBuff(self, TerriasIds.Resilient, reward.Resilient);
        AddSelfBuff(self, TerriasIds.KeenEdge, reward.KeenEdge);
        AddSelfBuff(self, TerriasIds.Evergreen, reward.Evergreen);
        AddSelfBuff(self, TerriasIds.Extraordinary, reward.Extraordinary);
        AddSelfBuff(self, TerriasIds.Rebirth, reward.Rebirth);

        var impregnable = MorningStarCurseFormula.ImpregnableGain(
            ExecutorApi.SelfBuffLevel(self, TerriasIds.Impregnable),
            reward.Impregnable);
        AddSelfBuff(self, TerriasIds.Impregnable, impregnable);
        AddSelfBuff(self, TerriasIds.VowPower, reward.VowPower);
        if (reward.Power > 0)
        {
            self.SetStatus("Self");
            self.ChangePower(reward.Power.ToString());
        }

        if (reward.Starlight > 0)
        {
            StarScoreService.AddStarlight(self, reward.Starlight);
        }
    }

    private static void AddSelfBuff(ScriptExecutor self, string buffId, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(buffId, amount.ToString());
    }

    private static bool CanResolveLocal(ScriptExecutor? self)
    {
        return self?.Self != null && PlayerApi.IsLocalPlayerOwner(self.Self);
    }

    private static bool SameCard(IDataConfig? left, IDataConfig? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        var leftId = InstanceId(left);
        var rightId = InstanceId(right);
        return leftId.Length > 0 && string.Equals(leftId, rightId, StringComparison.Ordinal);
    }

    private static string InstanceId(IDataConfig? config)
    {
        try
        {
            return (config?.InstanceID ?? "").Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string Normalize(string id)
    {
        return TerriasContentIdCompatibility.LocalId((id ?? "").Trim()).TrimStart('*');
    }
}
