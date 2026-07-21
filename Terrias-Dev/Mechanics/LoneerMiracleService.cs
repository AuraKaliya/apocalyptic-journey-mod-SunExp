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
    private static readonly object PendingStarStoneDrawSync = new();
    private static readonly Dictionary<string, PendingStarStoneDrawBatch> PendingStarStoneDraws = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ScriptExecutor> PendingBorrowedMiracles = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ScriptExecutor> PendingNaturalMorningStars = new(StringComparer.Ordinal);
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
        SetMorningPrayerCooldown(self, state, 0);
        StarScoreService.ClearScore(self);
        ClearCombatBuffs(self);
        StarStonePouchService.GrantInitial(self);
        MiracleClockService.Initialize(self, state, InitialClockMax);
        SunExpLog.Info("Loneer fight state initialized: owner=" + self.Self.InstanceId
            + ", starStoneBlack=" + StarStonePouchService.CurrentBlackStones(self)
            + ", clock=" + state.ClockValue);
        RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
    }

    private static void OnStarStonePouchDrawn(ScriptExecutor self, StarStonePouchDrawResult result)
    {
        if (self?.Self == null || result.OwnerStatusId != self.Self.InstanceId)
        {
            return;
        }

        var state = LoneerCombatStateStore.Get(self.Self);
        if (state == null || state.ActionResolving)
        {
            return;
        }

        if (!IsActiveOwner(self.Self, state))
        {
            return;
        }

        QueueStarStonePouchDraw(self, result);
    }

    private static void QueueStarStonePouchDraw(ScriptExecutor self, StarStonePouchDrawResult result)
    {
        var ownerId = self.Self.InstanceId;
        lock (PendingStarStoneDrawSync)
        {
            if (!PendingStarStoneDraws.TryGetValue(ownerId, out var batch))
            {
                batch = new PendingStarStoneDrawBatch(self);
                PendingStarStoneDraws[ownerId] = batch;
            }

            batch.Executor = self;
            batch.Results.Add(result);
        }

        var enqueued = ScheduleStarStonePouchDrawFlush(ownerId, 1);
        SunExpPerformanceCounters.Record(enqueued
            ? "Loneer.StarStonePouchDraw.Enqueued"
            : "Loneer.StarStonePouchDraw.Deduped");
    }

    private static bool ScheduleStarStonePouchDrawFlush(string ownerId, int delayFrames)
    {
        return SunExpFrameDispatcher.RunOnceAfterFrames(
            "Loneer.StarStonePouchDraw." + ownerId,
            Math.Max(1, delayFrames),
            () => FlushStarStonePouchDraws(ownerId));
    }

    private static void FlushStarStonePouchDraws(string ownerId)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        var segment = start;
        var scheduleNextDelay = 1;
        var hasMore = false;
        try
        {
            var combatUiBusy = SunExpCombatUiWorkload.IsBusy;
            segment = RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.UiBusyCheck", segment);
            if (combatUiBusy)
            {
                var delay = Math.Max(1, SunExpCombatUiWorkload.BusyFramesRemaining + 1);
                SunExpPerformanceCounters.Record("Loneer.StarStonePouchDraw.DeferredForCardUi");
                SunExpLog.Debug("Loneer star stone draw deferred while combat card UI is busy: owner="
                    + ownerId
                    + ", delayFrames="
                    + delay
                    + ", uiSource="
                    + SunExpCombatUiWorkload.LastSource);
                ScheduleStarStonePouchDrawFlush(ownerId, delay);
                RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.CardUiDeferral", segment);
                return;
            }

            ScriptExecutor self;
            StarStonePouchDrawResult result;
            lock (PendingStarStoneDrawSync)
            {
                if (!PendingStarStoneDraws.TryGetValue(ownerId, out var batch) || batch.Results.Count == 0)
                {
                    PendingStarStoneDraws.Remove(ownerId);
                    return;
                }

                self = batch.Executor;
                result = batch.Results[0];
                batch.Results.RemoveAt(0);
                hasMore = batch.Results.Count > 0;
                if (!hasMore)
                {
                    PendingStarStoneDraws.Remove(ownerId);
                }
            }
            segment = RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.DequeuePending", segment);

            if (self?.Self == null || self.Self.InstanceId != ownerId)
            {
                RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.ValidateOwner", segment);
                return;
            }
            segment = RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.ValidateOwner", segment);

            var state = LoneerCombatStateStore.Get(self.Self);
            if (state == null || state.ActionResolving)
            {
                RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.StateLookup", segment);
                return;
            }
            segment = RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.StateLookup", segment);

            if (!IsActiveOwner(self.Self, state))
            {
                RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.ValidateActiveOwner", segment);
                return;
            }
            segment = RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.ValidateActiveOwner", segment);

            EnsureInitialized(self, state);
            segment = RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.EnsureInitialized", segment);
            state.ActionResolving = true;
            try
            {
                scheduleNextDelay = ResolveStarStoneDrawStep(self, state, result) ? 2 : 1;
                segment = RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.ResolveStep", segment);
            }
            finally
            {
                state.ActionResolving = false;
                RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.ClearResolving", segment);
            }
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("Loneer.FlushStarStonePouchDraws", start);
            if (hasMore)
            {
                ScheduleStarStonePouchDrawFlush(ownerId, scheduleNextDelay);
            }
        }
    }

    private static bool ResolveStarStoneDrawStep(ScriptExecutor self, LoneerCombatState state, StarStonePouchDrawResult result)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        var segment = start;
        try
        {
            if (self?.Self == null || state == null || result.OwnerStatusId != self.Self.InstanceId)
            {
                RecordLoneerSegment("Loneer.ResolveStarStoneDrawStep.Validate", segment);
                return false;
            }
            segment = RecordLoneerSegment("Loneer.ResolveStarStoneDrawStep.Validate", segment);

            if (result.IsWhite)
            {
                ScheduleNaturalMorningStar(self, "StarStonePouch.White");
                RecordLoneerSegment("Loneer.ResolveStarStoneDrawStep.White", segment);
                return true;
            }
            segment = RecordLoneerSegment("Loneer.ResolveStarStoneDrawStep.WhiteCheck", segment);

            if (!result.IsBlack)
            {
                RecordLoneerSegment("Loneer.ResolveStarStoneDrawStep.BlackCheck", segment);
                return false;
            }
            segment = RecordLoneerSegment("Loneer.ResolveStarStoneDrawStep.BlackCheck", segment);

            var change = MiracleClockService.ReduceBy(self, state, 1, "StarStonePouch.Black");
            segment = RecordLoneerSegment("Loneer.ResolveStarStoneDrawStep.ReduceClock", segment);
            if (!change.Depleted)
            {
                return false;
            }

            ScheduleBorrowedMiracle(self);
            RecordLoneerSegment("Loneer.ResolveStarStoneDrawStep.ScheduleBorrowedMiracle", segment);
            return true;
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("Loneer.ResolveStarStoneDrawStep", start);
        }
    }

    private static void ScheduleNaturalMorningStar(ScriptExecutor self, string source)
    {
        if (self?.Self == null)
        {
            return;
        }

        var ownerId = self.Self.InstanceId;
        lock (PendingStarStoneDrawSync)
        {
            PendingNaturalMorningStars[ownerId] = self;
        }

        SunExpFrameDispatcher.RunOnceNextFrame(
            "Loneer.NaturalMorningStar." + ownerId,
            () => FlushNaturalMorningStar(ownerId, source));
    }

    private static void FlushNaturalMorningStar(string ownerId, string source)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        var segment = start;
        ScriptExecutor self;
        lock (PendingStarStoneDrawSync)
        {
            if (!PendingNaturalMorningStars.TryGetValue(ownerId, out self))
            {
                SunExpPerformanceCounters.RecordDuration("Loneer.FlushNaturalMorningStar", start);
                return;
            }

            PendingNaturalMorningStars.Remove(ownerId);
        }
        segment = RecordLoneerSegment("Loneer.FlushNaturalMorningStar.DequeuePending", segment);

        if (self?.Self == null || self.Self.InstanceId != ownerId)
        {
            RecordLoneerSegment("Loneer.FlushNaturalMorningStar.ValidateOwner", segment);
            SunExpPerformanceCounters.RecordDuration("Loneer.FlushNaturalMorningStar", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushNaturalMorningStar.ValidateOwner", segment);

        var state = LoneerCombatStateStore.Get(self.Self);
        if (state == null)
        {
            RecordLoneerSegment("Loneer.FlushNaturalMorningStar.StateLookup", segment);
            SunExpPerformanceCounters.RecordDuration("Loneer.FlushNaturalMorningStar", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushNaturalMorningStar.StateLookup", segment);

        if (!IsActiveOwner(self.Self, state))
        {
            RecordLoneerSegment("Loneer.FlushNaturalMorningStar.ValidateActiveOwner", segment);
            SunExpPerformanceCounters.RecordDuration("Loneer.FlushNaturalMorningStar", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushNaturalMorningStar.ValidateActiveOwner", segment);

        TriggerNaturalMorningStar(self, state, source);
        RecordLoneerSegment("Loneer.FlushNaturalMorningStar.Trigger", segment);
        SunExpPerformanceCounters.RecordDuration("Loneer.FlushNaturalMorningStar", start);
    }

    private static void ScheduleBorrowedMiracle(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return;
        }

        var ownerId = self.Self.InstanceId;
        lock (PendingStarStoneDrawSync)
        {
            PendingBorrowedMiracles[ownerId] = self;
        }

        SunExpFrameDispatcher.RunOnceNextFrame(
            "Loneer.BorrowedMiracle." + ownerId,
            () => FlushBorrowedMiracle(ownerId));
    }

    private static void FlushBorrowedMiracle(string ownerId)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        var segment = start;
        ScriptExecutor self;
        lock (PendingStarStoneDrawSync)
        {
            if (!PendingBorrowedMiracles.TryGetValue(ownerId, out self))
            {
                SunExpPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
                return;
            }

            PendingBorrowedMiracles.Remove(ownerId);
        }
        segment = RecordLoneerSegment("Loneer.FlushBorrowedMiracle.DequeuePending", segment);

        if (self?.Self == null || self.Self.InstanceId != ownerId)
        {
            RecordLoneerSegment("Loneer.FlushBorrowedMiracle.ValidateOwner", segment);
            SunExpPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushBorrowedMiracle.ValidateOwner", segment);

        var state = LoneerCombatStateStore.Get(self.Self);
        if (state == null)
        {
            RecordLoneerSegment("Loneer.FlushBorrowedMiracle.StateLookup", segment);
            SunExpPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushBorrowedMiracle.StateLookup", segment);

        if (!IsActiveOwner(self.Self, state))
        {
            RecordLoneerSegment("Loneer.FlushBorrowedMiracle.ValidateActiveOwner", segment);
            SunExpPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushBorrowedMiracle.ValidateActiveOwner", segment);

        TriggerBorrowedMiracle(self, state);
        RecordLoneerSegment("Loneer.FlushBorrowedMiracle.Trigger", segment);
        SunExpPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
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

        if (state.SelectionPending || state.SelectionScheduled)
        {
            PlayerApi.ShowCaption("【指引牌】选择尚未提交，晨星祈愿未释放。");
            SunExpLog.InfoAlways("[MorningPrayerAttempt] guidance selection still pending before use: owner="
                + self.Self.InstanceId
                + ", pending="
                + state.SelectionPending
                + ", scheduled="
                + state.SelectionScheduled
                + ".");
            return;
        }

        if (string.IsNullOrWhiteSpace(state.GuidanceCardId))
        {
            PlayerApi.ShowCaption("\u5c1a\u672a\u9009\u5b9a\u3010\u6307\u5f15\u724c\u3011\u3002");
            RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
            return;
        }

        TriggerNaturalMorningStar(self, state, "MorningStarPrayer");
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

    public static void EnsureMorningPrayerSkillState(ScriptExecutor? self, LoneerCombatState? state, string source)
    {
        var cooldown = MorningPrayerCooldown(state);
        SetMorningPrayerCooldown(self, state, cooldown);
        SunExpLog.Debug("Morning Star Prayer skill state synchronized from " + source
            + ": cooldown=" + cooldown + ".");
    }

    public static void EndCombatCleanup(ScriptExecutor self)
    {
        ClearCombatBuffs(self);
        StarScoreService.RemoveState(self?.Self);
        StarStonePouchService.RemoveState(self?.Self);
        ClearPendingStarStoneDraws(self?.Self);
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

    private static void TriggerNaturalMorningStar(ScriptExecutor self, LoneerCombatState state, string source)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            if (self?.Self == null || state == null)
            {
                return;
            }

            var owner = self.Self;
            var ownerId = owner.InstanceId;
            var copiedGuide = state.GuidanceCardId;
            var copied = false;
            var key = "Loneer.NaturalMorningStar.Sequence." + ownerId;
            SunExpFrameStepRunner.RunOnce(
                key,
                new[]
                {
                    new SunExpFrameStep("GrantGuidanceCopy", () =>
                    {
                        if (IsActiveOwner(owner, state))
                        {
                            copied = TryAddGuidedCard(self, state, "natural");
                        }
                    }),
                    new SunExpFrameStep("ResetClock", () =>
                    {
                        if (!IsActiveOwner(owner, state))
                        {
                            return;
                        }

                        MiracleClockService.ResetToMaxAndGrantStarlight(self, state, "NaturalMorningStar:" + source);
                        PlayerApi.ShowCaption("\u81ea\u7136\u6668\u661f\uff1a\u83b7\u5f97\u6307\u5f15\u724c\u590d\u5236\u3002");
                        SunExpLog.Info("Natural Morning Star resolved: owner=" + ownerId + ", copied=" + copiedGuide + ", success=" + copied);
                    }),
                    new SunExpFrameStep("RequestGuidanceSelection", () =>
                    {
                        if (IsActiveOwner(owner, state))
                        {
                            RequestGuidanceSelectionDeferred(self, state, "\u91cd\u65b0\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011", "NaturalMorningStar");
                        }
                    })
                },
                () => !IsActiveOwner(owner, state));
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("Loneer.TriggerNaturalMorningStar", start);
        }
    }

    private static void TriggerBorrowedMiracle(ScriptExecutor self, LoneerCombatState state)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            if (self?.Self == null || state == null)
            {
                return;
            }

            var owner = self.Self;
            var ownerId = owner.InstanceId;
            var copiedGuide = state.GuidanceCardId;
            var copied = false;
            var key = "Loneer.BorrowedMiracle.Sequence." + ownerId;
            SunExpFrameStepRunner.RunOnce(
                key,
                new[]
                {
                    new SunExpFrameStep("GrantGuidanceCopy", () =>
                    {
                        if (IsActiveOwner(owner, state))
                        {
                            copied = TryAddGuidedCard(self, state, "borrowed");
                        }
                    }),
                    new SunExpFrameStep("ResetClock", () =>
                    {
                        if (!IsActiveOwner(owner, state))
                        {
                            return;
                        }

                        MiracleClockService.ReduceMax(self, state, 1, MinClockMax, "BorrowedMiracle");
                        MiracleClockService.ResetToMaxAndGrantStarlight(self, state, "BorrowedMiracle");
                        PlayerApi.ShowCaption("\u501f\u6765\u7684\u5947\u8ff9\uff1a\u65f6\u949f\u4e0a\u9650\u4e0b\u964d\u3002");
                        SunExpLog.Info("Borrowed Miracle resolved: owner=" + ownerId + ", copied=" + copiedGuide + ", success=" + copied + ", clockMax=" + state.ClockMax);
                    }),
                    new SunExpFrameStep("RequestGuidanceSelection", () =>
                    {
                        if (IsActiveOwner(owner, state))
                        {
                            RequestGuidanceSelectionDeferred(self, state, "\u91cd\u65b0\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011", "BorrowedMiracle");
                        }
                    })
                },
                () => !IsActiveOwner(owner, state));
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("Loneer.TriggerBorrowedMiracle", start);
        }
    }

    private static void EnsureInitialized(ScriptExecutor self, LoneerCombatState state)
    {
        if (state.Initialized)
        {
            return;
        }

        InitializeState(state);
        MiracleClockService.Initialize(self, state, InitialClockMax);
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
        var start = SunExpPerformanceCounters.Timestamp();
        var segment = start;
        try
        {
            if (state.SelectionPending || self?.Self == null)
            {
                RecordLoneerSegment("Loneer.RequestGuidanceSelection.EntryCheck", segment);
                return;
            }
            segment = RecordLoneerSegment("Loneer.RequestGuidanceSelection.EntryCheck", segment);

            state.SelectionScheduled = false;
            var owner = self.Self;
            state.SelectionPending = true;
            var selectionVersion = ++state.SelectionVersion;
            var source = CardSelectionApi.CombatDrawAndDiscardCards(self, card => !IsExcludedActionCard(card));
            segment = RecordLoneerSegment("Loneer.RequestGuidanceSelection.CollectCandidates", segment);
            if (source.Count == 0)
            {
                state.SelectionPending = false;
                SetGuidance(state, SunExpIds.WitchStarScoreCardId, "\u9b54\u5973\u7684\u661f\u8c31");
                PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a\u9b54\u5973\u7684\u661f\u8c31");
                SunExpLog.Info("Loneer guidance fallback to Witch Star Score: owner=" + owner.InstanceId + ", version=" + selectionVersion);
                RecordLoneerSegment("Loneer.RequestGuidanceSelection.EmptyFallback", segment);
                return;
            }

            var opened = CardSelectionApi.SelectOneFromCards(
                self,
                source,
                card => !IsExcludedActionCard(card),
                card => ApplyGuidanceSelection(owner, state, selectionVersion, card, "selected"),
                caption,
                () => ResolveRandomGuidanceFallback(owner, state, selectionVersion, source, "cancelled"));
            segment = RecordLoneerSegment("Loneer.RequestGuidanceSelection.OpenSelectionUi", segment);

            if (opened)
            {
                return;
            }

            ResolveRandomGuidanceFallback(owner, state, selectionVersion, source, "ui_unavailable");
            RecordLoneerSegment("Loneer.RequestGuidanceSelection.UiUnavailableFallback", segment);
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("Loneer.RequestGuidanceSelection", start);
        }
    }

    private static void RequestGuidanceSelectionDeferred(
        ScriptExecutor self,
        LoneerCombatState state,
        string caption,
        string source)
    {
        if (state.SelectionPending || state.SelectionScheduled || self?.Self == null)
        {
            return;
        }

        var owner = self.Self;
        state.SelectionScheduled = true;
        var enqueued = SunExpFrameDispatcher.RunOnceNextFrame(
            "Loneer.GuidanceSelection." + owner.InstanceId,
            () =>
            {
                var start = SunExpPerformanceCounters.Timestamp();
                try
                {
                    var current = LoneerCombatStateStore.Get(owner);
                    if (!ReferenceEquals(current, state))
                    {
                        return;
                    }

                    if (SunExpCombatUiWorkload.IsBusy)
                    {
                        state.SelectionScheduled = false;
                        SunExpPerformanceCounters.Record("Loneer.GuidanceSelection.DeferredForCardUi");
                        RequestGuidanceSelectionDeferred(self, state, caption, source + ":card-ui-busy");
                        return;
                    }

                    RequestGuidanceSelection(self, state, caption);
                }
                finally
                {
                    SunExpPerformanceCounters.RecordDuration("Loneer.GuidanceSelection.Action", start);
                }
            });
        SunExpPerformanceCounters.Record(enqueued
            ? "Loneer.GuidanceSelection.Enqueued"
            : "Loneer.GuidanceSelection.Deduped");

        if (!enqueued)
        {
            SunExpLog.Debug("Loneer guidance selection already scheduled: owner=" + owner.InstanceId + ", source=" + source);
        }
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
        var displayName = CardDisplayName(card);
        SetGuidance(state, CardConfigApi.Id(card), displayName);
        PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a" + displayName);
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
        SetGuidance(state, SunExpIds.WitchStarScoreCardId, "\u9b54\u5973\u7684\u661f\u8c31");
        PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a\u9b54\u5973\u7684\u661f\u8c31");
        SunExpLog.Warn("Loneer guidance random fallback exhausted candidates; owner=" + owner.InstanceId + ", reason=" + reason + ", version=" + selectionVersion);
    }

    private static bool IsCurrentGuidanceSelection(IStatusManager owner, LoneerCombatState state, int selectionVersion)
    {
        var current = LoneerCombatStateStore.Get(owner);
        return ReferenceEquals(current, state) && state.SelectionVersion == selectionVersion;
    }

    private static bool IsCurrentLoneerState(IStatusManager owner, LoneerCombatState state)
    {
        var current = LoneerCombatStateStore.Get(owner);
        return ReferenceEquals(current, state);
    }

    private static bool IsActiveOwner(IStatusManager owner, LoneerCombatState state)
    {
        if (owner == null || state == null)
        {
            return false;
        }

        if (PolymorphStateStore.IsRoleSuppressedFor(owner, SunExpIds.LoneerCareerId))
        {
            return false;
        }

        return IsCurrentLoneerState(owner, state);
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
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var guidance = state.PreparedGuidance;
            var id = CardApi.ResolveCardId(guidance?.CardId ?? state.GuidanceCardId);
            if (string.IsNullOrWhiteSpace(id))
            {
                SunExpLog.Warn("Loneer guided card copy skipped: source=" + source + ", guidance=" + state.GuidanceCardId);
                return false;
            }

            var grantStart = SunExpPerformanceCounters.Timestamp();
            var result = guidance == null
                ? LoneerCardGrantService.GrantGuidanceCopyToHand(self, id, source)
                : LoneerCardGrantService.GrantGuidanceCopyToHand(self, guidance, source);
            SunExpPerformanceCounters.RecordDuration("Loneer.GrantGuidanceCopyToHand", grantStart);
            SunExpLog.Info("Loneer guided card copy: owner=" + self.Self.InstanceId
                + ", source=" + source
                + ", card=" + id
                + ", success=" + result.Success
                + (result.Success ? "" : ", step=" + result.FailureStep + ", error=" + result.FailureReason));
            return result.Success;
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("Loneer.TryAddGuidedCard", start);
        }
    }

    private static void SetGuidance(LoneerCombatState state, string cardId, string displayName)
    {
        var resolved = CardApi.ResolveCardId(cardId);
        if (string.Equals(resolved, SunExpIds.WitchStarScoreCardId, StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(resolved) && !IsExcludedActionCard(resolved)))
        {
            state.GuidanceCardId = resolved;
            state.PreparedGuidance = PreparedGuidanceCard.Create(
                resolved,
                displayName,
                IsWitchStarScoreGuidance(resolved));
        }
    }

    private static bool IsWitchStarScoreGuidance(string id)
    {
        var value = (id ?? "").Replace("*", "").Trim();
        return string.Equals(value, SunExpIds.WitchStarScoreCardId, StringComparison.Ordinal)
            || string.Equals(value, "witch_star_score", StringComparison.Ordinal);
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

    private static long RecordLoneerSegment(string name, long segmentStart)
    {
        SunExpPerformanceCounters.RecordDuration(name, segmentStart);
        return SunExpPerformanceCounters.Timestamp();
    }

    private static void SyncBuffs(ScriptExecutor self, LoneerCombatState state)
    {
        MiracleClockService.Sync(self, state);
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

    private static void ClearPendingStarStoneDraws(IStatusManager? owner)
    {
        var ownerId = owner?.InstanceId ?? "";
        if (ownerId.Length == 0)
        {
            return;
        }

        lock (PendingStarStoneDrawSync)
        {
            PendingStarStoneDraws.Remove(ownerId);
            PendingBorrowedMiracles.Remove(ownerId);
            PendingNaturalMorningStars.Remove(ownerId);
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

    private sealed class PendingStarStoneDrawBatch
    {
        public PendingStarStoneDrawBatch(ScriptExecutor executor)
        {
            Executor = executor;
        }

        public ScriptExecutor Executor { get; set; }

        public List<StarStonePouchDrawResult> Results { get; } = new();
    }
}
