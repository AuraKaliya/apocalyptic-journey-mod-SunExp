using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace AuraCg.Shared;

internal sealed class AuraCgUnityMediaRepository
{
    private const int MaximumCacheEntries = 512;
    private const long MaximumCacheEstimatedBytes = 256L * 1024L * 1024L;
    private const long EstimatedAssetBundleHandleBytes = 1024L * 1024L;
    private readonly Func<string, string, AssetBundle?> registeredBundleResolver;
    private readonly Func<string, string, string> bundlePathResolver;
    private readonly Func<string, bool> shouldApplyCpuAlphaMode;
    private readonly AuraCgMediaReleaseQueue<Sprite, AssetBundle> releaseQueue = new();
    private readonly AuraCgMediaCache<Sprite, AssetBundle> cache;
    private readonly AuraCgMediaRetentionLedger<Sprite, AssetBundle> sceneRetentions;
    private readonly HashSet<SceneMediaLease> sceneLeases = new();

    public AuraCgUnityMediaRepository(
        Func<string, string, AssetBundle?> registeredBundleResolver,
        Func<string, string, string> bundlePathResolver,
        Func<string, bool> shouldApplyCpuAlphaMode)
    {
        this.registeredBundleResolver = registeredBundleResolver ?? throw new ArgumentNullException(nameof(registeredBundleResolver));
        this.bundlePathResolver = bundlePathResolver ?? throw new ArgumentNullException(nameof(bundlePathResolver));
        this.shouldApplyCpuAlphaMode = shouldApplyCpuAlphaMode ?? throw new ArgumentNullException(nameof(shouldApplyCpuAlphaMode));
        sceneRetentions = new AuraCgMediaRetentionLedger<Sprite, AssetBundle>(releaseQueue.QueueSprite, releaseQueue.QueueBundle);
        cache = new AuraCgMediaCache<Sprite, AssetBundle>(
            MaximumCacheEntries,
            MaximumCacheEstimatedBytes,
            releaseQueue.QueueSprite,
            releaseQueue.QueueBundle);
    }

    public bool IsPreloaded(SkillCgRequest request)
    {
        if (string.Equals(request.MediaType, SkillCgMediaTypes.Sequence, StringComparison.OrdinalIgnoreCase))
        {
            return cache.ContainsSequence(AuraCgMediaCacheKeys.Sequence(request));
        }

        return cache.ContainsSprite(AuraCgMediaCacheKeys.Sprite(
            request.ImagePath,
            SkillCgAlphaModes.None,
            0.03f,
            0.08f));
    }

    public IEnumerator LoadSprite(string path, Action<Sprite?> onLoaded)
    {
        yield return LoadSprite(path, SkillCgAlphaModes.None, 0.03f, 0.08f, onLoaded);
    }

    public IEnumerator LoadSequenceSprites(
        SkillCgRequest request,
        Action<List<Sprite>> onLoaded,
        Func<bool>? keepLoading = null)
    {
        var cacheKey = AuraCgMediaCacheKeys.Sequence(request);
        if (cache.TryGetSequence(cacheKey, out var cached))
        {
            onLoaded(cached);
            yield break;
        }

        var result = new List<Sprite>();
        if (!string.IsNullOrWhiteSpace(request.BundlePath))
        {
            yield return LoadBundleSequenceSprites(request, result, keepLoading);
            if (!ShouldContinueLoading(keepLoading))
            {
                onLoaded(new List<Sprite>());
                yield break;
            }

            if (result.Count > 0)
            {
                cache.StoreSequence(
                    cacheKey,
                    result,
                    AuraSharedResourceCache.EstimateObjectBytes,
                    AuraCgMediaOwnership.External);
                onLoaded(result);
                yield break;
            }
        }

        foreach (var framePath in AuraCgMediaPathResolver.ResolveSequenceFramePaths(request.ImagePath))
        {
            if (!ShouldContinueLoading(keepLoading))
            {
                onLoaded(new List<Sprite>());
                yield break;
            }

            Sprite? frame = null;
            yield return LoadSprite(
                framePath,
                request.AlphaMode,
                request.KeyThreshold,
                request.KeySoftness,
                sprite => frame = sprite);
            if (!ShouldContinueLoading(keepLoading))
            {
                onLoaded(new List<Sprite>());
                yield break;
            }

            if (frame != null)
            {
                result.Add(frame);
            }
        }

        if (result.Count > 0)
        {
            cache.StoreSequence(
                cacheKey,
                result,
                AuraSharedResourceCache.EstimateObjectBytes,
                AuraCgMediaOwnership.RuntimeObjectAndTexture);
        }

        if (result.Count == 0)
        {
            AuraCgLog.WarnOnce("sequence-empty:" + request.ImagePath, "CG sequence has no loadable frames: " + request.ImagePath);
        }

        onLoaded(result);
    }

