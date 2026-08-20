using System;
using System.Collections.Generic;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

[Flags]
public enum TerriasPresentationDirtyFields
{
    None = 0,
    Cost = 1 << 0,
    Description = 1 << 1,
    Usability = 1 << 2,
    Tags = 1 << 3,
    Visual = 1 << 4,
    Skill = 1 << 5,
    EnemyIntent = 1 << 6,
    Structural = 1 << 7,
    Full = Cost | Description | Usability | Tags | Visual | Skill | EnemyIntent | Structural
}

public sealed class TerriasBuffPresentationRule
{
    public TerriasBuffPresentationRule(
        TerriasPresentationDirtyFields fields,
        params string[] cardIds)
        : this(fields, TerriasBuffPresentationScope.LocalPlayer, TerriasBuffChangeTrigger.AnyLevel, cardIds)
    {
    }

    public TerriasBuffPresentationRule(
        TerriasPresentationDirtyFields fields,
        TerriasBuffPresentationScope scope,
        params string[] cardIds)
        : this(fields, scope, TerriasBuffChangeTrigger.AnyLevel, cardIds)
    {
    }

    public TerriasBuffPresentationRule(
        TerriasPresentationDirtyFields fields,
        TerriasBuffPresentationScope scope,
        TerriasBuffChangeTrigger trigger,
        params string[] cardIds)
    {
        Fields = fields;
        Scope = scope;
        Trigger = trigger;
        CardIds = cardIds ?? Array.Empty<string>();
    }

    public TerriasPresentationDirtyFields Fields { get; }
    public TerriasBuffPresentationScope Scope { get; }
    public TerriasBuffChangeTrigger Trigger { get; }
    public IReadOnlyList<string> CardIds { get; }
    public bool IsNoImpact => Fields == TerriasPresentationDirtyFields.None;

    public bool ShouldInvalidate(int beforeLevel, int afterLevel)
    {
        return beforeLevel != afterLevel
               && (Trigger == TerriasBuffChangeTrigger.AnyLevel
                   || (beforeLevel > 0) != (afterLevel > 0));
    }
}

public enum TerriasBuffPresentationScope
{
    LocalPlayer,
    Enemy,
    AnyStatus
}

public enum TerriasBuffChangeTrigger
{
    AnyLevel,
    PresenceBoundary
}

public static class TerriasBuffPresentationDependencyCatalog
{
    private static readonly string[] TerriasOwnedBuffIds =
    {
        "solar_radiance", "solar_coefficient", "element_pyro", "element_electro", "element_cryo",
        "element_hydro", "element_dendro", "dendro_core", "frozen", "gathered_flame",
        "scorching_canopy", "samsara_garden", "body_burn", "ember", "ember_cloak",
        "solar_crown", "solar_crown_tier", "origin_core_radiance", "cycle_gathered_flame",
        "afterglow_omen", "polymorph_trait", "heart_change_control", "dusk_afterheat_recovery_trait",
        "boss_trait_mirror_array", "boss_trait_merciless_daylight", "boss_trait_white_radiance_saint",
        "boss_white_radiance_crown", "star_stone_pouch", "miracle_clock", "starlight",
        "star_blessing", "star_score", "resonance", "star_stage", "star_clay_body",
        "sandrone_cat_trait", "star_clay_doll_trait", "abyss_gaze_i", "abyss_gaze_ii",
        "abyss_gaze_iii", "abyss_blessing", "moonlight", "gravity_ripple", "gravity_value",
        "moon_domain", "constellation", "relic_star_stone_pouch", "false_gold", "debt_due_1",
        "debt_due_2", "debt_due_3", "golden_potential_zero", "golden_potential_k",
        "golden_potential_m", "golden_potential_b"
    };

