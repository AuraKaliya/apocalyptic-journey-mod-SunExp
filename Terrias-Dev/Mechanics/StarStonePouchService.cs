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
        string channelId,
        string buffId,
        string stone,
        int blackStonesRemaining,
        int starlightGain)
    {
        OwnerStatusId = ownerStatusId ?? "";
        ChannelId = channelId ?? "";
        BuffId = buffId ?? "";
        Stone = stone ?? "";
        BlackStonesRemaining = Math.Max(0, blackStonesRemaining);
        StarlightGain = Math.Max(0, starlightGain);
    }

    public string OwnerStatusId { get; }

    public string ChannelId { get; }

    public string BuffId { get; }

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

    public StarStonePouchResetPolicy ResetPolicy { get; set; }

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
        ResetPolicy = StarStonePouchResetPolicy.RemoveWhenExhausted;
    }
}

public static class StarStonePouchStateStore
{
    private static readonly Dictionary<string, StarStonePouchState> States = new(StringComparer.Ordinal);

    public static StarStonePouchState? GetOrCreate(IStatusManager? owner, string channelId = StarStonePouchService.CareerChannel)
    {
        var key = StateKey(owner, channelId);
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

    public static StarStonePouchState? Get(IStatusManager? owner, string channelId = StarStonePouchService.CareerChannel)
    {
        var key = StateKey(owner, channelId);
        return !string.IsNullOrWhiteSpace(key) && States.TryGetValue(key, out var state)
            ? state
            : null;
    }

    public static StarStonePouchState? ResetForFight(IStatusManager? owner, string channelId = StarStonePouchService.CareerChannel)
    {
        var state = GetOrCreate(owner, channelId);
        state?.Reset();
        return state;
    }

    public static void Remove(IStatusManager? owner, string channelId = StarStonePouchService.CareerChannel)
    {
        var key = StateKey(owner, channelId);
        if (!string.IsNullOrWhiteSpace(key))
        {
            States.Remove(key);
        }
    }

    public static void ClearAll()
    {
        States.Clear();
    }

    private static string StateKey(IStatusManager? owner, string channelId)
    {
        return MorningStarRelicFormula.PouchStateKey(owner?.InstanceId ?? "", channelId);
    }
}

public static class StarStonePouchService
{
    public const string BlackStone = "B";
    public const string WhiteStone = "W";
    public const string CareerChannel = MorningStarRelicFormula.CareerPouchChannel;
    public const string RelicChannel = MorningStarRelicFormula.RelicPouchChannel;

    private const int InitialBlackStones = 9;
    private const int InitialWhiteStones = 1;
    private const int MinBlackStones = 1;

    public static event Action<ScriptExecutor, StarStonePouchDrawResult>? Drawn;

    public static void GrantInitial(ScriptExecutor self)
    {
        GrantInitial(self, CareerChannel, StarStonePouchResetPolicy.NaturalMorningStar);
    }

    public static void GrantRelicInitial(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return;
        }

        var ownerIsLoneer = PolymorphStateStore.IsEffectiveCombatRoleFor(self.Self, TerriasIds.LoneerCareerId);
        GrantInitial(self, RelicChannel, MorningStarRelicFormula.RelicPouchResetPolicy(ownerIsLoneer));
    }

    private static void GrantInitial(ScriptExecutor self, string channelId, StarStonePouchResetPolicy resetPolicy)
    {
        if (self?.Self == null)
        {
            return;
        }

        var state = StarStonePouchStateStore.ResetForFight(self.Self, channelId);
        if (state == null)
        {
            return;
        }

        InitializeState(state, resetPolicy);
        self.SetStatus("Self");
        self.AddBuff(BuffId(channelId), InitialBlackStones.ToString());
        SyncBuff(self, state, channelId);
    }

    public static bool EnsurePresent(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return false;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self, CareerChannel);
        if (state == null)
        {
            return false;
        }

