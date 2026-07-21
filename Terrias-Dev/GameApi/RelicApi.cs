using System;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.GameApi;

public static class RelicApi
{
    private const string TerriasPrefix = "Terrias_terrias_";

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
                if (SameTerriasLocalId(id, localId))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[RelicApi] relic scan skipped: " + ex.Message);
        }

        return false;
    }

    private static bool SameTerriasLocalId(string? id, string localId)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var value = id!.Trim().TrimStart('*');
        var expected = localId.Trim().TrimStart('*');
        return string.Equals(value, expected, StringComparison.Ordinal)
               || string.Equals(value, TerriasPrefix + expected, StringComparison.Ordinal);
    }
}
