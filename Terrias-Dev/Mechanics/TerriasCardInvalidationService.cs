using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks;
using Terrias.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

[Flags]
public enum TerriasCardDirtyFields
{
    None = 0,
    TagIndex = 1 << 0,
    DerivedState = 1 << 1,
    Cost = 1 << 2,
    Description = 1 << 3,
    Usability = 1 << 4,
    Visual = 1 << 5,
    Structure = 1 << 6,
    Layout = 1 << 7
}

public static class TerriasCardInvalidationService
{
    private const double CooperativeSliceBudgetMilliseconds = 2.0;
    private const double SlowExecutionWarningMilliseconds = 8.0;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, PendingInvalidation> Pending = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, TerriasCardDirtyFields> Active = new(StringComparer.Ordinal);

    public static void Invalidate(CardItem? card, TerriasCardDirtyFields fields, string source)
    {
        if (card?.dataConfig == null || fields == TerriasCardDirtyFields.None)
        {
            return;
        }

        Enqueue(card.dataConfig, card, fields, source);
    }

    public static void Invalidate(IDataConfig? config, TerriasCardDirtyFields fields, string source)
    {
        if (config == null || fields == TerriasCardDirtyFields.None)
        {
            return;
        }

        Enqueue(config, null, fields, source);
    }

    public static int InvalidateHandCards(
        IEnumerable<CardItem>? handCards,
        IEnumerable<string>? cardIds,
        TerriasCardDirtyFields fields,
        string source)
    {
        if (handCards == null || cardIds == null || fields == TerriasCardDirtyFields.None)
        {
            return 0;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in cardIds)
        {
            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id.Trim());
        }

        var requested = 0;
        foreach (var card in handCards)
        {
            if (card?.dataConfig == null || !ids.Contains(CardConfigApi.Id(card.dataConfig)))
            {
                continue;
            }

            Invalidate(card, fields, source);
            requested++;
        }

        if (requested > 0)
        {
            TerriasPerformanceCounters.Record("CardInvalidation.HandSubsetRequested");
        }

