using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class LoneerMiracleService
{
    private const int InitialClockMax = 12;
    private const int MinClockMax = 6;
    private const int PrayerCooldownRounds = 2;
    private static readonly object PendingStarStoneDrawSync = new();
    private static readonly Dictionary<string, PendingStarStoneDrawBatch> PendingStarStoneDraws = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ScriptExecutor> PendingBorrowedMiracles = new(StringComparer.Ordinal);
    private static long morningStarSequence;
    private static long borrowedMiracleSequence;
    private static readonly string[] MorningPrayerCooldownKeys =
    {
        TerriasIds.LoneerMorningPrayerSkillCardId,
        "loneer_morning_star_prayer",
        "*loneer_morning_star_prayer"
    };

    public static void RegisterCareer(ScriptExecutor self)
    {
        PlayerApi.SetGameVar(TerriasIds.LoneerActive, "1");
        SetMorningPrayerCooldown(self, null, 0);
        StarStonePouchService.Drawn -= OnStarStonePouchDrawn;
        StarStonePouchService.Drawn += OnStarStonePouchDrawn;

        var token = (DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "TerriasLoneerCareerToken", "0")) + 1).ToString();
        var fightStartRegistered = ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
        {
            if (ExecutorApi.IsHookTokenActive(self, "TerriasLoneerCareerToken", token))
            {
                OnFightStart(self);
            }
        }), "loneer_career");
        var startRoundRegistered = ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            if (ExecutorApi.IsHookTokenActive(self, "TerriasLoneerCareerToken", token))
            {
                TickMorningPrayerCooldown(self);
            }
        }), "loneer_career");

        ExecutorApi.TryAddEvent(self, "Win", new Action(() => EndCombatCleanup(self)), "loneer_career");
        ExecutorApi.TryAddEvent(self, "Escape", new Action(() => EndCombatCleanup(self)), "loneer_career");

        if (fightStartRegistered && startRoundRegistered)
        {
            ExecutorApi.SetVar(self, "TerriasLoneerCareerHook", "1");
            ExecutorApi.SetVar(self, "TerriasLoneerCareerToken", token);
        }
    }

    public static bool IsActive()
    {
        return PolymorphStateStore.IsLocalEffectiveCombatRole(TerriasIds.LoneerCareerId);
    }

    public static void OnFightStart(ScriptExecutor self)
    {
        if (!IsActive())
        {
            return;
        }

        if (self?.Self == null)
        {
            TerriasLog.Warn("Loneer fight state initialization skipped: owner status unavailable.");
            return;
        }

        var state = LoneerCombatStateStore.ResetForFight(self.Self);
        if (state == null)
        {
            TerriasLog.Warn("Loneer fight state initialization skipped: owner status unavailable.");
            return;
        }

        InitializeState(state);
        SetMorningPrayerCooldown(self, state, 0);
        StarScoreService.ClearScore(self);
        ClearCombatBuffs(self);
        StarStonePouchService.GrantInitial(self);
        MiracleClockService.Initialize(self, state, InitialClockMax);
        TerriasLog.Info("Loneer fight state initialized: owner=" + self.Self.InstanceId
            + ", starStoneBlack=" + StarStonePouchService.CurrentBlackStones(self)
            + ", clock=" + state.ClockValue);
        RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
    }

    public static void PreparePolymorphEntry(ScriptExecutor self)
    {
        if (!IsActive() || self?.Self == null)
        {
            return;
        }

        var state = LoneerCombatStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            TerriasLog.Warn("Loneer polymorph initialization skipped: owner status unavailable.");
            return;
        }

        var initializedNow = !state.Initialized;
        if (initializedNow)
        {
            InitializeState(state);
            MiracleClockService.Initialize(self, state, InitialClockMax);
        }
        else
        {
            MiracleClockService.Sync(self, state);
        }

        // A polymorph form starts with a normalized active-skill cooldown. The
        // shared polymorph entry service applies its one-time cross-form floor
        // after the career script has finished seeding native cooldown values.
        SetMorningPrayerCooldown(self, state, 0);
        StarStonePouchService.EnsurePresent(self);
        if (string.IsNullOrWhiteSpace(state.GuidanceCardId)
            && !state.SelectionPending
            && !state.SelectionScheduled)
        {
            RequestGuidanceSelection(self, state, "\u9009\u62e9\u3010\u6307\u5f15\u724c\u3011");
        }

        TerriasLog.Info("Loneer polymorph state prepared: owner=" + self.Self.InstanceId
            + ", initializedNow=" + initializedNow
            + ", starStoneBlack=" + StarStonePouchService.CurrentBlackStones(self)
            + ", clock=" + state.ClockValue);
    }

    public static void ResumeAfterPolymorph(ScriptExecutor self)
    {
        if (!IsActive() || self?.Self == null)
        {
            return;
        }

        var state = LoneerCombatStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            return;
        }

        EnsureInitialized(self, state);
        StarStonePouchService.EnsurePresent(self);
        MiracleClockService.Sync(self, state);
        SetMorningPrayerCooldown(self, state, state.PrayerCooldown);
    }

    public static void DetachCareerRuntime(ScriptExecutor? self)
    {
        ExecutorApi.ClearHook(self, "TerriasLoneerCareerHook", "TerriasLoneerCareerToken");
    }

    private static void OnStarStonePouchDrawn(ScriptExecutor self, StarStonePouchDrawResult result)
    {
        if (self?.Self == null || result.OwnerStatusId != self.Self.InstanceId)
        {
            return;
        }

        if (!MorningStarRelicFormula.ParticipatesInStarStoneOrbit(result.ChannelId))
        {
            return;
        }

        var state = LoneerCombatStateStore.Get(self.Self);
        if (state == null)
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
        TerriasPerformanceCounters.Record(enqueued
            ? "Loneer.StarStonePouchDraw.Enqueued"
            : "Loneer.StarStonePouchDraw.Deduped");
    }

    private static bool ScheduleStarStonePouchDrawFlush(string ownerId, int delayFrames)
    {
        return TerriasFrameDispatcher.RunOnceAfterFrames(
            "Loneer.StarStonePouchDraw." + ownerId,
            Math.Max(1, delayFrames),
            () => FlushStarStonePouchDraws(ownerId));
    }

    private static void FlushStarStonePouchDraws(string ownerId)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        var segment = start;
        var scheduleNextDelay = 1;
        var hasMore = false;
        try
        {
            var combatUiBusy = TerriasCombatUiWorkload.IsBusy;
            segment = RecordLoneerSegment("Loneer.FlushStarStonePouchDraws.UiBusyCheck", segment);
            if (combatUiBusy)
            {
                var delay = Math.Max(1, TerriasCombatUiWorkload.BusyFramesRemaining + 1);
                TerriasPerformanceCounters.Record("Loneer.StarStonePouchDraw.DeferredForCardUi");
                TerriasLog.Debug("Loneer star stone draw deferred while combat card UI is busy: owner="
                    + ownerId
                    + ", delayFrames="
                    + delay
                    + ", uiSource="
                    + TerriasCombatUiWorkload.LastSource);
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
            TerriasPerformanceCounters.RecordDuration("Loneer.FlushStarStonePouchDraws", start);
            if (hasMore)
            {
                ScheduleStarStonePouchDrawFlush(ownerId, scheduleNextDelay);
            }
        }
    }

    private static bool ResolveStarStoneDrawStep(ScriptExecutor self, LoneerCombatState state, StarStonePouchDrawResult result)
    {
        var start = TerriasPerformanceCounters.Timestamp();
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
                TriggerNaturalMorningStar(self, state, "StarStonePouch.White");
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
            TerriasPerformanceCounters.RecordDuration("Loneer.ResolveStarStoneDrawStep", start);
        }
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

        TerriasFrameDispatcher.RunOnceNextFrame(
            "Loneer.BorrowedMiracle." + ownerId,
            () => FlushBorrowedMiracle(ownerId));
    }

    private static void FlushBorrowedMiracle(string ownerId)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        var segment = start;
        ScriptExecutor self;
        lock (PendingStarStoneDrawSync)
        {
            if (!PendingBorrowedMiracles.TryGetValue(ownerId, out self))
            {
                TerriasPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
                return;
            }

            PendingBorrowedMiracles.Remove(ownerId);
        }
        segment = RecordLoneerSegment("Loneer.FlushBorrowedMiracle.DequeuePending", segment);

        if (self?.Self == null || self.Self.InstanceId != ownerId)
        {
            RecordLoneerSegment("Loneer.FlushBorrowedMiracle.ValidateOwner", segment);
            TerriasPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushBorrowedMiracle.ValidateOwner", segment);

        var state = LoneerCombatStateStore.Get(self.Self);
        if (state == null)
        {
            RecordLoneerSegment("Loneer.FlushBorrowedMiracle.StateLookup", segment);
            TerriasPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushBorrowedMiracle.StateLookup", segment);

        if (!IsActiveOwner(self.Self, state))
        {
            RecordLoneerSegment("Loneer.FlushBorrowedMiracle.ValidateActiveOwner", segment);
            TerriasPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
            return;
        }
        segment = RecordLoneerSegment("Loneer.FlushBorrowedMiracle.ValidateActiveOwner", segment);

        TriggerBorrowedMiracle(self, state);
        RecordLoneerSegment("Loneer.FlushBorrowedMiracle.Trigger", segment);
        TerriasPerformanceCounters.RecordDuration("Loneer.FlushBorrowedMiracle", start);
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
            TerriasLog.Warn("Morning Star Prayer skipped: Loneer owner status unavailable.");
            return;
        }

        var state = LoneerCombatStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            TerriasLog.Warn("Morning Star Prayer skipped: Loneer owner status unavailable.");
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
            TerriasLog.InfoAlways("[MorningPrayerAttempt] guidance selection still pending before use: owner="
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

        TriggerNaturalMorningStar(self, state, "MorningStarPrayer", blackStoneCapReduction: 2);
        state.PrayerUseCount += 1;
        SetMorningPrayerCooldown(self, state, PrayerCooldownRounds);
        PolymorphCooldownService.MarkSkillUsed(
            self,
            "Loneer.MorningStarPrayer",
            TerriasIds.LoneerMorningPrayerSkillCardId);
        TerriasLog.Info("Morning Star Prayer accepted: owner=" + self.Self.InstanceId
            + ", cooldown=" + state.PrayerCooldown
            + ", useCount=" + state.PrayerUseCount);
    }

    public static void EnsureMorningPrayerSkillState(ScriptExecutor? self, LoneerCombatState? state, string source)
    {
        var cooldown = MorningPrayerCooldown(state);
        SetMorningPrayerCooldown(self, state, cooldown);
        TerriasLog.Debug("Morning Star Prayer skill state synchronized from " + source
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
        TerriasLog.Info("Loneer black stone cap reduced: owner=" + self.Self.InstanceId
            + ", beforeMax=" + beforeMax
            + ", afterMax=" + afterMax
            + ", currentBlack=" + StarStonePouchService.CurrentBlackStones(self)
            + ", prayerUses=" + state.PrayerUseCount);
    }

    private static void TriggerNaturalMorningStar(
        ScriptExecutor self,
        LoneerCombatState state,
        string source,
        int blackStoneCapReduction = 0)
    {
        var start = TerriasPerformanceCounters.Timestamp();
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
            var sequence = Interlocked.Increment(ref morningStarSequence);
            var key = "Loneer.NaturalMorningStar.Sequence." + ownerId + "." + sequence;
            TerriasFrameStepRunner.RunOnce(
                key,
                new[]
                {
                    new TerriasFrameStep("GrantGuidanceCopy", () =>
                    {
                        if (IsActiveOwner(owner, state))
                        {
                            copied = TryAddGuidedCard(self, state, "natural");
                        }
                    }),
                    new TerriasFrameStep("ResetPouchAndClock", () =>
                    {
                        if (!IsActiveOwner(owner, state))
                        {
                            return;
                        }

                        StarStonePouchService.ResetPouch(self);
                        if (blackStoneCapReduction > 0)
                        {
                            ReduceBlackStoneMax(self, state, blackStoneCapReduction);
                        }
                        MiracleClockService.ResetToMaxAndGrantStarlight(self, state, "NaturalMorningStar:" + source);
                        PlayerApi.ShowCaption("\u81ea\u7136\u6668\u661f\uff1a\u83b7\u5f97\u6307\u5f15\u724c\u590d\u5236\u3002");
                        TerriasLog.Info("Natural Morning Star resolved: owner=" + ownerId + ", copied=" + copiedGuide + ", success=" + copied);
                    }),
                    new TerriasFrameStep("RequestGuidanceSelection", () =>
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
            TerriasPerformanceCounters.RecordDuration("Loneer.TriggerNaturalMorningStar", start);
        }
    }

    private static void TriggerBorrowedMiracle(ScriptExecutor self, LoneerCombatState state)
    {
        var start = TerriasPerformanceCounters.Timestamp();
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
            var sequence = Interlocked.Increment(ref borrowedMiracleSequence);
            var key = "Loneer.BorrowedMiracle.Sequence." + ownerId + "." + sequence;
            TerriasFrameStepRunner.RunOnce(
                key,
                new[]
                {
                    new TerriasFrameStep("GrantGuidanceCopy", () =>
                    {
                        if (IsActiveOwner(owner, state))
                        {
                            copied = TryAddGuidedCard(self, state, "borrowed");
                        }
                    }),
                    new TerriasFrameStep("ResetClock", () =>
                    {
                        if (!IsActiveOwner(owner, state))
                        {
                            return;
                        }

                        MiracleClockService.ReduceMax(self, state, 1, MinClockMax, "BorrowedMiracle");
                        MiracleClockService.ResetToMaxAndGrantStarlight(self, state, "BorrowedMiracle");
                        PlayerApi.ShowCaption("\u501f\u6765\u7684\u5947\u8ff9\uff1a\u65f6\u949f\u4e0a\u9650\u4e0b\u964d\u3002");
                        TerriasLog.Info("Borrowed Miracle resolved: owner=" + ownerId + ", copied=" + copiedGuide + ", success=" + copied + ", clockMax=" + state.ClockMax);
                    }),
                    new TerriasFrameStep("RequestGuidanceSelection", () =>
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
            TerriasPerformanceCounters.RecordDuration("Loneer.TriggerBorrowedMiracle", start);
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
        var start = TerriasPerformanceCounters.Timestamp();
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
                SetGuidance(state, TerriasIds.WitchStarScoreCardId, "\u9b54\u5973\u7684\u661f\u8c31");
                PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a\u9b54\u5973\u7684\u661f\u8c31");
                TerriasLog.Info("Loneer guidance fallback to Witch Star Score: owner=" + owner.InstanceId + ", version=" + selectionVersion);
                RecordLoneerSegment("Loneer.RequestGuidanceSelection.EmptyFallback", segment);
                return;
            }

            var opened = CardSelectionApi.SelectOneFromCards(
                self,
                source,
                card => !IsExcludedActionCard(card),
                card => ApplyGuidanceSelection(owner, state, selectionVersion, card, "selected"),
                caption,
                () => ResolveRandomGuidanceFallback(owner, state, selectionVersion, source, "cancelled"),
                new AuraCombatAi.Shared.CombatInteractionHint
                {
                    OwnerModId = TerriasIds.ModId,
                    Purpose = "loneer-guidance",
                    Kind = AuraCombatAi.Shared.CombatPromptKind.Guidance,
                    Zone = AuraCombatAi.Shared.CombatPromptZone.Generated,
                    Forced = true,
                    PreferLowestValue = false
                });
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
            TerriasPerformanceCounters.RecordDuration("Loneer.RequestGuidanceSelection", start);
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
        var enqueued = TerriasFrameDispatcher.RunOnceNextFrame(
            "Loneer.GuidanceSelection." + owner.InstanceId,
            () =>
            {
                var start = TerriasPerformanceCounters.Timestamp();
                try
                {
                    var current = LoneerCombatStateStore.Get(owner);
                    if (!ReferenceEquals(current, state))
                    {
                        return;
                    }

                    if (TerriasCombatUiWorkload.IsBusy)
                    {
                        state.SelectionScheduled = false;
                        TerriasPerformanceCounters.Record("Loneer.GuidanceSelection.DeferredForCardUi");
                        RequestGuidanceSelectionDeferred(self, state, caption, source + ":card-ui-busy");
                        return;
                    }

                    RequestGuidanceSelection(self, state, caption);
                }
                finally
                {
                    TerriasPerformanceCounters.RecordDuration("Loneer.GuidanceSelection.Action", start);
                }
            });
        TerriasPerformanceCounters.Record(enqueued
            ? "Loneer.GuidanceSelection.Enqueued"
            : "Loneer.GuidanceSelection.Deduped");

        if (!enqueued)
        {
            TerriasLog.Debug("Loneer guidance selection already scheduled: owner=" + owner.InstanceId + ", source=" + source);
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
        TerriasLog.Info("Loneer guidance " + source + ": owner=" + owner.InstanceId + ", card=" + state.GuidanceCardId + ", version=" + selectionVersion);
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
        SetGuidance(state, TerriasIds.WitchStarScoreCardId, "\u9b54\u5973\u7684\u661f\u8c31");
        PlayerApi.ShowCaption("\u6307\u5f15\u724c\uff1a\u9b54\u5973\u7684\u661f\u8c31");
        TerriasLog.Warn("Loneer guidance random fallback exhausted candidates; owner=" + owner.InstanceId + ", reason=" + reason + ", version=" + selectionVersion);
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

        if (!PolymorphStateStore.IsEffectiveCombatRoleFor(owner, TerriasIds.LoneerCareerId))
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
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            var guidance = state.PreparedGuidance;
            var id = CardApi.ResolveCardId(guidance?.CardId ?? state.GuidanceCardId);
            if (string.IsNullOrWhiteSpace(id))
            {
                TerriasLog.Warn("Loneer guided card copy skipped: source=" + source + ", guidance=" + state.GuidanceCardId);
                return false;
            }

            var grantStart = TerriasPerformanceCounters.Timestamp();
            var result = guidance == null
                ? LoneerCardGrantService.GrantGuidanceCopyToHand(self, id, source)
                : LoneerCardGrantService.GrantGuidanceCopyToHand(self, guidance, source);
            TerriasPerformanceCounters.RecordDuration("Loneer.GrantGuidanceCopyToHand", grantStart);
            TerriasLog.Info("Loneer guided card copy: owner=" + self.Self.InstanceId
                + ", source=" + source
                + ", card=" + id
                + ", success=" + result.Success
                + (result.Success ? "" : ", step=" + result.FailureStep + ", error=" + result.FailureReason));
            return result.Success;
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("Loneer.TryAddGuidedCard", start);
        }
    }

    private static void SetGuidance(LoneerCombatState state, string cardId, string displayName)
    {
        var resolved = CardApi.ResolveCardId(cardId);
        if (string.Equals(resolved, TerriasIds.WitchStarScoreCardId, StringComparison.Ordinal)
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
        return string.Equals(value, TerriasIds.WitchStarScoreCardId, StringComparison.Ordinal)
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
            || CardMutationService.HasRuntimeMarker(config, TerriasIds.LoneerDerivedMarker)
            || CardMutationService.HasRuntimeMarker(config, TerriasIds.LoneerGuidanceMarker);
    }

    private static bool IsExcludedActionCard(string id)
    {
        var value = (id ?? "").Replace("*", "").Trim();
        return string.IsNullOrWhiteSpace(value)
            || StarScoreService.IsStellarOvertureCard(value)
            || value == "witch_star_score"
            || value == TerriasIds.WitchStarScoreCardId
            || value == "loneer_morning_star_prayer"
            || value == TerriasIds.LoneerMorningPrayerSkillCardId;
    }

    private static long RecordLoneerSegment(string name, long segmentStart)
    {
        TerriasPerformanceCounters.RecordDuration(name, segmentStart);
        return TerriasPerformanceCounters.Timestamp();
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
                     TerriasIds.StarStonePouch,
                     TerriasIds.MiracleClock,
                     TerriasIds.Starlight,
                     TerriasIds.StarBlessing,
                     TerriasIds.StarScore,
                     TerriasIds.Resonance
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
            TerriasLog.Debug("Morning Star Prayer cooldown UI refresh skipped: " + ex.Message);
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
