using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class EndlessAbyssEvolutionTraitRegistry
{
    private static readonly object SyncRoot = new();
    private static IReadOnlyList<string> weightedPool = BuiltInWeightedPool();

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var fallback = BuiltInWeightedPool();
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.EndlessAbyssEvolutionTraitRegistryFile);
            if (!File.Exists(path))
            {
                weightedPool = fallback;
                TerriasLog.Warn("[EndlessAbyssEvolutionTraitRegistry] missing registry; using built-in trait pool.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<EndlessAbyssEvolutionTraitRegistryDocument>(File.ReadAllText(path))
                    ?? new EndlessAbyssEvolutionTraitRegistryDocument();
                weightedPool = Normalize(loaded, fallback);
                TerriasLog.Info("[EndlessAbyssEvolutionTraitRegistry] loaded trait pool count=" + weightedPool.Count + ".");
            }
            catch (Exception ex)
            {
                weightedPool = fallback;
                TerriasLog.Warn("[EndlessAbyssEvolutionTraitRegistry] failed to load registry; using built-in trait pool: " + ex.Message);
            }
        }
    }

    public static IReadOnlyList<string> EvolutionTraitBuffIds()
    {
        lock (SyncRoot)
        {
            return weightedPool.ToArray();
        }
    }

    private static IReadOnlyList<string> Normalize(
        EndlessAbyssEvolutionTraitRegistryDocument loaded,
        IReadOnlyList<string> fallback)
    {
        var pool = (loaded.Pools ?? new List<EndlessAbyssEvolutionTraitPoolConfig>())
            .FirstOrDefault(item => string.Equals(item.Id, TerriasIds.EndlessAbyssEvolutionTraitPoolId, StringComparison.Ordinal));
        if (pool?.Entries == null)
        {
            return fallback;
        }

        var result = new List<string>();
        foreach (var entry in pool.Entries)
        {
            var id = (entry.BuffId ?? "").Trim();
            if (id.Length == 0)
            {
                continue;
            }

            var weight = Math.Max(1, entry.Weight);
            for (var i = 0; i < weight; i++)
            {
                result.Add(id);
            }
        }

        return result.Count == 0 ? fallback : result;
    }

    private static IReadOnlyList<string> BuiltInWeightedPool()
    {
        return new[]
        {
            "SpecialBuff_Law:Supreme",
            "SpecialBuff_BlessedByHeaven",
            "SpecialBuff_Snitch",
            "SpecialBuff_AllogeneicConcentric",
            "SpecialBuff_Phoenix",
            "SpecialBuff_ManInTheMirror",
            "SpecialBuff_believer",
            "SpecialBuff_hunting",
            "SpecialBuff_ThievesKing",
            "SpecialBuff_Law:Judgment",
            "SpecialBuff_Musician",
            "SpecialBuff_ThirstForBlood",
            "SpecialBuff_Twins",
            "SpecialBuff_CAR_HeroBlessing",
            "SpecialBuff_Dragon'sBlood",
            "SpecialBuff_Restrain",
            "SpecialBuff_Irritable",
            "SpecialBuff_CAR_Momentum",
            "SpecialBuff_DesireWitch",
            "SpecialBuff_Joker:King",
            "SpecialBuff_Priest",
            "SpecialBuff_FortuneBoy",
            "SpecialBuff_Hysteresis",
            "SpecialBuff_TrialsOfWisdom",
            "SpecialBuff_BackToBasics",
            "SpecialBuff_Transcendent",
            TerriasIds.BossTraitMirrorArray
        };
    }
}

public sealed class EndlessAbyssEvolutionTraitRegistryDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<EndlessAbyssEvolutionTraitPoolConfig> Pools { get; set; } = new();
}

public sealed class EndlessAbyssEvolutionTraitPoolConfig
{
    public string Id { get; set; } = "";

    public List<EndlessAbyssEvolutionTraitEntry> Entries { get; set; } = new();
}

public sealed class EndlessAbyssEvolutionTraitEntry
{
    public string BuffId { get; set; } = "";

    public int Weight { get; set; } = 1;
}
