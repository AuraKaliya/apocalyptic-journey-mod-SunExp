using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class EndlessAbyssRewardPoolService
{
    private const string CardKind = "card";
    private const string CardPackSource = "cardPack";
    private const string CardSource = "card";

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, EndlessAbyssRewardPoolConfig> Pools = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, IReadOnlyList<string>> CardPoolCache = new(StringComparer.Ordinal);

    public static void Initialize(IEnumerable<EndlessAbyssRewardPoolConfig>? configs)
    {
        lock (SyncRoot)
        {
            Pools.Clear();
            CardPoolCache.Clear();
            foreach (var config in configs ?? Array.Empty<EndlessAbyssRewardPoolConfig>())
            {
                if (config == null || string.IsNullOrWhiteSpace(config.Id))
                {
                    continue;
                }

                Pools[config.Id.Trim()] = config;
            }
        }

        SunExpLog.Info("[EndlessAbyssRewardPool] initialized pools=" + PoolCount());
    }

    public static IReadOnlyList<string> CardIds(string poolId)
    {
        if (string.IsNullOrWhiteSpace(poolId))
        {
            return Array.Empty<string>();
        }

        poolId = poolId.Trim();
        lock (SyncRoot)
        {
            if (CardPoolCache.TryGetValue(poolId, out var cached))
            {
                return cached;
            }
        }

        var built = BuildCardPool(poolId);
        lock (SyncRoot)
        {
            CardPoolCache[poolId] = built;
        }

        SunExpLog.Info("[EndlessAbyssRewardPool] card pool " + poolId + " size=" + built.Count);
        return built;
    }

    public static void ClearCache(string source)
    {
        lock (SyncRoot)
        {
            CardPoolCache.Clear();
        }

        SunExpLog.Debug("[EndlessAbyssRewardPool] cache cleared from " + source + ".");
    }

    private static IReadOnlyList<string> BuildCardPool(string poolId)
    {
        EndlessAbyssRewardPoolConfig? config;
        lock (SyncRoot)
        {
            Pools.TryGetValue(poolId, out config);
        }

        if (config == null || !string.Equals(config.Kind, CardKind, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var excluded = new HashSet<string>(
            (config.ExcludeCardIds ?? Array.Empty<string>())
            .Select(CardApi.ResolveCardId)
            .Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        var enabledPacks = config.RespectEnabledCardPacks
            ? EnabledCardPacks()
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in config.Sources ?? Array.Empty<EndlessAbyssRewardPoolSourceConfig>())
        {
            AddSourceCards(source, config.RespectEnabledCardPacks, enabledPacks, excluded, seen, result);
        }

        foreach (var cardId in config.IncludeCardIds ?? Array.Empty<string>())
        {
            AddCardId(cardId, excluded, seen, result);
        }

        return result
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddSourceCards(
        EndlessAbyssRewardPoolSourceConfig? source,
        bool respectEnabledPacks,
        HashSet<string> enabledPacks,
        HashSet<string> excluded,
        HashSet<string> seen,
        List<string> result)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.Type) || string.IsNullOrWhiteSpace(source.Id))
        {
            return;
        }

        if (string.Equals(source.Type, CardSource, StringComparison.OrdinalIgnoreCase))
        {
            AddCardId(source.Id, excluded, seen, result);
            return;
        }

        if (!string.Equals(source.Type, CardPackSource, StringComparison.OrdinalIgnoreCase))
        {
            SunExpLog.Warn("[EndlessAbyssRewardPool] unsupported source type=" + source.Type + ", id=" + source.Id);
            return;
        }

        foreach (var row in SunExpConfigIndex.Rows(DataType.Card))
        {
            var pack = DictionaryUtil.Get(row, "PackBelong");
            if (!CardPackMatches(pack, source.Id))
            {
                continue;
            }

            if (respectEnabledPacks && enabledPacks.Count > 0 && !enabledPacks.Contains(pack))
            {
                continue;
            }

            AddCardRow(row, excluded, seen, result);
        }
    }

    private static void AddCardRow(
        Dictionary<string, string> row,
        HashSet<string> excluded,
        HashSet<string> seen,
        List<string> result)
    {
        var id = DictionaryUtil.Get(row, "Id");
        if (string.IsNullOrWhiteSpace(id) || id.StartsWith("*", StringComparison.Ordinal))
        {
            return;
        }

        AddCardId(id, excluded, seen, result);
    }

    private static void AddCardId(
        string cardId,
        HashSet<string> excluded,
        HashSet<string> seen,
        List<string> result)
    {
        var resolved = CardApi.ResolveCardId(cardId);
        if (string.IsNullOrWhiteSpace(resolved)
            || excluded.Contains(resolved)
            || !seen.Add(resolved)
            || SunExpConfigIndex.Row(DataType.Card, resolved) == null)
        {
            return;
        }

        result.Add(resolved);
    }

    private static bool CardPackMatches(string rowPackId, string sourcePackId)
    {
        if (string.IsNullOrWhiteSpace(rowPackId) || string.IsNullOrWhiteSpace(sourcePackId))
        {
            return false;
        }

        var source = sourcePackId.Trim();
        return string.Equals(rowPackId, source, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rowPackId, "SunExp_sunexp_" + source, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> EnabledCardPacks()
    {
        try
        {
            var packs = Singleton<GameRuntimeData>.Instance.UseCardPack;
            return new HashSet<string>(
                packs.Where(pack => !string.IsNullOrWhiteSpace(pack)),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[EndlessAbyssRewardPool] failed to read enabled card packs: " + ex.Message);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static int PoolCount()
    {
        lock (SyncRoot)
        {
            return Pools.Count;
        }
    }
}
