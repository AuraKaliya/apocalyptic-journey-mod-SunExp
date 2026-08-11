using System;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.Infrastructure;
using Witch.Core;

namespace Terrias.Dll.GameApi;

public static class CareerApi
{
    public static DataConfig? Materialize(string careerId)
    {
        var id = (careerId ?? "").Trim().TrimStart('*');
        if (id.Length == 0)
        {
            return null;
        }

        try
        {
            var handle = AuraGameDataHostApi.ResolveHandle(DataType.Career, id)
                         ?? AuraGameDataHostApi.ResolveHandle(DataType.Enemy, id);
            return handle == null
                ? null
                : AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest
                {
                    Definition = handle
                }).Instance as DataConfig;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CareerApi] career config unavailable: " + id + "; " + ex.Message);
            return null;
        }
    }

    public static bool CommitLocalCareer(
        IStatusManager? ownerStatus,
        DataConfig? career,
        string source,
        bool resetAnimator = true)
    {
        if (ownerStatus == null || career == null || RoleTable.Instance == null)
        {
            return false;
        }

        var local = FightPlayer.Instance?.Status;
        if (local == null
            || (!ReferenceEquals(ownerStatus, local)
                && !string.Equals(ownerStatus.InstanceId, local.InstanceId, StringComparison.Ordinal)))
        {
            return false;
        }

        var careerId = DictionaryUtil.Get(career.data, "Id");
        if (careerId.Length == 0)
        {
            return false;
        }

        try
        {
            RoleTable.Instance.Career = career;
            FightPlayer.Instance?.ResetId(careerId);
            if (resetAnimator)
            {
                ownerStatus.ResetAnimator(false);
            }

            TerriasLog.Debug("[CareerApi] local career committed from " + source + ": " + careerId + ".");
            return true;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CareerApi] local career commit failed from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static bool IsCurrent(string careerId)
    {
        var expected = Normalize(careerId);
        var current = Normalize(DictionaryUtil.Get(RoleTable.Instance?.Career?.data, "Id"));
        return expected.Length > 0 && string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return TerriasContentIdCompatibility.Canonicalize((value ?? "").Trim().TrimStart('*'));
    }
}
