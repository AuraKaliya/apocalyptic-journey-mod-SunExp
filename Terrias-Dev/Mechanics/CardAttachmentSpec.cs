using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Witch.UI.Window;

namespace Terrias.Dll.Mechanics;

public enum CardAttachmentScope
{
    DisplayPreview,
    GrantOnly,
    RunPermanent,
    BattleTemporary
}

public sealed class CardAttachmentSpec
{
    public CardAttachmentSpec(
        IEnumerable<string>? nativeTags = null,
        IEnumerable<string>? specialTags = null,
        IEnumerable<string>? markers = null,
        CardAttachmentScope scope = CardAttachmentScope.RunPermanent,
        bool temporaryWhiteRadiance = false)
    {
        NativeTags = Normalize(nativeTags).ToArray();
        SpecialTags = Normalize(specialTags).ToArray();
        Markers = Normalize(markers).ToArray();
        Scope = scope;
        TemporaryWhiteRadiance = temporaryWhiteRadiance;
    }

    public IReadOnlyList<string> NativeTags { get; }

    public IReadOnlyList<string> SpecialTags { get; }

    public IReadOnlyList<string> Markers { get; }

    public CardAttachmentScope Scope { get; }

    public bool TemporaryWhiteRadiance { get; }

    public RuntimeCardAttachment ToRuntimeAttachment()
    {
        return new RuntimeCardAttachment(
            NativeTags,
            SpecialTags,
            Markers,
            TemporaryWhiteRadiance);
    }

    private static IEnumerable<string> Normalize(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal);
    }
}

public static class CardAttachmentService
{
    public static int AttachToConfig(IDataConfig? config, CardAttachmentSpec? spec, string source)
    {
        if (config == null || spec == null)
        {
            return 0;
        }

        var changed = RuntimeCardAttachmentService.AttachToConfig(config, spec.ToRuntimeAttachment());
        if (changed > 0)
        {
            TerriasLog.Debug("[CardAttachment] attached from "
                + source
                + "; scope="
                + spec.Scope
                + "; native="
                + string.Join("|", spec.NativeTags)
                + "; changed="
                + changed);
        }

        return changed;
    }

    public static int AttachToCardItem(CardItem? card, CardAttachmentSpec? spec, string source)
    {
        if (card == null || spec == null)
        {
            return 0;
        }

        var changed = 0;
        changed += AttachToConfig(card.dataConfig, spec, source + ":config");
        foreach (var tag in spec.NativeTags.Concat(spec.SpecialTags))
        {
            if (card.Tags != null && !card.Tags.Contains(tag))
            {
                card.Tags.Add(tag);
                changed++;
            }
        }

        if (changed > 0)
        {
            TerriasCardRefreshQueue.RequestFullRefresh(card, "CardAttachment");
        }

        return changed;
    }
}
