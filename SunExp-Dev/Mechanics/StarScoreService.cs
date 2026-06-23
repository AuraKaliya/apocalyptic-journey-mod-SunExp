using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class StarScoreService
{
    private const string NoteStart = "S";
    private const string NoteSustain = "U";
    private const string NoteTurn = "T";
    private const string NoteClose = "C";

    public static bool IsStellarOvertureCard(string id)
    {
        var value = NormalizeId(id);
        return value == "stellar_overture_start"
            || value == "stellar_overture_sustain"
            || value == "stellar_overture_turn"
            || value == "stellar_overture_close"
            || value == SunExpIds.StellarOvertureStartCardId
            || value == SunExpIds.StellarOvertureSustainCardId
            || value == SunExpIds.StellarOvertureTurnCardId
            || value == SunExpIds.StellarOvertureCloseCardId;
    }

    public static string PreludeCardForCost(int baseCost)
    {
        if (baseCost <= 0)
        {
            return SunExpIds.StellarOvertureStartCardId;
        }

        if (baseCost == 1)
        {
            return SunExpIds.StellarOvertureSustainCardId;
        }

        return baseCost == 2
            ? SunExpIds.StellarOvertureTurnCardId
            : SunExpIds.StellarOvertureCloseCardId;
    }

    public static string PreludeDisplayNameForCost(int baseCost)
    {
        if (baseCost <= 0)
        {
            return "星辰序曲·启";
        }

        if (baseCost == 1)
        {
            return "星辰序曲·承";
        }

        return baseCost == 2 ? "星辰序曲·转" : "星辰序曲·合";
    }

    public static void ClearScore(ScriptExecutor? self)
    {
        StarScoreCombatStateStore.GetOrCreate(self?.Self)?.Clear();
        SyncScoreBuff(self, 0);
    }

    public static void RemoveState(IStatusManager? owner)
    {
        StarScoreCombatStateStore.Remove(owner);
    }

    public static void Init(ScriptExecutor self, string id)
    {
        if (id == "stellar_overture_turn" || id == "stellar_overture_close")
        {
            ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
            if (id == "stellar_overture_close")
            {
                ExecutorApi.AddDamageDescription(self, "1", CalcCloseDamage(self));
            }
            return;
        }

        ExecutorApi.SetBaseScript(self, "CommonCardItem");
        if (id == "stellar_overture_sustain")
        {
            self.AddDescription("1", "Defence", "6");
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        switch (id)
        {
            case "stellar_overture_start":
                UseStart(self);
                Record(self, NoteStart);
                break;
            case "stellar_overture_sustain":
                UseSustain(self);
                Record(self, NoteSustain);
                break;
            case "stellar_overture_turn":
                UseTurn(self);
                Record(self, NoteTurn);
                break;
            case "stellar_overture_close":
                UseClose(self);
                Record(self, NoteClose);
                break;
        }
    }

    public static void AddStarlight(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var before = ExecutorApi.SelfBuffLevel(self, SunExpIds.Starlight);
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.Starlight, amount.ToString());
        var after = ExecutorApi.SelfBuffLevel(self, SunExpIds.Starlight);
        GrantBlessingsForThresholds(self, before, after);
        ClampStarlight(self);
    }

    public static void TryApplyResonanceBeforeAddBuff(object[]? args)
    {
        if (args == null || args.Length == 0 || ExecutorApi.CombatIntGet("SunExpStarScorePlayerActionPending") <= 0)
        {
            return;
        }

        var player = FightPlayer.Instance?.Status;
        if (player == null || BuffApi.Level(player, SunExpIds.Resonance) <= 0)
        {
            return;
        }

        var buffId = BuffIdFromArgs(args);
        if (string.IsNullOrWhiteSpace(buffId)
            || buffId == SunExpIds.Resonance
            || buffId == SunExpIds.StarScore
            || (!BuffApi.IsPositiveBuffId(buffId) && !BuffApi.IsNegativeBuffId(buffId)))
        {
            return;
        }

        if (!IncreaseBuffAmountArg(args))
        {
            return;
        }

        ConsumeBuff(player, SunExpIds.Resonance, 1);
    }

    private static void UseStart(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.DrawCount("1");
        if ((self.HandCard?.Count ?? 0) >= 5)
        {
            self.AddBuff(SunExpIds.Resonance, "1");
        }
    }

    private static void UseSustain(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.ChangeDefence("6");
        BuffApi.IncreaseRandomPositiveBuff(self.Self, 1);
    }

    private static void UseTurn(ScriptExecutor self)
    {
        var target = ExecutorApi.PrimaryTarget(self);
        ExecutorApi.SetStatusForTarget(self, target, "Target");
        if (!BuffApi.IncreaseRandomNegativeBuff(target, 1))
        {
            self.AddBuff("buff_weak", "1");
        }
    }

    private static void UseClose(ScriptExecutor self)
    {
        ExecutorApi.DealDamage(self, CalcCloseDamage(self));
    }

    private static void Record(ScriptExecutor self, string note)
    {
        var state = StarScoreCombatStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            SunExpLog.Warn("Star score record skipped: owner status unavailable.");
            return;
        }

        state.Record(note, 3);
        var notes = state.Notes.ToList();
        SyncScoreBuff(self, notes.Count);
        if (notes.Count == 3)
        {
            ResolveCadence(self, notes);
        }
    }

    private static void ResolveCadence(ScriptExecutor self, IReadOnlyList<string> notes)
    {
        var pattern = string.Join("", notes);
        switch (pattern)
        {
            case NoteStart + NoteStart + NoteStart:
                self.SetStatus("Self");
                self.DrawCount("2");
                self.AddBuff(SunExpIds.Resonance, "1");
                PlayerApi.ShowCaption("星律：急板");
                break;
            case NoteSustain + NoteSustain + NoteSustain:
                self.SetStatus("Self");
                self.ChangeDefence("8");
                BuffApi.IncreaseRandomPositiveBuff(self.Self, 1);
                BuffApi.IncreaseRandomPositiveBuff(self.Self, 1);
                PlayerApi.ShowCaption("星律：长音");
                break;
            case NoteTurn + NoteTurn + NoteTurn:
                foreach (var target in ExecutorApi.EnemyTargets(self))
                {
                    if (!BuffApi.IncreaseRandomNegativeBuff(target, 1))
                    {
                        ExecutorApi.AddStatusBuff(self, target, "buff_weak", 1);
                    }
                }
                PlayerApi.ShowCaption("星律：失谐");
                break;
            case NoteClose + NoteClose + NoteClose:
                foreach (var target in ExecutorApi.EnemyTargets(self))
                {
                    ExecutorApi.SetStatusForTarget(self, target, "Target");
                    ExecutorApi.DealDamage(self, 8);
                }
                PlayerApi.ShowCaption("星律：终止式");
                break;
            case NoteStart + NoteSustain + NoteTurn:
                self.SetStatus("Self");
                self.AddBuff(SunExpIds.Resonance, "2");
                PlayerApi.ShowCaption("星律：调律");
                break;
            case NoteSustain + NoteTurn + NoteClose:
                ExecutorApi.DealDamageToTarget(self, ExecutorApi.PrimaryTarget(self), 10);
                self.SetStatus("Self");
                self.ChangeDefence("6");
                PlayerApi.ShowCaption("星律：合奏");
                break;
            case NoteTurn + NoteSustain + NoteStart:
                self.SetStatus("Self");
                if (BuffApi.RemoveNegativeBuffs(self, self.Self))
                {
                    self.DrawCount("1");
                }
                else
                {
                    self.ChangeDefence("6");
                }
                PlayerApi.ShowCaption("星律：回旋");
                break;
            default:
                if (notes.Distinct().Count() == 3)
                {
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.Resonance, "1");
                    self.DrawCount("1");
                    PlayerApi.ShowCaption("星律：三声和弦");
                }
                break;
        }
    }

    private static int CalcCloseDamage(ScriptExecutor self)
    {
        return 6 + BuffApi.PositiveKindCount(self.Self) + BuffApi.NegativeKindCount(ExecutorApi.PrimaryTarget(self));
    }

    private static void GrantBlessingsForThresholds(ScriptExecutor self, int before, int after)
    {
        var gain = 0;
        if (before < 10 && after >= 10)
        {
            gain += 1;
        }

        if (before < 20 && after >= 20)
        {
            gain += 1;
        }

        if (before < 30 && after >= 30)
        {
            gain += 2;
        }

        if (gain > 0)
        {
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.StarBlessing, gain.ToString());
            PlayerApi.ShowCaption("星辰祝福+" + gain + "：下一张手牌将生成星辰序曲。");
        }
    }

    private static void ClampStarlight(ScriptExecutor self)
    {
        var level = ExecutorApi.SelfBuffLevel(self, SunExpIds.Starlight);
        if (level < 30)
        {
            return;
        }

        self.SetStatus("Self");
        self.RemoveBuff(SunExpIds.Starlight);
    }

    private static void SyncScoreBuff(ScriptExecutor? self, int level)
    {
        BuffApi.SetExactLevel(self?.Self, SunExpIds.StarScore, level);
    }

    private static string BuffIdFromArgs(object[] args)
    {
        if (args[0] is IBuffItemConfig config)
        {
            return config.BuffId ?? "";
        }

        return Convert.ToString(args[0]) ?? "";
    }

    private static bool IncreaseBuffAmountArg(object[] args)
    {
        if (args[0] is IBuffItemConfig config)
        {
            config.Level += 1;
            return true;
        }

        if (args.Length < 2)
        {
            return false;
        }

        var amount = DictionaryUtil.ParseInt(Convert.ToString(args[1]));
        args[1] = amount + 1;
        return true;
    }

    private static void ConsumeBuff(IStatusManager status, string buffId, int amount)
    {
        var buff = status.GetBuff(buffId);
        var level = buff?.buffConfig?.Level ?? 0;
        if (level <= amount)
        {
            status.RemoveBuff(buffId);
        }
        else if (buff?.buffConfig != null)
        {
            buff.buffConfig.Level = level - amount;
        }
    }

    private static string NormalizeId(string id)
    {
        return (id ?? "").Replace("*", "").Trim();
    }
}
