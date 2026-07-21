using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class MorningStarOvertureService
{
    private const string PendingMeasureKey = "SunExpMorningStarPendingMeasure";
    private static readonly Dictionary<int, int> PlayedCostCounts = new();
    private static readonly Stack<PendingCard> PendingActions = new();
    private static List<string>? compositionPool;
    private static long compositionPoolEpoch = -1;

    public static void ResetForFight()
    {
        PlayedCostCounts.Clear();
        PendingActions.Clear();
        compositionPool = null;
        compositionPoolEpoch = -1;
    }

    public static void ResetForTurn()
    {
        PlayedCostCounts.Clear();
    }

    public static void OnAction(IDataConfig? config)
    {
        if (config == null)
        {
            return;
        }

        PendingActions.Push(new PendingCard(config, CardConfigApi.CurrentCost(config)));
    }

    public static void OnActionAfter(ScriptExecutor? executor)
    {
        if (PendingActions.Count == 0)
        {
            return;
        }

        var pending = PendingActions.Pop();
        if (pending.Config != null && StarScoreService.IsStellarOvertureCard(CardConfigApi.Id(pending.Config)))
        {
            TriggerStarStage(executor);
        }

        var cost = Math.Max(0, pending.Cost);
        PlayedCostCounts.TryGetValue(cost, out var count);
        PlayedCostCounts[cost] = count + 1;
    }

    public static bool HasEncore(int currentCost)
    {
        return PlayedCostCounts.TryGetValue(Math.Max(0, currentCost), out var count) && count >= 2;
    }

    public static void SchedulePrelude(ScriptExecutor? self, StarScoreNote note)
    {
        if (self?.Self == null)
        {
            return;
        }

        var existing = ExecutorApi.GetVar(self, PendingMeasureKey);
        var code = StarScoreNoteCodes.PatternCode(note);
        if (!DictionaryUtil.ContainsToken(existing, code))
        {
            ExecutorApi.SetVar(self, PendingMeasureKey, string.IsNullOrWhiteSpace(existing) ? code : existing + "," + code);
        }

        EnsureRoundHooks(self);
        PlayerApi.ShowCaption("伏谱：" + StarScoreCadenceCatalog.DisplayName(note));
    }

    public static void ResolveScheduledPreludes(ScriptExecutor? self)
    {
        var value = ExecutorApi.GetVar(self, PendingMeasureKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        ExecutorApi.SetVar(self, PendingMeasureKey, "");
        foreach (var token in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (StarScoreNoteCodes.TryFromPatternCode(token.Trim(), out var note))
            {
                CardApi.AddCardToHand(self!, CardIdForNote(note));
            }
        }
    }

    public static void ApplyStarStage(ScriptExecutor? self)
    {
        EnsureRoundHooks(self);
    }

    public static void ClearStarStage(ScriptExecutor? self)
    {
        ExecutorApi.ClearHook(self, "SunExpStarStageHook", "SunExpStarStageToken");
    }

    public static void Compose(ScriptExecutor self)
    {
        var candidates = CompositionPool();
        if (candidates.Count == 0)
        {
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.StarBlessing, "1");
            PlayerApi.ShowCaption("谱曲：未找到曲牌，星辰祝福+1。");
            return;
        }

        var id = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        var request = CardGrantRequest.ToHand(id)
            .WithSource("morning-star-compose")
            .Configure(CardMutationService.AddSpecialTagsMutation(SunExpIds.MorningStarSealTag));
        var result = CardApi.GrantCardToHand(self, request);
        if (!result.Success)
        {
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.StarBlessing, "1");
        }
    }

    public static void SelectHandCardForTranspose(ScriptExecutor self)
    {
        var cards = (self.HandCard ?? Enumerable.Empty<CardItem>())
            .Select(card => card?.dataConfig)
            .Where(card => card != null && !StarScoreService.IsStellarOvertureCard(CardConfigApi.Id(card)))
            .Cast<IDataConfig>()
            .ToList();
        var opened = CardSelectionApi.SelectOneFromCards(
            self,
            cards,
            card => card != null,
            card =>
            {
                if (card is DataConfig config)
                {
                    CardMutationService.SetTemporaryCost(config, 1);
                    CardMutationService.SetRuntimeMarkers(config, "SunExpMorningStarTransposed");
                    SunExpCardRefreshQueue.RequestConfigTagRefresh(config, "MorningStarTranspose");
                    PlayerApi.ShowCaption("星轨换位：临时费用变为1。");
                }
            },
            "选择一张手牌，使其本回合费用视为1。");
        if (!opened)
        {
            PlayerApi.ShowCaption("星轨换位：没有可选择的手牌。");
        }
    }

    private static void TriggerStarStage(ScriptExecutor? executor)
    {
        var stage = BuffApi.Level(FightPlayer.Instance?.Status, SunExpIds.StarStage);
        if (stage <= 0 || executor == null)
        {
            return;
        }

        executor.SetStatus("Self");
        executor.DrawCount(stage.ToString());
    }

    private static void EnsureRoundHooks(ScriptExecutor? self)
    {
        if (self == null || ExecutorApi.GetVar(self, "SunExpMorningStarRoundHook", "0") == "1")
        {
            return;
        }

        var token = (DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpMorningStarRoundToken", "0")) + 1).ToString();
        ExecutorApi.SetVar(self, "SunExpMorningStarRoundHook", "1");
        ExecutorApi.SetVar(self, "SunExpMorningStarRoundToken", token);
        ExecutorApi.TryAddTokenedEvent(self, "FightStart", "SunExpMorningStarRoundToken", token, new Action(ResetForFight), "morning_star");
        ExecutorApi.TryAddTokenedEvent(self, "StartRound", "SunExpMorningStarRoundToken", token, new Action(() =>
        {
            ResolveScheduledPreludes(self);
            ResetForTurn();
        }), "morning_star");
        ExecutorApi.TryAddTokenedEvent(self, "Win", "SunExpMorningStarRoundToken", token, new Action(ResetForFight), "morning_star");
        ExecutorApi.TryAddTokenedEvent(self, "Escape", "SunExpMorningStarRoundToken", token, new Action(ResetForFight), "morning_star");
    }

    private static IReadOnlyList<string> CompositionPool()
    {
        var snapshot = AuraGameDataHostApi.AcquireSnapshot();
        if (!snapshot.Version.NativeReady)
        {
            return Array.Empty<string>();
        }

        if (compositionPool != null && compositionPoolEpoch == snapshot.Version.Epoch)
        {
            return compositionPool;
        }

        var enabledPacks = EnabledCardPacks();
        compositionPool = SunExpConfigIndex.Rows(DataType.Card)
            .Where(row => IsCompositionCandidate(row, enabledPacks))
            .Select(row => CardApi.ResolveCardId(DictionaryUtil.Get(row, "Id")))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        compositionPoolEpoch = snapshot.Version.Epoch;
        SunExpLog.Info("[MorningStar] composition pool size=" + compositionPool.Count);
        return compositionPool;
    }

    private static HashSet<string> EnabledCardPacks()
    {
        try
        {
            var packs = Singleton<GameRuntimeData>.Instance.UseCardPack;
            return new HashSet<string>(packs.Where(pack => !string.IsNullOrWhiteSpace(pack)), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsCompositionCandidate(Dictionary<string, string> row, HashSet<string> enabledPacks)
    {
        var id = DictionaryUtil.Get(row, "Id");
        if (string.IsNullOrWhiteSpace(id)
            || id.StartsWith("*", StringComparison.Ordinal)
            || IsCompositionExcludedId(id)
            || StarScoreService.IsStellarOvertureCard(id)
            || StarScoreService.IsWitchStarScoreCard(id))
        {
            return false;
        }

        var pack = DictionaryUtil.Get(row, "PackBelong");
        if (string.IsNullOrWhiteSpace(pack) || (enabledPacks.Count > 0 && !enabledPacks.Contains(pack)))
        {
            return false;
        }

        return CardNameContainsMusic(row);
    }

    private static bool IsCompositionExcludedId(string id)
    {
        var normalized = CardApi.ResolveCardId(id);
        return string.Equals(id, "wuna_grave_song", StringComparison.Ordinal)
            || string.Equals(normalized, "SunExp_wuna_grave_song", StringComparison.Ordinal)
            || string.Equals(normalized, "SunExp_wuna_wuna_grave_song", StringComparison.Ordinal)
            || string.Equals(normalized, SunExpIds.WitchStarScoreCardId, StringComparison.Ordinal);
    }

    private static bool CardNameContainsMusic(Dictionary<string, string> row)
    {
        var localized = LocalizedCardName(row);
        if (localized.Contains("圣庭墓曲") || localized.Contains("聖庭墓曲") || localized.Contains("曲"))
        {
            return !localized.Contains("圣庭墓曲") && !localized.Contains("聖庭墓曲");
        }

        foreach (var key in new[] { "Name", "Name_zh-Hant", "Name_zh", "CNName" })
        {
            if (DictionaryUtil.Get(row, key).Contains("曲"))
            {
                return true;
            }
        }

        return false;
    }

    private static string LocalizedCardName(Dictionary<string, string> row)
    {
        try
        {
            var localized = row.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }
        }
        catch
        {
            // Fall through to DataConfig-backed lookup.
        }

        try
        {
            var id = CardApi.ResolveCardId(DictionaryUtil.Get(row, "Id"));
            var data = SunExpConfigIndex.Row(DataType.Card, id);
            var localized = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localized) && localized != "Name")
            {
                return localized;
            }
        }
        catch
        {
            // Fall through to raw fields.
        }

        return DictionaryUtil.Get(row, "Name");
    }

    public static string CardIdForNote(StarScoreNote note)
    {
        return note switch
        {
            StarScoreNote.Opening => SunExpIds.StellarOvertureStartCardId,
            StarScoreNote.Sustain => SunExpIds.StellarOvertureSustainCardId,
            StarScoreNote.Turn => SunExpIds.StellarOvertureTurnCardId,
            StarScoreNote.Close => SunExpIds.StellarOvertureCloseCardId,
            _ => SunExpIds.StellarOvertureStartCardId
        };
    }

    private readonly struct PendingCard
    {
        public PendingCard(IDataConfig? config, int cost)
        {
            Config = config;
            Cost = cost;
        }

        public IDataConfig? Config { get; }

        public int Cost { get; }
    }
}
