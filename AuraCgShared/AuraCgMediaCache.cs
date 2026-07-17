using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal sealed class AuraCgMediaCache<TSprite, TBundle>
    where TSprite : class
    where TBundle : class
{
    private readonly int maximumEntries;
    private readonly long maximumEstimatedBytes;
    private readonly Dictionary<string, AuraCgMediaCacheEntry<TSprite, TBundle>> sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AuraCgMediaCacheEntry<TSprite, TBundle>> sequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AuraCgMediaCacheEntry<TSprite, TBundle>> bundles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, AuraCgMediaCacheEntry<TSprite, TBundle>> derivedSprites = new();
    private readonly LinkedList<AuraCgMediaCacheEntry<TSprite, TBundle>> recency = new();
    private readonly AuraCgMediaRetentionLedger<TSprite, TBundle> retentionLedger;

    public AuraCgMediaCache(
        int maximumEntries = int.MaxValue,
        long maximumEstimatedBytes = long.MaxValue,
        Action<TSprite, AuraCgMediaOwnership>? onSpriteReleased = null,
        Action<TBundle>? onBundleReleased = null)
    {
        this.maximumEntries = Math.Max(1, maximumEntries);
        this.maximumEstimatedBytes = Math.Max(1L, maximumEstimatedBytes);
        retentionLedger = new AuraCgMediaRetentionLedger<TSprite, TBundle>(onSpriteReleased, onBundleReleased);
    }

    public int SpriteCount => sprites.Count;

    public int SequenceCount => sequences.Count;

    public int BundleCount => bundles.Count;

    public int DerivedSpriteCount => derivedSprites.Count;

    public long EstimatedBytes => retentionLedger.EstimatedBytes;

    public int EntryCount => sprites.Count + sequences.Count + bundles.Count + derivedSprites.Count;

    public bool ContainsSprite(string key)
    {
        return TryFind(sprites, key, out _);
    }

    public bool TryGetSprite(string key, out TSprite? sprite)
    {
        if (TryFind(sprites, key, out var entry) && entry.Sprites.Count > 0)
        {
            sprite = entry.Sprites[0];
            return true;
        }

        sprite = null;
        return false;
    }

    public void StoreSprite(
        string key,
        TSprite sprite,
        long estimatedSpriteBytes = 0L,
        AuraCgMediaOwnership ownership = AuraCgMediaOwnership.External)
    {
        if (string.IsNullOrWhiteSpace(key) || sprite == null)
        {
            return;
        }

        var entry = AuraCgMediaCacheEntry<TSprite, TBundle>.ForSprite(
            AuraCgMediaCacheEntryKind.Sprite,
            key.Trim(),
            sprite,
            estimatedSpriteBytes,
            ownership);
        Store(sprites, entry.StringKey, entry);
    }

    public bool ContainsSequence(string key)
    {
        return TryFind(sequences, key, out _);
    }

    public bool TryGetSequence(string key, out List<TSprite> sequence)
    {
        if (TryFind(sequences, key, out var entry) && entry.Sprites.Count > 0)
        {
            sequence = entry.Sprites;
            return true;
        }

        sequence = new List<TSprite>();
        return false;
    }

    public void StoreSequence(
        string key,
        List<TSprite> sequence,
        Func<TSprite, long>? estimateSpriteBytes = null,
        AuraCgMediaOwnership ownership = AuraCgMediaOwnership.External)
    {
        if (string.IsNullOrWhiteSpace(key) || sequence == null || sequence.Count == 0)
        {
            return;
        }

        var entry = AuraCgMediaCacheEntry<TSprite, TBundle>.ForSequence(
            key.Trim(),
            sequence,
            estimateSpriteBytes,
            ownership);
        if (entry.Sprites.Count > 0)
        {
            Store(sequences, entry.StringKey, entry);
        }
    }

    public bool TryGetBundle(string key, out TBundle? bundle)
    {
        if (TryFind(bundles, key, out var entry))
        {
            bundle = entry.Bundle;
            return true;
        }

        bundle = null;
        return false;
    }

    public void StoreBundle(string key, TBundle? bundle, long estimatedBundleBytes = 0L)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var entry = AuraCgMediaCacheEntry<TSprite, TBundle>.ForBundle(key.Trim(), bundle, estimatedBundleBytes);
        Store(bundles, entry.StringKey, entry);
    }

    public bool TryGetDerivedSprite(int sourceId, out TSprite? sprite)
    {
        if (derivedSprites.TryGetValue(sourceId, out var entry) && entry.Sprites.Count > 0)
        {
            Touch(entry);
            sprite = entry.Sprites[0];
            return true;
        }

        sprite = null;
        return false;
    }

    public void StoreDerivedSprite(
        int sourceId,
        TSprite sprite,
        long estimatedSpriteBytes = 0L,
        AuraCgMediaOwnership ownership = AuraCgMediaOwnership.External)
    {
        if (sprite == null)
        {
            return;
        }

        var entry = AuraCgMediaCacheEntry<TSprite, TBundle>.ForSprite(
            AuraCgMediaCacheEntryKind.DerivedSprite,
            sourceId.ToString(),
            sprite,
            estimatedSpriteBytes,
            ownership);
        entry.IntKey = sourceId;
        StoreDerived(entry);
    }

    public bool ContainsSpriteReference(TSprite sprite)
    {
        return sprite != null && retentionLedger.ContainsSprite(sprite);
    }

    public bool ContainsBundleReference(TBundle bundle)
    {
        return bundle != null && retentionLedger.ContainsBundle(bundle);
    }

    public AuraCgMediaCacheStats GetStats()
    {
        return new AuraCgMediaCacheStats
        {
            EntryCount = EntryCount,
            SpriteCount = SpriteCount,
            SequenceCount = SequenceCount,
            BundleCount = BundleCount,
            DerivedSpriteCount = DerivedSpriteCount,
            EstimatedBytes = EstimatedBytes
        };
    }

    public void Clear()
    {
        while (recency.First != null)
        {
            Remove(recency.First.Value);
        }
    }

    private bool TryFind(
        Dictionary<string, AuraCgMediaCacheEntry<TSprite, TBundle>> index,
        string key,
        out AuraCgMediaCacheEntry<TSprite, TBundle> entry)
    {
        if (!string.IsNullOrWhiteSpace(key) && index.TryGetValue(key, out entry!))
        {
            Touch(entry);
            return true;
        }

        entry = null!;
        return false;
    }

    private void Store(
        Dictionary<string, AuraCgMediaCacheEntry<TSprite, TBundle>> index,
        string key,
        AuraCgMediaCacheEntry<TSprite, TBundle> entry)
    {
        index.TryGetValue(key, out var previous);
        if (previous != null && previous.HasSameResources(entry))
        {
            Touch(previous);
            return;
        }

        retentionLedger.Attach(entry);
        if (previous != null)
        {
            Remove(previous);
        }

        index[key] = entry;
        entry.Node = recency.AddLast(entry);
        EnforceLimits();
    }

    private void StoreDerived(AuraCgMediaCacheEntry<TSprite, TBundle> entry)
    {
        derivedSprites.TryGetValue(entry.IntKey, out var previous);
        if (previous != null && previous.HasSameResources(entry))
        {
            Touch(previous);
            return;
        }

        retentionLedger.Attach(entry);
        if (previous != null)
        {
            Remove(previous);
        }

        derivedSprites[entry.IntKey] = entry;
        entry.Node = recency.AddLast(entry);
        EnforceLimits();
    }

    private void Remove(AuraCgMediaCacheEntry<TSprite, TBundle> entry)
    {
        switch (entry.Kind)
        {
            case AuraCgMediaCacheEntryKind.Sprite:
                sprites.Remove(entry.StringKey);
                break;
            case AuraCgMediaCacheEntryKind.Sequence:
                sequences.Remove(entry.StringKey);
                break;
            case AuraCgMediaCacheEntryKind.Bundle:
                bundles.Remove(entry.StringKey);
                break;
            case AuraCgMediaCacheEntryKind.DerivedSprite:
                derivedSprites.Remove(entry.IntKey);
                break;
        }

        if (entry.Node?.List != null)
        {
            recency.Remove(entry.Node);
        }

        retentionLedger.Detach(entry);
    }

    private void EnforceLimits()
    {
        while ((EntryCount > maximumEntries || EstimatedBytes > maximumEstimatedBytes)
               && recency.First != null)
        {
            Remove(recency.First.Value);
        }
    }

    private void Touch(AuraCgMediaCacheEntry<TSprite, TBundle> entry)
    {
        if (entry.Node?.List == null)
        {
            return;
        }

        recency.Remove(entry.Node);
        recency.AddLast(entry.Node);
    }
}
