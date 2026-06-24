using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class LoneerMiracleService
{
    private const int InitialBlackStones = 9;
    private const int InitialWhiteStones = 1;
    private const int InitialClockMax = 12;
    private const int MinClockMax = 6;
    private const int MinBlackStones = 1;
    private const int PrayerCooldownRounds = 2;
    private const string BlackStone = "B";
    private const string WhiteStone = "W";
    private static readonly string[] MorningPrayerCooldownKeys =
    {
        SunExpIds.LoneerMorningPrayerSkillCardId,
        "loneer_morning_star_prayer",
        "*loneer_morning_star_prayer"
    };

    public static void RegisterCareer(ScriptExecutor self)
    {
        PlayerApi.SetGameVar(SunExpIds.LoneerActive, "1");
        SetMorningPrayerCooldown(null, 0);

        var token = (DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpLoneerCareerToken", "0")) + 1).ToString();
        var fightStartRegistered = ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
        {
            if (ExecutorApi.IsHookTokenActive(self, "SunExpLoneerCareerToken", token))
            {
                OnFightStart(self);
            }
        }), "loneer_career");
        var startRoundRegistered = ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            if (ExecutorApi.IsHookTokenActive(self, "SunExpLoneerCareerToken", token))
            {
                TickMorningPrayerCooldown(self);
            }
        }), "loneer_career");

        ExecutorApi.TryAddEvent(self, "Win", new Action(() => EndCombatCleanup(self)), "loneer_career");
        ExecutorApi.TryAddEvent(self, "Escape", new Action(() => EndCombatCleanup(self)), "loneer_career");

        if (fightStartRegistered && startRoundRegistered)
        {
            ExecutorApi.SetVar(self, "SunExpLoneerCareerHook", "1");
            ExecutorApi.SetVar(self, "SunExpLoneerCareerToken", token);
        }
    }

    public static bool IsActive()
    {
        var careerId = PlayerApi.GetCurrentCareerId();
        if (!string.IsNullOrWhiteSpace(careerId)
            && careerId.IndexOf(SunExpIds.LoneerCareerId, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(careerId)
            && PlayerApi.GetGameVar(SunExpIds.LoneerActive, "0") == "1";
    }

    public static void OnFightStart(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            SunExpLog.Warn("Loneer fight state initialization skipped: owner status unavailable.");
            return;
        }

        var state = LoneerCombatStateStore.ResetForFight(self.Self);
        if (state == null)
        {
            SunExpLog.Warn("Loneer fight state initialization skipped: owner status unavailable.");
            return;
        }

        InitializeState(state);
        StarScoreService.ClearScore(self);
        ClearCombatBuffs(self);
        SyncBuffs(self, state);
        SunExpLog.Info("Loneer fight state initialized: owner=" + self.Self.InstanceId + ", stones=" + state.Stones.Count + ", clock=" + state.ClockValue);
        RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
    }

    public static void OnCardActionAfter(ScriptExecutor self, IDataConfig config)
    {
        if (!IsActive() || self?.Self == null || config == null || IsExcludedActionCard(config))
        {
            return;
        }

        var state = LoneerCombatStateStore.GetOrCreate(self.Self);
        if (state == null || state.ActionResolving)
        {
            return;
        }

        EnsureInitialized(self, state);
        state.ActionResolving = true;
        try
        {
            DrawStone(self, state);
        }
        finally
        {
            state.ActionResolving = false;
        }
    }

    public static void UseMorningStarPrayer(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            SunExpLog.Warn("Morning Star Prayer skipped: Loneer owner status unavailable.");
            return;
        }

        var state = LoneerCombatStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            SunExpLog.Warn("Morning Star Prayer skipped: Loneer owner status unavailable.");
            return;
        }

        EnsureInitialized(self, state);
        var cooldown = MorningPrayerCooldown(state);
        if (cooldown > 0)
        {
            SetMorningPrayerCooldown(state, cooldown);
            PlayerApi.ShowCaption("\u6668\u661f\u7948\u613f\u5c1a\u672a\u51b7\u5374\u3002");
            return;
        }

        if (!TryAddGuidedCard(self, state, "skill"))
        {
            PlayerApi.ShowCaption("\u5c1a\u672a\u9009\u5b9a\u3010\u6307\u5f15\u724c\u3011\u3002");
            RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
            return;
        }

        state.PrayerUseCount += 1;
        ReduceBlackStoneMax(self, state, 2);
        SetMorningPrayerCooldown(state, PrayerCooldownRounds);
    }

    public static void EndCombatCleanup(ScriptExecutor self)
    {
        ClearCombatBuffs(self);
        StarScoreService.RemoveState(self?.Self);
        LoneerCombatStateStore.Remove(self?.Self);
    }

    private static void DrawStone(ScriptExecutor self, LoneerCombatState state)
    {
        if (state.Stones.Count == 0)
        {
            ResetStoneBag(state);
        }

        var stone = state.DrawStone();
        if (stone == WhiteStone)
        {
            TriggerNaturalMorningStar(self, state);
            return;
        }

        PlayerApi.ShowCaption("\u661f\u77f3\u888b\uff1a\u62bd\u51fa\u9ed1\u77f3\u3002");
        ReduceClock(self, state, 1);
    }

    private static void ReduceBlackStoneMax(ScriptExecutor self, LoneerCombatState state, int amount)
    {
        var beforeMax = Math.Max(MinBlackStones, state.BlackStoneMax <= 0 ? InitialBlackStones : state.BlackStoneMax);
        state.BlackStoneMax = Math.Max(MinBlackStones, beforeMax - Math.Max(0, amount));
        TrimBlackStonesToMax(state);
        SyncBuffs(self, state);
        SunExpLog.Info("Loneer black stone cap reduced: owner=" + self.Self.InstanceId
            + ", beforeMax=" + beforeMax
            + ", afterMax=" + state.BlackStoneMax
            + ", currentBlack=" + state.BlackStoneCount(BlackStone)
            + ", prayerUses=" + state.PrayerUseCount);
    }

    private static void TrimBlackStonesToMax(LoneerCombatState state)
    {
        while (state.BlackStoneCount(BlackStone) > state.BlackStoneMax)
        {
            var blackIndexes = state.Stones
                .Select((stone, index) => stone == BlackStone ? index : -1)
                .Where(index => index >= 0)
                .ToList();
            if (blackIndexes.Count == 0)
            {
                return;
            }

            state.RemoveStoneAt(blackIndexes[UnityEngine.Random.Range(0, blackIndexes.Count)]);
        }
    }

    private static void TriggerNaturalMorningStar(ScriptExecutor self, LoneerCombatState state)
    {
        var copiedGuide = state.GuidanceCardId;
        var copied = TryAddGuidedCard(self, state, "natural");
        ResetStoneBag(state);
        StarScoreService.AddStarlight(self, state.ClockMax);
        SyncBuffs(self, state);
        PlayerApi.ShowCaption("\u81ea\u7136\u6668\u661f\uff1a\u83b7\u5f97\u6307\u5f15\u724c\u590d\u5236\u3002");
        SunExpLog.Info("Natural Morning Star resolved: owner=" + self.Self.InstanceId + ", copied=" + copiedGuide + ", success=" + copied);
        RequestGuidanceSelection(self, state, "\u91cd\u65b0\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
    }

    private static void TriggerBorrowedMiracle(ScriptExecutor self, LoneerCombatState state)
    {
        var copiedGuide = state.GuidanceCardId;
        var copied = TryAddGuidedCard(self, state, "borrowed");
        state.ClockMax = Math.Max(MinClockMax, state.ClockMax - 1);
        ResetPouchAndClock(self, state, grantStarlight: true);
        PlayerApi.ShowCaption("\u501f\u6765\u7684\u5947\u8ff9\uff1a\u65f6\u949f\u4e0a\u9650\u4e0b\u964d\u3002");
        SunExpLog.Info("Borrowed Miracle resolved: owner=" + self.Self.InstanceId + ", copied=" + copiedGuide + ", success=" + copied + ", clockMax=" + state.ClockMax);
        RequestGuidanceSelection(self, state, "\u91cd\u65b0\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
    }

    private static void ReduceClock(ScriptExecutor self, LoneerCombatState state, int amount)
    {
        state.ClockValue = Math.Max(0, state.ClockValue - Math.Max(0, amount));
        if (state.ClockValue <= 0)
        {
            TriggerBorrowedMiracle(self, state);
            return;
        }

        SyncBuffs(self, state);
    }

    private static void ResetPouchAndClock(ScriptExecutor self, LoneerCombatState state, bool grantStarlight)
    {
        ResetStoneBag(state);
        state.ClockValue = state.ClockMax;
        if (grantStarlight)
        {
            StarScoreService.AddStarlight(self, state.ClockMax);
        }

        SyncBuffs(self, state);
    }

    private static void EnsureInitialized(ScriptExecutor self, LoneerCombatState state)
    {
        if (state.Initialized)
        {
            return;
        }

        InitializeState(state);
        SyncBuffs(self, state);
        RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
    }

    private static void InitializeState(LoneerCombatState state)
    {
        state.ClockMax = InitialClockMax;
        state.ClockValue = InitialClockMax;
        state.BlackStoneMax = InitialBlackStones;
        state.PrayerCooldown = 0;
        state.PrayerUseCount = 0;
        state.ActionResolving = false;
        ResetStoneBag(state);
        state.Initialized = true;
    }

    private static void RequestGuidanceSelection(ScriptExecutor self, LoneerCombatState state, string caption)
    {
        if (state.SelectionPending || self?.Self == null)
        {
            return;
        }

        var owner = self.Self;
        state.SelectionPending = true;
        var selectionVersion = ++state.SelectionVersion;
        var opened = CardSelectionApi.SelectOneFromRoleDeck(
            self,
            card => !IsExcludedActionCard(CardConfigApi.Id(card)),
            card =>
            {
                var current = LoneerCombatStateStore.Get(owner);
                if (!ReferenceEquals(current, state) || state.SelectionVersion != selectionVersion)
                {
                    return;
                }

                state.SelectionPending = false;
                SetGuidance(state, CardConfigApi.Id(card));
                PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a" + CardDisplayName(card));
                SunExpLog.Info("Loneer guidance selected: owner=" + owner.InstanceId + ", card=" + state.GuidanceCardId + ", version=" + selectionVersion);
            },
            caption);

        if (opened)
        {
            return;
        }

        state.SelectionPending = false;
        var fallback = FirstDeckCardId();
        SetGuidance(state, fallback);
        SunExpLog.Warn("Loneer guidance selection UI unavailable; deterministic fallback=" + state.GuidanceCardId);
        if (!string.IsNullOrWhiteSpace(state.GuidanceCardId))
        {
            PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a" + state.GuidanceCardId);
        }
    }

    private static bool TryAddGuidedCard(ScriptExecutor self, LoneerCombatState state, string source)
    {
        var id = CardApi.ResolveCardId(state.GuidanceCardId);
        if (string.IsNullOrWhiteSpace(id))
        {
            SunExpLog.Warn("Loneer guided card copy skipped: source=" + source + ", guidance=" + state.GuidanceCardId);
            return false;
        }

        var added = CardApi.AddCardToHand(self, id, SunExpIds.LoneerDerivedTag);
        SunExpLog.Info("Loneer guided card copy: owner=" + self.Self.InstanceId + ", source=" + source + ", card=" + id + ", success=" + added);
        return added;
    }

    private static void SetGuidance(LoneerCombatState state, string cardId)
    {
        var resolved = CardApi.ResolveCardId(cardId);
        if (!string.IsNullOrWhiteSpace(resolved) && !IsExcludedActionCard(resolved))
        {
            state.GuidanceCardId = resolved;
        }
    }

    private static void ResetStoneBag(LoneerCombatState state)
    {
        var stones = new List<string>();
        var blackStoneMax = Math.Max(MinBlackStones, state.BlackStoneMax <= 0 ? InitialBlackStones : state.BlackStoneMax);
        state.BlackStoneMax = blackStoneMax;
        for (var i = 0; i < blackStoneMax; i++)
        {
            stones.Add(BlackStone);
        }

        for (var i = 0; i < InitialWhiteStones; i++)
        {
            stones.Add(WhiteStone);
        }

        Shuffle(stones);
        state.ReplaceStones(stones);
    }

    private static void Shuffle(IList<string> stones)
    {
        for (var i = stones.Count - 1; i > 0; i--)
        {
            var j = UnityEngine.Random.Range(0, i + 1);
            (stones[i], stones[j]) = (stones[j], stones[i]);
        }
    }

    private static string FirstDeckCardId()
    {
        foreach (var card in CardSelectionApi.RoleDeckCards(candidate => !IsExcludedActionCard(CardConfigApi.Id(candidate))))
        {
            return CardApi.ResolveCardId(CardConfigApi.Id(card));
        }

        return "";
    }

    private static string CardDisplayName(IDataConfig card)
    {
        try
        {
            var localizedName = card.data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localizedName) && localizedName != "Name")
            {
                return localizedName;
            }

            return DictionaryUtil.Get(card.data, "Name", CardConfigApi.Id(card));
        }
        catch
        {
            return CardConfigApi.Id(card);
        }
    }

    private static bool IsExcludedActionCard(IDataConfig config)
    {
        return IsExcludedActionCard(CardConfigApi.Id(config))
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.data, "Tag"), SunExpIds.LoneerDerivedTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "Tag"), SunExpIds.LoneerDerivedTag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config.Vars, "SpecialTag"), SunExpIds.LoneerDerivedTag);
    }

    private static bool IsExcludedActionCard(string id)
    {
        var value = (id ?? "").Replace("*", "").Trim();
        return string.IsNullOrWhiteSpace(value)
            || StarScoreService.IsStellarOvertureCard(value)
            || value == "loneer_morning_star_prayer"
            || value == SunExpIds.LoneerMorningPrayerSkillCardId;
    }

    private static void SyncBuffs(ScriptExecutor self, LoneerCombatState state)
    {
        BuffApi.SetExactLevel(self?.Self, SunExpIds.StarStonePouch, state.BlackStoneCount(BlackStone));
        BuffApi.SetExactLevel(self?.Self, SunExpIds.MiracleClock, state.ClockValue);
    }

    private static void ClearCombatBuffs(ScriptExecutor self)
    {
        var status = self?.Self;
        if (status == null)
        {
            return;
        }

        foreach (var buffId in new[]
                 {
                     SunExpIds.StarStonePouch,
                     SunExpIds.MiracleClock,
                     SunExpIds.Starlight,
                     SunExpIds.StarBlessing,
                     SunExpIds.StarScore,
                     SunExpIds.Resonance
                 })
        {
            BuffApi.SetExactLevel(status, buffId, 0);
        }
    }

    private static int MorningPrayerCooldown(LoneerCombatState? state)
    {
        return Math.Max(state?.PrayerCooldown ?? 0, MorningPrayerUiCooldown());
    }

    private static int MorningPrayerUiCooldown()
    {
        var cooldown = 0;
        foreach (var key in MorningPrayerCooldownKeys)
        {
            cooldown = Math.Max(cooldown, PlayerApi.GetSkillTime(key));
        }

        return cooldown;
    }

    private static void SetMorningPrayerCooldown(LoneerCombatState? state, int cooldown)
    {
        var next = Math.Max(0, cooldown);
        if (state != null)
        {
            state.PrayerCooldown = next;
        }

        foreach (var key in MorningPrayerCooldownKeys)
        {
            PlayerApi.SetSkillTime(key, next);
        }
    }

    private static void TickMorningPrayerCooldown(ScriptExecutor self)
    {
        var state = LoneerCombatStateStore.Get(self?.Self);
        var cooldown = MorningPrayerCooldown(state);
        if (cooldown > 0)
        {
            SetMorningPrayerCooldown(state, cooldown - 1);
        }
    }
}
