using System;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using Witch.Core;

namespace SunExp.Dll.Mechanics;

public static class FieldStartSourceService
{
    private const string BlazingCrownHeartRelicId = "blazing_crown_heart";

    public static int ApplyFightStartSources(ScriptExecutor? executor, string source)
    {
        var applied = 0;
        applied += ApplyScorchedWorld(executor, source) ? 1 : 0;
        applied += ApplyBlazingCrownHeart(executor, source) ? 1 : 0;
        return applied;
    }

    private static bool ApplyScorchedWorld(ScriptExecutor? executor, string source)
    {
        var level = Math.Max(0, Math.Min(4, SunExpHardTagState.Level(SunExpHardTagIds.ScorchedWorld)));
        if (level <= 0)
        {
            return false;
        }

        var status = FightPlayer.Instance?.Status;
        var statusId = StatusId(status);
        if (!AuraLifecycleOperationLedger.TryClaimBattleOperation(
                SunExpIds.ModId,
                "FieldStartSource",
                "ScorchedWorld",
                statusId,
                "field",
                SunExpIds.ScorchingCanopy + ":" + level))
        {
            return false;
        }

        FieldApi.ActivateField(executor, SunExpFieldId.ScorchingCanopy, level, "FieldStartSource.ScorchedWorld:" + source);
        return true;
    }

    private static bool ApplyBlazingCrownHeart(ScriptExecutor? executor, string source)
    {
        if (!HasRelic(BlazingCrownHeartRelicId))
        {
            return false;
        }

        var status = FightPlayer.Instance?.Status;
        if (status == null)
        {
            return false;
        }

        var statusId = StatusId(status);
        if (!AuraLifecycleOperationLedger.TryClaimBattleOperation(
                SunExpIds.ModId,
                "FieldStartSource",
                "BlazingCrownHeart",
                statusId,
                "start-effect",
                BlazingCrownHeartRelicId))
        {
            return false;
        }

        status.AddBuff(SunExpIds.SolarRadiance, 8);
        FieldApi.ActivateField(executor, SunExpFieldId.ScorchingCanopy, 2, "FieldStartSource.BlazingCrownHeart:" + source);
        status.AddBuff(SunExpIds.SolarCrown, 1);
        return true;
    }

    private static bool HasRelic(string localId)
    {
        try
        {
            var relics = RoleTable.Instance?.relicList;
            if (relics == null)
            {
                return false;
            }

            foreach (var relic in relics)
            {
                var id = DictionaryUtil.Get(relic?.data, "Id");
                if (SameSunExpLocalId(id, localId))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[FieldStartSource] relic scan skipped: " + ex.Message);
        }

        return false;
    }

    private static bool SameSunExpLocalId(string? id, string localId)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var value = id!.Trim();
        return string.Equals(value, localId, StringComparison.Ordinal)
               || string.Equals(value, SunExpIds.ModId + "_sunexp_" + localId, StringComparison.Ordinal);
    }

    private static string StatusId(IStatusManager? status)
    {
        return string.IsNullOrWhiteSpace(status?.InstanceId) ? "local" : status!.InstanceId;
    }
}
