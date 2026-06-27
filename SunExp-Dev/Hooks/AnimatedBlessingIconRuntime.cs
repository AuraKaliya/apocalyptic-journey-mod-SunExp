using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class AnimatedBlessingIconRuntime
{
    private const string TargetKind = "blessing-icon";
    private const string LogPrefix = "[AnimatedBlessingIcon]";

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BlessItem.Init", AttachFromContext);
        RegisterAfter(modConfig, "BlessingChoiceGenerator.CreateBlessUI", AttachFromContext);
        RegisterAfter(modConfig, "DictionaryUI.SetRelicDes", AttachFromContext);
        RegisterAfter(modConfig, "StatusUI.ShowBless", AttachFromContext);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Animated blessing icon " + message));
    }

    private static void AttachFromContext(ModHookContext context)
    {
        try
        {
            var visited = new HashSet<int>();
            TryAttachIn(context.Target, visited);

            foreach (var arg in context.Arguments ?? Array.Empty<object>())
            {
                TryAttachIn(arg, visited);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Animated blessing icon attach failed", ex);
        }
    }

    private static void TryAttachIn(object? value, HashSet<int> visited)
    {
        switch (value)
        {
            case Transform transform:
                AttachInTransform(transform, visited);
                break;
            case GameObject gameObject:
                AttachInTransform(gameObject.transform, visited);
                break;
            case UnityEngine.Component component:
                AttachInTransform(component.transform, visited);
                break;
        }
    }

    private static void AttachInTransform(Transform root, HashSet<int> visited)
    {
        if (!visited.Add(root.GetInstanceID()))
        {
            return;
        }

        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            var spec = VisualRegistry.FrameAnimationBySpriteName(image.sprite?.name, TargetKind);
            FrameAnimationAttacher.Attach(image, spec, LogPrefix);
        }
    }
}
