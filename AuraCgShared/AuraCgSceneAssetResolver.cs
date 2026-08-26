using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace AuraCg.Shared;

public sealed class AuraCgResolvedSceneAsset
{
    public string OwnerModId { get; set; } = "";

    public string AssetId { get; set; } = "";

    public string ImagePath { get; set; } = "";

    public string MediaType { get; set; } = SkillCgMediaTypes.Image;

    public string BundlePath { get; set; } = "";

    public string BundleAssetPrefix { get; set; } = "";

    public float FrameSeconds { get; set; } = 0.08f;

    public bool Loop { get; set; } = true;

    public List<Sprite> DirectSprites { get; set; } = new();

    public bool OwnsDirectSprites { get; set; }

    public void Normalize(AuraCgSceneAssetReference reference)
    {
        reference ??= new AuraCgSceneAssetReference();
        reference.Normalize();
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId)
            ? reference.OwnerModId
            : OwnerModId.Trim();
        AssetId = string.IsNullOrWhiteSpace(AssetId)
            ? reference.AssetId
            : AssetId.Trim();
        ImagePath = (ImagePath ?? "").Trim();
        MediaType = SkillCgMediaTypes.Normalize(MediaType);
        BundlePath = (BundlePath ?? "").Trim().Replace('\\', '/').TrimStart('/');
        BundleAssetPrefix = (BundleAssetPrefix ?? "").Trim().Replace('\\', '/').TrimStart('/');
        FrameSeconds = Math.Max(0.01f, FrameSeconds);
        DirectSprites = (DirectSprites ?? new List<Sprite>())
            .Where(sprite => sprite != null)
            .ToList();
    }

    public SkillCgRequest ToMediaRequest()
    {
        return new SkillCgRequest
        {
            ProviderId = OwnerModId + ".SceneAsset." + AssetId,
            OwnerModId = OwnerModId,
            ImagePath = ImagePath,
            ImageResource = AssetId,
            MediaType = MediaType,
            BundlePath = BundlePath,
            BundleAssetPrefix = BundleAssetPrefix,
            FrameSeconds = FrameSeconds,
            DisableSync = true
        };
    }

    public static AuraCgResolvedSceneAsset? FromObject(
        object? source,
        AuraCgSceneAssetReference reference)
    {
        if (source == null)
        {
            return null;
        }

        if (source is AuraCgResolvedSceneAsset resolved)
        {
            resolved.Normalize(reference);
            return resolved;
        }

        var type = source.GetType();
        var result = new AuraCgResolvedSceneAsset
        {
            OwnerModId = ReadString(type, source, "OwnerModId"),
            AssetId = ReadString(type, source, "AssetId"),
            ImagePath = ReadString(type, source, "ImagePath"),
            MediaType = ReadString(type, source, "MediaType"),
            BundlePath = ReadString(type, source, "BundlePath"),
            BundleAssetPrefix = ReadString(type, source, "BundleAssetPrefix"),
            FrameSeconds = ReadFloat(type, source, "FrameSeconds", 0.08f),
            Loop = ReadBool(type, source, "Loop", true),
            DirectSprites = ReadSprites(type, source, "DirectSprites"),
            OwnsDirectSprites = ReadBool(type, source, "OwnsDirectSprites", false)
        };
        result.Normalize(reference);
        return string.IsNullOrWhiteSpace(result.ImagePath)
               && string.IsNullOrWhiteSpace(result.BundlePath)
               && result.DirectSprites.Count == 0
            ? null
            : result;
    }

    private static List<Sprite> ReadSprites(Type type, object source, string name)
    {
        try
        {
            return (type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source)
                    as IEnumerable<Sprite> ?? Array.Empty<Sprite>())
                .Where(sprite => sprite != null)
                .ToList();
        }
        catch
        {
            return new List<Sprite>();
        }
    }

    private static string ReadString(Type type, object source, string name)
    {
        try
        {
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) as string ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static float ReadFloat(Type type, object source, string name, float fallback)
    {
        try
        {
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) is float value
                ? value
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(Type type, object source, string name, bool fallback)
    {
        try
        {
            return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) is bool value
                ? value
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

internal sealed class AuraCgSceneAssetResolverCoordinator
{
    private readonly List<ResolverHandle> resolvers = new();

    public int Count => resolvers.Count;

    public bool Register(object? provider, out string description)
    {
        description = "";
        if (provider == null)
        {
            return false;
        }

        var handle = new ResolverHandle(provider);
        if (!handle.IsValid)
        {
            return false;
        }

        resolvers.RemoveAll(item => string.Equals(
            item.QualifiedProviderId,
            handle.QualifiedProviderId,
            StringComparison.OrdinalIgnoreCase));
        resolvers.Add(handle);
        resolvers.Sort(ResolverHandle.Compare);
        description = handle.QualifiedProviderId;
        return true;
    }

    public AuraCgResolvedSceneAsset? Resolve(
        AuraCgSceneAssetReference reference,
        string roleId = "",
        string roleVariantId = "")
    {
        reference ??= new AuraCgSceneAssetReference();
        reference.Normalize();
        if (!reference.IsValid())
        {
            return null;
        }

        foreach (var resolver in resolvers)
        {
            if (!string.Equals(resolver.OwnerModId, reference.OwnerModId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resolved = resolver.Resolve(reference, roleId, roleVariantId);
            if (resolved != null)
            {
                return resolved;
            }
        }

        return null;
    }

    private sealed class ResolverHandle
    {
        private readonly object provider;
        private readonly MethodInfo? resolveMethod;
        private readonly bool acceptsParticipantContext;

        public ResolverHandle(object provider)
        {
            this.provider = provider;
            var type = provider.GetType();
            ProviderId = ReadString(type, provider, "ProviderId");
            OwnerModId = ReadString(type, provider, "OwnerModId");
            Priority = ReadInt(type, provider, "Priority");
            resolveMethod = type.GetMethod(
                "ResolveSceneAsset",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(string), typeof(string) },
                null);
            acceptsParticipantContext = resolveMethod != null;
            resolveMethod ??= type.GetMethod(
                "ResolveSceneAsset",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(string) },
                null);
        }

        public string ProviderId { get; }

        public string OwnerModId { get; }

        public int Priority { get; }

        public string QualifiedProviderId => OwnerModId + ":" + ProviderId;

        public bool IsValid => !string.IsNullOrWhiteSpace(ProviderId)
                               && !string.IsNullOrWhiteSpace(OwnerModId)
                               && resolveMethod != null;

        public AuraCgResolvedSceneAsset? Resolve(
            AuraCgSceneAssetReference reference,
            string roleId,
            string roleVariantId)
        {
            try
            {
                var arguments = acceptsParticipantContext
                    ? new object[] { reference.AssetId, roleId ?? "", roleVariantId ?? "" }
                    : new object[] { reference.AssetId };
                return AuraCgResolvedSceneAsset.FromObject(
                    resolveMethod?.Invoke(provider, arguments),
                    reference);
            }
            catch (Exception ex)
            {
                AuraCgLog.WarnOnce(
                    "scene-resolver-failed:" + QualifiedProviderId + ":" + reference.AssetId,
                    "CG scene asset resolver failed: provider=" + QualifiedProviderId
                    + ", asset=" + reference.AssetId
                    + ", error=" + ex.Message);
                return null;
            }
        }

        public static int Compare(ResolverHandle left, ResolverHandle right)
        {
            var priority = right.Priority.CompareTo(left.Priority);
            return priority != 0
                ? priority
                : string.Compare(left.QualifiedProviderId, right.QualifiedProviderId, StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(Type type, object source, string property)
        {
            try
            {
                return (type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) as string ?? "").Trim();
            }
            catch
            {
                return "";
            }
        }

        private static int ReadInt(Type type, object source, string property)
        {
            try
            {
                return type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source) is int value
                    ? value
                    : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
