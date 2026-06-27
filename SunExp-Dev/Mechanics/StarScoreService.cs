using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class StarScoreService
{
    private const string NoteStart = StarScoreNoteCodes.Opening;
    private const string NoteSustain = StarScoreNoteCodes.Sustain;
    private const string NoteTurn = StarScoreNoteCodes.Turn;
    private const string NoteClose = StarScoreNoteCodes.Close;

    public static event Action<StarScoreDisplaySnapshot>? Changed;

    public static bool IsStellarOvertureCard(string id)
    {
        return StarScoreNoteCodes.TryFromCardId(id, out _);
    }

    public static bool IsWitchStarScoreCard(string id)
    {
        var value = NormalizeId(id);
        return value == "witch_star_score"
            || value == SunExpIds.WitchStarScoreCardId;
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
        var owner = self?.Self;
        var state = StarScoreCombatStateStore.GetOrCreate(owner);
        state?.Clear();
        SyncScoreBuff(self, 0);
        if (state != null)
        {
            PublishChanged(owner, state);
        }
    }

    public static void RemoveState(IStatusManager? owner)
    {
        StarScoreCombatStateStore.Remove(owner);
    }

    public static void Init(ScriptExecutor self, string id)
    {
        if (StarScoreNoteCodes.TryFromCardId(id, out var note)
            && (note == StarScoreNote.Turn || note == StarScoreNote.Close))
        {
            ExecutorApi.SetBaseScript(self, "AttackCardItem", canSelf: false);
            if (note == StarScoreNote.Close)
            {
                ExecutorApi.AddDamageDescription(self, "1", CalcCloseDamage(self));
            }
            return;
        }

        ExecutorApi.SetBaseScript(self, "CommonCardItem");
        if (IsWitchStarScoreCard(id))
        {
            return;
        }

        if (note == StarScoreNote.Sustain)
        {
            self.AddDescription("1", "Defence", "6");
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        if (StarScoreNoteCodes.TryFromCardId(id, out var note))
        {
            switch (note)
            {
                case StarScoreNote.Opening:
                UseStart(self);
                    break;
                case StarScoreNote.Sustain:
                    UseSustain(self);
                    break;
                case StarScoreNote.Turn:
                    UseTurn(self);
                    break;
                case StarScoreNote.Close:
                    UseClose(self);
                    break;
            }

            Record(self, note);
            return;
        }

        if (IsWitchStarScoreCard(id))
        {
            ReplayCompletedCadences(self);
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
        self.AddBuff(SunExpIds.Resonance, "1");
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
        self.AddBuff("buff_weak", "1");
        self.AddBuff("buff_vulnerability", "1");
    }

    private static void UseClose(ScriptExecutor self)
    {
        ExecutorApi.DealDamage(self, CalcCloseDamage(self));
    }

    private static void Record(ScriptExecutor self, StarScoreNote note)
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
            var pattern = StarScoreNoteCodes.PatternFromNotes(notes);
            state.RecordCompletedCadence(pattern);
            PublishChanged(self.Self, state, isCadencePreview: true, completedCadencePattern: pattern);
            ResolveCadence(self, notes);
            state.RetainLastNoteAsCadenceStart();
            SyncScoreBuff(self, state.Notes.Count);
            PublishChanged(self.Self, state);
            return;
        }

        PublishChanged(self.Self, state);
    }

    private static void ResolveCadence(ScriptExecutor self, IReadOnlyList<StarScoreNote> notes)
    {
        ApplyCadenceEffect(self, StarScoreNoteCodes.PatternFromNotes(notes));
    }

    private static void ReplayCompletedCadences(ScriptExecutor self)
    {
        var state = StarScoreCombatStateStore.GetOrCreate(self.Self);
        var completed = state?.CompletedCadences.ToList() ?? new List<string>();
        if (completed.Count == 0)
        {
            PlayerApi.ShowCaption("\u9b54\u5973\u7684\u661f\u8c31\uff1a\u5c1a\u672a\u5b8c\u6210\u661f\u8c31\u3002");
            return;
        }

        foreach (var pattern in completed)
        {
            ApplyCadenceEffect(self, pattern);
        }

        PlayerApi.ShowCaption("\u9b54\u5973\u7684\u661f\u8c31\uff1a\u91cd\u594f\u5df2\u5b8c\u6210\u661f\u8c31\u00d7" + completed.Count + "\u3002");
    }

    private static void ApplyCadenceEffect(ScriptExecutor self, string pattern)
    {
        switch (pattern)
        {
            case NoteStart + NoteStart + NoteStart:
                DrawCards(self, 1);
                AddBuffToFriendlyParty(self, SunExpIds.Resonance, 1);
                PlayerApi.ShowCaption("星律：急板");
                break;
            case NoteSustain + NoteSustain + NoteSustain:
                ChangeDefenceForFriendlyParty(self, 8);
                IncreaseRandomPositiveBuffsForFriendlyParty(self, 2);
                PlayerApi.ShowCaption("星律：长音");
                break;
            case NoteTurn + NoteTurn + NoteTurn:
                foreach (var target in ExecutorApi.EnemyTargets(self))
                {
                    BuffApi.IncreaseAllNegativeBuffs(target, 2);
                }
                PlayerApi.ShowCaption("星律：失谐");
                break;
            case NoteClose + NoteClose + NoteClose:
                DealDamageAllEnemies(self, CalcPartyCloseDamage(self, 8));
                PlayerApi.ShowCaption("星律：终止式");
                break;
            case NoteStart + NoteSustain + NoteTurn:
                self.SetStatus("Self");
                self.ChangePower("1");
                AddBuffToFriendlyParty(self, SunExpIds.Resonance, 1);
                PlayerApi.ShowCaption("星律：调律");
                break;
            case NoteSustain + NoteTurn + NoteClose:
                var hitCount = DealDamageAllEnemies(self, 10);
                self.SetStatus("Self");
                self.ChangeDefence(Math.Max(0, hitCount * 10).ToString());
                PlayerApi.ShowCaption("星律：合奏");
                break;
            case NoteTurn + NoteSustain + NoteStart:
                var selfCleared = 0;
                foreach (var target in ExecutorApi.FriendlyTargets(self, includeSelf: true))
                {
                    var cleared = BuffApi.RemoveNegativeBuffsAndCount(self, target);
                    if (ExecutorApi.IsSelf(self, target))
                    {
                        selfCleared = cleared;
                    }
                }

                if (selfCleared > 0)
                {
                    DrawCards(self, selfCleared);
                }

                PlayerApi.ShowCaption("星律：回旋");
                break;
            default:
                self.SetStatus("Self");
                self.AddBuff(SunExpIds.Resonance, "1");
                self.DrawCount("1");
                PlayerApi.ShowCaption("星律：三声和弦");
                break;
        }
    }

    private static int CalcCloseDamage(ScriptExecutor self)
    {
        return 6 + BuffApi.BuffKindCount(self.Self) + BuffApi.BuffKindCount(ExecutorApi.PrimaryTarget(self));
    }

    private static int CalcPartyCloseDamage(ScriptExecutor self, int baseDamage)
    {
        var allies = ExecutorApi.FriendlyTargets(self, includeSelf: true);
        var enemies = ExecutorApi.EnemyTargets(self);
        return Math.Max(0, baseDamage)
            + BuffApi.PartyBuffKindSum(allies)
            + BuffApi.PartyBuffKindSum(enemies);
    }

    private static void DrawCards(ScriptExecutor self, int count)
    {
        if (count <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.DrawCount(count.ToString());
    }

    private static void AddBuffToFriendlyParty(ScriptExecutor self, string buffId, int amount)
    {
        foreach (var target in ExecutorApi.FriendlyTargets(self, includeSelf: true))
        {
            ExecutorApi.AddStatusBuff(self, target, buffId, amount, "Self");
        }
    }

    private static void ChangeDefenceForFriendlyParty(ScriptExecutor self, int amount)
    {
        foreach (var target in ExecutorApi.FriendlyTargets(self, includeSelf: true))
        {
            ExecutorApi.SetStatusForTarget(self, target, "Self");
            self.ChangeDefence(amount.ToString());
        }
    }

    private static void IncreaseRandomPositiveBuffsForFriendlyParty(ScriptExecutor self, int times)
    {
        var count = Math.Max(0, times);
        if (count <= 0)
        {
            return;
        }

        foreach (var target in ExecutorApi.FriendlyTargets(self, includeSelf: true))
        {
            ExecutorApi.SetStatusForTarget(self, target, "Self");
            self.RandomAddGoodBuff(count.ToString());
        }
    }

    private static int DealDamageAllEnemies(ScriptExecutor self, int amount)
    {
        var hitCount = 0;
        foreach (var target in ExecutorApi.EnemyTargets(self))
        {
            if (ExecutorApi.DealDamageToTarget(self, target, amount))
            {
                hitCount++;
            }
        }

        return hitCount;
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

        if (before < 30 && after >= 30)
        {
            CardApi.AddCardToHand(self, SunExpIds.StellarOvertureCloseCardId);
            PlayerApi.ShowCaption("星辉抵达30：获得星辰序曲·合。");
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

    private static void PublishChanged(
        IStatusManager? owner,
        StarScoreCombatState state,
        bool isCadencePreview = false,
        string completedCadencePattern = "")
    {
        var handlers = Changed;
        if (handlers == null)
        {
            return;
        }

        var snapshot = state.Snapshot(owner?.InstanceId ?? "", isCadencePreview, completedCadencePattern);
        foreach (Action<StarScoreDisplaySnapshot> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(snapshot);
            }
            catch (Exception ex)
            {
                SunExpLog.Error("Star score display subscriber failed", ex);
            }
        }
    }
}
