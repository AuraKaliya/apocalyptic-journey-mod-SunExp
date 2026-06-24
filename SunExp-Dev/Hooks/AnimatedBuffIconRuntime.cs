using System;
using System.Collections.Generic;
using System.Reflection;
using AuraShared.Core;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class AnimatedBuffIconRuntime
{
    private const float FrameSeconds = 0.2f;

    private static readonly string[] DuskFramePaths =
    {
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_1",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_2",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_3",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_4",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_3",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_2",
    };

    private static readonly string[] StarClayDollFramePaths =
    {
        "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_1",
        "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_2",
        "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_3",
        "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_4",
        "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_3",
        "Mods/SunExp/ModResource/Images/Buff/Loneer/renkui_2",
    };

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BuffItem.Init", AttachFromContext);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, message => SunExpLog.Warn("Animated buff icon " + message));
    }

    private static void AttachFromContext(ModHookContext context)
    {
        try
        {
            var buffId = GetBuffId(context.Target);
            var framePaths = GetFramePaths(buffId);
            if (framePaths == null)
            {
                return;
            }

            if (context.Target is not UnityEngine.Component component)
            {
                return;
            }

            var image = component.transform.Find("Content/Image")?.GetComponent<SpriteRenderer>();
            if (image == null)
            {
                return;
            }

            var animator = image.GetComponent<AnimatedBuffSpriteIcon>() ?? image.gameObject.AddComponent<AnimatedBuffSpriteIcon>();
            animator.Configure(framePaths, FrameSeconds);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Animated buff icon attach failed", ex);
        }
    }

    private static string[]? GetFramePaths(string buffId)
    {
        if (buffId == SunExpIds.DuskAfterheatRecoveryTrait)
        {
            return DuskFramePaths;
        }

        if (buffId == SunExpIds.StarClayBody || buffId == SunExpIds.StarClayDollTrait)
        {
            return StarClayDollFramePaths;
        }

        return null;
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

public sealed class AnimatedBuffSpriteIcon : MonoBehaviour
{
    private string[] framePaths = Array.Empty<string>();
    private Sprite[]? frames;
    private SpriteRenderer? spriteRenderer;
    private float frameSeconds = 0.2f;
    private float elapsed;
    private int index;

    public void Configure(string[] paths, float seconds)
    {
        framePaths = paths;
        frames = null;
        elapsed = 0f;
        index = 0;
        frameSeconds = Mathf.Max(0.05f, seconds);
        spriteRenderer ??= GetComponent<SpriteRenderer>();
        EnsureFrames();
        SetFrame(0);
        enabled = true;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnEnable()
    {
        EnsureFrames();
        SetFrame(index);
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0 || spriteRenderer == null)
        {
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        if (elapsed < frameSeconds)
        {
            return;
        }

        elapsed -= frameSeconds;
        SetFrame(index + 1);
    }

    private void SetFrame(int nextIndex)
    {
        if (frames == null || frames.Length == 0 || spriteRenderer == null)
        {
            return;
        }

        index = nextIndex % frames.Length;
        var frame = frames[index];
        if (frame != null)
        {
            spriteRenderer.sprite = frame;
        }
    }

    private void EnsureFrames()
    {
        if (frames != null)
        {
            return;
        }

        var loaded = new List<Sprite>();
        foreach (var path in framePaths)
        {
            var sprite = ResourceLoader.Load<Sprite>(path, true);
            if (sprite != null)
            {
                loaded.Add(sprite);
            }
        }

        frames = loaded.ToArray();
    }
}
