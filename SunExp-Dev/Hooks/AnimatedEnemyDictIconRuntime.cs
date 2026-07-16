using System;
using System.Collections.Generic;
using System.Reflection;
using AuraShared.Core;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class AnimatedEnemyDictIconRuntime
{
    private const string TargetKind = "enemy-dictionary-icon";
    private const string LogPrefix = "[AnimatedEnemyDictIcon]";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "EnemyItem.Init", AttachFromContext);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "AnimatedEnemyDictIcon");
    }

    private static void AttachFromContext(ModHookContext context)
    {
        try
        {
            var spec = VisualRegistry.FrameAnimationByMatchId(DataConfigId(context.Target), TargetKind);
            if (spec == null || context.Target is not UnityEngine.Component component)
            {
                return;
            }

            var image = component.transform.Find("Rect/Rect/Role")?.GetComponent<Image>();
            FrameAnimationAttacher.Attach(image, spec, LogPrefix);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Animated enemy dictionary icon attach failed", ex);
        }
    }

    private static string DataConfigId(object? target)
    {
        var config = ReadMember(target, "dataConfig");
        var data = ReadMember(config, "data");
        if (data is IDictionary<string, string> values && values.TryGetValue("Id", out var id))
        {
            return id;
        }

        return "";
    }

    private static object? ReadMember(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return target.GetType().GetProperty(name, flags)?.GetValue(target)
            ?? target.GetType().GetField(name, flags)?.GetValue(target);
    }
}
