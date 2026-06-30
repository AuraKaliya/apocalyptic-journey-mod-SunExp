using System;
using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

public static class CardFrameEffectRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly List<RegisteredEffect> Effects = new();
    private static readonly Dictionary<string, CardFrameEffectSpec> HitCache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> MissCache = new(StringComparer.Ordinal);
    private static long sequence;

    public static void Register(CardFrameEffectSpec spec)
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

    public static CardFrameEffectSpec? Resolve(CardVisualSkinSpec? skin)
    {
        if (skin == null || string.IsNullOrWhiteSpace(skin.Id))
        {
            return null;
        }

        var skinId = skin.Id.Trim();
        lock (SyncRoot)
        {
            if (HitCache.TryGetValue(skinId, out var cached))
            {
                return cached;
            }

            if (MissCache.Contains(skinId))
            {
                return null;
            }

            foreach (var effect in Effects)
            {
                if (!string.Equals(effect.Spec.SkinId, skinId, StringComparison.Ordinal))
                {
                    continue;
                }

                HitCache[skinId] = effect.Spec;
                return effect.Spec;
            }

            MissCache.Add(skinId);
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

    private static bool IsValid(CardFrameEffectSpec spec)
    {
        return !string.IsNullOrWhiteSpace(spec.OwnerModId)
            && !string.IsNullOrWhiteSpace(spec.Id)
            && !string.IsNullOrWhiteSpace(spec.SkinId)
            && !string.IsNullOrWhiteSpace(spec.VisualEffectId);
    }

    private readonly struct RegisteredEffect
    {
        public RegisteredEffect(CardFrameEffectSpec spec, long sequence)
        {
            Spec = spec;
            Sequence = sequence;
        }

        public CardFrameEffectSpec Spec { get; }

        public long Sequence { get; }
    }
}
