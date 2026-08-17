using System;

namespace AuraToolsExp.Dll.Config;

internal static class AuraToolsConfigSchemaPolicy
{
    internal const int CurrentEnvelopeVersion = 1;

    internal static bool IsNewer(
        int storedEnvelopeVersion,
        object? storedValue,
        object? supportedValue)
    {
        var supportedValueVersion = ReadValueVersion(supportedValue);
        return storedEnvelopeVersion > CurrentEnvelopeVersion
               || supportedValueVersion > 0
               && ReadValueVersion(storedValue) > supportedValueVersion;
    }

    internal static int ReadValueVersion(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        try
        {
            var property = value.GetType().GetProperty("SchemaVersion");
            return property?.PropertyType == typeof(int)
                ? Math.Max(0, (int)(property.GetValue(value) ?? 0))
                : 0;
        }
        catch
        {
            return 0;
        }
    }
}