    public List<Sprite> RegisterDirectSceneSprites(
        SkillCgRequest request,
        IEnumerable<Sprite> sprites,
        bool ownsSprites)
    {
        var result = (sprites ?? Array.Empty<Sprite>())
            .Where(sprite => sprite != null)
            .ToList();
        var cacheKey = AuraCgMediaCacheKeys.Sequence(request);
        if (cache.TryGetSequence(cacheKey, out var cached))
        {
            if (ownsSprites)
            {
                foreach (var duplicate in result.Where(candidate =>
                             cached.All(retained => !ReferenceEquals(retained, candidate))))
                {
                    releaseQueue.QueueSprite(duplicate, AuraCgMediaOwnership.RuntimeObject);
                }
            }

            return cached;
        }

        if (result.Count == 0)
        {
            return result;
        }

        cache.StoreSequence(
            cacheKey,
            result,
            AuraSharedResourceCache.EstimateObjectBytes,
            ownsSprites ? AuraCgMediaOwnership.RuntimeObject : AuraCgMediaOwnership.External);
        return result;
    }

    public Sprite CreateInvertedSprite(Sprite source)
    {
        var texture = source.texture;
        var key = texture.GetInstanceID();
        if (cache.TryGetDerivedSprite(key, out var cached) && cached != null)
        {
            return cached;
        }

        try
        {
            var pixels = texture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];
                color.r = (byte)(255 - color.r);
                color.g = (byte)(255 - color.g);
                color.b = (byte)(255 - color.b);
                pixels[i] = color;
            }

