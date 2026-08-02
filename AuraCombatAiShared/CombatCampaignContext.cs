using System;
using System.Collections.Generic;
using AuraCombatSimulation.Shared;

namespace AuraCombatAi.Shared;

/// <summary>
/// Stable public feature names for current adventure progress. The feature
/// contract intentionally excludes future encounter identities and rewards.
/// </summary>
public static class CombatCampaignContextFeatureNames
{
    public const string Prefix = "campaign:";

    public const string ContextKnown = Prefix + "context-known";

    public const string ContextConfidence = Prefix + "context-confidence";

    public const string BattleIndex = Prefix + "battle-index";

    public const string TotalBattles = Prefix + "total-battles";

    public const string RemainingBattles = Prefix + "remaining-battles";

    public const string Progress = Prefix + "progress";

    public const string LayerNumber = Prefix + "layer-number";

    public const string TotalLayers = Prefix + "total-layers";

    public const string EncounterKind = Prefix + "encounter-kind";

    public const string GameLevel = Prefix + "game-level";

    public const string FinalBoss = Prefix + "final-boss";

    public static void ProjectScenario(
        CombatScenarioDefinition? scenario,
        IDictionary<string, double> features)
    {
        if (scenario?.Player?.Variables == null || features == null)
        {
            return;
        }
        var values = scenario.Player.Variables;
        if (!values.ContainsKey(CombatCampaignPublicContextKeys.BattleIndex)
            && !values.ContainsKey(CombatCampaignPublicContextKeys.GameLevel))
        {
            return;
        }
        features[ContextKnown] = 1d;
        features[ContextConfidence] = 1d;
        Copy(values, CombatCampaignPublicContextKeys.BattleIndex, features, BattleIndex);
        Copy(values, CombatCampaignPublicContextKeys.TotalBattles, features, TotalBattles);
        Copy(values, CombatCampaignPublicContextKeys.RemainingBattles, features, RemainingBattles);
        Copy(values, CombatCampaignPublicContextKeys.Progress, features, Progress, clampUnit: true);
        Copy(values, CombatCampaignPublicContextKeys.LayerNumber, features, LayerNumber);
        Copy(values, CombatCampaignPublicContextKeys.TotalLayers, features, TotalLayers);
        Copy(values, CombatCampaignPublicContextKeys.EncounterKind, features, EncounterKind);
        Copy(values, CombatCampaignPublicContextKeys.GameLevel, features, GameLevel);
        Copy(values, CombatCampaignPublicContextKeys.FinalBoss, features, FinalBoss, clampUnit: true);
    }

    private static void Copy(
        IReadOnlyDictionary<string, double> source,
        string sourceKey,
        IDictionary<string, double> target,
        string targetKey,
        bool clampUnit = false)
    {
        if (!source.TryGetValue(sourceKey, out var value)
            || double.IsNaN(value)
            || double.IsInfinity(value))
        {
            return;
        }
        target[targetKey] = clampUnit
            ? Math.Max(0d, Math.Min(1d, value))
            : Math.Max(0d, value);
    }
}
