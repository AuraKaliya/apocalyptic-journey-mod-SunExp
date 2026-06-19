using System;
using System.Collections.Generic;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class AnimatedEnemyDictIconRuntime
{
    private const string SaintWunaEnemyId = "SunExp_sunexp_boss_saint_wuna";
    private const string SecondSunEnemyId = "SunExp_sunexp_boss_second_sun_last_day";
    private const float SaintWunaFrameSeconds = 0.2f;
    private const float SecondSunFrameSeconds = 0.2f;

    private static readonly Dictionary<string, EnemyDictAnimationSpec> Specs = new(StringComparer.OrdinalIgnoreCase)
    {
        [SaintWunaEnemyId] = new(
            SaintWunaFrameSeconds,
            "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Dict/Dict_00",
            "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Dict/Dict_01",
            "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Dict/Dict_02",
            "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Dict/Dict_03",
            "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Dict/Dict_04",
            "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Dict/Dict_05",
            "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Dict/Dict_06",
            "Mods/SunExp/ModResource/AnimationLib/WuNa_e/Dict/Dict_07"),
        [SecondSunEnemyId] = new(
            SecondSunFrameSeconds,
            "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Dict/Dict_00",
            "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Dict/Dict_01",
            "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Dict/Dict_02",
            "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Dict/Dict_03",
            "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Dict/Dict_04",
            "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Dict/Dict_05",
            "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Dict/Dict_06",
            "Mods/SunExp/ModResource/AnimationLib/SecondSunWeel_e/Dict/Dict_07"),
    };

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "EnemyItem.Init", AttachFromContext);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Animated enemy dictionary icon hook registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Animated enemy dictionary icon hook failed: " + target + " -> " + ex.Message);
        }
    }

    private static void AttachFromContext(ModHookContext context)
    {
        try
        {
            if (!Specs.TryGetValue(DataConfigId(context.Target), out var spec))
            {
                return;
            }

            if (context.Target is not UnityEngine.Component component)
            {
                return;
            }

            var image = component.transform.Find("Rect/Rect/Role")?.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            var animator = image.GetComponent<AnimatedEnemyDictIcon>() ?? image.gameObject.AddComponent<AnimatedEnemyDictIcon>();
            animator.Configure(spec.FrameSeconds, spec.FramePaths);
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

    private sealed class EnemyDictAnimationSpec
    {
        public EnemyDictAnimationSpec(float frameSeconds, params string[] framePaths)
        {
            FrameSeconds = frameSeconds;
            FramePaths = framePaths;
        }

        public float FrameSeconds { get; }

        public string[] FramePaths { get; }
    }
}

public sealed class AnimatedEnemyDictIcon : MonoBehaviour
{
    private Sprite[]? frames;
    private Image? image;
    private float frameSeconds = 0.2f;
    private float elapsed;
    private int index;

    public void Configure(float seconds, IReadOnlyList<string> framePaths)
    {
        frameSeconds = Mathf.Max(0.05f, seconds);
        image ??= GetComponent<Image>();
        frames = LoadFrames(framePaths);
        elapsed = 0f;
        SetFrame(0);
        enabled = frames.Length > 1;
    }

    private void Awake()
    {
        image = GetComponent<Image>();
    }

    private void OnEnable()
    {
        if (frames is { Length: > 0 })
        {
            SetFrame(index);
        }
    }

    private void Update()
    {
        if (frames == null || frames.Length <= 1 || image == null)
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

    private static Sprite[] LoadFrames(IReadOnlyList<string> framePaths)
    {
        var loaded = new List<Sprite>();
        foreach (var path in framePaths)
        {
            var sprite = ResourceLoader.Load<Sprite>(path, true);
            if (sprite != null)
            {
                loaded.Add(sprite);
            }
        }

        return loaded.ToArray();
    }
}