        return requested;
    }

    public static void Acknowledge(IDataConfig? config, TerriasCardDirtyFields fields, string source)
    {
        if (config == null || fields == TerriasCardDirtyFields.None) return;
        var key = ConfigKey(config);
        lock (Gate)
        {
            if (!Pending.TryGetValue(key, out var pending)) return;
            pending.Remove(fields);
            if (pending.Fields == TerriasCardDirtyFields.None) Pending.Remove(key);
        }
        TerriasPerformanceCounters.Record("CardInvalidation.Acknowledged");
    }

    private static void Enqueue(
        IDataConfig config,
        CardItem? card,
        TerriasCardDirtyFields fields,
        string source)
    {
        var key = ConfigKey(config);
        if (key.Length == 0)
        {
            Execute(new PendingInvalidation(config, card, fields, source));
            return;
        }

        lock (Gate)
        {
            if (Active.TryGetValue(key, out var activeFields))
            {
                fields &= ~activeFields;
                if (fields == TerriasCardDirtyFields.None)
                {
                    TerriasPerformanceCounters.Record("CardInvalidation.ReentrantSubsetSuppressed");
                    return;
                }
            }

            if (Pending.TryGetValue(key, out var pending))
            {
                pending.Merge(card, fields, source);
            }
            else
            {
                Pending[key] = new PendingInvalidation(config, card, fields, source);
            }
        }

        ScheduleFlush();
    }

    private static void ScheduleFlush()
    {
        if (!AuraSharedFrameScheduler.RunCooperative(new AuraSharedFrameWorkRequest
            {
                OwnerId = TerriasIds.ModId,
                Key = "TerriasCardInvalidation.Flush",
                Source = "Terrias.CardInvalidation",
                DelayFrames = 1,
                Phase = AuraSharedFramePhase.Presentation,
                Priority = 100,
                EstimatedCost = 4,
                SliceBudgetMilliseconds = CooperativeSliceBudgetMilliseconds,
                ExecuteSlice = FlushSlice
            }))
        {
            TerriasPerformanceCounters.Record("CardInvalidation.FlushDeduped");
        }
    }

    private static bool FlushSlice(AuraSharedFrameSliceContext context)
    {
        var processed = false;
        while (!processed || !context.IsBudgetExhausted)
        {
            processed = true;
            string key;
            PendingInvalidation item;
            lock (Gate)
            {
                if (Pending.Count == 0)
                {
                    return true;
                }

                using var enumerator = Pending.GetEnumerator();
                enumerator.MoveNext();
                key = enumerator.Current.Key;
                item = enumerator.Current.Value;
                Pending.Remove(key);
                Active[key] = item.Fields;
            }

            try
            {
                Execute(item);
            }
            finally
            {
                lock (Gate)
                {
                    Active.Remove(key);
                }
            }
        }

        lock (Gate)
        {
            return Pending.Count == 0;
        }
    }

    private static void Execute(PendingInvalidation item)
    {
        var started = TerriasPerformanceCounters.Timestamp();
        try
        {
            var config = item.Config;
            var card = ValidCard(item.Card, config);
            var fields = item.Fields;

            if ((fields & TerriasCardDirtyFields.TagIndex) != 0)
            {
                FightCardManager.Instance?.RefreshTag(config);
                TerriasPerformanceCounters.Record("CardInvalidation.TagIndex");
            }

            if (card == null)
            {
                return;
            }

            var structureApplied = false;
            var requiresFullDataUpdate = false;
            if ((fields & TerriasCardDirtyFields.Structure) != 0)
            {
                if (config is DataConfig dataConfig)
                {
                    card = card.TransformToConfiguredType(dataConfig) ?? card;
                    TerriasActiveCardPresentationIndex.Observe(card);
                    structureApplied = true;
                    fields |= TerriasCardDirtyFields.Visual | TerriasCardDirtyFields.Layout;
                    TerriasPerformanceCounters.Record("CardInvalidation.StructureRebound");
                }
                else
                {
                    requiresFullDataUpdate = true;
                }
            }

            if (!structureApplied
                && !requiresFullDataUpdate
                && (fields & (TerriasCardDirtyFields.DerivedState | TerriasCardDirtyFields.Usability)) != 0
                && !TerriasCardDescriptionProjector.TryRecompute(card))
            {
                requiresFullDataUpdate = true;
            }

            if (!structureApplied && !requiresFullDataUpdate && (fields & TerriasCardDirtyFields.Description) != 0)
            {
                requiresFullDataUpdate = !TerriasCardDescriptionProjector.TryApplyDescription(card);
            }

            if (!structureApplied && !requiresFullDataUpdate && (fields & TerriasCardDirtyFields.Cost) != 0)
            {
                requiresFullDataUpdate = !AuraCardPresentationDelta.TrySetCost(
                    card.transform,
                    CardConfigApi.NativeDisplayCost(config, FightPlayer.Instance?.Status).ToString());
            }

            if (requiresFullDataUpdate)
            {
                RunDataUpdateGuarded(card, config);
                TerriasPerformanceCounters.Record("CardInvalidation.DataUpdateFallback");
            }

            if ((fields & TerriasCardDirtyFields.Visual) != 0)
            {
                TerriasCardPresentationRouter.RequestApply(new TerriasCardPresentationContext
                {
                    Root = card.transform,
                    Config = config,
                    Card = card,
                    Source = "CardInvalidation:" + item.Source,
                    Surface = TerriasCardPresentationSurface.CombatCard
                });
            }

            if ((fields & TerriasCardDirtyFields.Layout) != 0)
            {
                FightUiCardLayoutApi.RequestCurrentHandLayout("CardInvalidation:" + item.Source);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("Card invalidation skipped from " + item.Source + ": " + ex.Message);
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("CardInvalidation.Execute", started);
            if (started > 0L
                && TerriasPerformanceCounters.ElapsedMilliseconds(started) >= SlowExecutionWarningMilliseconds)
            {
                TerriasLog.Warn("Slow card invalidation: fields=" + item.Fields + ", source=" + item.Source);
            }
        }
    }

    private static void RunDataUpdateGuarded(CardItem card, IDataConfig expectedConfig)
    {
        if (ValidCard(card, expectedConfig) == null)
        {
            TerriasPerformanceCounters.Record("CardInvalidation.StaleViewSuppressed");
            return;
        }

        card.DataUpdate();
    }

    private static CardItem? ValidCard(CardItem? card, IDataConfig config)
    {
        if (card?.dataConfig == null || card.gameObject == null)
        {
            return null;
        }

        if (ReferenceEquals(card.dataConfig, config))
        {
            return card;
        }
        return null;
    }

    private static string ConfigKey(IDataConfig config)
    {
        var instanceId = config.InstanceID;
        return (!string.IsNullOrWhiteSpace(instanceId) ? "instance:" + instanceId + ":" : "")
               + "object:"
               + RuntimeHelpers.GetHashCode(config);
    }

    private sealed class PendingInvalidation
    {
        public PendingInvalidation(
            IDataConfig config,
            CardItem? card,
            TerriasCardDirtyFields fields,
            string source)
        {
            Config = config;
            Card = card;
            Fields = fields;
            Source = source ?? "";
        }

        public IDataConfig Config { get; }
        public CardItem? Card { get; private set; }
        public TerriasCardDirtyFields Fields { get; private set; }
        public string Source { get; private set; }

        public void Merge(CardItem? card, TerriasCardDirtyFields fields, string source)
        {
            if (card != null) Card = card;
            Fields |= fields;
            if (!string.IsNullOrWhiteSpace(source)) Source = source;
        }

        public void Remove(TerriasCardDirtyFields fields)
        {
            Fields &= ~fields;
        }
    }
}
