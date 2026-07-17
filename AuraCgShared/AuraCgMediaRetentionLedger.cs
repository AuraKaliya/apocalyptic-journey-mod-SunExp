using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal sealed class AuraCgMediaRetentionLedger<TSprite, TBundle>
    where TSprite : class
    where TBundle : class
{
    private readonly Action<TSprite, AuraCgMediaOwnership>? onSpriteReleased;
    private readonly Action<TBundle>? onBundleReleased;
    private readonly Dictionary<TSprite, SpriteRetention> spriteRetentions = new(AuraCgReferenceComparer<TSprite>.Instance);
    private readonly Dictionary<TBundle, BundleRetention> bundleRetentions = new(AuraCgReferenceComparer<TBundle>.Instance);

    public AuraCgMediaRetentionLedger(
        Action<TSprite, AuraCgMediaOwnership>? onSpriteReleased,
        Action<TBundle>? onBundleReleased)
    {
        this.onSpriteReleased = onSpriteReleased;
        this.onBundleReleased = onBundleReleased;
    }

    public long EstimatedBytes { get; private set; }

    public bool ContainsSprite(TSprite sprite)
    {
        return spriteRetentions.ContainsKey(sprite);
    }

    public bool ContainsBundle(TBundle bundle)
    {
        return bundleRetentions.ContainsKey(bundle);
    }

    public void Attach(AuraCgMediaCacheEntry<TSprite, TBundle> entry)
    {
        var seen = new HashSet<TSprite>(AuraCgReferenceComparer<TSprite>.Instance);
        for (var i = 0; i < entry.Sprites.Count; i++)
        {
            var sprite = entry.Sprites[i];
            if (!seen.Add(sprite))
            {
                continue;
            }

            var bytes = i < entry.SpriteBytes.Count ? Math.Max(0L, entry.SpriteBytes[i]) : 0L;
            if (!spriteRetentions.TryGetValue(sprite, out var retention))
            {
                retention = new SpriteRetention(bytes, entry.SpriteOwnership);
                spriteRetentions[sprite] = retention;
                EstimatedBytes = SaturatingAdd(EstimatedBytes, bytes);
            }
            else
            {
                if (bytes > retention.EstimatedBytes)
                {
                    EstimatedBytes = SaturatingAdd(EstimatedBytes, bytes - retention.EstimatedBytes);
                    retention.EstimatedBytes = bytes;
                }

                if (entry.SpriteOwnership > retention.Ownership)
                {
                    retention.Ownership = entry.SpriteOwnership;
                }
            }

            retention.ReferenceCount++;
        }

        if (entry.Bundle == null)
        {
            return;
        }

        if (!bundleRetentions.TryGetValue(entry.Bundle, out var bundleRetention))
        {
            bundleRetention = new BundleRetention(Math.Max(0L, entry.BundleBytes));
            bundleRetentions[entry.Bundle] = bundleRetention;
            EstimatedBytes = SaturatingAdd(EstimatedBytes, bundleRetention.EstimatedBytes);
        }
        else if (entry.BundleBytes > bundleRetention.EstimatedBytes)
        {
            EstimatedBytes = SaturatingAdd(EstimatedBytes, entry.BundleBytes - bundleRetention.EstimatedBytes);
            bundleRetention.EstimatedBytes = entry.BundleBytes;
        }

        bundleRetention.ReferenceCount++;
    }

    public void Detach(AuraCgMediaCacheEntry<TSprite, TBundle> entry)
    {
        var seen = new HashSet<TSprite>(AuraCgReferenceComparer<TSprite>.Instance);
        foreach (var sprite in entry.Sprites)
        {
            if (!seen.Add(sprite) || !spriteRetentions.TryGetValue(sprite, out var retention))
            {
                continue;
            }

            retention.ReferenceCount--;
            if (retention.ReferenceCount > 0)
            {
                continue;
            }

            spriteRetentions.Remove(sprite);
            EstimatedBytes = Math.Max(0L, EstimatedBytes - retention.EstimatedBytes);
            if (retention.Ownership != AuraCgMediaOwnership.External)
            {
                InvokeRelease(() => onSpriteReleased?.Invoke(sprite, retention.Ownership));
            }
        }

        if (entry.Bundle == null || !bundleRetentions.TryGetValue(entry.Bundle, out var bundleRetention))
        {
            return;
        }

        bundleRetention.ReferenceCount--;
        if (bundleRetention.ReferenceCount <= 0)
        {
            bundleRetentions.Remove(entry.Bundle);
            EstimatedBytes = Math.Max(0L, EstimatedBytes - bundleRetention.EstimatedBytes);
            InvokeRelease(() => onBundleReleased?.Invoke(entry.Bundle));
        }
    }

    private static void InvokeRelease(Action release)
    {
        try
        {
            release();
        }
        catch
        {
        }
    }

    private static long SaturatingAdd(long left, long right)
    {
        return right > 0L && long.MaxValue - left < right ? long.MaxValue : left + right;
    }

    private sealed class SpriteRetention
    {
        public SpriteRetention(long estimatedBytes, AuraCgMediaOwnership ownership)
        {
            EstimatedBytes = estimatedBytes;
            Ownership = ownership;
        }

        public int ReferenceCount { get; set; }

        public long EstimatedBytes { get; set; }

        public AuraCgMediaOwnership Ownership { get; set; }
    }

    private sealed class BundleRetention
    {
        public BundleRetention(long estimatedBytes)
        {
            EstimatedBytes = estimatedBytes;
        }

        public int ReferenceCount { get; set; }

        public long EstimatedBytes { get; set; }
    }
}
