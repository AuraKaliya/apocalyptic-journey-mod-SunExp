using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Hooks.Visual;
using UnityEngine;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpResourcePreloader
{
    private static readonly object SyncRoot = new();
    private static readonly List<WarmupItem> Pending = new();
    private static int generation;
    private static int nextDelayFrames = 1;
    private static bool battleActive;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        SunExpBattleLifecycleRouter.Register("ResourcePreloader", new SunExpBattleLifecycleSubscription
        {
            AdventureStarting = _ => BeginAdventureWarmup(),
            FightInitializing = _ => battleActive = true,
            FightEnded = _ =>
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
            Pending.Clear();
            AddItems(CoreTexturePaths(), "visual", 300, path => SunExpResourceCache.Load<Texture2D>(path, true, "visual"));
            AddItems(CoreSpritePaths(), "ui", 250, path => SunExpResourceCache.Load<Sprite>(path, true, "ui"));
            AddItems(
                PolymorphRoleRegistry.CardFacePaths(12),
                SunExpIds.PolymorphSourceResourceCategory,
                50,
                path => SunExpResourceCache.Load<Sprite>(path, true, SunExpIds.PolymorphSourceResourceCategory));
            foreach (var role in PolymorphRoleRegistry.AllRoles().Take(12))
            {
                var captured = role;
                Pending.Add(new WarmupItem(
                    captured.Id,
                    "polymorph-card-face",
                    25,
                    _ => PolymorphCardFaceCache.GetOrCreate(captured)));
            }
        }

        SunExpPerformanceCounters.Record("ResourcePreloader.AdventureQueueCreated");
        ScheduleNext();
    }

    private static void AddItems(IEnumerable<string> paths, string category, int priority, Action<string> load)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var normalized = (path ?? "").Trim();
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            Pending.Add(new WarmupItem(normalized, category, priority, load));
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
        var delayFrames = Math.Max(1, nextDelayFrames);
        nextDelayFrames = 1;
        SunExpFrameScheduler.RunOnceAfterFrames(
            key,
            delayFrames,
            () => ExecuteItem(currentGeneration, next),
            AuraSharedFramePhase.Background,
            next.Priority,
            estimatedCost: 1);
    }

    private static void ExecuteItem(int expectedGeneration, WarmupItem item)
    {
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
        }

        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            item.Load(item.Path);
            SunExpPerformanceCounters.Record("ResourcePreloader.ItemLoaded");
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[ResourcePreloader] item skipped: " + item.Path + " (" + ex.Message + ")");
            SunExpPerformanceCounters.Record("ResourcePreloader.ItemFailed");
        }
        finally
        {
            var elapsed = SunExpPerformanceCounters.ElapsedMilliseconds(start);
            SunExpPerformanceCounters.RecordDuration("ResourcePreloader.Item", start);
            // Synchronous Unity resource APIs cannot be pre-empted. After an
            // expensive item, leave recovery frames before starting the next one.
            nextDelayFrames = elapsed < 8d ? 1 : Math.Min(30, Math.Max(2, (int)Math.Ceiling(elapsed / 4d)));
            ScheduleNext();
        }
    }

    private sealed class WarmupItem
    {
        public WarmupItem(string path, string category, int priority, Action<string> load)
        {
            Path = path;
            Category = category;
            Priority = priority;
            Load = load;
        }

        public string Path { get; }
        public string Category { get; }
        public int Priority { get; }
        public Action<string> Load { get; }
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
                     SunExpIds.CardFaceFoilHoloVisualEffectId,
                     SunExpIds.CardFaceStardustVisualEffectId,
                     "sunexp.wuna.orbit_fire.core.back",
                     "sunexp.wuna.orbit_fire.core.front",
                     "sunexp.wuna.orbit_fire.back",
                     "sunexp.wuna.orbit_fire.front"
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
        foreach (var path in StarScoreHudAssets.AllPaths())
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