    private static readonly Dictionary<string, TerriasBuffPresentationRule> Rules = BuildRules();
    private static readonly Dictionary<string, TerriasBuffPresentationRule> ResolutionCache =
        new(Rules, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> OwnedBuffIds => TerriasOwnedBuffIds;

    public static bool TryResolve(string buffId, out TerriasBuffPresentationRule rule)
    {
        var value = buffId ?? "";
        if (ResolutionCache.TryGetValue(value, out rule!))
        {
            return true;
        }

        var local = Local(value);
        if (!Rules.TryGetValue(local, out rule!))
        {
            return false;
        }

        ResolutionCache[value] = rule;
        return true;
    }

    private static Dictionary<string, TerriasBuffPresentationRule> BuildRules()
    {
        var rules = new Dictionary<string, TerriasBuffPresentationRule>(StringComparer.Ordinal);
        foreach (var id in TerriasOwnedBuffIds)
        {
            rules[id] = new TerriasBuffPresentationRule(
                TerriasPresentationDirtyFields.None,
                TerriasBuffPresentationScope.AnyStatus);
        }

        var solarCards = new[]
        {
            "radiant_flame_slash",
            "burning_star_hex",
            "blazing_crown_collapse",
            "morning_light_bulwark"
        };
        rules["solar_radiance"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Description,
            solarCards);
        rules["gathered_flame"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Description,
            "radiant_flame_slash",
            "burning_star_hex",
            "blazing_crown_collapse",
            "morning_light_bulwark",
            "gathered_flame_shield",
            "solar_scorching_light");
        rules["solar_crown"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Description,
            TerriasBuffPresentationScope.LocalPlayer,
            TerriasBuffChangeTrigger.PresenceBoundary,
            solarCards);
        rules["solar_crown_tier"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Description,
            "blazing_crown_collapse");
        rules["starlight"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Description,
            "stellar_overture_close");
        rules["ember"] = new TerriasBuffPresentationRule(TerriasPresentationDirtyFields.Skill);
        rules["false_gold"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Usability | TerriasPresentationDirtyFields.Description,
            "fortune_throw",
            "wager");
        rules["golden_potential_zero"] = PresenceRule(TerriasPresentationDirtyFields.Usability, "blank_check");
        rules["golden_potential_k"] = PresenceRule(TerriasPresentationDirtyFields.Usability, "blank_check");
        rules["golden_potential_m"] = PresenceRule(TerriasPresentationDirtyFields.Usability, "blank_check");
        rules["golden_potential_b"] = PresenceRule(TerriasPresentationDirtyFields.Usability, "blank_check");

        // The native Burn buff is not owned by Terrias, but Terrias card formulas
        // explicitly read it and can therefore be safely incrementally managed.
        rules["buff_burn"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Description,
            TerriasBuffPresentationScope.Enemy,
            "radiant_flame_slash",
            "burning_star_hex",
            "smoke_erosion",
            "solar_scorching_light");
        rules["buff_extraordinary"] = LocalDescriptionRule();
        rules["buff_keenedge"] = LocalDescriptionRule();
        rules["buff_weak"] = LocalDescriptionRule();
        rules["buff_vulnerability"] = EnemyDescriptionRule();
        rules["buff_resilient"] = EnemyDescriptionRule();
        rules["buff_impregnable"] = EnemyDescriptionRule();

        foreach (var id in new[]
                 {
                     "buff_cripple", "buff_elements", "buff_evergreen", "buff_poised",
                     "buff_rebirth", "buff_rotten", "buff_toxin", "buff_vitality", "buff_VowPower"
                 })
        {
            rules[id] = new TerriasBuffPresentationRule(
                TerriasPresentationDirtyFields.None,
                TerriasBuffPresentationScope.AnyStatus);
        }

        rules["buff_eclipsedmoon"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Structural,
            TerriasBuffPresentationScope.AnyStatus);
        rules["buff_Soul"] = new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Structural,
            TerriasBuffPresentationScope.AnyStatus);
        return rules;
    }

    private static TerriasBuffPresentationRule LocalDescriptionRule()
    {
        return new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Description,
            TerriasBuffPresentationScope.LocalPlayer,
            "*");
    }

    private static TerriasBuffPresentationRule EnemyDescriptionRule()
    {
        return new TerriasBuffPresentationRule(
            TerriasPresentationDirtyFields.Description,
            TerriasBuffPresentationScope.Enemy,
            "*");
    }

    private static TerriasBuffPresentationRule PresenceRule(
        TerriasPresentationDirtyFields fields,
        params string[] cardIds)
    {
        return new TerriasBuffPresentationRule(
            fields,
            TerriasBuffPresentationScope.LocalPlayer,
            TerriasBuffChangeTrigger.PresenceBoundary,
            cardIds);
    }

    private static string Local(string id)
    {
        return TerriasContentIdCompatibility.LocalId(id).TrimStart('*');
    }
}
