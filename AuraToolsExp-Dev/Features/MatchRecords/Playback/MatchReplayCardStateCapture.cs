using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Maintains a presentation-only hand keyed by recorded card identity. Ordinary action
/// projection reuses native CardItem objects; only explicit seek/reset rebuilds the hand.
/// </summary>
internal static class MatchReplayCardStateCapture
{
    private const string Draw = "Draw";
    private const string Discard = "Discard";
    private const string Nascent = "Nascent";
    private const string Hand = "Hand";
    private const float NativeHandScale = 0.6f;
    private const float HandSafeInset = 210f;
    private const float HandMotionDurationMilliseconds = 280f;
    private static readonly List<HandMotion> Motions = new();
    private static float motionClock;

    internal static List<MatchReplayCardState> Capture(out int cardTopCount)
    {
        cardTopCount = 0;
        var result = new List<MatchReplayCardState>();
        try
        {
            var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
            cardTopCount = fightUi?.CardTopCount ?? 0;
            Add(result, Draw, FightCardManager.Instance?.cardList);
            Add(result, Discard, FightCardManager.Instance?.usedCardList);
            Add(result, Nascent, FightCardManager.Instance?.nascentList);

            var hand = (FightUI.cardItemList ?? new List<CardItem>())
                .Where(item => item != null && item.dataConfig != null)
                .Select(item => item.dataConfig)
                .Concat(fightUi?.createCardQueue ?? Enumerable.Empty<DataConfig>());
            Add(result, Hand, hand);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] card checkpoint capture degraded: " + ex.Message);
            result.Clear();
        }

