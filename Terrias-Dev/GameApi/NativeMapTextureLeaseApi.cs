using System;
using UnityEngine;

namespace Terrias.Dll.GameApi;

/// <summary>
/// Holds one host-resolved native map texture for presentation-only custom
/// building nodes. MapItem uses ResourceLoader and may otherwise lose its only
/// live Resources reference after Terrias replaces the rendered material.
/// </summary>
public static class NativeMapTextureLeaseApi
{
    private const string CacheCategory = "DimensionShop.NativeMapPresentation";
    private static readonly object Gate = new();
    private static readonly Candidate[] Candidates =
    {
        new("shop", "Icon/Map/旅行商人"),
        new("tree", "Icon/Map/天界赐福"),
        new("ench", "Icon/Map/血脉铭刻"),
        new("Breaks", "Icon/Map/建筑  一息安隅"),
        new("TerriasPresentationFallback", "Icon/Map/建筑  新的起点")
    };

    private static Texture2D? retainedTexture;
    private static string retainedNodeId = "";
    private static string retainedPath = "";

    public static bool TryEnsurePresentationTexture(
        out string nativeNodeId,
        out string diagnostic)
    {
        lock (Gate)
        {
            if (retainedTexture != null
                && !string.IsNullOrWhiteSpace(retainedNodeId))
            {
                nativeNodeId = retainedNodeId;
                diagnostic = "native map texture retained through ResourceLoader: "
                             + retainedPath;
                return true;
            }

            // A cached Unity object can compare equal to null after Unity has
            // destroyed it. Clear only this Terrias-owned category and resolve
            // again through the host loader instead of bypassing it.
            TerriasResourceCache.ClearCategory(CacheCategory);
            foreach (var candidate in Candidates)
            {
                var texture = TerriasResourceCache.Load<Texture2D>(
                    candidate.Path,
                    loadFromMod: true,
                    category: CacheCategory);
                if (texture == null)
                {
                    continue;
                }

                retainedTexture = texture;
                retainedNodeId = candidate.NodeId;
                retainedPath = candidate.Path;
                nativeNodeId = retainedNodeId;
                diagnostic = "native map texture retained through ResourceLoader: "
                             + retainedPath;
                return true;
            }

            retainedTexture = null;
            retainedNodeId = "";
            retainedPath = "";
            nativeNodeId = "";
            diagnostic = "no native Build presentation texture could be resolved; "
                         + "the custom map candidate will be hidden for safety";
            return false;
        }
    }

    private sealed class Candidate
    {
        public Candidate(string nodeId, string path)
        {
            NodeId = nodeId;
            Path = path;
        }

        public string NodeId { get; }

        public string Path { get; }
    }
}
