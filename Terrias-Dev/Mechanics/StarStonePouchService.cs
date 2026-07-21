using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public sealed class StarStonePouchDrawResult
{
    public StarStonePouchDrawResult(
        string ownerStatusId,
        string stone,
        int blackStonesRemaining,
        int starlightGain)
    {
        OwnerStatusId = ownerStatusId ?? "";
        Stone = stone ?? "";
        BlackStonesRemaining = Math.Max(0, blackStonesRemaining);
        StarlightGain = Math.Max(0, starlightGain);
    }

    public string OwnerStatusId { get; }

    public string Stone { get; }

    public int BlackStonesRemaining { get; }

    public int StarlightGain { get; }

    public bool IsBlack => Stone == StarStonePouchService.BlackStone;

    public bool IsWhite => Stone == StarStonePouchService.WhiteStone;
}

public sealed class StarStonePouchState
{
    private readonly List<string> stones = new();

    public int BlackStoneMax { get; set; }

    public bool Initialized { get; set; }

    public IReadOnlyList<string> Stones => stones;

    public int BlackStoneCount()
    {
        return stones.Count(stone => stone == StarStonePouchService.BlackStone);
    }

    public void ReplaceStones(IEnumerable<string> values)
    {
        stones.Clear();
        stones.AddRange(values);
    }

    public string DrawStone()
    {
        if (stones.Count == 0)
        {
            return "";
        }

        var stone = stones[0];
        stones.RemoveAt(0);
        return stone;
    }

    public void Reset()
    {
        stones.Clear();
        BlackStoneMax = 0;
        Initialized = false;
    }
}

public static class StarStonePouchStateStore
{
    private static readonly Dictionary<string, StarStonePouchState> States = new(StringComparer.Ordinal);

    public static StarStonePouchState? GetOrCreate(IStatusManager? owner)
    {
        var key = OwnerKey(owner);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (!States.TryGetValue(key, out var state))
        {
            state = new StarStonePouchState();
            States[key] = state;
        }

        return state;
    }

    public static StarStonePouchState? Get(IStatusManager? owner)
    {
        var key = OwnerKey(owner);
        return !string.IsNullOrWhiteSpace(key) && States.TryGetValue(key, out var state)
            ? state
            : null;
    }

    public static StarStonePouchState? ResetForFight(IStatusManager? owner)
    {
        var state = GetOrCreate(owner);
        state?.Reset();
        return state;
    }

    public static void Remove(IStatusManager? owner)
    {
        var key = OwnerKey(owner);
        if (!string.IsNullOrWhiteSpace(key))
        {
            States.Remove(key);
        }
    }

    public static void ClearAll()
    {
        States.Clear();
    }

    private static string OwnerKey(IStatusManager? owner)
    {
        return owner?.InstanceId ?? "";
    }
}

public static class StarStonePouchService
{
    public const string BlackStone = "B";
    public const string WhiteStone = "W";

    private const int InitialBlackStones = 9;
    private const int InitialWhiteStones = 1;
    private const int MinBlackStones = 1;

    public static event Action<ScriptExecutor, StarStonePouchDrawResult>? Drawn;

    public static void GrantInitial(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return;
        }

        var state = StarStonePouchStateStore.ResetForFight(self.Self);
        if (state == null)
        {
            return;
        }