        return result;
    }

    internal static MatchReplayCardState? CaptureOne(IDataConfig? config, string zone = "Source")
    {
        if (config == null)
        {
            return null;
        }

        var dataConfig = config as DataConfig;
        return new MatchReplayCardState
        {
            Zone = zone,
            ReplayCardId = dataConfig?.InstanceID ?? Value(config.Vars, "InstanceID"),
            CardId = Value(config.data, "Id"),
            DataType = dataConfig == null ? 0 : (int)dataConfig.Type,
            Data = CaptureValues(config.data),
            Vars = CaptureValues(config.Vars)
        };
    }

    internal static int Restore(
        IReadOnlyList<MatchReplayCardState>? source,
        int cardTopCount,
        bool rebuild = false)
    {
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        var manager = FightCardManager.Instance;
        if (fightUi == null || manager == null || source == null)
        {
            return 0;
        }

        var cards = new List<(MatchReplayCardState State, DataConfig Config)>();
        foreach (var state in source.OrderBy(item => ZoneRank(item.Zone)).ThenBy(item => item.Order))
        {
            try
            {
                cards.Add((state, Rehydrate(state)));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "无法恢复卡牌 " + (state.CardId ?? "unknown") + "：" + ex.Message,
                    ex);
            }
        }

        if (rebuild)
        {
            ClearHandObjects();
        }

        FightUI.cardItemList ??= new List<CardItem>();
        var existing = FightUI.cardItemList
            .Where(item => item != null && item.dataConfig != null)
            .GroupBy(item => item.dataConfig.InstanceID ?? "", StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var targetHandIds = new HashSet<string>(
            cards.Where(item => item.State.Zone == Hand).Select(item => item.State.ReplayCardId),
            StringComparer.Ordinal);
        foreach (var cardItem in FightUI.cardItemList.ToList())
        {
            var id = cardItem?.dataConfig?.InstanceID ?? "";
            if (cardItem != null && !targetHandIds.Contains(id))
            {
                FightUI.cardItemList.Remove(cardItem);
                UnityEngine.Object.Destroy(cardItem.gameObject);
            }
        }

        FightUI.SelectedCard?.Clear();
        fightUi.createCardQueue.Clear();
        manager.cardList.Clear();
        manager.usedCardList.Clear();
        manager.nascentList.Clear();
        manager.FightcardList.Clear();
        manager.CardTags.Clear();
        fightUi.CardTopCount = Math.Max(0, cardTopCount);

        var orderedHand = new List<CardItem>();
        foreach (var item in cards)
        {
            var config = item.Config;
            manager.FightcardList.Add(config);
            manager.CardTagCheck(config);
            switch (item.State.Zone)
            {
                case Draw:
                    manager.cardList.Add(config);
                    break;
                case Discard:
                    manager.usedCardList.Add(config);
                    break;
                case Nascent:
                    manager.nascentList.Add(config);
                    break;
                case Hand:
                    if (!existing.TryGetValue(item.State.ReplayCardId ?? "", out var cardItem)
                        || cardItem == null)
                    {
                        cardItem = CreateCardPresentation(fightUi.cardContainer.transform, config);
                        var rect = cardItem.GetComponent<RectTransform>();
                        if (rect != null)
                        {
                            rect.anchoredPosition += new Vector2(420f, -120f);
                            rect.localScale = Vector3.one * 0.2f;
                        }
                    }
                    else
                    {
                        cardItem.transform.SetParent(fightUi.cardContainer.transform, worldPositionStays: false);
                        cardItem.selectContainer = fightUi.selectCardContainer;
                        cardItem.cardcontainer = fightUi.cardContainer;
                        BindPresentation(cardItem, config);
                        DisableInput(cardItem);
                    }

                    orderedHand.Add(cardItem);
                    break;
            }
        }

        FightUI.cardItemList.Clear();
        FightUI.cardItemList.AddRange(orderedHand);
        if (fightUi.cardContainer != null)
        {
            fightUi.cardContainer.AFKAnimation = false;
        }

        LayoutHand(rebuild);
        return orderedHand.Count;
    }

    internal static CardItem? TakeSourceForPresentation(MatchReplayActionFrame frame)
    {
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi?.cardContainer == null || frame.SourcePresentation == null)
        {
            return null;
        }

        var sourceId = frame.SourceInstanceId ?? "";
        var leavesHand = (frame.CardTransitions ?? new List<MatchReplayCardTransition>()).Any(item =>
            string.Equals(item.ReplayCardId, sourceId, StringComparison.Ordinal)
            && string.Equals(item.FromZone, Hand, StringComparison.Ordinal)
            && !string.Equals(item.ToZone, Hand, StringComparison.Ordinal));
        var source = FightUI.cardItemList.FirstOrDefault(item =>
            item != null && string.Equals(item.dataConfig?.InstanceID, sourceId, StringComparison.Ordinal));
        if (source != null && leavesHand)
        {
            FightUI.cardItemList.Remove(source);
            source.ignore = true;
            DisableInput(source);
            return source;
        }

        try
        {
            var copy = CreateCardPresentation(fightUi.cardContainer.transform, Rehydrate(frame.SourcePresentation));
            if (source != null)
            {
                copy.transform.position = source.transform.position;
                copy.transform.rotation = source.transform.rotation;
                copy.transform.localScale = source.transform.lossyScale;
            }

            copy.ignore = true;
            return copy;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] replay card visual creation skipped: " + ex.Message);
            return null;
        }
    }

    internal static void Tick(float deltaMilliseconds)
    {
        if (Motions.Count == 0)
        {
            return;
        }

        motionClock += Math.Max(0f, deltaMilliseconds);
        var progress = Mathf.Clamp01(motionClock / HandMotionDurationMilliseconds);
        var eased = 1f - Mathf.Pow(1f - progress, 3f);
        foreach (var motion in Motions)
        {
            if (motion.Rect == null)
            {
                continue;
            }

            motion.Rect.anchoredPosition = Vector2.LerpUnclamped(motion.StartPosition, motion.TargetPosition, eased);
            motion.Rect.localEulerAngles = Vector3.LerpUnclamped(motion.StartRotation, motion.TargetRotation, eased);
            motion.Rect.localScale = Vector3.LerpUnclamped(motion.StartScale, motion.TargetScale, eased);
        }

        if (progress >= 1f)
        {
            Motions.Clear();
            motionClock = 0f;
        }
    }

    internal static void Reset()
    {
        Motions.Clear();
        motionClock = 0f;
        ClearHandObjects();
    }

    private static CardItem CreateCardPresentation(Transform parent, DataConfig config)
    {
        var prefab = ResourceLoader.Load<GameObject>("UI/CardItem")
                     ?? throw new InvalidOperationException("原生卡牌预制体不可用。");
        var gameObject = UnityEngine.Object.Instantiate(prefab, parent);
        // The configured runtime subclass is combat logic. A base CardItem is sufficient for
        // the native card face and avoids custom Awake/Init side effects during replay.
        var cardItem = gameObject.AddComponent<CardItem>();
        cardItem.ClearEvent();
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        cardItem.selectContainer = fightUi?.selectCardContainer;
        cardItem.cardcontainer = fightUi?.cardContainer;
        BindPresentation(cardItem, config);
        DisableInput(cardItem);
        return cardItem;
    }

    private static void BindPresentation(CardItem cardItem, DataConfig config)
    {
        cardItem.status = FightPlayer.Instance?.Status as StatusManager;
        cardItem.dataConfig = config;
        cardItem.data = config.data;
        cardItem.Vars = config.Vars;
        var state = CaptureOne(config) ?? throw new InvalidOperationException("卡牌展示数据为空。");
        var pureConfig = Rehydrate(state, CalculateRecordedCost(config, cardItem.status));
        ICard.SetCardStyle(cardItem.transform, pureConfig);
        ICard.SetPureMsg(cardItem.transform, pureConfig);
    }

    private static int CalculateRecordedCost(DataConfig config, StatusManager? status)
    {
        var baseCost = ParseInt(Value(config.data, "Expend"));
        var multiplier = 1f;
        if (status?.dynamicVariables != null
            && status.dynamicVariables.TryGetValue("CardCost", out var recordedMultiplier))
        {
            multiplier = recordedMultiplier;
        }

        var extra = ParseInt(Value(config.Vars, "TotalExCost"))
                    + ParseInt(Value(config.Vars, "ExCost"))
                    + ParseInt(Value(config.Vars, "OnceExCost"));
        return Math.Max(0, (int)Math.Abs(baseCost * multiplier) + extra);
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static void DisableInput(CardItem cardItem)
    {
        cardItem.enabled = false;
        var trigger = cardItem.transform.Find("Trigger");
        if (trigger != null)
        {
            trigger.gameObject.SetActive(false);
        }
    }

    private static void LayoutHand(bool immediate)
    {
        Motions.Clear();
        motionClock = 0f;
        var hand = FightUI.cardItemList.Where(item => item != null).ToList();
        if (hand.Count == 0)
        {
            return;
        }

        var spacing = Math.Min(180f, 1120f / Math.Max(1, hand.Count));
        var start = -(hand.Count - 1) * spacing * 0.5f;
        var middle = (hand.Count - 1) * 0.5f;
        for (var index = 0; index < hand.Count; index++)
        {
            var cardItem = hand[index];
            var rect = cardItem.GetComponent<RectTransform>();
            if (rect == null)
            {
                continue;
            }

            cardItem.SetIndex(index);
            var normalized = middle <= 0f ? 0f : (index - middle) / middle;
            var targetPosition = new Vector2(
                start + index * spacing,
                HandSafeInset + (hand.Count >= 6 ? (1f - normalized * normalized) * 34f : 0f));
            var targetRotation = new Vector3(0f, cardItem.isReverse ? 180f : 0f, hand.Count >= 6 ? -normalized * 7f : 0f);
            var targetScale = Vector3.one * NativeHandScale;
            cardItem.initPosition = targetPosition;
            cardItem.initAngle = targetRotation;
            if (immediate)
            {
                rect.anchoredPosition = targetPosition;
                rect.localEulerAngles = targetRotation;
                rect.localScale = targetScale;
            }
            else
            {
                Motions.Add(new HandMotion
                {
                    Rect = rect,
                    StartPosition = rect.anchoredPosition,
                    TargetPosition = targetPosition,
                    StartRotation = rect.localEulerAngles,
                    TargetRotation = targetRotation,
                    StartScale = rect.localScale,
                    TargetScale = targetScale
                });
            }
        }
    }

    private static void ClearHandObjects()
    {
        var objects = new HashSet<GameObject>();
        foreach (var cardItem in (FightUI.cardItemList ?? new List<CardItem>())
                     .Concat(FightUI.WaitCard ?? new List<CardItem>()))
        {
            if (cardItem != null && cardItem.gameObject != null)
            {
                objects.Add(cardItem.gameObject);
            }
        }

        foreach (var gameObject in objects)
        {
            UnityEngine.Object.Destroy(gameObject);
        }

        FightUI.cardItemList?.Clear();
        FightUI.WaitCard?.Clear();
        FightUI.SelectedCard?.Clear();
    }

    private static void Add(
        ICollection<MatchReplayCardState> target,
        string zone,
        IEnumerable<DataConfig>? cards)
    {
        if (cards == null)
        {
            return;
        }

        var order = 0;
        foreach (var card in cards)
        {
            if (card == null)
            {
                continue;
            }

            target.Add(new MatchReplayCardState
            {
                Zone = zone,
                Order = order++,
                ReplayCardId = card.InstanceID ?? "",
                CardId = Value(card.data, "Id"),
                DataType = (int)card.Type,
                Data = CaptureValues(card.data),
                Vars = CaptureValues(card.Vars)
            });
        }
    }

    internal static DataConfig Rehydrate(MatchReplayCardState state, int? displayedCost = null)
    {
        var payload = MatchReplayCardPresentationData.Compose(state, displayedCost);
        return new DataConfig(
            payload.Data,
            payload.Vars,
            ifPreCompile: true,
            type: (DataType)payload.DataType);
    }

    private static List<MatchReplayStringValue> CaptureValues(IDictionary<string, string>? values)
    {
        return (values ?? new Dictionary<string, string>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new MatchReplayStringValue
            {
                Key = item.Key ?? "",
                Value = item.Value ?? ""
            })
            .ToList();
    }

    private static string Value(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static int ZoneRank(string zone)
    {
        switch (zone)
        {
            case Draw: return 0;
            case Discard: return 1;
            case Nascent: return 2;
            case Hand: return 3;
            default: return 4;
        }
    }

    private sealed class HandMotion
    {
        internal RectTransform? Rect { get; set; }
        internal Vector2 StartPosition { get; set; }
        internal Vector2 TargetPosition { get; set; }
        internal Vector3 StartRotation { get; set; }
        internal Vector3 TargetRotation { get; set; }
        internal Vector3 StartScale { get; set; }
        internal Vector3 TargetScale { get; set; }
    }
}
