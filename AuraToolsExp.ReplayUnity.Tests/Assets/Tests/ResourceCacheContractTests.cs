using System;
using System.Collections.Generic;
using AuraShared.Core;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Witch.Core { internal static class ResourceCacheFixtureNamespace { } }
namespace AuraShared.Core
{
    internal static class AuraSharedLog { public static void WarnOnce(string owner, string key, string message) { } }
}

// The boundary models the verified native custom-image contract: Texture and
// Sprite are accepted; Texture2D is not a supported generic request type.
internal static class ResourceLoader
{
    internal static int Calls;
    internal static Type LastType;
    internal static readonly List<Object> Owned = new();
    public static T Load<T>(string path, bool fromMod) where T : Object
    {
        Calls++; LastType = typeof(T);
        if (path == "missing") return null;
        var texture = new Texture2D(8, 8); Owned.Add(texture);
        return typeof(T) == typeof(Texture) ? texture as T : null;
    }
    public static T[] LoadAll<T>(string path) where T : Object => new[] { Load<T>(path, true) };
}

public sealed class ResourceCacheContractTests
{
    [SetUp] public void Setup() { AuraSharedResourceCache.Clear("fixture"); ResourceLoader.Calls = 0; }
    [TearDown] public void Cleanup()
    {
        AuraSharedResourceCache.Clear("fixture");
        foreach (var item in ResourceLoader.Owned) if (item != null) Object.DestroyImmediate(item);
        ResourceLoader.Owned.Clear();
    }
    [Test] public void Texture2DUsesNativeTextureContractAndSharesItsCache()
    {
        var first = AuraSharedResourceCache.Load<Texture2D>("fixture", "picture", true, "art", out var firstHit);
        var second = AuraSharedResourceCache.Load<Texture>("fixture", "picture", true, "art", out var secondHit);
        Assert.That(first, Is.Not.Null); Assert.That(firstHit, Is.False);
        Assert.That(ResourceLoader.LastType, Is.EqualTo(typeof(Texture)));
        Assert.That(secondHit, Is.True); Assert.That(second, Is.SameAs(first)); Assert.That(ResourceLoader.Calls, Is.EqualTo(1));
    }
    [Test] public void DestroyedAssetIsReloadedInsteadOfBeingReturnedAsAHit()
    {
        var first = AuraSharedResourceCache.Load<Texture>("fixture", "picture"); Object.DestroyImmediate(first);
        var second = AuraSharedResourceCache.Load<Texture2D>("fixture", "picture", true, "art", out var hit);
        Assert.That(second, Is.Not.Null); Assert.That(hit, Is.False); Assert.That(ResourceLoader.Calls, Is.EqualTo(2));
    }
    [Test] public void CachedMissingResultIsDistinctFromSuccessfulLoading()
    {
        Assert.That(AuraSharedResourceCache.Load<Texture>("fixture", "missing", true, "art", out var firstHit), Is.Null);
        Assert.That(firstHit, Is.False);
        Assert.That(AuraSharedResourceCache.Load<Texture>("fixture", "missing", true, "art", out var nextHit), Is.Null);
        Assert.That(nextHit, Is.True);
    }
    [Test] public void TextureArrayAliasRetainsEveryNativeFrame()
    {
        var first = AuraSharedResourceCache.LoadAll<Texture>("fixture", "frames");
        var second = AuraSharedResourceCache.LoadAll<Texture2D>("fixture", "frames", "art", out var hit);
        Assert.That(hit, Is.True); Assert.That(second.Length, Is.EqualTo(1)); Assert.That(second[0], Is.SameAs(first[0]));
    }
}
