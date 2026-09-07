using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Hooks.Ui;
using Terrias.Dll.Hooks.Visual;
using UnityEngine;
using Witch.Mod;

namespace Terrias.Dll.Hooks;

public static class TerriasResourcePreloader
{
    private const int OpportunityDelayFrames = 45;
    private static readonly object SyncRoot = new();
    private static readonly List<WarmupItem> Pending = new();
    private static int generation;
    private static int nextDelayFrames = 1;
    private static int essentialFailed;
    private static int essentialTotal;
    private static int essentialRemaining;
    private static long adventureWarmupStarted;
    private static bool essentialCompletionLogged;
    private static bool battleActive;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        TerriasBattleLifecycleRouter.Register("ResourcePreloader", new TerriasBattleLifecycleSubscription
        {
            AdventureStarting = _ => BeginAdventureWarmup(),
            BattleInitializing = _ => OnFightInitializing(),
            BattleEnded = _ =>
            {
                battleActive = false;
                ScheduleNext();
            }
        });
    }

    private static void BeginAdventureWarmup()
    {
        lock (SyncRoot)
        {
            generation++;
            nextDelayFrames = 1;
            battleActive = false;
            essentialTotal = 0;
            essentialRemaining = 0;
            essentialFailed = 0;
            adventureWarmupStarted = TerriasPerformanceCounters.Timestamp();
            essentialCompletionLogged = false;
            Pending.Clear();
            AddItems(CoreTexturePaths(), "visual", 300, WarmupTier.Essential, path => TerriasResourceCache.Load<Texture>(path, true, "visual") != null);
            AddPolymorphCardFaceItems();
            AddItems(CoreSpritePaths(), "ui", 250, WarmupTier.Essential, path => TerriasResourceCache.Load<Sprite>(path, true, "ui") != null);
        }

        TerriasPerformanceCounters.Record("ResourcePreloader.AdventureQueueCreated");
        TerriasPerformanceCounters.Record("ResourcePreloader.HeavyOptionalDeferred");
        TerriasLog.Info("[ResourcePreloader] adventure warmup queued: essential="
            + essentialTotal
            + ", opportunity="
            + Math.Max(0, Pending.Count - essentialTotal)
            + ".");
        TryLogEssentialCompletion();
        ScheduleNext();
    }

    private static void AddPolymorphCardFaceItems()
    {
        var roles = PolymorphRoleRegistry.AllRoles()
            .Where(role => !string.IsNullOrWhiteSpace(role.Id) && !string.IsNullOrWhiteSpace(role.CardFacePath))
            .GroupBy(role => role.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var current = PolymorphRoleRegistry.CurrentRole();
        if (current != null
            && !string.IsNullOrWhiteSpace(current.Id)
            && !string.IsNullOrWhiteSpace(current.CardFacePath)
            && roles.All(role => !string.Equals(role.Id, current.Id, StringComparison.Ordinal)))
        {
            roles.Insert(0, current);
        }

        if (roles.Count == 0)
        {
            return;
        }

        var byId = roles.ToDictionary(role => role.Id, StringComparer.Ordinal);
        var currentId = current?.Id ?? "";
        if (currentId.Length > 0 && byId.ContainsKey(currentId))
        {
            AddItems(
                new[] { currentId },
                "polymorph-role-current",
                290,
                WarmupTier.Essential,
                id => PreloadPolymorphSource(byId[id], "current"));
        }

        AddItems(
            roles.Select(role => role.Id).Where(id => !string.Equals(id, currentId, StringComparison.Ordinal)),
            "polymorph-role",
            120,
            WarmupTier.Opportunity,
            id => PreloadPolymorphSource(byId[id], "opportunity"));
    }

    private static bool PreloadPolymorphSource(PolymorphRoleSpec role, string tier)
    {
        if (TerriasResourceCache.Load<Sprite>(role.CardFacePath, true, TerriasIds.PolymorphSourceResourceCategory) == null)
        {
            throw new InvalidOperationException("polymorph role card source unavailable: " + role.Id);
        }

        TerriasPerformanceCounters.Record("ResourcePreloader.PolymorphCardSource." + tier);
        return true;
    }

    private static void AddItems(
        IEnumerable<string> paths,
        string category,
        int priority,
        WarmupTier tier,
        Func<string, bool> load)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var normalized = (path ?? "").Trim();
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            Pending.Add(new WarmupItem(normalized, category, priority, tier, load));
            if (tier == WarmupTier.Essential)
            {
                essentialTotal++;
                essentialRemaining++;
            }
        }
    }

    private static void OnFightInitializing()
    {
        battleActive = true;
        int remaining;
        lock (SyncRoot)
        {
            remaining = essentialRemaining;
        }

        if (remaining > 0)
        {
            TerriasPerformanceCounters.Record("ResourcePreloader.EssentialIncompleteAtFight");
            TerriasLog.Warn("[ResourcePreloader] battle initialization paused warmup with "
                + remaining
                + " essential item(s) still pending; first-show cache fallbacks remain enabled.");
        }
    }

    private static void ScheduleNext()
    {
        WarmupItem? next;
        int currentGeneration;
        lock (SyncRoot)
        {
            if (battleActive || Pending.Count == 0)
            {
                return;
            }

            Pending.Sort((left, right) => right.Priority.CompareTo(left.Priority));
            next = Pending[0];
            Pending.RemoveAt(0);
            currentGeneration = generation;
        }

        var key = "ResourcePreloader.Adventure." + currentGeneration + "." + next.Category + "." + next.Path;
        var delayFrames = Math.Max(
            next.Tier == WarmupTier.Opportunity ? OpportunityDelayFrames : 1,
            nextDelayFrames);
        nextDelayFrames = 1;
        TerriasFrameScheduler.RunOnceAfterFrames(
            key,
            delayFrames,
            () => ExecuteItem(currentGeneration, next),
            AuraSharedFramePhase.Background,
            next.Priority,
            estimatedCost: 1);
    }

    private static void ExecuteItem(int expectedGeneration, WarmupItem item)
    {
        var deferredForUi = false;
        lock (SyncRoot)
        {
            if (expectedGeneration != generation || battleActive)
            {
                if (expectedGeneration == generation)
                {
                    Pending.Add(item);
                }

                return;
            }

            if (item.Tier == WarmupTier.Opportunity && TerriasCombatUiWorkload.IsBusy)
            {
                Pending.Add(item);
                deferredForUi = true;
            }
        }

        if (deferredForUi)
        {
            nextDelayFrames = OpportunityDelayFrames;
            TerriasPerformanceCounters.Record("ResourcePreloader.OpportunityDeferredForUi");
            ScheduleNext();
            return;
        }

        var start = TerriasPerformanceCounters.Timestamp();
        var loaded = false;
        try
        {
            loaded = item.Load(item.Path);
            TerriasPerformanceCounters.Record(loaded ? "ResourcePreloader.ItemLoaded" : "ResourcePreloader.ItemMissing");
            if (!loaded) TerriasLog.Warn("[ResourcePreloader] resource not loaded: " + item.Path);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[ResourcePreloader] item skipped: " + item.Path + " (" + ex.Message + ")");
            TerriasPerformanceCounters.Record("ResourcePreloader.ItemFailed");
        }
        finally
        {
            var elapsed = TerriasPerformanceCounters.ElapsedMilliseconds(start);
            TerriasPerformanceCounters.RecordDuration("ResourcePreloader.Item", start);
            if (item.Tier == WarmupTier.Essential)
            {
                lock (SyncRoot)
                {
                    essentialRemaining = Math.Max(0, essentialRemaining - 1);
                    if (!loaded) essentialFailed++;
                }

                TryLogEssentialCompletion();
            }

            // Synchronous Unity resource APIs cannot be pre-empted. After an
            // expensive item, leave recovery frames before starting the next one.
            nextDelayFrames = elapsed < 8d ? 1 : Math.Min(30, Math.Max(2, (int)Math.Ceiling(elapsed / 4d)));
            if (item.Tier == WarmupTier.Opportunity)
            {
                nextDelayFrames = Math.Max(nextDelayFrames, OpportunityDelayFrames);
                TerriasPerformanceCounters.Record("ResourcePreloader.OpportunityPaced");
            }
            ScheduleNext();
        }
    }

    private static void TryLogEssentialCompletion()
    {
        int total;
        lock (SyncRoot)
        {
            if (essentialCompletionLogged || essentialRemaining > 0)
            {
                return;
            }

            essentialCompletionLogged = true;
            total = essentialTotal;
        }

        TerriasPerformanceCounters.Record("ResourcePreloader.EssentialCompleted");
        TerriasLog.Info("[ResourcePreloader] essential adventure warmup finished: items="
            + total
            + ", failed=" + essentialFailed
            + ", elapsedMs="
            + TerriasPerformanceCounters.ElapsedMilliseconds(adventureWarmupStarted).ToString("0.###")
            + ".");
    }

    private enum WarmupTier
    {
        Essential,
        Opportunity
    }

    private sealed class WarmupItem
    {
        public WarmupItem(string path, string category, int priority, WarmupTier tier, Func<string, bool> load)
        {
            Path = path;
            Category = category;
            Priority = priority;
            Tier = tier;
            Load = load;
        }

        public string Path { get; }
        public string Category { get; }
        public int Priority { get; }

        public WarmupTier Tier { get; }
        public Func<string, bool> Load { get; }
    }

    private static IEnumerable<string> CoreTexturePaths()
    {
        var eventCard = VisualRegistry.TexturePath("solar_memory.event_map_card") ?? "";
        if (!string.IsNullOrWhiteSpace(eventCard))
        {
            yield return eventCard;
        }

        foreach (var spec in VisualRegistry.MapNodeArtSpecs())
        {
            if (!string.IsNullOrWhiteSpace(spec.TexturePath))
            {
                yield return spec.TexturePath;
            }
        }

        foreach (var effectId in new[]
                 {
                     TerriasIds.StellarOvertureCardUseVisualEffectId,
                     "terrias.wuna.orbit_fire.core.back",
                     "terrias.wuna.orbit_fire.core.front",
                     "terrias.wuna.orbit_fire.back",
                     "terrias.wuna.orbit_fire.front"
                 })
        {
            var effect = VisualRegistry.Effect(effectId);
            if (effect?.Textures == null)
            {
                continue;
            }

            foreach (var path in effect.Textures.Values)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    yield return path;
                }
            }
        }
    }

    private static IEnumerable<string> CoreSpritePaths()
    {
        foreach (var path in StarScoreHudAssets.StructuralPaths())
        {
            yield return path;
        }

        foreach (var path in StarScoreFlightGlyphAssets.AllPaths())
        {
            yield return path;
        }

        foreach (var modeEntryId in new[] { "solar_memory", "endless_abyss" })
        {
            var modeEntry = VisualRegistry.ModeEntry(modeEntryId);
            var normalTitleSprite = modeEntry?.NormalTitleSprite ?? "";
            if (!string.IsNullOrWhiteSpace(normalTitleSprite))
            {
                yield return normalTitleSprite;
            }

            var highlightedTitleSprite = modeEntry?.HighlightedTitleSprite ?? "";
            if (!string.IsNullOrWhiteSpace(highlightedTitleSprite))
            {
                yield return highlightedTitleSprite;
            }
        }
    }
}
