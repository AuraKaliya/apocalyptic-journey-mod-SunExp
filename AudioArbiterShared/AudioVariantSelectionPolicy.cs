using System;

namespace AudioArbiter.Shared;

internal static class AudioVariantSelectionPolicy
{
    public static int SelectStartIndex(
        string eventId,
        string providerIdentity,
        int variantCount)
    {
        if (variantCount <= 1)
        {
            return 0;
        }

        var hash = StableHash((eventId ?? "") + "|" + (providerIdentity ?? ""));
        return (int)(hash % (uint)variantCount);
    }

    private static uint StableHash(string value)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;
            var hash = offset;
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                hash ^= (byte)(character & 0xff);
                hash *= prime;
                hash ^= (byte)(character >> 8);
                hash *= prime;
            }

            return hash;
        }
    }
}
