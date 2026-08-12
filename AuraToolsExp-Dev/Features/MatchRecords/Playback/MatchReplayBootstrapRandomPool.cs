using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Provides deterministic random values only for native fight-view construction.
/// Replay state and presentation never consume this pool as simulation input.
/// </summary>
internal static class MatchReplayBootstrapRandomPool
{
    internal const int DefaultSize = 8192;

    internal static float[] Create(string seedMaterial, int size = DefaultSize)
    {
        var count = Math.Max(1, size);
        var state = StableSeed(seedMaterial ?? "");
        var result = new float[count];
        for (var index = 0; index < result.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            if (state == 0)
            {
                state = 0x9E3779B9u;
            }

            result[index] = (state & 0x00FFFFFFu) / 16777216f;
        }

        return result;
    }

    private static uint StableSeed(string value)
    {
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash ^= (byte)character;
            hash *= 16777619u;
            hash ^= (byte)(character >> 8);
            hash *= 16777619u;
        }

        return hash == 0 ? 0xA341316Cu : hash;
    }
}
