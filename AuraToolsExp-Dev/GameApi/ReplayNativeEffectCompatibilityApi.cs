using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AuraToolsExp.Dll.GameApi;

internal sealed class ReplayNativeEffectSpec
{
    internal string EffectId { get; set; } = "";
    internal GameObject Prefab { get; set; } = null!;
    internal long DurationMicroseconds { get; set; }
    internal string PositionType { get; set; } = "Center";
}

/// <summary>Resolves the native EffectBase catalog without executing EffectBase.Play.</summary>
internal static class ReplayNativeEffectCompatibilityApi
{
    private static readonly object Gate = new();
    private static IReadOnlyDictionary<string, EffectBase>? catalog;

    internal static IReadOnlyList<ReplayNativeEffectSpec> Resolve(string effectNames)
    {
        var requested = (effectNames ?? "")
            .Replace(" ", "")
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length == 0) return Array.Empty<ReplayNativeEffectSpec>();
        var byName = Catalog();
        var result = new List<ReplayNativeEffectSpec>();
        foreach (var id in requested)
        {
            if (!byName.TryGetValue(id, out var value)) continue;
            result.Add(new ReplayNativeEffectSpec
            {
                EffectId = id,
                Prefab = value.effectPrefab,
                DurationMicroseconds = Math.Max(1L, (long)Math.Round(value.duration * 1_000_000d)),
                PositionType = value.positionType.ToString()
            });
        }
        return result;
    }

    private static IReadOnlyDictionary<string, EffectBase> Catalog()
    {
        lock (Gate)
        {
            return catalog ??= ResourceLoader.LoadAll<EffectBase>("Configs/Effects")
                .Where(item => item != null && item.effectPrefab != null)
                .GroupBy(item => item.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        }
    }
}
