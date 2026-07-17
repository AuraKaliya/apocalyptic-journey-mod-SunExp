using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal sealed class AuraCgMediaCache<TSprite, TBundle>
    where TSprite : class
    where TBundle : class
{
    private readonly Dictionary<string, TSprite> sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TSprite>> sequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TBundle?> bundles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TSprite> derivedSprites = new();

    public int SpriteCount => sprites.Count;

    public int SequenceCount => sequences.Count;

    public int BundleCount => bundles.Count;

    public bool ContainsSprite(string key)
    {
        return sprites.ContainsKey(key);
    }

    public bool TryGetSprite(string key, out TSprite? sprite)
    {
        return sprites.TryGetValue(key, out sprite);
    }

    public void StoreSprite(string key, TSprite sprite)
    {
        if (!string.IsNullOrWhiteSpace(key) && sprite != null)
        {
            sprites[key] = sprite;
        }
    }

    public bool ContainsSequence(string key)
    {
        return sequences.ContainsKey(key);
    }

    public bool TryGetSequence(string key, out List<TSprite> sequence)
    {
        if (sequences.TryGetValue(key, out var cached) && cached.Count > 0)
        {
            sequence = cached;
            return true;
        }

        sequence = new List<TSprite>();
        return false;
    }

    public void StoreSequence(string key, List<TSprite> sequence)
    {
        if (!string.IsNullOrWhiteSpace(key) && sequence != null && sequence.Count > 0)
        {
            sequences[key] = sequence;
        }
    }

    public bool TryGetBundle(string key, out TBundle? bundle)
    {
        return bundles.TryGetValue(key, out bundle);
    }

    public void StoreBundle(string key, TBundle? bundle)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            bundles[key] = bundle;
        }
    }

    public bool TryGetDerivedSprite(int sourceId, out TSprite? sprite)
    {
        return derivedSprites.TryGetValue(sourceId, out sprite);
    }

    public void StoreDerivedSprite(int sourceId, TSprite sprite)
    {
        if (sprite != null)
        {
            derivedSprites[sourceId] = sprite;
        }
    }
}
