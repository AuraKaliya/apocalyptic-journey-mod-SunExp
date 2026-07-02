using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class LoneerMiracleService
{
    private const int InitialClockMax = 12;
    private const int MinClockMax = 6;
    private const int PrayerCooldownRounds = 2;
    private static readonly string[] MorningPrayerCooldownKeys =
    {
        SunExpIds.LoneerMorningPrayerSkillCardId,
        "loneer_morning_star_prayer",
        "*loneer_morning_star_prayer"
    };

    public static void RegisterCareer(ScriptExecutor self)
    {
        PlayerApi.SetGameVar(SunExpIds.LoneerActive, "1");
        SetMorningPrayerCooldown(self, null, 0);
        StarStonePouchService.Drawn -= OnStarStonePouchDrawn;
        StarStonePouchService.Drawn += OnStarStonePouchDrawn;

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
        if (PolymorphStateStore.IsLocalRoleSuppressed(SunExpIds.LoneerCareerId))
        {
            return false;
        }

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
        if (!IsActive())
        {
            return;
        }

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
        StarStonePouchService.GrantInitial(self);
        SyncBuffs(self, state);
        SunExpLog.Info("Loneer fight state initialized: owner=" + self.Self.InstanceId
            + ", starStoneBlack=" + StarStonePouchService.CurrentBlackStones(self)
            + ", clock=" + state.ClockValue);
        RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
    }

    private static void OnStarStonePouchDrawn(ScriptExecutor self, StarStonePouchDrawResult result)
    {
        if (!IsActive() || self?.Self == null || result.OwnerStatusId != self.Self.InstanceId)
        {
            return;
        }

        var state = LoneerCombatStateStore.Get(self.Self);
        if (state == null || state.ActionResolving)
        {
            return;
        }

        EnsureInitialized(self, state);
        state.ActionResolving = true;
        try
        {
            if (result.IsWhite)
            {
                TriggerNaturalMorningStar(self, state);
            }
            else if (result.IsBlack)
            {
                ReduceClock(self, state, 1);
            }
        }
        finally
        {
            state.ActionResolving = false;
        }
    }

    public static void UseMorningStarPrayer(ScriptExecutor self)
    {
        if (!IsActive())
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u6d1b\u5948\u5c14\u6280\u80fd\u5df2\u88ab\u5f53\u524d\u5316\u8eab\u8986\u76d6\u3002");
            return;
        }

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

        if (PolymorphCooldownService.TryUseSharedSkill(self, "Loneer.MorningStarPrayer"))
        {
            return;
        }

        EnsureInitialized(self, state);
        var cooldown = MorningPrayerCooldown(state);
        if (cooldown > 0)
        {
            SetMorningPrayerCooldown(self, state, cooldown);
            PlayerApi.ShowCaption("\u6668\u661f\u7948\u613f\u5c1a\u672a\u51b7\u5374\u3002");
            return;
        }

        if (string.IsNullOrWhiteSpace(state.GuidanceCardId))
        {
            PlayerApi.ShowCaption("\u5c1a\u672a\u9009\u5b9a\u3010\u6307\u5f15\u724c\u3011\u3002");
            RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
            return;
        }

        TriggerNaturalMorningStar(self, state);
        state.PrayerUseCount += 1;
        ReduceBlackStoneMax(self, state, 2);
        if (!PolymorphCooldownService.MarkSkillUsed(self, "Loneer.MorningStarPrayer"))
        {
            SetMorningPrayerCooldown(self, state, PrayerCooldownRounds);
        }
        SunExpLog.Info("Morning Star Prayer resolved: owner=" + self.Self.InstanceId
            + ", cooldown=" + state.PrayerCooldown
            + ", blackStoneMax=" + StarStonePouchService.BlackStoneMax(self)
            + ", useCount=" + state.PrayerUseCount);
    }

    public static void EndCombatCleanup(ScriptExecutor self)
    {
        ClearCombatBuffs(self);
        StarScoreService.RemoveState(self?.Self);
        StarStonePouchService.RemoveState(self?.Self);
        LoneerCombatStateStore.Remove(self?.Self);
    }

    private static void ReduceBlackStoneMax(ScriptExecutor self, LoneerCombatState state, int amount)
    {
        var beforeMax = StarStonePouchService.BlackStoneMax(self);
        var afterMax = StarStonePouchService.ReduceBlackStoneMax(self, amount);
        SunExpLog.Info("Loneer black stone cap reduced: owner=" + self.Self.InstanceId
            + ", beforeMax=" + beforeMax
            + ", afterMax=" + afterMax
            + ", currentBlack=" + StarStonePouchService.CurrentBlackStones(self)
            + ", prayerUses=" + state.PrayerUseCount);
    }

    private static void TriggerNaturalMorningStar(ScriptExecutor self, LoneerCombatState state)
    {
        var copiedGuide = state.GuidanceCardId;
        var copied = TryAddGuidedCard(self, state, "natural");
        StarStonePouchService.ResetPouch(self);
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
        StarStonePouchService.ResetPouch(self);
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
        state.PrayerCooldown = 0;
        state.PrayerUseCount = 0;
        state.ActionResolving = false;
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
        var source = CardSelectionApi.CombatDrawAndDiscardCards(self, card => !IsExcludedActionCard(card));
        if (source.Count == 0)
        {
            state.SelectionPending = false;
            SetGuidance(state, SunExpIds.WitchStarScoreCardId);
            PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a\u9b54\u5973\u7684\u661f\u8c31");
            SunExpLog.Info("Loneer guidance fallback to Witch Star Score: owner=" + owner.InstanceId + ", version=" + selectionVersion);
            return;
        }

        var opened = CardSelectionApi.SelectOneFromCards(
            self,
            source,
            card => !IsExcludedActionCard(card),
            card => ApplyGuidanceSelection(owner, state, selectionVersion, card, "selected"),
            caption,
            () => ResolveRandomGuidanceFallback(owner, state, selectionVersion, source, "cancelled"));

        if (opened)
        {
            return;
        }

        ResolveRandomGuidanceFallback(owner, state, selectionVersion, source, "ui_unavailable");
    }

    private static void ApplyGuidanceSelection(
        IStatusManager owner,
        LoneerCombatState state,
        int selectionVersion,
        IDataConfig card,
        string source)
    {
        if (!IsCurrentGuidanceSelection(owner, state, selectionVersion))
        {
            return;
        }

        state.SelectionPending = false;
        SetGuidance(state, CardConfigApi.Id(card));
        PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a" + CardDisplayName(card));
        SunExpLog.Info("Loneer guidance " + source + ": owner=" + owner.InstanceId + ", card=" + state.GuidanceCardId + ", version=" + selectionVersion);
    }

    private static void ResolveRandomGuidanceFallback(
        IStatusManager owner,
        LoneerCombatState state,
        int selectionVersion,
        IReadOnlyList<IDataConfig> candidates,
        string reason)
    {
        if (!IsCurrentGuidanceSelection(owner, state, selectionVersion))
        {
            return;
        }

        var card = RandomGuidanceCard(candidates);
        if (card != null)
        {
            ApplyGuidanceSelection(owner, state, selectionVersion, card, "random_" + reason);
            return;
        }

        state.SelectionPending = false;
        SetGuidance(state, SunExpIds.WitchStarScoreCardId);
        PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a\u9b54\u5973\u7684\u661f\u8c31");
        SunExpLog.Warn("Loneer guidance random fallback exhausted candidates; owner=" + owner.InstanceId + ", reason=" + reason + ", version=" + selectionVersion);
    }

    private static bool IsCurrentGuidanceSelection(IStatusManager owner, LoneerCombatState state, int selectionVersion)
    {
        var current = LoneerCombatStateStore.Get(owner);
        return ReferenceEquals(current, state) && state.SelectionVersion == selectionVersion;
    }

    private static IDataConfig? RandomGuidanceCard(IReadOnlyList<IDataConfig> candidates)
    {
        var pool = candidates?
            .Where(card => card != null && !IsExcludedActionCard(card))
            .ToList() ?? new List<IDataConfig>();
        return pool.Count == 0 ? null : pool[UnityEngine.Random.Range(0, pool.Count)];
    }

    private static bool TryAddGuidedCard(ScriptExecutor self, LoneerCombatState state, string source)
    {
        var id = CardApi.ResolveCardId(state.GuidanceCardId);
        if (string.IsNullOrWhiteSpace(id))
        {
            SunExpLog.Warn("Loneer guided card copy skipped: source=" + source + ", guidance=" + state.GuidanceCardId);
            return false;
        }

        var result = LoneerCardGrantService.GrantGuidanceCopyToHand(self, id, source);
        SunExpLog.Info("Loneer guided card copy: owner=" + self.Self.InstanceId
            + ", source=" + source
            + ", card=" + id
            + ", success=" + result.Success
            + (result.Success ? "" : ", step=" + result.FailureStep + ", error=" + result.FailureReason));
        return result.Success;
    }

    private static void SetGuidance(LoneerCombatState state, string cardId)
    {
        var resolved = CardApi.ResolveCardId(cardId);
        if (string.Equals(resolved, SunExpIds.WitchStarScoreCardId, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(resolved) && !IsExcludedActionCard(resolved)))
        {
            state.GuidanceCardId = resolved;
        }
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
            || CardMutationService.HasRuntimeMarker(config, SunExpIds.LoneerDerivedMarker)
            || CardMutationService.HasRuntimeMarker(config, SunExpIds.LoneerGuidanceMarker);
    }

    private static bool IsExcludedActionCard(string id)
    {
        var value = (id ?? "").Replace("*", "").Trim();
        return string.IsNullOrWhiteSpace(value)
            || StarScoreService.IsStellarOvertureCard(value)
            || value == "witch_star_score"
            || value == SunExpIds.WitchStarScoreCardId
            || value == "loneer_morning_star_prayer"
            || value == SunExpIds.LoneerMorningPrayerSkillCardId;
    }

    private static void SyncBuffs(ScriptExecutor self, LoneerCombatState state)
    {
        BuffApi.SetExactLevel(self?.Self, SunExpIds.MiracleClock, state.ClockValue);
    }

    private static void ClearCombatBuffs(ScriptExecutor self)
    {
        var status = self?.Self;
        if (status == null)
        {
            return;
        }

        StarStonePouchService.RemoveState(status);
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

    private static void SetMorningPrayerCooldown(ScriptExecutor? self, LoneerCombatState? state, int cooldown)
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

        try
        {
            self?.UpdateSkillTime();
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Morning Star Prayer cooldown UI refresh skipped: " + ex.Message);
        }
    }

    private static void TickMorningPrayerCooldown(ScriptExecutor self)
    {
        if (PolymorphCooldownService.IsActive(self?.Self))
        {
            return;
        }

        var state = LoneerCombatStateStore.Get(self?.Self);
        var cooldown = MorningPrayerCooldown(state);
        if (cooldown > 0)
        {
            SetMorningPrayerCooldown(self, state, cooldown - 1);
        }
    }
}
