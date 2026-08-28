using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public sealed class SpiritArtifactLoadoutView
{
    public SpiritArtifactBattleSnapshot Battle { get; set; } = new();
    public Dictionary<string, int> SetCounts { get; set; } = new(StringComparer.Ordinal);
}

public static class SpiritArtifactLoadoutResolver
{
    public static SpiritArtifactLoadoutView Resolve(SpiritCollectionDocument? collection, SpiritInstance? spirit)
    {
        collection ??= new SpiritCollectionDocument();
        spirit ??= new SpiritInstance();
        var byUid = (collection.ArtifactInventory?.Artifacts ?? new List<SpiritArtifactInstance>())
            .Where(value => value != null && !string.IsNullOrWhiteSpace(value.ArtifactUid))
            .ToDictionary(value => value.ArtifactUid, StringComparer.Ordinal);
        var items = new List<SpiritArtifactBattleItemSnapshot>();
        foreach (var slot in SpiritArtifactSlots.All)
        {
            var uid = spirit.ArtifactLoadout?.Get(slot) ?? "";
            if (!byUid.TryGetValue(uid, out var artifact) || artifact.SlotId != slot) continue;
            items.Add(ToBattleItem(artifact));
        }
        return Build(items, spirit.ArtifactLoadout?.Revision ?? 0);
    }

