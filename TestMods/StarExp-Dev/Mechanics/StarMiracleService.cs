using System;
using StarExp.Dll.GameApi;
using StarExp.Dll.Infrastructure;

namespace StarExp.Dll.Mechanics;

public static class StarMiracleService
{
    private const int InitialBlackStones = 9;
    private const int MaxClock = 8;

    public static void RegisterCareer(ScriptExecutor self)
    {
        PlayerApi.SetGameVar(StarExpIds.CareerActive, "1");
        PlayerApi.SetSkillTime(StarExpIds.MorningStarSkillCardId, 0);
        PlayerApi.SetSkillTime(StarExpIds.BorrowedMiracleSkillCardId, 0);
        EnsureCombatHooks(self);

        ExecutorApi.TryAddEvent(self, "FightStart", new Action(() => OnFightStart(self)), "star_miracle_career");
        ExecutorApi.TryAddEvent(self, "Action", new Action(() => OnAction(self)), "star_miracle_career");
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() => OnStartRound(self)), "star_miracle_career");
        ExecutorApi.TryAddEvent(self, "Win", new Action(() => EndCombatCleanup(self)), "star_miracle_career");
        ExecutorApi.TryAddEvent(self, "Escape", new Action(() => EndCombatCleanup(self)), "star_miracle_career");
    }

    public static void EnsureCombatHooks(ScriptExecutor self)
    {
        if (ExecutorApi.CombatIntGet(StarExpIds.CoreHooksRegistered) == 1)
        {
            return;
        }

        var registered = ExecutorApi.TryAddEvent(self, "StartRound", new Action(() => OnStartRound(self)), "star_miracle_core");
        ExecutorApi.TryAddEvent(self, "Win", new Action(() => EndCombatCleanup(self)), "star_miracle_core");
        ExecutorApi.TryAddEvent(self, "Escape", new Action(() => EndCombatCleanup(self)), "star_miracle_core");
        if (registered)
        {
            ExecutorApi.CombatIntSet(StarExpIds.CoreHooksRegistered, 1);
        }
    }

    public static void OnFightStart(ScriptExecutor self)
    {
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneThisRound, 0);
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneThisCombat, 0);
        ExecutorApi.CombatIntSet(StarExpIds.WaiveNextDebt, 0);
        ResetPouchAndClock(self);
        ClearCombatBuffs(self);
        SyncBuffs(self);
    }

    public static void OnAction(ScriptExecutor self)
    {
        if (ExecutorApi.CombatIntGet(StarExpIds.ActionResolving) == 1)
        {
            return;
        }

        ExecutorApi.CombatIntSet(StarExpIds.ActionResolving, 1);
        try
        {
            DrawStone(self);
        }
        finally
        {
            ExecutorApi.CombatIntSet(StarExpIds.ActionResolving, 0);
        }
    }

    public static void OnStartRound(ScriptExecutor self)
    {
        TickSkill(StarExpIds.MorningStarSkillCardId);
        TickSkill(StarExpIds.BorrowedMiracleSkillCardId);
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneThisRound, 0);

        var debt = ClockDebt(self);
        if (debt > 0)
        {
            self.SetStatus("Self");
            ExecutorApi.DealDamage(self, debt, "True");
        }

        if (debt >= 3 && !BuffApi.Has(self.Self, StarExpIds.TimeErosion))
        {
            self.SetStatus("Self");
            self.AddBuff(StarExpIds.TimeErosion, "1");
        }
        else if (debt < 3 && BuffApi.Has(self.Self, StarExpIds.TimeErosion))
        {
            self.SetStatus("Self");
            self.RemoveBuff(StarExpIds.TimeErosion);
        }
    }

    public static void DrawStone(ScriptExecutor self)
    {
        EnsureInitialized(self);
        var black = BlackStones();
        var total = black + 1;
        if (black <= 0 || UnityEngine.Random.Range(0, total) == 0)
        {
            TriggerNaturalMorningStar(self);
            return;
        }

        black -= 1;
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneRemaining, black);
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneThisRound, ExecutorApi.CombatIntGet(StarExpIds.BlackStoneThisRound) + 1);
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneThisCombat, ExecutorApi.CombatIntGet(StarExpIds.BlackStoneThisCombat) + 1);
        ReduceClock(self, 1, canWaiveDebt: false);
        SyncBuffs(self);
    }

    public static void RemoveBlackStones(ScriptExecutor self, int amount)
    {
        EnsureInitialized(self);
        var next = Math.Max(0, BlackStones() - Math.Max(0, amount));
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneRemaining, next);
        SyncBuffs(self);
    }

    public static void ReduceClock(ScriptExecutor self, int amount, bool canWaiveDebt)
    {
        EnsureInitialized(self);
        var next = Math.Max(0, Clock() - Math.Max(0, amount));
        ExecutorApi.CombatIntSet(StarExpIds.MiracleClockValue, next);
        if (canWaiveDebt)
        {
            ExecutorApi.CombatIntSet(StarExpIds.WaiveNextDebt, 1);
        }

        if (next <= 0)
        {
            TriggerBorrowedMiracle(self);
        }
        else
        {
            SyncBuffs(self);
        }
    }

    public static void TriggerNaturalMorningStar(ScriptExecutor self)
    {
        AddGuidedCard(self);
        AddStarlight(self, 1 + ExecutorApi.SelfBuffLevel(self, StarExpIds.WhiteStonePower));
        ApplyTimeErosion(self);
        ResetPouchAndClock(self);
        SyncBuffs(self);
        PlayerApi.ShowCaption("晨星触发：白石抵达。");
    }

    public static void TriggerBorrowedMiracle(ScriptExecutor self)
    {
        AddGuidedCard(self);
        var debt = ExecutorApi.CombatIntGet(StarExpIds.WaiveNextDebt) == 1 ? 0 : 1;
        ExecutorApi.CombatIntSet(StarExpIds.WaiveNextDebt, 0);
        if (debt > 0)
        {
            AddClockDebt(self, debt);
        }

        ApplyTimeErosion(self);
        ResetPouchAndClock(self);
        SyncBuffs(self);
        PlayerApi.ShowCaption(debt > 0 ? "借来的奇迹：钟债增加。" : "借来的奇迹：钟债被免除。");
    }

    public static void AddStarlight(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(StarExpIds.Starlight, amount.ToString());
    }

    public static void AddClockDebt(ScriptExecutor self, int amount)
    {
        var remaining = Math.Max(0, amount);
        if (remaining <= 0)
        {
            return;
        }

        var starlight = ExecutorApi.SelfBuffLevel(self, StarExpIds.Starlight);
        while (remaining > 0 && starlight >= 3)
        {
            ExecutorApi.RemoveSelfBuffStacks(self, StarExpIds.Starlight, 3);
            starlight -= 3;
            remaining -= 1;
        }

        if (remaining <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.AddBuff(StarExpIds.ClockDebt, remaining.ToString());
    }

    public static int BlackStonesThisRound()
    {
        return ExecutorApi.CombatIntGet(StarExpIds.BlackStoneThisRound);
    }

    public static int BlackStonesThisCombat()
    {
        return ExecutorApi.CombatIntGet(StarExpIds.BlackStoneThisCombat);
    }

    public static int ClockDebt(ScriptExecutor self)
    {
        return ExecutorApi.SelfBuffLevel(self, StarExpIds.ClockDebt);
    }

    public static void ClearDebt(ScriptExecutor self)
    {
        ExecutorApi.RemoveSelfBuffStacks(self, StarExpIds.ClockDebt, int.MaxValue);
        self.SetStatus("Self");
        self.RemoveBuff(StarExpIds.TimeErosion);
    }

    public static void EndCombatCleanup(ScriptExecutor self)
    {
        ClearCombatBuffs(self);
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneRemaining, 0);
        ExecutorApi.CombatIntSet(StarExpIds.MiracleClockValue, 0);
        ExecutorApi.CombatIntSet(StarExpIds.ActionResolving, 0);
    }

    private static void EnsureInitialized(ScriptExecutor self)
    {
        if (BlackStones() <= 0 && Clock() <= 0)
        {
            ResetPouchAndClock(self);
        }

        SyncBuffs(self);
    }

    private static int BlackStones()
    {
        return ExecutorApi.CombatIntGet(StarExpIds.BlackStoneRemaining, InitialBlackStones);
    }

    private static int Clock()
    {
        return ExecutorApi.CombatIntGet(StarExpIds.MiracleClockValue, MaxClock);
    }

    private static void ResetPouchAndClock(ScriptExecutor self)
    {
        ExecutorApi.CombatIntSet(StarExpIds.BlackStoneRemaining, InitialBlackStones);
        ExecutorApi.CombatIntSet(StarExpIds.MiracleClockValue, MaxClock);
        SyncBuffs(self);
    }

    private static void SyncBuffs(ScriptExecutor self)
    {
        SyncTrait(self, StarExpIds.MiraclePouch, BlackStones());
        SyncTrait(self, StarExpIds.MiracleClock, Clock());
    }

    private static void SyncTrait(ScriptExecutor self, string buffId, int level)
    {
        self.SetStatus("Self");
        self.RemoveBuff(buffId);
        if (level > 0)
        {
            self.AddBuff(buffId, level.ToString());
        }
    }

    private static void ClearCombatBuffs(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.RemoveBuff(StarExpIds.MiraclePouch);
        self.RemoveBuff(StarExpIds.MiracleClock);
        self.RemoveBuff(StarExpIds.ClockDebt);
        self.RemoveBuff(StarExpIds.Starlight);
        self.RemoveBuff(StarExpIds.TimeErosion);
        self.RemoveBuff(StarExpIds.WhiteStonePower);
    }

    private static void AddGuidedCard(ScriptExecutor self)
    {
        CardApi.AddCardToHand(self, StarExpIds.WhiteStoneCardId);
    }

    private static void ApplyTimeErosion(ScriptExecutor self)
    {
        if (!BuffApi.Has(self.Self, StarExpIds.TimeErosion))
        {
            return;
        }

        self.SetStatus("Self");
        ExecutorApi.DealDamage(self, 2, "True");
    }

    private static void TickSkill(string key)
    {
        var current = PlayerApi.GetSkillTime(key);
        if (current > 0)
        {
            PlayerApi.SetSkillTime(key, current - 1);
        }
    }
}