            var invertedTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false)
            {
                name = texture.name + "_masked_invert"
            };
            invertedTexture.SetPixels32(pixels);
            invertedTexture.Apply(false, false);

            var sprite = Sprite.Create(
                invertedTexture,
                new Rect(0f, 0f, invertedTexture.width, invertedTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = source.name + "_masked_invert";
            cache.StoreDerivedSprite(
                key,
                sprite,
                AuraSharedResourceCache.EstimateObjectBytes(sprite),
                AuraCgMediaOwnership.RuntimeObjectAndTexture);
            return sprite;
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("masked-invert-cpu-failed:" + source.name, "CPU masked invert fallback failed: " + ex.Message);
            return source;
        }
    }

    public void FlushReleasedMedia(bool isIdle)
    {
        releaseQueue.Flush(
            isIdle,
            sprite => cache.ContainsSpriteReference(sprite) || sceneRetentions.ContainsSprite(sprite),
            cache.ContainsBundleReference,
            ReleaseSprite,
            ReleaseBundle);
    }

    internal IDisposable RetainSceneFrames(List<Sprite> frames, AuraCgMediaOwnership ownership)
    {
        var entry = AuraCgMediaCacheEntry<Sprite, AssetBundle>.ForSequence(
            "scene-presentation", frames, AuraSharedResourceCache.EstimateObjectBytes, ownership);
        sceneRetentions.Attach(entry);
        SceneMediaLease? lease = null;
        lease = new SceneMediaLease(() =>
        {
            sceneRetentions.Detach(entry);
            sceneLeases.Remove(lease!);
        });
        sceneLeases.Add(lease);
        return lease;
    }

    internal void Dispose()
    {
        foreach (var lease in sceneLeases.ToArray()) lease.Dispose();
        cache.Clear();
        FlushReleasedMedia(true);
    }

    private sealed class SceneMediaLease : IDisposable
    {
        private Action? release;
        internal SceneMediaLease(Action release) { this.release = release; }
        public void Dispose()
        {
            var current = release;
            release = null;
            current?.Invoke();
        }
    }

    private IEnumerator LoadBundleSequenceSprites(
        SkillCgRequest request,
        List<Sprite> result,
        Func<bool>? keepLoading)
    {
        var bundle = ResolveAssetBundle(request.OwnerModId, request.BundlePath);
        if (bundle == null)
        {
            yield break;
        }

        string[] assetNames;
        try
        {
            var prefix = AuraCgMediaPathResolver.NormalizeRelativeResourcePath(request.BundleAssetPrefix);
            assetNames = bundle.GetAllAssetNames()
                .Where(name => AuraCgMediaPathResolver.IsBundleSequenceAsset(name, prefix))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("bundle-sequence-list-failed:" + request.BundlePath, "CG bundle sequence list failed: " + request.BundlePath + ", error=" + ex.Message);
            yield break;
        }

        foreach (var assetName in assetNames)
        {
            if (!ShouldContinueLoading(keepLoading))
            {
                yield break;
            }

            Sprite? sprite = null;
            var spriteRequest = bundle.LoadAssetAsync<Sprite>(assetName);
            yield return spriteRequest;
            if (!ShouldContinueLoading(keepLoading))
            {
                yield break;
            }

            sprite = spriteRequest.asset as Sprite;
            if (sprite == null)
            {
                var textureRequest = bundle.LoadAssetAsync<Texture2D>(assetName);
                yield return textureRequest;
                if (!ShouldContinueLoading(keepLoading))
                {
                    yield break;
                }

                if (textureRequest.asset is Texture2D texture)
                {
                    sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = Path.GetFileNameWithoutExtension(assetName);
                }
            }

            if (sprite != null)
            {
                result.Add(sprite);
            }
        }

        if (result.Count > 0)
        {
            AuraCgLog.InfoOnce(
                "bundle-sequence-loaded:" + request.BundlePath + ":" + request.BundleAssetPrefix,
                "CG bundle sequence loaded: bundle=" + request.BundlePath
                + ", prefix=" + request.BundleAssetPrefix
                + ", frames=" + result.Count);
        }
    }

    public void InvalidateBundleMiss(string ownerModId, string bundlePath)
    {
        cache.RemoveMissingBundle(AuraCgMediaCacheKeys.Bundle(ownerModId, bundlePath));
    }

    private AssetBundle? ResolveAssetBundle(string ownerModId, string bundlePath)
    {
        var id = AuraCgMediaPathResolver.NormalizeBundleId(bundlePath);
        if (id.Length == 0)
        {
            return null;
        }

        var registered = registeredBundleResolver(ownerModId, id);
        if (registered != null)
        {
            return registered;
        }

        var cacheKey = AuraCgMediaCacheKeys.Bundle(ownerModId, id);
        if (cache.TryGetBundle(cacheKey, out var cached))
        {
            return cached;
        }

        var resolved = bundlePathResolver(ownerModId, id);
        if (!File.Exists(resolved))
        {
            cache.StoreBundle(cacheKey, null);
            AuraCgLog.WarnOnce(
                "bundle-missing:" + cacheKey,
                "CG asset bundle is not registered or found: owner=" + ownerModId + ", bundle=" + id + ", resolved=" + resolved);
            return null;
        }

        try
        {
            var bundle = AssetBundle.LoadFromFile(resolved);
            cache.StoreBundle(cacheKey, bundle, EstimatedAssetBundleHandleBytes);
            return bundle;
        }
        catch (Exception ex)
        {
            cache.StoreBundle(cacheKey, null);
            AuraCgLog.WarnOnce(
                "bundle-load-failed:" + cacheKey,
                "CG asset bundle load failed: owner=" + ownerModId + ", bundle=" + id + ", resolved=" + resolved + ", error=" + ex.Message);
            return null;
        }
    }

    private IEnumerator LoadSprite(
        string path,
        string alphaMode,
        float keyThreshold,
        float keySoftness,
        Action<Sprite?> onLoaded)
    {
        var cacheKey = AuraCgMediaCacheKeys.Sprite(path, alphaMode, keyThreshold, keySoftness);
        if (cache.TryGetSprite(cacheKey, out var cached) && cached != null)
        {
            onLoaded(cached);
            yield break;
        }

        if (!File.Exists(path))
        {
            AuraCgLog.WarnOnce("missing-image:" + path, "CG image not found: " + path);
            onLoaded(null);
            yield break;
        }

        using var request = UnityWebRequestTexture.GetTexture(new Uri(path).AbsoluteUri);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            AuraCgLog.WarnOnce("image-load-failed:" + path, "CG image failed to load: " + Path.GetFileName(path) + ", error=" + request.error);
            onLoaded(null);
            yield break;
        }

        var texture = DownloadHandlerTexture.GetContent(request);
        if (texture == null)
        {
            AuraCgLog.WarnOnce("image-empty:" + path, "CG image load returned empty texture: " + path);
            onLoaded(null);
            yield break;
        }

        texture.name = Path.GetFileNameWithoutExtension(path);
        if (shouldApplyCpuAlphaMode(alphaMode))
        {
            ApplyAlphaMode(texture, alphaMode, keyThreshold, keySoftness, path);
        }

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = texture.name;
        cache.StoreSprite(
            cacheKey,
            sprite,
            AuraSharedResourceCache.EstimateObjectBytes(sprite),
            AuraCgMediaOwnership.RuntimeObjectAndTexture);
        AuraCgLog.InfoOnce("image-loaded:" + path, "CG image loaded: " + Path.GetFileName(path) + " (" + texture.width + "x" + texture.height + ")");
        onLoaded(sprite);
    }

    private static bool ShouldContinueLoading(Func<bool>? keepLoading)
    {
        return keepLoading == null || keepLoading();
    }

    private static void ApplyAlphaMode(Texture2D texture, string alphaMode, float keyThreshold, float keySoftness, string path)
    {
        if (!string.Equals(SkillCgAlphaModes.Normalize(alphaMode), SkillCgAlphaModes.BlackKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var pixels = texture.GetPixels32();
            var threshold = Mathf.Clamp01(keyThreshold);
            var softness = Mathf.Clamp(keySoftness, 0.001f, 1f);
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];
                var luma = (0.299f * color.r + 0.587f * color.g + 0.114f * color.b) / 255f;
                var alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((luma - threshold) / softness));
                color.a = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * alpha), 0, 255);
                pixels[i] = color;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("black-key-failed:" + path, "CG black-key alpha fallback failed: " + Path.GetFileName(path) + ", error=" + ex.Message);
        }
    }

    private static void ReleaseSprite(Sprite sprite, AuraCgMediaOwnership ownership)
    {
        Texture2D? texture = null;
        if (ownership == AuraCgMediaOwnership.RuntimeObjectAndTexture)
        {
            try
            {
                texture = sprite.texture;
            }
            catch
            {
            }
        }

        UnityEngine.Object.Destroy(sprite);
        if (texture != null)
        {
            UnityEngine.Object.Destroy(texture);
        }
    }

    private static void ReleaseBundle(AssetBundle bundle)
    {
        try
        {
            bundle.Unload(false);
        }
        catch (Exception ex)
        {
            AuraCgLog.WarnOnce("bundle-release-failed:" + bundle.name, "CG asset bundle release failed: " + ex.Message);
        }
    }
}
