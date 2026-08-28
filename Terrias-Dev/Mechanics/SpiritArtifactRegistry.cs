using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public static class SpiritArtifactRegistry
{
    public const int SupportedSchemaVersion = 1;
    public const int BattleProtocolVersion = 1;
    public const int MaximumArtifactSetDamageBonusPercent = 50;

    private static readonly object SyncRoot = new();
    private static SpiritArtifactRegistryDocument document = new();
    private static Dictionary<string, SpiritArtifactSetDefinition> sets = new(StringComparer.Ordinal);
    private static Dictionary<string, SpiritArtifactPieceDefinition> pieces = new(StringComparer.Ordinal);
    private static Dictionary<string, SpiritArtifactPoolDefinition> pools = new(StringComparer.Ordinal);
    private static Dictionary<string, SpiritArtifactStatRangeProfile> ranges = new(StringComparer.Ordinal);
    private static string registryHash = "00000000";
    private static string diagnostic = "not-loaded";
    private static bool ready;

    public static bool IsReady
    {
        get { lock (SyncRoot) return ready; }
    }

    public static string RegistryHash
    {
        get { lock (SyncRoot) return registryHash; }
    }

    public static string LastLoadDiagnostic
    {
        get { lock (SyncRoot) return diagnostic; }
    }

    public static int InventoryCapacity
    {
        get { lock (SyncRoot) return document.InventoryCapacity; }
    }

    public static SpiritArtifactDrawRules DrawRules
    {
        get { lock (SyncRoot) return Clone(document.Draw); }
    }

    public static SpiritArtifactEnhancementRules EnhancementRules
    {
        get { lock (SyncRoot) return Clone(document.Enhancement); }
    }

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.SpiritArtifactRegistryFile);
            ready = false;
            if (!File.Exists(path))
            {
                diagnostic = "missing:" + path;
                TerriasLog.Warn("[SpiritArtifactRegistry] registry is missing; artifact feature disabled: " + path);
                SetDocument(new SpiritArtifactRegistryDocument());
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<SpiritArtifactRegistryDocument>(File.ReadAllText(path))
                             ?? throw new InvalidDataException("deserialized registry is null");
                NormalizeAndValidate(loaded);
                SetDocument(loaded);
                ready = true;
                diagnostic = "ready:" + path;
                TerriasLog.Info("[SpiritArtifactRegistry] ready; sets=" + document.Sets.Count
                                + ", pools=" + document.Pools.Count
                                + ", pieces=" + pieces.Count
                                + ", hash=" + registryHash + ".");
            }
            catch (Exception ex)
            {
                diagnostic = "invalid:" + ex.Message;
                SetDocument(new SpiritArtifactRegistryDocument());
                TerriasLog.Error("[SpiritArtifactRegistry] invalid registry; artifact feature disabled", ex);
            }
        }
    }

    public static IReadOnlyList<SpiritArtifactPoolDefinition> Pools()
    {
        lock (SyncRoot) return document.Pools.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<SpiritArtifactSetDefinition> Sets()
    {
        lock (SyncRoot) return document.Sets.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();
    }

    public static SpiritArtifactPoolDefinition? Pool(string? poolId)
    {
        lock (SyncRoot)
        {
            return pools.TryGetValue((poolId ?? "").Trim(), out var value) ? value : null;
        }
    }

    public static SpiritArtifactSetDefinition? Set(string? setId)
    {
        lock (SyncRoot)
        {
            return sets.TryGetValue((setId ?? "").Trim(), out var value) ? value : null;
        }
    }

    public static SpiritArtifactPieceDefinition? Piece(string? pieceId)
    {
        lock (SyncRoot)
        {
            return pieces.TryGetValue((pieceId ?? "").Trim(), out var value) ? value : null;
        }
    }

    public static SpiritArtifactPieceDefinition? PieceFor(string? setId, string? slotId)
    {
        var normalizedSlot = SpiritArtifactSlots.Normalize(slotId);
        lock (SyncRoot)
        {
            return sets.TryGetValue((setId ?? "").Trim(), out var set)
                ? set.Pieces.FirstOrDefault(value => string.Equals(value.SlotId, normalizedSlot, StringComparison.Ordinal))
                : null;
        }
    }

    public static SpiritArtifactIntegerRange Range(string? statId, int rarity, bool main)
    {
        var normalized = SpiritArtifactStats.Normalize(statId);
        var key = Math.Max(1, Math.Min(3, rarity)).ToString();
        lock (SyncRoot)
        {
            if (!ranges.TryGetValue(normalized, out var profile)) return new SpiritArtifactIntegerRange();
            var source = main ? profile.Main : profile.Sub;
            return source.TryGetValue(key, out var range) ? range.Clone() : new SpiritArtifactIntegerRange();
        }
    }

    public static IReadOnlyList<SpiritArtifactWeightedStat> SubStatWeights()
    {
        lock (SyncRoot)
        {
            return document.SubStatWeights.Select(value => new SpiritArtifactWeightedStat
            {
                StatId = value.StatId,
                Weight = value.Weight
            }).ToArray();
        }
    }

    public static int UpgradeCost(int currentLevel)
    {
        lock (SyncRoot)
        {
            var index = currentLevel - 1;
            return index >= 0 && index < document.Enhancement.UpgradeCosts.Count
                ? document.Enhancement.UpgradeCosts[index]
                : 0;
        }
    }

    public static int DismantleBaseEssence(int rarity)
    {
        lock (SyncRoot)
        {
            return document.Enhancement.DismantleBaseEssence.TryGetValue(
                Math.Max(1, Math.Min(3, rarity)).ToString(), out var value)
                ? value
                : 0;
        }
    }

    public static string Name(SpiritArtifactSetDefinition? set)
    {
        return set?.Name?.Resolve(TerriasLanguageApi.CurrentLocale, set.Id) ?? "";
    }

    public static string Name(SpiritArtifactPieceDefinition? piece)
    {
        return piece?.Name?.Resolve(TerriasLanguageApi.CurrentLocale, piece.Id) ?? "";
    }

    public static string Description(SpiritArtifactSetBonusDefinition? bonus)
    {
        return bonus?.Description?.Resolve(TerriasLanguageApi.CurrentLocale, "") ?? "";
    }

    private static void NormalizeAndValidate(SpiritArtifactRegistryDocument source)
    {
        if (source.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException("unsupported schemaVersion=" + source.SchemaVersion);
        if (!string.Equals((source.OwnerModId ?? "").Trim(), TerriasIds.ModId, StringComparison.Ordinal))
            throw new InvalidDataException("ownerModId must be " + TerriasIds.ModId);
        if (source.InventoryCapacity != 1000)
            throw new InvalidDataException("inventoryCapacity must be 1000");

        source.Draw ??= new SpiritArtifactDrawRules();
        source.Enhancement ??= new SpiritArtifactEnhancementRules();
        source.SubStatWeights ??= new List<SpiritArtifactWeightedStat>();
        source.StatRanges ??= new List<SpiritArtifactStatRangeProfile>();
        source.Pools ??= new List<SpiritArtifactPoolDefinition>();
        source.Sets ??= new List<SpiritArtifactSetDefinition>();

        if (source.Draw.Count != 10 || source.Draw.TruthCost != 160
            || source.Draw.MinimumTwoStarPerBatch != 1 || source.Draw.ThreeStarHardPity != 30
            || source.Draw.TargetSetWeightPercent != 50)
            throw new InvalidDataException("draw contract must be 10 pulls, 160 Truth, 2-star batch guarantee, 30 pity, 50% target");
        RequireKeys(source.Draw.RarityWeights, new[] { "1", "2", "3" }, "rarityWeights");
        if (source.Draw.RarityWeights.Values.Any(value => value <= 0)
            || source.Draw.RarityWeights.Values.Sum() != 10000)
            throw new InvalidDataException("rarityWeights must be positive and total 10000");

        if (source.Enhancement.MaximumLevel != 5
            || !source.Enhancement.UpgradeCosts.SequenceEqual(new[] { 10, 20, 30, 40 }))
            throw new InvalidDataException("enhancement costs must be 10/20/30/40");
        RequireKeys(source.Enhancement.DismantleBaseEssence, new[] { "1", "2", "3" }, "dismantleBaseEssence");
        if (source.Enhancement.InvestedEssenceRefundPercent != 70)
            throw new InvalidDataException("investedEssenceRefundPercent must be 70");

        EnsureUnique(source.SubStatWeights.Select(value => value.StatId), "sub stat weight");
        if (source.SubStatWeights.Sum(value => value.Weight) != 100
            || source.SubStatWeights.Any(value => value.Weight <= 0)
            || !source.SubStatWeights.Select(value => SpiritArtifactStats.Normalize(value.StatId))
                .SequenceEqual(SpiritArtifactStats.SubStats, StringComparer.Ordinal))
            throw new InvalidDataException("subStatWeights must follow the canonical 15/5 stat order and total 100");
        foreach (var weight in source.SubStatWeights) weight.StatId = SpiritArtifactStats.Normalize(weight.StatId);

        EnsureUnique(source.StatRanges.Select(value => value.StatId), "stat range");
        foreach (var profile in source.StatRanges)
        {
            profile.StatId = SpiritArtifactStats.Normalize(profile.StatId);
            if (profile.StatId.Length == 0) throw new InvalidDataException("unknown stat range id");
            profile.Main ??= new Dictionary<string, SpiritArtifactIntegerRange>();
            profile.Sub ??= new Dictionary<string, SpiritArtifactIntegerRange>();
            foreach (var range in profile.Main.Values.Concat(profile.Sub.Values))
            {
                if (range == null || range.Minimum <= 0 || range.Maximum < range.Minimum)
                    throw new InvalidDataException("invalid stat range for " + profile.StatId);
            }
            if (SpiritArtifactStats.MainChoiceStats.Contains(profile.StatId, StringComparer.Ordinal)
                || profile.StatId == SpiritArtifactStats.Life)
                RequireKeys(profile.Main, new[] { "1", "2", "3" }, "main range " + profile.StatId);
            RequireKeys(profile.Sub, new[] { "1", "2", "3" }, "sub range " + profile.StatId);
        }
        if (!source.StatRanges.Select(value => value.StatId)
            .OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(SpiritArtifactStats.SubStats.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("statRanges must define every artifact stat exactly once");

        EnsureUnique(source.Sets.Select(value => value.Id), "artifact set");
        if (source.Sets.Count != 12) throw new InvalidDataException("the first artifact release must contain 12 sets");
        var pieceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var set in source.Sets)
        {
            set.Id = (set.Id ?? "").Trim();
            if (set.Id.Length == 0 || string.IsNullOrWhiteSpace(set.Name?.ZhHans))
                throw new InvalidDataException("artifact set id/name is missing");
            set.Pieces ??= new List<SpiritArtifactPieceDefinition>();
            set.Bonuses ??= new List<SpiritArtifactSetBonusDefinition>();
            if (set.Pieces.Count != 5
                || !set.Pieces.Select(value => SpiritArtifactSlots.Normalize(value.SlotId))
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(SpiritArtifactSlots.All.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException("set " + set.Id + " must contain each slot exactly once");
            foreach (var piece in set.Pieces)
            {
                piece.Id = (piece.Id ?? "").Trim();
                piece.SlotId = SpiritArtifactSlots.Normalize(piece.SlotId);
                if (piece.Id.Length == 0 || !pieceIds.Add(piece.Id) || string.IsNullOrWhiteSpace(piece.IconPath)
                    || string.IsNullOrWhiteSpace(piece.Name?.ZhHans))
                    throw new InvalidDataException("invalid or duplicate piece in " + set.Id);
            }
            var representative = set.Pieces.FirstOrDefault(value => value.Id == set.RepresentativePieceId);
            if (representative == null || representative.SlotId != SpiritArtifactSlots.Flower)
                throw new InvalidDataException("set " + set.Id + " representative must be its flower");
            if (!set.Bonuses.Select(value => value.RequiredPieces).SequenceEqual(new[] { 2, 4 }))
                throw new InvalidDataException("set " + set.Id + " must define cumulative 2/4-piece bonuses");
            foreach (var bonus in set.Bonuses)
            {
                if (string.IsNullOrWhiteSpace(bonus.Description?.ZhHans) || bonus.Effects == null || bonus.Effects.Count == 0)
                    throw new InvalidDataException("set bonus is incomplete for " + set.Id);
                EnsureUnique(bonus.Effects.Select(value => value.Id), "effect in " + set.Id);
                foreach (var effect in bonus.Effects)
                {
                    effect.Id = (effect.Id ?? "").Trim();
                    effect.HandlerId = (effect.HandlerId ?? "").Trim();
                    if (effect.Id.Length == 0 || !SpiritArtifactEffectHandlerRegistry.Supports(effect.HandlerId))
                        throw new InvalidDataException("unknown artifact handler " + effect.HandlerId + " in " + set.Id);
                    if (effect.Amount < 0 || effect.SecondaryAmount < 0 || effect.Maximum < 0)
                        throw new InvalidDataException("negative artifact effect parameter in " + set.Id);
                }
            }
        }

        EnsureUnique(source.Pools.Select(value => value.Id), "artifact pool");
        if (source.Pools.Count != 3 || source.Pools.Any(value => value.SetIds == null || value.SetIds.Count != 4))
            throw new InvalidDataException("the first artifact release must contain three four-set pools");
        foreach (var pool in source.Pools)
        {
            pool.Id = (pool.Id ?? "").Trim();
            pool.SetIds = (pool.SetIds ?? new List<string>()).Select(value => (value ?? "").Trim()).ToList();
            if (pool.Id.Length == 0 || string.IsNullOrWhiteSpace(pool.Name?.ZhHans)
                || pool.SetIds.Any(value => value.Length == 0))
                throw new InvalidDataException("artifact pool id, name, or set identity is missing");
        }
        var assignedSets = source.Pools.SelectMany(value => value.SetIds).ToArray();
        if (assignedSets.Length != assignedSets.Distinct(StringComparer.Ordinal).Count()
            || !assignedSets.OrderBy(value => value, StringComparer.Ordinal)
                .SequenceEqual(source.Sets.Select(value => value.Id).OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("each artifact set must belong to exactly one pool");
    }

    private static void SetDocument(SpiritArtifactRegistryDocument source)
    {
        document = source ?? new SpiritArtifactRegistryDocument();
        sets = document.Sets.ToDictionary(value => value.Id, StringComparer.Ordinal);
        pieces = document.Sets.SelectMany(value => value.Pieces).ToDictionary(value => value.Id, StringComparer.Ordinal);
        pools = document.Pools.ToDictionary(value => value.Id, StringComparer.Ordinal);
        ranges = document.StatRanges.ToDictionary(value => value.StatId, StringComparer.Ordinal);
        var json = JsonConvert.SerializeObject(document, Formatting.None);
        registryHash = StableHash(json).ToString("X8");
    }

    private static void EnsureUnique(IEnumerable<string> values, string kind)
    {
        var normalized = values.Select(value => (value ?? "").Trim()).ToArray();
        if (normalized.Any(value => value.Length == 0)
            || normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new InvalidDataException("duplicate or blank " + kind);
    }

    private static void RequireKeys<T>(IDictionary<string, T>? values, IReadOnlyCollection<string> expected, string kind)
    {
        if (values == null || values.Count != expected.Count || expected.Any(key => !values.ContainsKey(key)))
            throw new InvalidDataException(kind + " must define " + string.Join(",", expected));
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in value ?? "")
            {
                hash ^= ch;
                hash *= 16777619u;
            }
            return hash;
        }
    }

    private static SpiritArtifactDrawRules Clone(SpiritArtifactDrawRules value)
    {
        return new SpiritArtifactDrawRules
        {
            Count = value.Count,
            TruthCost = value.TruthCost,
            RarityWeights = new Dictionary<string, int>(value.RarityWeights),
            MinimumTwoStarPerBatch = value.MinimumTwoStarPerBatch,
            ThreeStarHardPity = value.ThreeStarHardPity,
            TargetSetWeightPercent = value.TargetSetWeightPercent,
            GuaranteeTargetAfterOffTargetThreeStar = value.GuaranteeTargetAfterOffTargetThreeStar
        };
    }

    private static SpiritArtifactEnhancementRules Clone(SpiritArtifactEnhancementRules value)
    {
        return new SpiritArtifactEnhancementRules
        {
            MaximumLevel = value.MaximumLevel,
            UpgradeCosts = new List<int>(value.UpgradeCosts),
            DismantleBaseEssence = new Dictionary<string, int>(value.DismantleBaseEssence),
            InvestedEssenceRefundPercent = value.InvestedEssenceRefundPercent
        };
    }
}
