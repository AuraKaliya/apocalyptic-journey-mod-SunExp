using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class SpiritElementService
{
    public const int AssignmentRevision = 1;
    public const string CaptureDefaultSource = "capture-default";
    public const string LegacyMigrationSource = "legacy-migration";
    public const string ExplicitOverrideSource = "explicit-override";
    public const string TransformationSource = "transformed";

    private static readonly ElementalType[] AssignableElements =
    {
        ElementalType.Pyro,
        ElementalType.Hydro,
        ElementalType.Geo,
        ElementalType.Dendro,
        ElementalType.Electro,
        ElementalType.Cryo,
        ElementalType.Anemo
    };

    private static readonly HashSet<string> AssignmentSources = new(StringComparer.Ordinal)
    {
        CaptureDefaultSource,
        LegacyMigrationSource,
        ExplicitOverrideSource,
        TransformationSource
    };

    public static string NormalizeId(string? value)
    {
        return ElementalTypeParser.TryParse(value, out var element) ? Id(element) : "";
    }

    public static bool TryParse(string? value, out ElementalType element)
    {
        return ElementalTypeParser.TryParse(value, out element) && element != ElementalType.None;
    }

    public static string Id(ElementalType element)
    {
        return element switch
        {
            ElementalType.Pyro => "pyro",
            ElementalType.Hydro => "hydro",
            ElementalType.Geo => "geo",
            ElementalType.Dendro => "dendro",
            ElementalType.Electro => "electro",
            ElementalType.Cryo => "cryo",
            ElementalType.Anemo => "anemo",
            _ => ""
        };
    }

    public static string CaptureDefaultFor(SpiritSpeciesGrowthProfile? profile)
    {
        var configured = NormalizeId(profile?.CaptureElement);
        return configured.Length > 0
            ? configured
            : DeterministicDefault(profile?.SpeciesId ?? profile?.ProfileId ?? "external");
    }

    public static string DeterministicDefault(string? identity)
    {
        var normalized = string.IsNullOrWhiteSpace(identity) ? "external" : identity!.Trim();
        var index = (int)(SpiritGrowthService.StableHash("spirit-element:" + normalized)
                          % (uint)AssignableElements.Length);
        return Id(AssignableElements[index]);
    }

    public static void Assign(SpiritInstance instance, string elementId, string source)
    {
        if (instance == null)
        {
            return;
        }

        var normalizedElement = NormalizeId(elementId);
        if (normalizedElement.Length == 0)
        {
            throw new ArgumentException("Spirit element must be one of the seven supported elements.", nameof(elementId));
        }

        instance.ElementId = normalizedElement;
        var normalizedSource = source ?? "";
        instance.ElementSource = AssignmentSources.Contains(normalizedSource)
            ? normalizedSource
            : ExplicitOverrideSource;
        instance.ElementAssignmentRevision = AssignmentRevision;
    }

    public static void AssignCaptureDefault(SpiritInstance instance, SpiritSpeciesGrowthProfile profile, bool legacy)
    {
        Assign(instance, CaptureDefaultFor(profile), legacy ? LegacyMigrationSource : CaptureDefaultSource);
    }

    public static void NormalizePersisted(SpiritInstance instance, SpiritSpeciesGrowthProfile profile, bool legacy)
    {
        if (!TryParse(instance?.ElementId, out var parsed))
        {
            AssignCaptureDefault(instance!, profile, legacy: true);
            return;
        }

        instance!.ElementId = Id(parsed);
        if (!AssignmentSources.Contains(instance.ElementSource ?? ""))
        {
            instance.ElementSource = legacy ? LegacyMigrationSource : ExplicitOverrideSource;
        }
        instance.ElementAssignmentRevision = Math.Max(AssignmentRevision, instance.ElementAssignmentRevision);
    }

    public static bool ValidateDeploymentSnapshot(CapturedEnemySnapshot? snapshot, out string reason)
    {
        if (snapshot == null || !TryParse(snapshot.SpiritElementId, out _))
        {
            reason = "精灵的元素快照无效。";
            return false;
        }

        reason = "";
        return true;
    }

    public static string DisplayName(string? elementId)
    {
        var normalized = NormalizeId(elementId);
        return normalized.Length == 0
            ? ""
            : TerriasTextCatalog.Get("element." + normalized);
    }

    public static string IconPath(string? elementId)
    {
        return NormalizeId(elementId) switch
        {
            "pyro" => "Mods/Terrias/ModResource/Images/Buff/GenshinImpact/元素-火",
            "hydro" => "Mods/Terrias/ModResource/Images/Buff/GenshinImpact/元素-水",
            "geo" => "Mods/Terrias/ModResource/Images/Buff/GenshinImpact/元素-岩",
            "dendro" => "Mods/Terrias/ModResource/Images/Buff/GenshinImpact/元素-草",
            "electro" => "Mods/Terrias/ModResource/Images/Buff/GenshinImpact/元素-雷",
            "cryo" => "Mods/Terrias/ModResource/Images/Buff/GenshinImpact/元素-冰",
            "anemo" => "Mods/Terrias/ModResource/Images/Buff/GenshinImpact/元素-风",
            _ => ""
        };
    }

    public static string AttackSummary(string? elementId, int value, int repeatCount)
    {
        var name = DisplayName(elementId);
        var amount = Math.Max(0, value).ToString();
        if (repeatCount > 1)
        {
            amount += "×" + repeatCount;
        }
        return name.Length == 0 ? amount : name + " · " + amount;
    }
}

public static class SpiritElementalAttackService
{
    public static bool TryCommitSegment(
        ScriptExecutor executor,
        IStatusManager target,
        int baseDamage,
        out ElementalResolutionResult result)
    {
        result = new ElementalResolutionResult();
        var state = SpiritStateStore.Find(executor?.Self?.InstanceId ?? "");
        if (state == null || !SpiritElementService.TryParse(state.Snapshot.SpiritElementId, out var element))
        {
            return false;
        }

        result = ElementalReactionService.Hit(
            executor,
            target,
            element,
            Math.Max(0, baseDamage),
            "spirit-intent-segment:" + state.Snapshot.SpiritUid);
        return true;
    }
}
