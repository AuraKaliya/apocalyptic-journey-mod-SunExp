using System;
using System.Reflection;
using AuraShared.Core;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class AnimatedBuffIconRuntime
{
    private const string TargetKind = "buff-icon";
    private const string LogPrefix = "[AnimatedBuffIcon]";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BuffItem.Init", AttachFromContext);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        SunExpHookRegistry.After(config, target, action, "AnimatedBuffIcon");
    }

    private static void AttachFromContext(ModHookContext context)
    {
        try
        {
            var spec = VisualRegistry.FrameAnimationByMatchId(GetBuffId(context.Target), TargetKind);
            if (spec == null || context.Target is not UnityEngine.Component component)
            {
                return;
            }

            var image = component.transform.Find("Content/Image")?.GetComponent<SpriteRenderer>();
            FrameAnimationAttacher.Attach(image, spec, LogPrefix);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Animated buff icon attach failed", ex);
        }
    }

    private static string GetBuffId(object? target)
    {
        var config = ReadMember(target, "buffConfig");
        return Convert.ToString(ReadMember(config, "BuffId")) ?? "";
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
