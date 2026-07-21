using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public readonly struct MiracleClockChangeResult
{
    public MiracleClockChangeResult(int before, int after, bool depleted)
    {
        Before = Math.Max(0, before);
        After = Math.Max(0, after);
        Depleted = depleted;
    }

    public int Before { get; }

    public int After { get; }

    public bool Depleted { get; }
}

public static class MiracleClockService
{
    public static void Initialize(ScriptExecutor self, LoneerCombatState state, int initialMax)
    {
        if (self?.Self == null || state == null)
        {
            return;
        }

        state.ClockMax = Math.Max(1, initialMax);
        state.ClockValue = state.ClockMax;
        Sync(self, state);
    }

    public static MiracleClockChangeResult ReduceBy(ScriptExecutor self, LoneerCombatState state, int amount, string source)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            if (self?.Self == null || state == null)
            {
                return new MiracleClockChangeResult(0, 0, false);
            }

            var before = Math.Max(0, state.ClockValue);
            var delta = Math.Max(0, amount);
            var after = Math.Max(0, before - delta);
            state.ClockValue = after;
            Sync(self, state);
            var depleted = before > 0 && after <= 0;
            TerriasLog.Debug("Miracle Clock reduced: owner="
                + self.Self.InstanceId
                + ", before="
                + before
                + ", after="
                + after
                + ", amount="
                + delta
                + ", source="
                + source);
            return new MiracleClockChangeResult(before, after, depleted);
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("MiracleClock.ReduceBy", start);
        }
    }

    public static int ReduceMax(ScriptExecutor self, LoneerCombatState state, int amount, int min, string source)
    {
        if (self?.Self == null || state == null)
        {
            return 0;
        }

        var before = Math.Max(1, state.ClockMax);
        state.ClockMax = Math.Max(Math.Max(1, min), before - Math.Max(0, amount));
        if (state.ClockValue > state.ClockMax)
        {
            state.ClockValue = state.ClockMax;
        }

        Sync(self, state);
        TerriasLog.Info("Miracle Clock cap changed: owner="
            + self.Self.InstanceId
            + ", beforeMax="
            + before
            + ", afterMax="
            + state.ClockMax
            + ", source="
            + source);
        return state.ClockMax;
    }

    public static bool ResetToMaxAndGrantStarlight(ScriptExecutor self, LoneerCombatState state, string source)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            if (self?.Self == null || state == null)
            {
                return false;
            }

            var max = Math.Max(1, state.ClockMax);
            state.ClockValue = max;
            Sync(self, state);
            StarScoreService.AddStarlight(self, max);
            TerriasLog.Info("Miracle Clock reset: owner="
                + self.Self.InstanceId
                + ", clockMax="
                + max
                + ", starlight="
                + max
                + ", source="
                + source);
            return true;
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("MiracleClock.Reset", start);
        }
    }

    public static void Sync(ScriptExecutor self, LoneerCombatState state)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            if (self?.Self == null || state == null)
            {
                return;
            }

            EnsureBuffExistsForZero(self, state);
            BuffApi.SetExactLevel(self.Self, TerriasIds.MiracleClock, state.ClockValue, keepZero: true);
            var buff = self.Self.GetBuff(TerriasIds.MiracleClock);
            if (buff?.buffConfig != null)
            {
                buff.buffConfig.UpperBound = Math.Max(1, state.ClockMax);
            }
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("MiracleClock.SyncBuff", start);
        }
    }

    private static void EnsureBuffExistsForZero(ScriptExecutor self, LoneerCombatState state)
    {
        if (self?.Self == null || state.ClockValue > 0 || self.Self.GetBuff(TerriasIds.MiracleClock) != null)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(TerriasIds.MiracleClock, Math.Max(1, state.ClockMax).ToString());
    }
}
