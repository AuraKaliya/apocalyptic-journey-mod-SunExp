using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class CardVisualSkinRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly List<RegisteredRule> Rules = new();
    private static readonly Dictionary<string, CardVisualSkinSpec> HitCache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissCache = new(StringComparer.Ordinal);
    private static long sequence;

    public static void Register(CardVisualSkinRule rule)
    {
        if (rule == null)
        {
            return;
        }

        lock (SyncRoot)
        {
            Rules.Add(new RegisteredRule(rule, sequence++));
            Rules.Sort((left, right) =>
            {
                var priority = right.Rule.Priority.CompareTo(left.Rule.Priority);
                return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
            });
            HitCache.Clear();
            MissCache.Clear();
            CardVisualInterestIndex.Invalidate();
        }
    }

    public static void ClearOwner(string ownerModId)
    {
        var owner = (ownerModId ?? "").Trim();
        if (owner.Length == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            Rules.RemoveAll(rule => string.Equals(rule.Rule.Skin.OwnerModId, owner, StringComparison.OrdinalIgnoreCase));
            HitCache.Clear();
            MissCache.Clear();
            CardVisualInterestIndex.Invalidate();
        }
    }

    public static CardVisualSkinSpec? Resolve(IDataConfig? config)
    {
        if (config == null)
        {
            return null;
        }

        var key = CacheKey(config);
        lock (SyncRoot)
        {
            if (HitCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (MissCache.Contains(key))
            {
                return null;
            }

            foreach (var rule in Rules)
            {
                if (!rule.Rule.Matches(config))
                {
                    continue;
                }

                HitCache[key] = rule.Rule.Skin;
                return rule.Rule.Skin;
            }

            MissCache.Add(key);
            return null;
        }
    }

    public static int RuleCount
    {
        get
        {
            lock (SyncRoot)
            {
                return Rules.Count;
            }
        }
    }

    private static string CacheKey(IDataConfig config)
    {
        return CardConfigApi.Id(config)
            + "\u001f"
            + DictionaryUtil.Get(config.data, "PackBelong")
            + "\u001f"
            + DictionaryUtil.Get(config.data, "Icon")
            + "\u001f"
            + DictionaryUtil.Get(config.Vars, TerriasIds.RuntimeMarkersKey);
    }

    private readonly struct RegisteredRule
    {
        public RegisteredRule(CardVisualSkinRule rule, long sequence)
        {
            Rule = rule;
            Sequence = sequence;
        }

        public CardVisualSkinRule Rule { get; }

        public long Sequence { get; }
    }
}
