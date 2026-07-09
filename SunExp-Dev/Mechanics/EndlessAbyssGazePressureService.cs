using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class EndlessAbyssGazePressureService
{
    private const string CostAppliedMarker = "SunExpAbyssGazeCostApplied";
    private static readonly Dictionary<string, GazePlayerState> States = new(StringComparer.Ordinal);

    public static void ResetPlayerTurn(ScriptExecutor? executor, string source)
    {
        var owner = OwnerKey(executor);
        var state = GetState(owner);
        var status = executor?.Self ?? FightPlayer.Instance?.Status;
        if (status != null && IsLocalStatus(status))
        {
            BuffApi.SetExactLevel(status, SunExpIds.AbyssGazeBuffI, 0);
            BuffApi.SetExactLevel(status, SunExpIds.AbyssGazeBuffII, 0);
            BuffApi.SetExactLevel(status, SunExpIds.AbyssGazeBuffIII, 0);
        }

        ClearPendingCost(state, "ResetPlayerTurn:" + source, true);
        state.Threshold10Triggered = false;
        state.Threshold15Triggered = false;
        state.Threshold20Triggered = false;
        state.SeenCardGainKeys.Clear();
        SunExpLog.Info("[EndlessAbyssGaze] reset owner="
            + owner
            + " from "
            + source
            + "; hardLevel="
            + HardLevel()
            + ".");
    }

    public static void BeginCostPreview(CardItem? item, string source)
    {
        try
        {
            var state = LocalState();
            var config = item?.dataConfig;
            if (config == null || !state.CostPending)
            {
                return;
            }

            if (ReferenceEquals(state.ActiveCostConfig, config))
            {
                return;
            }

            if (state.ActiveCostConfig != null && !state.ActiveCostActionObserved)
            {
                CancelActiveCost(state, "BeginPreview:stale:" + source);
            }

            if (ApplyCostToCurrentUse(state, config, source, preview: true))
            {
                SunExpCardRefreshQueue.RequestDataUpdate(item, "AbyssGazePreview:" + source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssGaze] preview failed from " + source + ": " + ex.Message);
        }
    }

    public static bool CancelCostPreview(CardItem? item, string source)
    {
        try
        {
            var state = LocalState();
            var config = item?.dataConfig;
            if (config == null
                || state.ActiveCostConfig == null
                || state.ActiveCostActionObserved
                || !state.ActiveCostPreview
                || !ReferenceEquals(state.ActiveCostConfig, config))
            {
                return false;
            }

            CancelActiveCost(state, "CancelPreview:" + source);
            SunExpCardRefreshQueue.RequestDataUpdate(item, "AbyssGazePreviewCancel:" + source);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssGaze] preview cancel failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static void OnCardGained(ScriptExecutor? executor, IDataConfig? card, string source)
    {
        try
        {
            if (card == null || !IsLocalPlayerExecutor(executor))
            {
                return;
            }

            var state = GetState(OwnerKey(executor));
            if (!MarkCardGain(state, card, source))
            {
                return;
            }

            AddStackForGain(executor, state, source);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssGaze] card gained hook failed from " + source + ": " + ex.Message);
        }
    }

    public static void OnCardGainedById(ScriptExecutor? executor, string cardId, string source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cardId)
                || EndlessAbyssCurseService.SuppressGazeCardGain
                || !IsLocalPlayerExecutor(executor))
            {
                return;
            }

            AddStackForGain(executor, GetState(OwnerKey(executor)), source + ":" + CardApi.ResolveCardId(cardId));
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssGaze] card gained id hook failed from " + source + ": " + ex.Message);
        }
    }

    public static void OnCardUseBefore(CardItem? item, string source)
    {
        try
        {
            var state = LocalState();
            var config = item?.dataConfig;
            if (config == null)
            {
                return;
            }

            if (ReferenceEquals(state.ActiveCostConfig, config))
            {
                state.ActiveCostPreview = false;
                PlayerApi.ShowCaption("\u6df1\u6e0a\u51dd\u89c6\uff1a\u672c\u6b21\u51fa\u724c\u8017\u8d39+1\u3002");
                return;
            }

            if (state.ActiveCostConfig != null && !state.ActiveCostActionObserved)
            {
                CancelActiveCost(state, "CardUseBefore:stale:" + source);
            }

            if (!state.CostPending)
            {
                return;
            }

            if (ApplyCostToCurrentUse(state, config, source, preview: false))
            {
                SunExpCardRefreshQueue.RequestDataUpdate(item, "AbyssGazeCost:" + source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssGaze] before-use cost hook failed from " + source + ": " + ex.Message);
        }
    }

    public static void OnCardUseAfter(CardItem? item, string source)
    {
        try
        {
            var state = ActiveStateFor(item?.dataConfig) ?? LocalState();
            if (state.ActiveCostConfig == null)
            {
                return;
            }

            if (state.ActiveCostActionObserved)
            {
                ClearPendingCost(state, "CardUseAfter:" + source, true);
                return;
            }

            CancelActiveCost(state, "CardUseAfter:cancelled:" + source);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssGaze] after-use cost hook failed from " + source + ": " + ex.Message);
        }
    }

    public static void OnCardAction(IDataConfig? config, string source)
    {
        var state = ActiveStateFor(config);
        if (state == null)
        {
            return;
        }

        state.ActiveCostPreview = false;
        state.ActiveCostActionObserved = true;
        SunExpLog.Debug("[EndlessAbyssGaze] action observed for pending cost from " + source + ".");
    }

    public static void OnCardActionAfter(string source)
    {
        foreach (var state in States.Values.ToList())
        {
            if (state.ActiveCostConfig != null && state.ActiveCostActionObserved)
            {
                ClearPendingCost(state, "ActionAfter:" + source, true);
            }
        }
    }

    private static void ResolveThresholds(ScriptExecutor executor, GazePlayerState state, int hardLevel, int stacks, string source)
    {
        if (stacks >= 10 && !state.Threshold10Triggered)
        {
            state.Threshold10Triggered = true;
            EndlessAbyssCurseService.AddRandomCurseToCombatDeck(executor, source + ":10");
            SunExpLog.Info("[EndlessAbyssGaze] threshold 10 triggered owner=" + state.Owner + " from " + source + ".");
        }

        if (hardLevel >= 2 && stacks >= 15 && !state.Threshold15Triggered)
        {
            state.Threshold15Triggered = true;
            EndlessAbyssCurseService.AddRandomCurseToCombatDeck(executor, source + ":15");
            state.CostPending = true;
            SunExpLog.Info("[EndlessAbyssGaze] threshold 15 triggered owner=" + state.Owner + " from " + source + ".");
        }

        if (hardLevel >= 3 && stacks >= 20 && !state.Threshold20Triggered)
        {
            state.Threshold20Triggered = true;
            SunExpLog.Info("[EndlessAbyssGaze] threshold 20 triggered owner=" + state.Owner + " from " + source + ".");
            SunExpFrameDispatcher.RunOnceNextFrame(
                "EndlessAbyssGaze.ForceEndTurn." + state.Owner,
                () =>
                {
                    try
                    {
                        PlayerApi.ShowCaption("\u6df1\u6e0a\u51dd\u89c6\u8fbe\u523020\u5c42\uff0c\u9b54\u5973\u56de\u5408\u88ab\u5f3a\u5236\u7ed3\u675f\u3002");
                        FightManager.Instance?.TurnEnd();
                    }
                    catch (Exception ex)
                    {
                        SunExpLog.Warn("[EndlessAbyssGaze] force end turn failed: " + ex.Message);
                    }
                });
        }
    }

    private static bool ApplyCostToCurrentUse(GazePlayerState state, IDataConfig config, string source, bool preview)
    {
        if (state.ActiveCostConfig != null || CardMutationService.HasRuntimeMarker(config, CostAppliedMarker))
        {
            return false;
        }

        state.ActiveCostConfig = config;
        state.ActiveCostOriginalOnce = DictionaryUtil.GetInt(config.Vars, "OnceExCost");
        state.ActiveCostActionObserved = false;
        state.ActiveCostPreview = preview;
        CardMutationService.AdjustOnceCost(config, 1);
        CardMutationService.SetRuntimeMarkers(config, CostAppliedMarker);
        if (!preview)
        {
            PlayerApi.ShowCaption("\u6df1\u6e0a\u51dd\u89c6\uff1a\u672c\u6b21\u51fa\u724c\u8017\u8d39+1\u3002");
        }

        SunExpLog.Debug("[EndlessAbyssGaze] next card cost +1 marker applied to "
            + CardConfigApi.Id(config)
            + " owner="
            + state.Owner
            + " from "
            + source
            + (preview ? " preview." : "."));
        return true;
    }

    private static void ClearPendingCost(GazePlayerState state, string source, bool consumePending)
    {
        if (consumePending)
        {
            state.CostPending = false;
        }

        RestoreActiveCost(state);
        SunExpLog.Debug("[EndlessAbyssGaze] cleared pending next-cost owner="
            + state.Owner
            + " from "
            + source
            + ".");
    }

    private static void CancelActiveCost(GazePlayerState state, string source)
    {
        RestoreActiveCost(state);
        SunExpLog.Debug("[EndlessAbyssGaze] cancelled active next-cost owner="
            + state.Owner
            + " from "
            + source
            + ".");
    }

    private static void RestoreActiveCost(GazePlayerState state)
    {
        if (state.ActiveCostConfig == null)
        {
            return;
        }

        DictionaryUtil.Set(state.ActiveCostConfig.Vars, "OnceExCost", state.ActiveCostOriginalOnce.ToString());
        DictionaryUtil.Set(state.ActiveCostConfig.Vars, SunExpIds.RuntimeMarkersKey, RemoveToken(
            DictionaryUtil.Get(state.ActiveCostConfig.Vars, SunExpIds.RuntimeMarkersKey),
            CostAppliedMarker));
        state.ActiveCostConfig = null;
        state.ActiveCostOriginalOnce = 0;
        state.ActiveCostActionObserved = false;
        state.ActiveCostPreview = false;
    }

    private static int HardLevel()
    {
        return Math.Max(0, Math.Min(3, SunExpHardTagState.Level(SunExpHardTagIds.AbyssGaze)));
    }

    private static void AddStackForGain(ScriptExecutor? executor, GazePlayerState state, string source)
    {
        var level = HardLevel();
        if (level <= 0 || executor?.Self == null || !IsLocalPlayerExecutor(executor))
        {
            return;
        }

        var buffId = BuffIdForLevel(level);
        executor.Self.AddBuff(buffId, 1);
        var stacks = BuffApi.Level(executor.Self, buffId);
        SunExpLog.Debug("[EndlessAbyssGaze] stack +1 owner="
            + state.Owner
            + " level="
            + level
            + " stacks="
            + stacks
            + " from "
            + source
            + ".");
        ResolveThresholds(executor, state, level, stacks, source);
    }

    private static bool MarkCardGain(GazePlayerState state, IDataConfig card, string source)
    {
        var key = card.InstanceID;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = CardConfigApi.Id(card) + ":" + DictionaryUtil.Get(card.Vars, "SunExpRuntimeCreatedAt", source);
        }

        return string.IsNullOrWhiteSpace(key) || state.SeenCardGainKeys.Add(key);
    }

    private static GazePlayerState? ActiveStateFor(IDataConfig? config)
    {
        if (config == null)
        {
            return null;
        }

        return States.Values.FirstOrDefault(state => ReferenceEquals(state.ActiveCostConfig, config));
    }

    private static GazePlayerState LocalState()
    {
        return GetState(LocalOwnerKey());
    }

    private static GazePlayerState GetState(string owner)
    {
        var key = string.IsNullOrWhiteSpace(owner) ? "local" : owner.Trim();
        if (!States.TryGetValue(key, out var state))
        {
            state = new GazePlayerState(key);
            States[key] = state;
        }

        return state;
    }

    private static string OwnerKey(ScriptExecutor? executor)
    {
        return executor?.Self?.InstanceId ?? LocalOwnerKey();
    }

    private static string LocalOwnerKey()
    {
        var local = PlayerApi.LocalPlayerStatusId();
        return string.IsNullOrWhiteSpace(local)
            ? FightPlayer.Instance?.Status?.InstanceId ?? "local"
            : local;
    }

    private static string BuffIdForLevel(int level)
    {
        return level >= 3
            ? SunExpIds.AbyssGazeBuffIII
            : level == 2
                ? SunExpIds.AbyssGazeBuffII
                : SunExpIds.AbyssGazeBuffI;
    }

    private static bool IsLocalPlayerExecutor(ScriptExecutor? executor)
    {
        var status = executor?.Self;
        if (status == null)
        {
            return false;
        }

        return IsLocalStatus(status);
    }

    private static bool IsLocalStatus(IStatusManager status)
    {
        var local = PlayerApi.LocalPlayerStatusId();
        if (!string.IsNullOrWhiteSpace(local))
        {
            return string.Equals(status.InstanceId, local, StringComparison.Ordinal);
        }

        return string.Equals(status.fatherObject?.GetType().Name, "FightPlayer", StringComparison.Ordinal);
    }

    private static string RemoveToken(string text, string token)
    {
        return string.Join(",", (text ?? "")
            .Split(new[] { ',', '|', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(item => !string.Equals(item.Trim(), token, StringComparison.Ordinal)));
    }

    private sealed class GazePlayerState
    {
        public GazePlayerState(string owner)
        {
            Owner = owner;
        }

        public string Owner { get; }

        public bool Threshold10Triggered { get; set; }

        public bool Threshold15Triggered { get; set; }

        public bool Threshold20Triggered { get; set; }

        public bool CostPending { get; set; }

        public IDataConfig? ActiveCostConfig { get; set; }

        public int ActiveCostOriginalOnce { get; set; }

        public bool ActiveCostActionObserved { get; set; }

        public bool ActiveCostPreview { get; set; }

        public HashSet<string> SeenCardGainKeys { get; } = new(StringComparer.Ordinal);
    }
}
