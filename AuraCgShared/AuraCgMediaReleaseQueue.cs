using System;
using System.Collections.Generic;

namespace AuraCg.Shared;

internal sealed class AuraCgMediaReleaseQueue<TSprite, TBundle>
    where TSprite : class
    where TBundle : class
{
    private readonly Dictionary<TSprite, AuraCgMediaOwnership> sprites = new(AuraCgReferenceComparer<TSprite>.Instance);
    private readonly HashSet<TBundle> bundles = new(AuraCgReferenceComparer<TBundle>.Instance);

    public int SpriteCount => sprites.Count;

    public int BundleCount => bundles.Count;

    public void QueueSprite(TSprite sprite, AuraCgMediaOwnership ownership)
    {
        if (sprite == null || ownership == AuraCgMediaOwnership.External)
        {
            return;
        }

        if (!sprites.TryGetValue(sprite, out var current) || ownership > current)
        {
            sprites[sprite] = ownership;
        }
    }

    public void QueueBundle(TBundle bundle)
    {
        if (bundle != null)
        {
            bundles.Add(bundle);
        }
    }

    public bool Flush(
        bool canRelease,
        Func<TSprite, bool> isSpriteRetained,
        Func<TBundle, bool> isBundleRetained,
        Action<TSprite, AuraCgMediaOwnership> releaseSprite,
        Action<TBundle> releaseBundle)
    {
        if (!canRelease)
        {
            return false;
        }

        var pendingSprites = new List<KeyValuePair<TSprite, AuraCgMediaOwnership>>(sprites);
        var pendingBundles = new List<TBundle>(bundles);
        sprites.Clear();
        bundles.Clear();

        foreach (var pending in pendingSprites)
        {
            if (!isSpriteRetained(pending.Key))
            {
                releaseSprite(pending.Key, pending.Value);
            }
        }

        foreach (var bundle in pendingBundles)
        {
            if (!isBundleRetained(bundle))
            {
                releaseBundle(bundle);
            }
        }

        return true;
    }
}
