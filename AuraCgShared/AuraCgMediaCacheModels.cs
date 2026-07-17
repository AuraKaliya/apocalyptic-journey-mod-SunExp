using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AuraCg.Shared;

internal enum AuraCgMediaOwnership
{
    External = 0,
    RuntimeObject = 1,
    RuntimeObjectAndTexture = 2
}

internal sealed class AuraCgMediaCacheStats
{
    public int EntryCount { get; set; }

    public int SpriteCount { get; set; }

    public int SequenceCount { get; set; }

    public int BundleCount { get; set; }

    public int DerivedSpriteCount { get; set; }

    public long EstimatedBytes { get; set; }
}

internal enum AuraCgMediaCacheEntryKind
{
    Sprite,
    Sequence,
    Bundle,
    DerivedSprite
}

internal sealed class AuraCgMediaCacheEntry<TSprite, TBundle>
    where TSprite : class
    where TBundle : class
{
    private AuraCgMediaCacheEntry(AuraCgMediaCacheEntryKind kind, string stringKey)
    {
        Kind = kind;
        StringKey = stringKey;
    }

    public AuraCgMediaCacheEntryKind Kind { get; }

    public string StringKey { get; }

    public int IntKey { get; set; }

    public List<TSprite> Sprites { get; } = new();

    public List<long> SpriteBytes { get; } = new();

    public AuraCgMediaOwnership SpriteOwnership { get; private set; }

    public TBundle? Bundle { get; private set; }

    public long BundleBytes { get; private set; }

    public LinkedListNode<AuraCgMediaCacheEntry<TSprite, TBundle>>? Node { get; set; }

    public static AuraCgMediaCacheEntry<TSprite, TBundle> ForSprite(
        AuraCgMediaCacheEntryKind kind,
        string key,
        TSprite sprite,
        long estimatedBytes,
        AuraCgMediaOwnership ownership)
    {
        var entry = new AuraCgMediaCacheEntry<TSprite, TBundle>(kind, key)
        {
            SpriteOwnership = ownership
        };
        entry.Sprites.Add(sprite);
        entry.SpriteBytes.Add(Math.Max(0L, estimatedBytes));
        return entry;
    }

    public static AuraCgMediaCacheEntry<TSprite, TBundle> ForSequence(
        string key,
        List<TSprite> sequence,
        Func<TSprite, long>? estimateSpriteBytes,
        AuraCgMediaOwnership ownership)
    {
        var entry = new AuraCgMediaCacheEntry<TSprite, TBundle>(AuraCgMediaCacheEntryKind.Sequence, key)
        {
            SpriteOwnership = ownership
        };
        foreach (var sprite in sequence)
        {
            if (sprite == null)
            {
                continue;
            }

            entry.Sprites.Add(sprite);
            entry.SpriteBytes.Add(Estimate(estimateSpriteBytes, sprite));
        }

        return entry;
    }

    public static AuraCgMediaCacheEntry<TSprite, TBundle> ForBundle(
        string key,
        TBundle? bundle,
        long estimatedBytes)
    {
        return new AuraCgMediaCacheEntry<TSprite, TBundle>(AuraCgMediaCacheEntryKind.Bundle, key)
        {
            Bundle = bundle,
            BundleBytes = Math.Max(0L, estimatedBytes)
        };
    }

    public bool HasSameResources(AuraCgMediaCacheEntry<TSprite, TBundle> other)
    {
        if (Kind != other.Kind
            || !ReferenceEquals(Bundle, other.Bundle)
            || BundleBytes != other.BundleBytes
            || SpriteOwnership != other.SpriteOwnership
            || Sprites.Count != other.Sprites.Count)
        {
            return false;
        }

        for (var i = 0; i < Sprites.Count; i++)
        {
            if (!ReferenceEquals(Sprites[i], other.Sprites[i]) || SpriteBytes[i] != other.SpriteBytes[i])
            {
                return false;
            }
        }

        return true;
    }

    private static long Estimate(Func<TSprite, long>? estimate, TSprite sprite)
    {
        if (estimate == null)
        {
            return 0L;
        }

        try
        {
            return Math.Max(0L, estimate(sprite));
        }
        catch
        {
            return 0L;
        }
    }
}

internal sealed class AuraCgReferenceComparer<T> : IEqualityComparer<T>
    where T : class
{
    public static readonly AuraCgReferenceComparer<T> Instance = new();

    public bool Equals(T? left, T? right)
    {
        return ReferenceEquals(left, right);
    }

    public int GetHashCode(T value)
    {
        return RuntimeHelpers.GetHashCode(value);
    }
}