        EnsureInitialized(state, StarStonePouchResetPolicy.NaturalMorningStar);
        if (state.Stones.Count == 0)
        {
            // Natural Morning Star normally performs the reset. This recovery
            // covers a form change that cancelled the already-triggered frame
            // sequence before the reset step could run.
            ResetStoneBag(state);
        }
        if (!BuffApi.Has(self.Self, TerriasIds.StarStonePouch))
        {
            self.SetStatus("Self");
            self.AddBuff(TerriasIds.StarStonePouch, Math.Max(1, state.BlackStoneCount()).ToString());
        }

        SyncBuff(self, state, CareerChannel);
        return BuffApi.Has(self.Self, TerriasIds.StarStonePouch);
    }

    public static void Apply(ScriptExecutor self)
    {
        Apply(self, CareerChannel, StarStonePouchResetPolicy.NaturalMorningStar);
    }

    public static void ApplyRelic(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return;
        }

        var state = StarStonePouchStateStore.Get(self.Self, RelicChannel);
        var ownerIsLoneer = PolymorphStateStore.IsEffectiveCombatRoleFor(self.Self, TerriasIds.LoneerCareerId);
        Apply(self, RelicChannel, state?.ResetPolicy ?? MorningStarRelicFormula.RelicPouchResetPolicy(ownerIsLoneer));
    }

    private static void Apply(ScriptExecutor self, string channelId, StarStonePouchResetPolicy resetPolicy)
    {
        if (self?.Self == null)
        {
            return;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self, channelId);
        if (state == null)
        {
            return;
        }

        EnsureInitialized(state, resetPolicy);
        SyncBuff(self, state, channelId);

        TerriasActionPassiveRegistry.Register(
            self,
            RegistrationId(channelId),
            AuraShared.Core.AuraCardActionPhase.Committed,
            _ => DrawForAction(self, channelId));
    }

    public static void Clear(ScriptExecutor self)
    {
        Clear(self, CareerChannel);
    }

    public static void ClearRelic(ScriptExecutor self)
    {
        Clear(self, RelicChannel);
    }

    private static void Clear(ScriptExecutor self, string channelId)
    {
        TerriasActionPassiveRegistry.Unregister(self, RegistrationId(channelId));
        StarStonePouchStateStore.Remove(self?.Self, channelId);
    }

    public static void RemoveState(IStatusManager? owner)
    {
        StarStonePouchStateStore.Remove(owner, CareerChannel);
    }

    public static void RemoveRelicState(IStatusManager? owner)
    {
        StarStonePouchStateStore.Remove(owner, RelicChannel);
    }

    public static int ReduceBlackStoneMax(ScriptExecutor self, int amount)
    {
        if (self?.Self == null)
        {
            return 0;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self, CareerChannel);
        if (state == null)
        {
            return 0;
        }

        EnsureInitialized(state, StarStonePouchResetPolicy.NaturalMorningStar);
        var beforeMax = CurrentBlackStoneMax(state);
        state.BlackStoneMax = Math.Max(MinBlackStones, beforeMax - Math.Max(0, amount));
        TrimBlackStonesToMax(state);
        SyncBuff(self, state, CareerChannel);

        return state.BlackStoneMax;
    }

    public static void ResetPouch(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self, CareerChannel);
        if (state == null)
        {
            return;
        }

        EnsureInitialized(state, StarStonePouchResetPolicy.NaturalMorningStar);
        ResetStoneBag(state);
        SyncBuff(self, state, CareerChannel);
    }

    public static int BlackStoneMax(ScriptExecutor self)
    {
        var state = StarStonePouchStateStore.Get(self?.Self, CareerChannel);
        return state == null ? InitialBlackStones : CurrentBlackStoneMax(state);
    }

    public static int CurrentBlackStones(ScriptExecutor self)
    {
        var state = StarStonePouchStateStore.Get(self?.Self, CareerChannel);
        return state?.BlackStoneCount() ?? 0;
    }

    public static int RelicBlackStoneMax(ScriptExecutor self)
    {
        var state = StarStonePouchStateStore.Get(self?.Self, RelicChannel);
        return state == null ? InitialBlackStones : CurrentBlackStoneMax(state);
    }

    public static int CurrentRelicBlackStones(ScriptExecutor self)
    {
        return StarStonePouchStateStore.Get(self?.Self, RelicChannel)?.BlackStoneCount() ?? 0;
    }

    private static void DrawForAction(ScriptExecutor self, string channelId)
    {
        var buffId = BuffId(channelId);
        if (self?.Self == null || !BuffApi.Has(self.Self, buffId))
        {
            return;
        }

        if (MorningStarRelicFormula.ParticipatesInStarStoneOrbit(channelId)
            && !PolymorphStateStore.IsEffectiveCombatRoleFor(self.Self, TerriasIds.LoneerCareerId))
        {
            return;
        }

        var state = StarStonePouchStateStore.GetOrCreate(self.Self, channelId);
        if (state == null)
        {
            return;
        }

        var fallbackPolicy = channelId == CareerChannel
            ? StarStonePouchResetPolicy.NaturalMorningStar
            : MorningStarRelicFormula.RelicPouchResetPolicy(
                PolymorphStateStore.IsEffectiveCombatRoleFor(self.Self, TerriasIds.LoneerCareerId));
        EnsureInitialized(state, fallbackPolicy);
        if (state.Stones.Count == 0)
        {
            ResolveExhaustedPouch(self, state, channelId);
            return;
        }

        var stone = state.DrawStone();
        if (string.IsNullOrWhiteSpace(stone))
        {
            return;
        }

        var blackStonesRemaining = state.BlackStoneCount();
        var starlightGain = stone == WhiteStone ? blackStonesRemaining : 1;
        StarScoreService.AddStarlight(self, starlightGain);

        var pouchName = channelId == RelicChannel ? "备用星石袋" : "星石袋";
        if (stone == WhiteStone)
        {
            PlayerApi.ShowCaption(pouchName + "：抽出白石，星辉+" + starlightGain + "。");
        }
        else
        {
            PlayerApi.ShowCaption(pouchName + "：抽出黑石，星辉+1。");
        }

        PublishDrawn(self, new StarStonePouchDrawResult(
            self.Self.InstanceId,
            channelId,
            buffId,
            stone,
            blackStonesRemaining,
            starlightGain));

        if (state.Stones.Count == 0)
        {
            ResolveExhaustedPouch(self, state, channelId);
        }
        else
        {
            SyncBuff(self, state, channelId);
        }
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

    private static void EnsureInitialized(StarStonePouchState state, StarStonePouchResetPolicy resetPolicy)
    {
        if (state.Initialized)
        {
            return;
        }

        InitializeState(state, resetPolicy);
    }

    private static void InitializeState(StarStonePouchState state, StarStonePouchResetPolicy resetPolicy)
    {
        state.BlackStoneMax = InitialBlackStones;
        state.ResetPolicy = resetPolicy;
        ResetStoneBag(state);
        state.Initialized = true;
    }

    private static void ResolveExhaustedPouch(ScriptExecutor self, StarStonePouchState state, string channelId)
    {
        if (state.ResetPolicy == StarStonePouchResetPolicy.WhenExhausted)
        {
            ResetStoneBag(state);
            SyncBuff(self, state, channelId);
            PlayerApi.ShowCaption("备用星石袋已重新装填。");
            return;
        }

        if (state.ResetPolicy == StarStonePouchResetPolicy.RemoveWhenExhausted)
        {
            BuffApi.SetExactLevel(self.Self, BuffId(channelId), 0);
            Clear(self, channelId);
            PlayerApi.ShowCaption("备用星石袋已经用尽。");
            return;
        }

        SyncBuff(self, state, channelId);
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

    private static void SyncBuff(ScriptExecutor self, StarStonePouchState state, string channelId)
    {
        BuffApi.SetExactLevel(self?.Self, BuffId(channelId), state.BlackStoneCount(), keepZero: true);
    }

    private static string BuffId(string channelId)
    {
        return channelId == RelicChannel ? TerriasIds.RelicStarStonePouch : TerriasIds.StarStonePouch;
    }

    private static string RegistrationId(string channelId)
    {
        return channelId == RelicChannel ? "Buff.RelicStarStonePouch" : "Buff.StarStonePouch";
    }
}
