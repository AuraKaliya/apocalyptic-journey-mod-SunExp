using System;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.GameApi;

public static class RelicApi
{
    private const string SunExpPrefix = "SunExp_sunexp_";

    public static bool HasRelic(string localId)
    {
        if (string.IsNullOrWhiteSpace(localId))
        {
            return false;
        }

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
            SunExpLog.Debug("[RelicApi] relic scan skipped: " + ex.Message);
        }

        return false;
    }

    private static bool SameSunExpLocalId(string? id, string localId)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var value = id!.Trim().TrimStart('*');
        var expected = localId.Trim().TrimStart('*');
        return string.Equals(value, expected, StringComparison.Ordinal)
               || string.Equals(value, SunExpPrefix + expected, StringComparison.Ordinal);
    }
}
