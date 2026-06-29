using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class MapNodeCardArtRuntime
{
    private static readonly Dictionary<MapItem, MapItemIconBaseline> Baselines =
        new(ReferenceComparer<MapItem>.Instance);

    public static void Initialize(ModConfig modConfig)
    {
        RegisterBefore(modConfig, "MapItem.Init", CaptureMapItemBaseline);
        RegisterAfter(modConfig, "MapItem.Init", ApplyMapNodeCardArt);
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Map node card art " + message));
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Map node card art " + message));
    }

    private static void CaptureMapItemBaseline(ModHookContext context)
    {
        try
        {
            if (!IsConfiguredNode(context, out _) || context.Target is not MapItem item)
            {
                return;
            }

            if (MapItemApi.TryCaptureIconBaseline(item, out var baseline))
            {
                Baselines[item] = baseline;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[MapNodeCardArt] baseline capture failed: " + ex.Message);
        }
    }

    private static void ApplyMapNodeCardArt(ModHookContext context)
    {
        try
        {
            if (!IsConfiguredNode(context, out var spec) || context.Target is not MapItem item || spec == null)
            {
                return;
            }

            Baselines.TryGetValue(item, out var baseline);
            Baselines.Remove(item);

            var texture = SunExpResourceCache.Load<Texture>(spec.TexturePath, true);
            if (texture == null)
            {
                SunExpLog.Warn("[MapNodeCardArt] texture missing: " + spec.TexturePath);
                return;
            }

            if (!MapItemApi.ApplyTexture(item, texture, spec, baseline))
            {
                SunExpLog.Warn("[MapNodeCardArt] skipped: Front/icon missing for " + spec.TexturePath);
                return;
            }

            SunExpLog.Info("[MapNodeCardArt] applied texture: " + spec.TexturePath);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Map node card art apply failed", ex);
        }
    }

    private static bool IsConfiguredNode(ModHookContext context, out MapNodeCardArtSpec? spec)
    {
        spec = null;
        if (context.Arguments == null
            || context.Arguments.Length == 0
            || context.Arguments[0] is not MapTree.Node node)
        {
            return false;
        }

        spec = MapNodeCardArtRegistry.Resolve(node.data);
        return spec != null;
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? left, T? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(T value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
