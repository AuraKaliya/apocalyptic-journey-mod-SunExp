using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class CardVisualEffectRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly List<RegisteredEffect> Effects = new();
    private static readonly Dictionary<string, CardVisualEffectSpec> HitCache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissCache = new(StringComparer.Ordinal);
    private static long sequence;

    public static void Register(CardVisualEffectSpec spec)
    {
        if (spec == null || !IsValid(spec))
        {
            return;
        }

        lock (SyncRoot)
        {
            Effects.Add(new RegisteredEffect(spec, sequence++));
            Effects.Sort((left, right) =>
            {
                var priority = right.Spec.Priority.CompareTo(left.Spec.Priority);
                return priority != 0 ? priority : left.Sequence.CompareTo(right.Sequence);
            });
            HitCache.Clear();
            MissCache.Clear();
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
            Effects.RemoveAll(effect => string.Equals(effect.Spec.OwnerModId, owner, StringComparison.OrdinalIgnoreCase));
            HitCache.Clear();
            MissCache.Clear();
        }
    }

    public static CardVisualEffectSpec? Resolve(CardVisualEffectTarget target, IDataConfig? config)
    {
        if (config == null)
        {
            return null;
        }

        var cardId = CardConfigApi.Id(config);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        var cacheKey = target + "\u001f" + cardId;
        lock (SyncRoot)
        {
            if (HitCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            if (MissCache.Contains(cacheKey))
            {
                return null;
            }

            foreach (var effect in Effects)
            {
                if (!Matches(effect.Spec, target, cardId))
                {
                    continue;
                }

                HitCache[cacheKey] = effect.Spec;
                return effect.Spec;
            }

            MissCache.Add(cacheKey);
            return null;
        }
    }

    public static int EffectCount
    {
        get
        {
            lock (SyncRoot)
            {
                return Effects.Count;
            }
        }
    }

    private static bool IsValid(CardVisualEffectSpec spec)
    {
        return !string.IsNullOrWhiteSpace(spec.OwnerModId)
            && !string.IsNullOrWhiteSpace(spec.Id)
            && !string.IsNullOrWhiteSpace(spec.VisualEffectId)
            && spec.CardIds.Count > 0;
    }

    private static bool Matches(CardVisualEffectSpec spec, CardVisualEffectTarget target, string cardId)
    {
        if (spec.Target != target)
        {
            return false;
        }

        foreach (var pattern in spec.CardIds)
        {
            if (string.Equals(pattern, cardId, StringComparison.Ordinal)
                || WildcardMatches(pattern, cardId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WildcardMatches(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.IndexOf('*') < 0)
        {
            return false;
        }

        var parts = pattern.Split(new[] { '*' }, StringSplitOptions.None);
        var index = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0)
            {
                continue;
            }

            var next = value.IndexOf(part, index, StringComparison.Ordinal);
            if (next < 0 || i == 0 && next != 0 && !pattern.StartsWith("*", StringComparison.Ordinal))
            {
                return false;
            }

            index = next + part.Length;
        }

        var lastPart = parts.Length == 0 ? "" : parts[parts.Length - 1];
        return pattern.EndsWith("*", StringComparison.Ordinal)
            || lastPart.Length == 0
            || value.EndsWith(lastPart, StringComparison.Ordinal);
    }

    private readonly struct RegisteredEffect
    {
        public RegisteredEffect(CardVisualEffectSpec spec, long sequence)
        {
            Spec = spec;
            Sequence = sequence;
        }

        public CardVisualEffectSpec Spec { get; }

        public long Sequence { get; }
    }
}
