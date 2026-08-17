using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class MorningStarCardScripts
{
    private static readonly Dictionary<string, Action<ScriptExecutor>> InitHandlers = new(StringComparer.Ordinal)
    {
        [TerriasIds.ReverseFormulaCardShortId] = self => MorningStarCurseService.InitCard(self, TerriasIds.ReverseFormulaCardShortId),
        [TerriasIds.MorningStarAfterglowCardShortId] = self => MorningStarCurseService.InitCard(self, TerriasIds.MorningStarAfterglowCardShortId),
        [TerriasIds.OmenTransferCardShortId] = self => MorningStarCurseService.InitCard(self, TerriasIds.OmenTransferCardShortId),
        [TerriasIds.AllBeingsAspectCardShortId] = self => MorningStarCurseService.InitCard(self, TerriasIds.AllBeingsAspectCardShortId),
        [TerriasIds.AllBeingsWishCardShortId] = self => MorningStarCurseService.InitCard(self, TerriasIds.AllBeingsWishCardShortId),
        [TerriasIds.AllBeingsFerryCardShortId] = self => MorningStarCurseService.InitCard(self, TerriasIds.AllBeingsFerryCardShortId),
        [TerriasIds.MorningStarElegyCardShortId] = self => MorningStarCurseService.InitCard(self, TerriasIds.MorningStarElegyCardShortId)
    };

    private static readonly Dictionary<string, Action<ScriptExecutor>> UseHandlers = new(StringComparer.Ordinal)
    {
        ["star_map"] = UseStarMap,
        ["blank_star_score"] = UseBlankStarScore,
        ["meter_rewrite"] = UseMeterRewrite,
        ["prewritten_measure"] = UsePrewrittenMeasure,
        ["star_orbit_transpose"] = UseStarOrbitTranspose,
        ["rest_mark"] = UseRestMark,
        ["morning_star_stage"] = UseMorningStarStage,
        ["star_score_echo"] = UseStarScoreEcho,
        [TerriasIds.ReverseFormulaCardShortId] = MorningStarCurseService.UseReverseFormula,
        [TerriasIds.MorningStarAfterglowCardShortId] = MorningStarCurseService.UseMorningStarAfterglow,
        [TerriasIds.OmenTransferCardShortId] = MorningStarCurseService.UseOmenTransfer,
        [TerriasIds.AllBeingsAspectCardShortId] = MorningStarCurseService.UseAllBeingsAspect,
        [TerriasIds.AllBeingsWishCardShortId] = MorningStarCurseService.UseAllBeingsWish,
        [TerriasIds.AllBeingsFerryCardShortId] = MorningStarCurseService.UseAllBeingsFerry,
        [TerriasIds.MorningStarElegyCardShortId] = MorningStarCurseService.UseMorningStarElegy
    };

    public static bool IsMorningStarCard(string id)
    {
        return UseHandlers.ContainsKey(NormalizeId(id));
    }

    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            id = NormalizeId(id);
            if (InitHandlers.TryGetValue(id, out var handler))
            {
                handler(self);
                return;
            }

            ExecutorApi.SetBaseScript(self, "CommonCardItem");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("MorningStar Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            id = NormalizeId(id);
            if (UseHandlers.TryGetValue(id, out var handler))
            {
                handler(self);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("MorningStar Use failed: " + id, ex);
        }
    }

    private static void UseStarMap(ScriptExecutor self)
    {
        self.SetStatus("Self");
        var existing = CurrentHandConfigs(self);
        self.DrawCount("3");
        AttachMorningStarSealToNewHandCards(self, existing);
        CardApi.SelectAndBurnHandCards(self, 3);
    }

    private static void UseBlankStarScore(ScriptExecutor self)
    {
        StarScoreService.ClearCurrentNotes(self);
        self.SetStatus("Self");
        self.AddBuff(TerriasIds.StarBlessing, "1");
        self.DrawCount("1");
    }

    private static void UseMeterRewrite(ScriptExecutor self)
    {
        StarScoreService.CycleLastNote(self);
    }

    private static void UsePrewrittenMeasure(ScriptExecutor self)
    {
        MorningStarOvertureService.SchedulePrelude(self, StarScoreNote.Sustain);
        if (MorningStarOvertureService.HasEncore(1))
        {
            MorningStarOvertureService.SchedulePrelude(self, StarScoreNote.Turn);
        }
    }

    private static void UseStarOrbitTranspose(ScriptExecutor self)
    {
        foreach (var note in StarScoreService.ClearCurrentNotesAndReturn(self))
        {
            CardApi.AddCardToHand(self, MorningStarOvertureService.CardIdForNote(note));
        }
    }

    private static void UseRestMark(ScriptExecutor self)
    {
        var cleared = StarScoreService.ClearCurrentNotes(self);
        if (cleared <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(TerriasIds.Resonance, cleared.ToString());
        self.DrawCount(cleared.ToString());
    }

    private static void UseMorningStarStage(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(TerriasIds.StarStage, "1");
    }

    private static void UseStarScoreEcho(ScriptExecutor self)
    {
        if (!StarScoreService.ReplayMostRecentCadence(self))
        {
            CardApi.AddCardToHand(self, TerriasIds.StellarOvertureStartCardId);
            CardApi.AddCardToHand(self, TerriasIds.StellarOvertureSustainCardId);
            CardApi.AddCardToHand(self, TerriasIds.StellarOvertureTurnCardId);
            return;
        }

        MorningStarOvertureService.Compose(self);
    }

    private static HashSet<IDataConfig> CurrentHandConfigs(ScriptExecutor self)
    {
        return (self.HandCard ?? Enumerable.Empty<CardItem>())
            .Select(card => card?.dataConfig)
            .Where(config => config != null)
            .Cast<IDataConfig>()
            .ToHashSet();
    }

    private static void AttachMorningStarSealToNewHandCards(ScriptExecutor self, HashSet<IDataConfig> existing)
    {
        foreach (var card in self.HandCard ?? Enumerable.Empty<CardItem>())
        {
            if (card?.dataConfig != null && !existing.Contains(card.dataConfig))
            {
                CardMutationService.AddSpecialTags(card, TerriasIds.MorningStarSealTag);
            }
        }
    }

    private static string NormalizeId(string id)
    {
        return (id ?? "").Replace("*", "").Trim();
    }
}