        InitializeState(state);
        self.SetStatus("Self");
        self.AddBuff(TerriasIds.StarStonePouch, InitialBlackStones.ToString());
        SyncBuff(self, state);
    }

    public static void Apply(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            return;
        }

        EnsureInitialized(state);
        SyncBuff(self, state);

        var token = ExecutorApi.RegisterHook(self, "TerriasStarStonePouchHook", "TerriasStarStonePouchToken");
        if (token == null)
        {
            return;
        }

        ExecutorApi.TryAddTokenedEvent(self, "ActionAfter", "TerriasStarStonePouchToken", token,
            new Action(() => DrawForAction(self)), "star_stone_pouch");
    }

    public static void Clear(ScriptExecutor self)
    {
        ExecutorApi.ClearHook(self, "TerriasStarStonePouchHook", "TerriasStarStonePouchToken");
        StarStonePouchStateStore.Remove(self?.Self);
    }

    public static void RemoveState(IStatusManager? owner)
    {
        StarStonePouchStateStore.Remove(owner);
    }

    public static int ReduceBlackStoneMax(ScriptExecutor self, int amount)
    {
        if (self?.Self == null)
        {
            return 0;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            return 0;
        }

        EnsureInitialized(state);
        var beforeMax = CurrentBlackStoneMax(state);
        state.BlackStoneMax = Math.Max(MinBlackStones, beforeMax - Math.Max(0, amount));
        TrimBlackStonesToMax(state);
        SyncBuff(self, state);
        return state.BlackStoneMax;
    }

    public static void ResetPouch(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            return;
        }

        EnsureInitialized(state);
        ResetStoneBag(state);
        SyncBuff(self, state);
    }

    public static int BlackStoneMax(ScriptExecutor self)
    {
        var state = StarStonePouchStateStore.Get(self?.Self);
        return state == null ? InitialBlackStones : CurrentBlackStoneMax(state);
    }

    public static int CurrentBlackStones(ScriptExecutor self)
    {
        var state = StarStonePouchStateStore.Get(self?.Self);
        return state?.BlackStoneCount() ?? 0;
    }

    private static void DrawForAction(ScriptExecutor self)
    {
        if (self?.Self == null || !BuffApi.Has(self.Self, TerriasIds.StarStonePouch))
        {
            return;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self);
        if (state == null)
        {
            return;
        }

        EnsureInitialized(state);
        if (state.Stones.Count == 0)
        {
            ResetStoneBag(state);
        }

        var stone = state.DrawStone();
        if (string.IsNullOrWhiteSpace(stone))
        {
            return;
        }

        var blackStonesRemaining = state.BlackStoneCount();
        var starlightGain = stone == WhiteStone ? blackStonesRemaining : 1;
        StarScoreService.AddStarlight(self, starlightGain);
        SyncBuff(self, state);

        if (stone == WhiteStone)
        {
            PlayerApi.ShowCaption("\u661f\u77f3\u888b\uff1a\u62bd\u51fa\u767d\u77f3\uff0c\u661f\u8f89+" + starlightGain + "\u3002");
        }
        else
        {
            PlayerApi.ShowCaption("\u661f\u77f3\u888b\uff1a\u62bd\u51fa\u9ed1\u77f3\uff0c\u661f\u8f89+1\u3002");
        }

        PublishDrawn(self, new StarStonePouchDrawResult(
            self.Self.InstanceId,
            stone,
            blackStonesRemaining,
            starlightGain));
    }

    private static void PublishDrawn(ScriptExecutor self, StarStonePouchDrawResult result)
    {
        var handlers = Drawn;
        if (handlers == null)
        {
            return;
        }

        foreach (Action<ScriptExecutor, StarStonePouchDrawResult> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(self, result);
            }
            catch (Exception ex)
            {
                TerriasLog.Error("Star stone pouch draw subscriber failed", ex);
            }
        }
    }

    private static void EnsureInitialized(StarStonePouchState state)
    {
        if (state.Initialized)
        {
            return;
        }

        InitializeState(state);
    }

    private static void InitializeState(StarStonePouchState state)
    {
        state.BlackStoneMax = InitialBlackStones;
        ResetStoneBag(state);
        state.Initialized = true;
    }

    private static void ResetStoneBag(StarStonePouchState state)
    {
        var stones = new List<string>();
        var blackStoneMax = CurrentBlackStoneMax(state);
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

    private static int CurrentBlackStoneMax(StarStonePouchState state)
    {
        return Math.Max(MinBlackStones, state.BlackStoneMax <= 0 ? InitialBlackStones : state.BlackStoneMax);
    }

    private static void TrimBlackStonesToMax(StarStonePouchState state)
    {
        while (state.BlackStoneCount() > CurrentBlackStoneMax(state))
        {
            var blackIndexes = state.Stones
                .Select((stone, index) => stone == BlackStone ? index : -1)
                .Where(index => index >= 0)
                .ToList();
            if (blackIndexes.Count == 0)
            {
                return;
            }

            var removeIndex = blackIndexes[UnityEngine.Random.Range(0, blackIndexes.Count)];
            state.ReplaceStones(state.Stones.Where((_, index) => index != removeIndex).ToList());
        }
    }

    private static void Shuffle(IList<string> stones)
    {
        for (var i = stones.Count - 1; i > 0; i--)
        {
            var j = UnityEngine.Random.Range(0, i + 1);
            (stones[i], stones[j]) = (stones[j], stones[i]);
        }
    }

    private static void SyncBuff(ScriptExecutor self, StarStonePouchState state)
    {
        BuffApi.SetExactLevel(self?.Self, TerriasIds.StarStonePouch, state.BlackStoneCount(), keepZero: true);
    }
}
