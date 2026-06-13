using System;
using System.Collections.Generic;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class AnimatedBlessingIconRuntime
{
    private const string FirstFrameSpriteName = "huanghun_1";
    private const float FrameSeconds = 0.2f;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "BlessItem.Init", AttachFromContext);
        RegisterAfter(modConfig, "BlessingChoiceGenerator.CreateBlessUI", AttachFromContext);
        RegisterAfter(modConfig, "DictionaryUI.SetRelicDes", AttachFromContext);
        RegisterAfter(modConfig, "StatusUI.ShowBless", AttachFromContext);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Animated blessing icon hook registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Animated blessing icon hook failed: " + target + " -> " + ex.Message);
        }
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
            if (!IsDuskBlessingIcon(image))
            {
                continue;
            }

            var animator = image.GetComponent<AnimatedBlessingIcon>() ?? image.gameObject.AddComponent<AnimatedBlessingIcon>();
            animator.Configure(FrameSeconds);
        }
    }

    private static bool IsDuskBlessingIcon(Image image)
    {
        var spriteName = image != null ? image.sprite?.name : null;
        return spriteName != null
            && spriteName.IndexOf(FirstFrameSpriteName, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

public sealed class AnimatedBlessingIcon : MonoBehaviour
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

    private Image? image;
    private float frameSeconds = 0.2f;
    private float elapsed;
    private int index;

    public void Configure(float seconds)
    {
        frameSeconds = Mathf.Max(0.05f, seconds);
        image ??= GetComponent<Image>();
        EnsureFrames();
        SetFrame(0);
        enabled = true;
    }

    private void Awake()
    {
        image = GetComponent<Image>();
    }

    private void OnEnable()
    {
        EnsureFrames();
        SetFrame(index);
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0 || image == null)
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
        if (frames == null || frames.Length == 0 || image == null)
        {
            return;
        }

        index = nextIndex % frames.Length;
        var frame = frames[index];
        if (frame != null)
        {
            image.sprite = frame;
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
