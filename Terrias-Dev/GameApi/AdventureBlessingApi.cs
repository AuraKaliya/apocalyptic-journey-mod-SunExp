using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.GameApi;

public static class AdventureBlessingApi
{
    public static IReadOnlyList<string> OwnedBlessingIds()
    {
        var blessings = RoleTable.Instance?.blessingConfigs;
        if (blessings == null)
        {
            return Array.Empty<string>();
        }

        return blessings
            .Where(config => config != null)
            .Select(config =>
            {
                var id = DictionaryUtil.Get(config.Vars, "Id");
                return string.IsNullOrWhiteSpace(id) ? DictionaryUtil.Get(config.data, "Id") : id;
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(TerriasContentIdCompatibility.LocalId)
            .Select(id => id.TrimStart('*'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool TryGrantLocalAdventureBlessing(
        ScriptExecutor? source,
        string blessingId,
        string context)
    {
        var owner = source?.Self;
        var role = RoleTable.Instance;
        if (source == null
            || owner == null
            || role == null
            || !PlayerApi.IsLocalPlayerOwner(owner)
            || string.IsNullOrWhiteSpace(blessingId))
        {
            return false;
        }

        var localId = TerriasContentIdCompatibility.LocalId(blessingId).TrimStart('*');
        if (OwnedBlessingIds().Any(id => string.Equals(id, localId, StringComparison.Ordinal)))
        {
            return false;
        }

        try
        {
            var candidates = TerriasContentIdCompatibility.LookupCandidates(blessingId, "terrias");
            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Bless, candidates);
            var materialized = handle == null
                ? null
                : AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest { Definition = handle });
            var config = materialized?.Instance as DataConfig;
            if (config == null)
            {
                TerriasLog.Warn("[AdventureBlessing] definition unavailable: " + blessingId + ", context=" + context + ".");
                return false;
            }

            role.blessingConfigs.Add(config);
            InitializeCurrentFight(config, owner, context);
            SyncLocalRole(role, context);
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[AdventureBlessing] grant failed: " + blessingId + ", context=" + context + ", error=" + ex.Message);
            return false;
        }
    }

    private static void InitializeCurrentFight(DataConfig config, IStatusManager owner, string context)
    {
        if (FightManager.Instance == null || FightManager.Instance.fightType == FightType.None)
        {
            return;
        }

        try
        {
            var executor = config.scriptExecutor;
            executor.Self = owner;
            executor.Object.Clear();
            executor.Object.Add(owner);
            executor.RunScript("FightScript");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[AdventureBlessing] current-fight initialization failed from " + context + ": " + ex.Message);
        }
    }

    private static void SyncLocalRole(RoleTable role, string context)
    {
        if (!PlayerApi.IsMultiplayerSession() || PlayerManager.Instance == null)
        {
            return;
        }

        try
        {
            PlayerManager.Instance.CmdSyncRoleTable(role);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[AdventureBlessing] native role sync failed from " + context + ": " + ex.Message);
        }
    }
}
