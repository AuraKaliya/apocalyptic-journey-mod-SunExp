using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Infrastructure;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Projects recorded enemy intent visuals onto the native intent slots. This class never
/// calls OtherObj.SetAction, ObjectAction.ActionExecute, ObjectCard.UseCard, or scripts.
/// </summary>
internal static class MatchReplayEnemyIntentPresenter
{
    private const float AnnouncementDurationMilliseconds = 680f;
    private const int MaximumNativeSlots = 4;
    private static readonly Dictionary<string, Sprite> SpriteCache = new(StringComparer.Ordinal);
    private static ActiveAnnouncement? activeAnnouncement;

    internal static void Project(IEnumerable<MatchReplayEnemyIntentState>? source)
    {
        var manager = FightManager.Instance;
        if (manager?.statuses == null)
        {
            return;
        }

        var plans = (source ?? Enumerable.Empty<MatchReplayEnemyIntentState>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ActorId))
            .GroupBy(item => item.ActorId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.SlotIndex).ToList(),
                StringComparer.Ordinal);
        foreach (var status in manager.statuses.Values.Where(item => item?.fatherObject is Enemy))
        {
            plans.TryGetValue(status.InstanceId ?? "", out var intents);
            Project(status, intents ?? new List<MatchReplayEnemyIntentState>());
        }
    }

    internal static void Play(MatchReplayActionFrame frame)
    {
        var intent = frame?.IntentPresentation;
        var manager = FightManager.Instance;
        if (intent == null
            || manager == null
            || !manager.statuses.TryGetValue(intent.ActorId ?? "", out var actor)
            || actor?.fatherObject is not Enemy enemy)
        {
            return;
        }

        ClearAnnouncement();
        var slot = Slot(actor, intent.SlotIndex);
        if (slot != null)
        {
            slot.SetActive(true);
            slot.transform.localScale = Vector3.one * 1.12f;
            SetSlotAlpha(slot, 1f);
        }

        var message = enemy.transform.Find("head/Msg")?.gameObject;
        var messageText = message?.transform.Find("val")?.GetComponent<TMP_Text>();
        var messageBackground = message?.GetComponent<SpriteRenderer>();
        if (message != null)
        {
            message.SetActive(true);
        }

        if (messageText != null)
        {
            messageText.SetText(string.IsNullOrWhiteSpace(intent.Label) ? intent.IntentId : intent.Label);
            SetAlpha(messageText, 1f);
        }

        if (messageBackground != null)
        {
            SetAlpha(messageBackground, 1f);
        }

        activeAnnouncement = new ActiveAnnouncement
        {
            Slot = slot,
            Message = message,
            MessageText = messageText,
            MessageBackground = messageBackground
        };
    }

    internal static void Tick(float deltaMilliseconds)
    {
        var active = activeAnnouncement;
        if (active == null)
        {
            return;
        }

        active.ElapsedMilliseconds += Math.Max(0f, deltaMilliseconds);
        var progress = Mathf.Clamp01(active.ElapsedMilliseconds / AnnouncementDurationMilliseconds);
        if (active.Slot != null)
        {
            var pulse = 1f + Mathf.Sin(Mathf.Clamp01(progress / 0.35f) * Mathf.PI) * 0.12f;
            active.Slot.transform.localScale = Vector3.one * pulse;
            if (progress >= 0.36f)
            {
                SetSlotAlpha(active.Slot, Mathf.Clamp01(1f - (progress - 0.36f) / 0.25f));
            }
        }

        if (progress >= 0.58f)
        {
            var alpha = Mathf.Clamp01(1f - (progress - 0.58f) / 0.42f);
            SetAlpha(active.MessageText, alpha);
            SetAlpha(active.MessageBackground, alpha);
        }

        if (progress >= 1f)
        {
            ClearAnnouncement();
        }
    }

    internal static void Reset()
    {
        ClearAnnouncement();
        var manager = FightManager.Instance;
        if (manager?.statuses == null)
        {
            return;
        }

        foreach (var status in manager.statuses.Values.Where(item => item?.fatherObject is Enemy))
        {
            Project(status, new List<MatchReplayEnemyIntentState>());
        }
    }

    private static void Project(StatusManager status, IReadOnlyList<MatchReplayEnemyIntentState> intents)
    {
        var slots = status.actionObj;
        if (slots == null)
        {
            if (intents.Count > 0) throw new InvalidOperationException("Native enemy intent slots are unavailable.");
            return;
        }

        if (intents.Any(item => item.SlotIndex < 0 || item.SlotIndex >= Math.Min(MaximumNativeSlots, slots.Length)))
            throw new InvalidOperationException("Recorded enemy intent slot exceeds the native view capacity.");

        var bySlot = intents
            .Where(item => item.SlotIndex >= 0 && item.SlotIndex < Math.Min(MaximumNativeSlots, slots.Length))
            .GroupBy(item => item.SlotIndex)
            .ToDictionary(group => group.Key, group => group.Last());
        for (var index = 0; index < Math.Min(MaximumNativeSlots, slots.Length); index++)
        {
            var slot = slots[index];
            if (slot == null)
            {
                continue;
            }

            if (!bySlot.TryGetValue(index, out var intent))
            {
                slot.SetActive(false);
                if (status.actionText != null && index < status.actionText.Length && status.actionText[index] != null)
                {
                    status.actionText[index].SetText("", "", new List<string>(), type: "Action");
                }
                continue;
            }

            try
            {
                ConfigureSlot(status, slot, index, intent);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Native enemy intent projection failed at slot " + index + ".", ex);
            }
        }
    }

    private static void ConfigureSlot(
        StatusManager status,
        GameObject slot,
        int index,
        MatchReplayEnemyIntentState intent)
    {
        slot.SetActive(true);
        slot.transform.localScale = Vector3.one;
        SetSlotAlpha(slot, 1f);

        var background = slot.transform.Find("Icon")?.GetComponent<Image>();
        var icon = slot.transform.Find("Icon/child")?.GetComponent<Image>();
        var value = slot.transform.Find("Icon/val")?.GetComponent<TMP_Text>();
        var backgroundSprite = LoadSprite(intent.BackIcon) ?? LoadSprite("Icon/ActionIcon/攻击底");
        var iconSprite = LoadSprite(intent.Icon) ?? LoadSprite("Icon/ActionIcon/蓄力");
        if (background != null && backgroundSprite != null)
        {
            background.sprite = backgroundSprite;
        }
        if (icon != null && iconSprite != null)
        {
            icon.sprite = iconSprite;
            icon.SetNativeSize();
        }
        if (value != null)
        {
            value.SetText(intent.DisplayValue ?? "");
        }

        if (status.actionText != null && index < status.actionText.Length && status.actionText[index] != null)
        {
            status.actionText[index].SetText(
                string.IsNullOrWhiteSpace(intent.Label) ? intent.IntentId : intent.Label,
                intent.Description ?? "",
                new List<string>(),
                type: "Action");
        }
    }

    private static Sprite? LoadSprite(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalized = path!.Trim();
        if (SpriteCache.TryGetValue(normalized, out var cached) && cached != null)
        {
            return cached;
        }

        var loaded = ResourceLoader.Load<Sprite>(normalized);
        if (loaded != null)
        {
            SpriteCache[normalized] = loaded;
        }
        return loaded;
    }

    private static GameObject? Slot(StatusManager status, int index)
    {
        return status.actionObj != null && index >= 0 && index < status.actionObj.Length
            ? status.actionObj[index]
            : null;
    }

    private static void SetSlotAlpha(GameObject slot, float alpha)
    {
        SetAlpha(slot.transform.Find("Icon")?.GetComponent<Image>(), alpha);
        SetAlpha(slot.transform.Find("Icon/child")?.GetComponent<Image>(), alpha);
        SetAlpha(slot.transform.Find("Icon/val")?.GetComponent<TMP_Text>(), alpha);
    }

    private static void SetAlpha(Graphic? graphic, float alpha)
    {
        if (graphic == null)
        {
            return;
        }

        var color = graphic.color;
        color.a = Mathf.Clamp01(alpha);
        graphic.color = color;
    }

    private static void SetAlpha(SpriteRenderer? renderer, float alpha)
    {
        if (renderer == null)
        {
            return;
        }

        var color = renderer.color;
        color.a = Mathf.Clamp01(alpha);
        renderer.color = color;
    }

    private static void ClearAnnouncement()
    {
        var active = activeAnnouncement;
        activeAnnouncement = null;
        if (active == null)
        {
            return;
        }

        if (active.Slot != null)
        {
            active.Slot.transform.localScale = Vector3.one;
            SetSlotAlpha(active.Slot, 1f);
        }
        SetAlpha(active.MessageText, 0f);
        SetAlpha(active.MessageBackground, 0f);
        if (active.Message != null)
        {
            active.Message.SetActive(false);
        }
    }

    private sealed class ActiveAnnouncement
    {
        internal GameObject? Slot { get; set; }
        internal GameObject? Message { get; set; }
        internal TMP_Text? MessageText { get; set; }
        internal SpriteRenderer? MessageBackground { get; set; }
        internal float ElapsedMilliseconds { get; set; }
    }
}
