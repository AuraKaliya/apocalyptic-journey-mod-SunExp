using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class MorningStarCardScripts
{
    private static readonly Dictionary<string, Action<ScriptExecutor>> UseHandlers = new(StringComparer.Ordinal)
    {
        ["star_map"] = UseStarMap,
        ["blank_star_score"] = UseBlankStarScore,
        ["meter_rewrite"] = UseMeterRewrite,
        ["prewritten_measure"] = UsePrewrittenMeasure,
        ["star_orbit_transpose"] = UseStarOrbitTranspose,
        ["rest_mark"] = UseRestMark,
        ["morning_star_stage"] = UseMorningStarStage,
        ["star_score_echo"] = UseStarScoreEcho
    };

    public static bool IsMorningStarCard(string id)
    {
        return UseHandlers.ContainsKey(NormalizeId(id));
    }

    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, "CommonCardItem");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("MorningStar Init failed: " + id, ex);
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
            SunExpLog.Error("MorningStar Use failed: " + id, ex);
        }
    }

    private static void UseStarMap(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.DrawCount("3");
        ThrowHandCards(self, 2);
    }

    private static void UseBlankStarScore(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.DrawCount("1");
        if (StarScoreService.IsScoreEmpty(self))
        {
            self.AddBuff(SunExpIds.StarBlessing, "1");
        }
    }

    private static void UseMeterRewrite(ScriptExecutor self)
    {
        if (StarScoreService.CycleLastNote(self))
        {
            return;
        }

        CardApi.AddCardToHand(self, SunExpIds.StellarOvertureStartCardId);
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
        MorningStarOvertureService.SelectHandCardForTranspose(self);
    }

    private static void UseRestMark(ScriptExecutor self)
    {
        var cleared = StarScoreService.ClearCurrentNotes(self);
        if (cleared <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(SunExpIds.Resonance, cleared.ToString());
        if (cleared >= 2)
        {
            self.AddBuff(SunExpIds.StarBlessing, "1");
        }
    }

    private static void UseMorningStarStage(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.StarStage, "1");
    }

    private static void UseStarScoreEcho(ScriptExecutor self)
    {
        if (!StarScoreService.ReplayMostRecentCadence(self))
        {
            CardApi.AddCardToHand(self, SunExpIds.StellarOvertureStartCardId);
            CardApi.AddCardToHand(self, SunExpIds.StellarOvertureSustainCardId);
            CardApi.AddCardToHand(self, SunExpIds.StellarOvertureTurnCardId);
            return;
        }

        MorningStarOvertureService.Compose(self);
    }

    private static void ThrowHandCards(ScriptExecutor self, int count)
    {
        if (count <= 0 || CardApi.HandCardCount(self) <= 0)
        {
            return;
        }

        try
        {
            self.ThrowCard(count.ToString(), "Hand");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Star Map discard skipped: " + ex.Message);
        }
    }

    private static string NormalizeId(string id)
    {
        return (id ?? "").Replace("*", "").Trim();
    }
}
