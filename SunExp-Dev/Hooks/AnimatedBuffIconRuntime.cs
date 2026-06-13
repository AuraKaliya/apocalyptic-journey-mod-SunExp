using System;
using System.Collections.Generic;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class AnimatedBuffIconRuntime
{
    private const float DuskFrameSeconds = 0.2f;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BuffItem.Init", AttachFromContext);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Animated buff icon hook registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Animated buff icon hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static void AttachFromContext(ModHookContext context)
    {
        try
        {
            var buffId = GetBuffId(context.Target);
            if (buffId != SunExpIds.DuskAfterheatRecoveryTrait)
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
            animator.Configure(DuskFrameSeconds);
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

public sealed class AnimatedBuffSpriteIcon : MonoBehaviour
{
    private static readonly string[] FramePaths =
    {
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_1",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_2",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_3",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_4",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_3",
        "Mods/SunExp/ModResource/Images/Buff/SunExp/huanghun_2",
    };

    private static Sprite[]? frames;

    private SpriteRenderer? spriteRenderer;
    private float frameSeconds = 0.2f;
    private float elapsed;
    private int index;

    public void Configure(float seconds)
    {
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

    private static void EnsureFrames()
    {
        if (frames != null)
        {
            return;
        }

        var loaded = new List<Sprite>();
        foreach (var path in FramePaths)
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
