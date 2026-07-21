using System;

namespace AuraGameData.Shared;

internal static class AuraGameDataFieldPolicy
{
    internal static bool IsIdentityOrScriptField(string? field)
    {
        return string.IsNullOrWhiteSpace(field)
            || string.Equals(field, "Id", StringComparison.Ordinal)
            || string.Equals(field, "InstanceID", StringComparison.Ordinal)
            || string.Equals(field, "RawData", StringComparison.Ordinal)
            || IsScriptField(field);
    }

    internal static bool IsScriptField(string? field)
    {
        var fieldName = field ?? "";
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        var scriptIndex = fieldName.LastIndexOf("Script", StringComparison.OrdinalIgnoreCase);
        if (scriptIndex < 0)
        {
            return false;
        }

        var suffixStart = scriptIndex + "Script".Length;
        for (var index = suffixStart; index < fieldName.Length; index++)
        {
            if (fieldName[index] < '0' || fieldName[index] > '9')
            {
                return false;
            }
        }

        return true;
    }
}
