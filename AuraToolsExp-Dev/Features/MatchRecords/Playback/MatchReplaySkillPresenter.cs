using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.Core;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Projects recorded career skills into the native FightUI skill slots. SkillItem.Init is
/// intentionally never called because it executes InitScript and registers combat input.
/// </summary>
internal static class MatchReplaySkillPresenter
{
    private const float PulseDurationMilliseconds = 640f;
    private static readonly Dictionary<int, SkillSlot> Slots = new();
    private static SkillSlot? active;
    private static float elapsedMilliseconds;

    internal static void Initialize(FightUI fightUi)
    {
        Slots.Clear();
        active = null;
        elapsedMilliseconds = 0f;
        if (fightUi == null || RoleTable.Instance?.Career?.data == null)
        {
            return;
        }

        var career = RoleTable.Instance.Career.data;
        var hasFirstSkill = !string.IsNullOrWhiteSpace(Read(career, "ActionImage1"));
        var usesTwoSkills = !string.IsNullOrWhiteSpace(Read(career, "ActionImage2"));
        var oneSkillRoot = fightUi.transform.Find("Left/Skill1");
        var twoSkillRoot = fightUi.transform.Find("Left/Skill2");
        if (oneSkillRoot != null) oneSkillRoot.gameObject.SetActive(hasFirstSkill && !usesTwoSkills);
        if (twoSkillRoot != null) twoSkillRoot.gameObject.SetActive(usesTwoSkills);

        if (usesTwoSkills)
        {
            ConfigureSlot(twoSkillRoot?.Find("Skill1"), 1, career);
            ConfigureSlot(twoSkillRoot?.Find("Skill2"), 2, career);
        }
        else
        {
            ConfigureSlot(oneSkillRoot, 1, career);
        }
    }

    internal static void Play(MatchReplayActionFrame frame)
    {
        if (frame?.SourcePresentation == null)
        {
            return;
        }

        var slot = ResolveSlot(frame.SourceId);
        if (slot == null)
        {
            AuraToolsLog.Debug("[MatchRecords] replay skill slot unavailable: skill=" + frame.SourceId);
            return;
        }

        RestoreActive();
        active = slot;
        elapsedMilliseconds = 0f;
        slot.Root.gameObject.SetActive(true);
        if (slot.Icon != null)
        {
            var recordedIcon = Value(frame.SourcePresentation.Data, "Icon");
            if (slot.Icon.sprite == null && !string.IsNullOrWhiteSpace(recordedIcon))
            {
                slot.Icon.sprite = ResourceLoader.Load<Sprite>(recordedIcon);
            }

            slot.Icon.color = Color.white;
        }

        UpdateCooldown(slot, frame.SourcePresentation);
    }

    internal static void Tick(float deltaMilliseconds)
    {
        if (active == null || active.Root == null)
        {
            return;
        }

        elapsedMilliseconds += Math.Max(0f, deltaMilliseconds);
        var progress = Mathf.Clamp01(elapsedMilliseconds / PulseDurationMilliseconds);
        var pulse = progress < 0.35f
            ? Mathf.Lerp(1f, 1.2f, EaseOut(progress / 0.35f))
            : Mathf.Lerp(1.2f, 1f, EaseOut((progress - 0.35f) / 0.65f));
        active.Root.localScale = active.BaseScale * pulse;
        if (progress >= 1f)
        {
            RestoreActive();
        }
    }

    internal static void ResetAnimation()
    {
        RestoreActive();
    }

    private static void ConfigureSlot(
        Transform? root,
        int index,
        IDictionary<string, string> career)
    {
        if (root == null)
        {
            return;
        }

        var skillId = Read(career, "Skill" + index);
        var iconPath = Read(career, "ActionImage" + index);
        var icon = root.Find("Icon")?.GetComponent<Image>();
        var configured = !string.IsNullOrWhiteSpace(skillId);
        root.gameObject.SetActive(configured);
        if (icon != null)
        {
            icon.gameObject.SetActive(configured);
            if (configured && !string.IsNullOrWhiteSpace(iconPath))
            {
                icon.sprite = ResourceLoader.Load<Sprite>(iconPath);
            }

            icon.enabled = configured;
        }

        foreach (var skillItem in root.GetComponentsInChildren<SkillItem>(includeInactive: true))
        {
            skillItem.enabled = false;
        }

        foreach (var trigger in root.GetComponentsInChildren<EventTrigger>(includeInactive: true))
        {
            trigger.enabled = false;
        }

        foreach (var selectable in root.GetComponentsInChildren<Selectable>(includeInactive: true))
        {
            selectable.enabled = false;
        }

        foreach (var graphic in root.GetComponentsInChildren<Graphic>(includeInactive: true))
        {
            graphic.raycastTarget = false;
        }

        if (!configured)
        {
            return;
        }

        Slots[index] = new SkillSlot
        {
            Index = index,
            SkillIds = SplitSkillIds(skillId),
            Root = root,
            Icon = icon,
            BaseScale = root.localScale,
            BaseColor = icon?.color ?? Color.white
        };
    }

    private static SkillSlot? ResolveSlot(string skillId)
    {
        var normalized = (skillId ?? "").Trim();
        return Slots.Values.FirstOrDefault(slot => slot.SkillIds.Contains(normalized))
               ?? Slots.Values.OrderBy(slot => slot.Index).FirstOrDefault();
    }

    private static void UpdateCooldown(SkillSlot slot, MatchReplayCardState source)
    {
        var cooldownText = Value(source.Vars, "DesVal1");
        var cooldown = int.TryParse(
            cooldownText,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? Math.Max(0, parsed)
            : 0;
        var cooldownRoot = slot.Root.Find("CD");
        if (cooldownRoot == null)
        {
            return;
        }

        cooldownRoot.gameObject.SetActive(cooldown > 0);
        var value = cooldownRoot.Find("val")?.GetComponent<TMP_Text>();
        if (value != null)
        {
            value.SetText(cooldown.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void RestoreActive()
    {
        if (active != null && active.Root != null)
        {
            active.Root.localScale = active.BaseScale;
            if (active.Icon != null)
            {
                active.Icon.color = active.BaseColor;
            }
        }

        active = null;
        elapsedMilliseconds = 0f;
    }

    private static HashSet<string> SplitSkillIds(string value)
    {
        return new HashSet<string>(
            (value ?? "")
            .Split(new[] { ',', ';', '|', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0),
            StringComparer.Ordinal);
    }

    private static string Read(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }

    private static string Value(IEnumerable<MatchReplayStringValue>? values, string key)
    {
        return values?.LastOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))?.Value ?? "";
    }

    private static float EaseOut(float value)
    {
        value = Mathf.Clamp01(value);
        return 1f - Mathf.Pow(1f - value, 3f);
    }

    private sealed class SkillSlot
    {
        internal int Index { get; set; }
        internal HashSet<string> SkillIds { get; set; } = new(StringComparer.Ordinal);
        internal Transform Root { get; set; } = null!;
        internal Image? Icon { get; set; }
        internal Vector3 BaseScale { get; set; }
        internal Color BaseColor { get; set; }
    }
}
