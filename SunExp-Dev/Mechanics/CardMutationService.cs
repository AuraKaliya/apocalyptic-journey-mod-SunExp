using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.UI.Window;

namespace SunExp.Dll.Mechanics;

public static class CardMutationService
{
    public static CardGrantMutation AddSpecialTagsMutation(params string[] tags)
    {
        return new CardGrantMutation("special-tags", config => AddSpecialTags(config, tags));
    }

    public static CardGrantMutation AddNativeTagsMutation(params string[] tags)
    {
        return new CardGrantMutation("native-tags", config => AddNativeTags(config, tags));
    }

    public static CardGrantMutation SetRuntimeMarkersMutation(params string[] markers)
    {
        return new CardGrantMutation("runtime-markers", config => SetRuntimeMarkers(config, markers));
    }

    public static CardGrantMutation SetTemporaryCostMutation(int targetCost)
    {
        return new CardGrantMutation("temporary-cost", config => SetTemporaryCost(config, targetCost));
    }

    public static CardGrantMutation MarkTemporaryWhiteRadianceMutation()
    {
        return new CardGrantMutation("temporary-white-radiance", MarkTemporaryWhiteRadiance);
    }

    public static bool AddSpecialTags(IDataConfig? config, params string[] tags)
    {
        if (config == null)
        {
            return false;
        }

        var changed = false;
        var existing = DictionaryUtil.Get(config.Vars, "SpecialTag");
        foreach (var tag in NormalizeTags(tags))
        {
            if (HasSpecialTag(config, tag))
            {
                continue;
            }

            if (!DictionaryUtil.ContainsToken(existing, tag))
            {
                existing = string.IsNullOrWhiteSpace(existing) ? tag : existing + "," + tag;
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        DictionaryUtil.Set(config.Vars, "SpecialTag", existing);
        RefreshDataConfigTags(config);
        return true;
    }

    public static bool AddSpecialTags(CardItem? card, params string[] tags)
    {
        if (card == null)
        {
            return false;
        }

        var changed = AddSpecialTags(card.dataConfig, tags);
        var existing = DictionaryUtil.Get(card.Vars, "SpecialTag");
        foreach (var tag in NormalizeTags(tags))
        {
            if (!DictionaryUtil.ContainsToken(existing, tag))
            {
                existing = string.IsNullOrWhiteSpace(existing) ? tag : existing + "," + tag;
                changed = true;
            }

            if (card.Tags != null && !card.Tags.Contains(tag))
            {
                card.Tags.Add(tag);
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        DictionaryUtil.Set(card.Vars, "SpecialTag", existing);
        RefreshCardItem(card);
        return true;
    }

    public static bool AddNativeTags(IDataConfig? config, params string[] tags)
    {
        if (config == null)
        {
            return false;
        }

        var changed = false;
        var existing = CurrentNativeTagText(config);
        foreach (var tag in NormalizeTags(tags))
        {
            if (DictionaryUtil.ContainsToken(existing, tag))
            {
                continue;
            }

            existing = string.IsNullOrWhiteSpace(existing) ? tag : existing + "," + tag;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        DictionaryUtil.Set(config.Vars, "Tag", existing);
        RefreshDataConfigTags(config);
        return true;
    }

    public static bool AddNativeTags(CardItem? card, params string[] tags)
    {
        if (card == null)
        {
            return false;
        }

        var changed = AddNativeTags(card.dataConfig, tags);
        var existing = CurrentNativeTagText(card.dataConfig);
        foreach (var tag in NormalizeTags(tags))
        {
            if (!DictionaryUtil.ContainsToken(existing, tag))
            {
                existing = string.IsNullOrWhiteSpace(existing) ? tag : existing + "," + tag;
                changed = true;
            }

            if (card.Tags != null && !card.Tags.Contains(tag))
            {
                card.Tags.Add(tag);
                changed = true;
            }
        }

        if (!changed)
        {
            return false;
        }

        DictionaryUtil.Set(card.Vars, "Tag", existing);
        RefreshCardItem(card);
        return true;
    }

    public static bool HasSpecialTag(IDataConfig? config, string tag)
    {
        return DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.Vars, "SpecialTag"), tag)
            || DictionaryUtil.ContainsToken(DictionaryUtil.Get(config?.data, "Tag"), tag);
    }

    public static bool HasSpecialTag(CardItem? card, string tag)
    {
        return card != null
            && (HasSpecialTag(card.dataConfig, tag)
                || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.data, "Tag"), tag)
                || DictionaryUtil.ContainsToken(DictionaryUtil.Get(card.Vars, "SpecialTag"), tag)
                || card.Tags?.Contains(tag) == true);
    }

    public static bool SetRuntimeMarkers(IDataConfig? config, params string[] markers)
    {
        if (config == null)
        {
            return false;
        }

        var changed = false;
        var existing = DictionaryUtil.Get(config.Vars, SunExpIds.RuntimeMarkersKey);
        foreach (var marker in NormalizeTags(markers))
        {
            if (DictionaryUtil.ContainsToken(existing, marker))
            {
                continue;
            }

            existing = string.IsNullOrWhiteSpace(existing) ? marker : existing + "," + marker;
            changed = true;
        }

        if (changed)
        {
            DictionaryUtil.Set(config.Vars, SunExpIds.RuntimeMarkersKey, existing);
        }

        return changed;
    }

    public static bool HasRuntimeMarker(IDataConfig? config, string marker)
    {
        return DictionaryUtil.ContainsToken(
            DictionaryUtil.Get(config?.Vars, SunExpIds.RuntimeMarkersKey),
            marker);
    }

    public static void SetTemporaryCost(IDataConfig config, int targetCost)
    {
        var baseCost = CardConfigApi.BaseCost(config);
        DictionaryUtil.Set(config.Vars, "TotalExCost", (Math.Max(0, targetCost) - baseCost).ToString());
    }

    public static void AdjustOnceCost(IDataConfig config, int delta)
    {
        var oldOnce = DictionaryUtil.GetInt(config.Vars, "OnceExCost");
        DictionaryUtil.Set(config.Vars, "OnceExCost", (oldOnce + delta).ToString());
    }

    public static void MakeCurrentUseFree(IDataConfig config)
    {
        var currentCost = CardConfigApi.CurrentCost(config);
        if (currentCost > 0)
        {
            AdjustOnceCost(config, -currentCost);
        }
    }

    public static int ReduceCurrentCostBy(IDataConfig config, int amount)
    {
        var consumed = Math.Min(Math.Max(0, amount), CardConfigApi.CurrentCost(config));
        if (consumed > 0)
        {
            AdjustOnceCost(config, -consumed);
        }

        return consumed;
    }

    public static void MarkTemporaryWhiteRadiance(IDataConfig config)
    {
        RuntimeCardAttachmentService.AttachToConfig(
            config,
            new RuntimeCardAttachment(
                specialTags: new[] { SunExpIds.WhiteRadianceTag },
                markers: new[] { SunExpIds.TempWhiteRadiance },
                temporaryWhiteRadiance: true));
    }

    private static IEnumerable<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.Ordinal);
    }

    private static string CurrentNativeTagText(IDataConfig? config)
    {
        var existing = DictionaryUtil.Get(config?.Vars, "Tag");
        return string.IsNullOrWhiteSpace(existing)
            ? DictionaryUtil.Get(config?.data, "Tag")
            : existing;
    }

    private static void RefreshDataConfigTags(IDataConfig config)
    {
        try
        {
            FightCardManager.Instance?.RefreshTag(config);
        }
        catch
        {
            // Tag refresh is presentation-only for deck/discard configs.
        }
    }

    private static void RefreshCardItem(CardItem card)
    {
        try
        {
            card.RefreshTag();
            card.DataUpdate();
            if (card.dataConfig != null)
            {
                FightCardManager.Instance?.RefreshTag(card.dataConfig);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Card item refresh skipped: " + ex.Message);
        }
    }
}
