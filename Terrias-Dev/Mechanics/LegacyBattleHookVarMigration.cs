using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class LegacyBattleHookVarMigration
{
    private static readonly HashSet<string> ExactKeys = new(StringComparer.Ordinal)
    {
        "TerriasFlamewheelCostHook", "TerriasFlamewheelCostToken",
        "TerriasWunaCareerHook", "TerriasWunaCareerToken",
        "TerriasLoneerCareerHook", "TerriasLoneerCareerToken",
        "TerriasMorningStarRoundHook", "TerriasMorningStarRoundToken",
        "TerriasStarStageHook", "TerriasStarStageToken",
        "TerriasBlackSunCrossHook", "TerriasBlackSunCrossToken",
        "TerriasTimelessClockHook", "TerriasTimelessClockToken",
        "TerriasLoneerStarStonePouchRelicHook", "TerriasLoneerStarStonePouchRelicToken",
        "TerriasFoxWomanHarpHook", "TerriasFoxWomanHarpToken",
        "TerriasDimStarStoneHook", "TerriasDimStarStoneToken",
        "TerriasSolarWitchHook", "TerriasSolarWitchToken",
        "TerriasSunPriestHook", "TerriasSunPriestToken",
        "TerriasPolymorphTraitHook", "TerriasPolymorphTraitToken",
        "TerriasDuskAfterheatHook", "TerriasDuskAfterheatToken",
        "TerriasFamiliarDuskHook",
        "TerriasSolarRadianceHook", "TerriasSolarRadianceToken",
        "TerriasMoonlightHook", "TerriasMoonlightToken",
        "TerriasFrozenHook", "TerriasFrozenToken",
        "TerriasDendroCoreHook", "TerriasDendroCoreToken",
        "TerriasGatheredFlameHook", "TerriasGatheredFlameToken",
        "TerriasBodyBurnHook", "TerriasBodyBurnToken",
        "TerriasEmberHook", "TerriasEmberToken",
        "TerriasBurnWardHook", "TerriasBurnWardToken",
        "TerriasMiniCoronaHook", "TerriasMiniCoronaToken",
        "TerriasMeltingWheelHook", "TerriasMeltingWheelToken",
        "TerriasAfterglowHook", "TerriasAfterglowToken",
        "TerriasStarStonePouchHook", "TerriasStarStonePouchToken",
        "TerriasRelicStarStonePouchHook", "TerriasRelicStarStonePouchToken"
    };

    private static readonly string[] Prefixes =
    {
        "TerriasMorningStarBlessingHook_",
        "TerriasMorningStarBlessingToken_",
        "TerriasWunaBurnListener_",
        "TerriasBossTrait_"
    };

    public static int ReconcileCurrentRole()
    {
        var role = RoleTable.Instance;
        if (role == null)
        {
            return 0;
        }

        var configs = new List<IDataConfig?> { role.Career };
        configs.AddRange(role.cardList.Cast<IDataConfig?>());
        configs.AddRange(role.relicList.Cast<IDataConfig?>());
        configs.AddRange(role.blessingConfigs.Cast<IDataConfig?>());
        var removed = 0;
        foreach (var config in configs.Where(config => config?.Vars != null).Distinct())
        {
            removed += RemoveFrom(config!.Vars);
        }

        if (removed > 0)
        {
            TerriasLog.Info("Removed retired persistent battle-hook Vars: count=" + removed + ".");
        }

        return removed;
    }

    public static int RemoveFrom(IDictionary<string, string>? vars)
    {
        if (vars == null || vars.Count == 0)
        {
            return 0;
        }

        var removed = 0;
        foreach (var key in vars.Keys.ToArray())
        {
            if (!IsRetiredKey(key) || !vars.Remove(key))
            {
                continue;
            }

            removed++;
        }

        return removed;
    }

    private static bool IsRetiredKey(string key)
    {
        if (ExactKeys.Contains(key ?? ""))
        {
            return true;
        }

        foreach (var prefix in Prefixes)
        {
            if ((key ?? "").StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