    public static SpiritArtifactLoadoutView Build(
        IReadOnlyList<SpiritArtifactBattleItemSnapshot>? sourceItems,
        int loadoutRevision)
    {
        var items = (sourceItems ?? Array.Empty<SpiritArtifactBattleItemSnapshot>())
            .Where(value => value != null)
            .OrderBy(value => SpiritArtifactSlots.All.IndexOf(value.SlotId))
            .Select(value => value.Clone())
            .ToList();
        var bonuses = new SpiritArtifactBonusBuilder();
        foreach (var item in items)
        {
            ApplyRoll(item.MainStat, bonuses);
            foreach (var roll in item.SubStatRolls ?? new List<SpiritArtifactStatRoll>()) ApplyRoll(roll, bonuses);
        }
        var setCounts = items.GroupBy(value => value.SetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var active = new List<SpiritArtifactActiveEffectSnapshot>();
        foreach (var pair in setCounts.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var set = SpiritArtifactRegistry.Set(pair.Key);
            if (set == null) continue;
            foreach (var bonus in set.Bonuses.Where(value => value.RequiredPieces <= pair.Value)
                         .OrderBy(value => value.RequiredPieces))
            {
                foreach (var definition in bonus.Effects)
                {
                    var effect = new SpiritArtifactActiveEffectSnapshot
                    {
                        SetId = set.Id,
                        RequiredPieces = bonus.RequiredPieces,
                        EffectId = definition.Id,
                        HandlerId = definition.HandlerId,
                        Amount = definition.Amount,
                        SecondaryAmount = definition.SecondaryAmount,
                        Maximum = definition.Maximum
                    };
                    active.Add(effect);
                    SpiritArtifactEffectHandlerRegistry.ApplyStatic(effect, bonuses);
                }
            }
        }
        var hash = SpiritArtifactMath.LoadoutHash(items, SpiritArtifactRegistry.RegistryHash);
        return new SpiritArtifactLoadoutView
        {
            SetCounts = setCounts,
            Battle = new SpiritArtifactBattleSnapshot
            {
                ProtocolVersion = SpiritArtifactRegistry.BattleProtocolVersion,
                RegistryHash = SpiritArtifactRegistry.RegistryHash,
                LoadoutRevision = Math.Max(0, loadoutRevision),
                LoadoutHash = hash,
                Items = items,
                OriginMagic = bonuses.OriginMagic,
                OriginSpirit = bonuses.OriginSpirit,
                OriginLuck = bonuses.OriginLuck,
                OriginPerception = bonuses.OriginPerception,
                FlatLife = bonuses.FlatLife,
                FlatArmor = bonuses.FlatArmor,
                MaxMagic = bonuses.MaxMagic,
                Speed = bonuses.Speed,
                StartExtraordinary = bonuses.StartExtraordinary,
                ActiveEffects = active
            }
        };
    }

    public static bool ValidateBattleSnapshot(SpiritArtifactBattleSnapshot? snapshot, out string reason)
    {
        if (snapshot == null || snapshot.ProtocolVersion != SpiritArtifactRegistry.BattleProtocolVersion
            || !string.Equals(snapshot.RegistryHash, SpiritArtifactRegistry.RegistryHash, StringComparison.Ordinal))
        {
            reason = "圣遗物战斗协议或注册表不兼容。";
            return false;
        }
        if (snapshot.Items == null || snapshot.Items.Count > SpiritArtifactSlots.All.Count
            || snapshot.Items.Select(value => value.SlotId).Distinct(StringComparer.Ordinal).Count() != snapshot.Items.Count
            || snapshot.Items.Select(value => value.ArtifactUid).Distinct(StringComparer.Ordinal).Count() != snapshot.Items.Count)
        {
            reason = "圣遗物战斗快照包含重复或超额部件。";
            return false;
        }
        foreach (var item in snapshot.Items)
        {
            if (!ValidateItem(item, out reason)) return false;
        }
        var rebuilt = Build(snapshot.Items, snapshot.LoadoutRevision).Battle;
        if (!string.Equals(snapshot.LoadoutHash, rebuilt.LoadoutHash, StringComparison.Ordinal)
            || snapshot.OriginMagic != rebuilt.OriginMagic
            || snapshot.OriginSpirit != rebuilt.OriginSpirit
            || snapshot.OriginLuck != rebuilt.OriginLuck
            || snapshot.OriginPerception != rebuilt.OriginPerception
            || snapshot.FlatLife != rebuilt.FlatLife
            || snapshot.FlatArmor != rebuilt.FlatArmor
            || snapshot.MaxMagic != rebuilt.MaxMagic
            || snapshot.Speed != rebuilt.Speed
            || snapshot.StartExtraordinary != rebuilt.StartExtraordinary
            || !SameEffects(snapshot.ActiveEffects, rebuilt.ActiveEffects))
        {
            reason = "圣遗物战斗快照的词条、套装效果或哈希不一致。";
            return false;
        }
        reason = "";
        return true;
    }

    public static bool ValidateItem(SpiritArtifactBattleItemSnapshot? item, out string reason)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.ArtifactUid))
        {
            reason = "圣遗物实例标识为空。";
            return false;
        }
        var piece = SpiritArtifactRegistry.Piece(item.PieceId);
        if (piece == null || !string.Equals(item.SlotId, piece.SlotId, StringComparison.Ordinal)
            || !string.Equals(item.SetId, SpiritArtifactRegistry.Sets()
                .FirstOrDefault(set => set.Pieces.Any(value => value.Id == item.PieceId))?.Id, StringComparison.Ordinal))
        {
            reason = "圣遗物套装、部件或槽位不一致。";
            return false;
        }
        if (item.Rarity is < 1 or > 3 || item.Level is < 1 or > 5
            || (item.SubStatRolls?.Count ?? 0) != item.Level - 1)
        {
            reason = "圣遗物星级、等级或副词条数量无效。";
            return false;
        }
        var expectedMain = item.SlotId == SpiritArtifactSlots.Flower
            ? new[] { SpiritArtifactStats.Life }
            : SpiritArtifactStats.MainChoiceStats;
        if (item.MainStat == null || !expectedMain.Contains(item.MainStat.StatId, StringComparer.Ordinal)
            || !Within(item.MainStat, item.Rarity, main: true))
        {
            reason = "圣遗物主词条无效。";
            return false;
        }
        foreach (var roll in item.SubStatRolls ?? new List<SpiritArtifactStatRoll>())
        {
            if (!SpiritArtifactStats.SubStats.Contains(roll.StatId, StringComparer.Ordinal)
                || !Within(roll, item.Rarity, main: false))
            {
                reason = "圣遗物副词条无效。";
                return false;
            }
        }
        reason = "";
        return true;
    }

    public static SpiritArtifactBattleItemSnapshot ToBattleItem(SpiritArtifactInstance artifact)
    {
        return new SpiritArtifactBattleItemSnapshot
        {
            ArtifactUid = artifact.ArtifactUid,
            SetId = artifact.SetId,
            PieceId = artifact.PieceId,
            SlotId = artifact.SlotId,
            Rarity = artifact.Rarity,
            Level = artifact.Level,
            MainStat = artifact.MainStat?.Clone() ?? new SpiritArtifactStatRoll(),
            SubStatRolls = (artifact.SubStatRolls ?? new List<SpiritArtifactStatRoll>())
                .Select(value => value.Clone()).ToList()
        };
    }

    private static void ApplyRoll(SpiritArtifactStatRoll? roll, SpiritArtifactBonusBuilder bonuses)
    {
        var value = Math.Max(0, roll?.Value ?? 0);
        switch (SpiritArtifactStats.Normalize(roll?.StatId))
        {
            case SpiritArtifactStats.Life: bonuses.FlatLife += value; break;
            case SpiritArtifactStats.Magic: bonuses.OriginMagic += value; break;
            case SpiritArtifactStats.Spirit: bonuses.OriginSpirit += value; break;
            case SpiritArtifactStats.Luck: bonuses.OriginLuck += value; break;
            case SpiritArtifactStats.Perception: bonuses.OriginPerception += value; break;
            case SpiritArtifactStats.Speed: bonuses.Speed += value; break;
            case SpiritArtifactStats.MaxMagic: bonuses.MaxMagic += value; break;
            case SpiritArtifactStats.Extraordinary: bonuses.StartExtraordinary += value; break;
        }
    }

    private static bool Within(SpiritArtifactStatRoll roll, int rarity, bool main)
    {
        var range = SpiritArtifactRegistry.Range(roll.StatId, rarity, main);
        return range.Minimum > 0 && roll.Value >= range.Minimum && roll.Value <= range.Maximum;
    }

    private static bool SameEffects(
        IReadOnlyList<SpiritArtifactActiveEffectSnapshot>? left,
        IReadOnlyList<SpiritArtifactActiveEffectSnapshot>? right)
    {
        var a = (left ?? Array.Empty<SpiritArtifactActiveEffectSnapshot>())
            .OrderBy(value => value.EffectId, StringComparer.Ordinal).ToArray();
        var b = (right ?? Array.Empty<SpiritArtifactActiveEffectSnapshot>())
            .OrderBy(value => value.EffectId, StringComparer.Ordinal).ToArray();
        if (a.Length != b.Length) return false;
        for (var index = 0; index < a.Length; index++)
        {
            if (a[index].SetId != b[index].SetId || a[index].RequiredPieces != b[index].RequiredPieces
                || a[index].EffectId != b[index].EffectId || a[index].HandlerId != b[index].HandlerId
                || a[index].Amount != b[index].Amount || a[index].SecondaryAmount != b[index].SecondaryAmount
                || a[index].Maximum != b[index].Maximum) return false;
        }
        return true;
    }
}

internal static class SpiritArtifactListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
            if (string.Equals(values[index], value, StringComparison.Ordinal)) return index;
        return int.MaxValue;
    }
}
