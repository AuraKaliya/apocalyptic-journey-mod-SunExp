using System;
using System.Collections.Generic;
using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayPresentationModes
{
    internal const string Standard = "Standard";
    internal const string Compact = "Compact";
    internal const string Showcase = "Showcase";
}

internal static class MatchReplayPresentationSchedule
{
    internal static List<long> Build(IReadOnlyList<MatchReplayEvent> source, string mode)
    {
        var result = new List<long>(source.Count);
        long clock = 0;
        var previousTurn = source.Count == 0 ? 0 : source[0].TurnIndex;
        foreach (var item in source)
        {
            if (previousTurn > 0 && item.TurnIndex != previousTurn)
            {
                clock += Scale(520, mode);
            }

            clock += Duration(item, mode);
            result.Add(clock);
            previousTurn = item.TurnIndex;
        }

        return result;
    }

    internal static int Duration(MatchReplayEvent item, string mode)
    {
        if (item.Kind == MatchReplayEventKinds.Checkpoint) return 0;
        var value = item.Semantic?.Category == MatchSemanticCategories.Card ? 720
            : item.Semantic?.Category == MatchSemanticCategories.Damage ? 360
            : item.Semantic?.Category == MatchSemanticCategories.Status ? 260
            : item.Semantic?.Category == MatchSemanticCategories.Target ? 180
            : 220;
        return Scale(value, mode);
    }

    private static int Scale(int milliseconds, string mode)
    {
        var scale = string.Equals(mode, MatchReplayPresentationModes.Compact, StringComparison.OrdinalIgnoreCase) ? 0.58
            : string.Equals(mode, MatchReplayPresentationModes.Showcase, StringComparison.OrdinalIgnoreCase) ? 1.35
            : 1d;
        return Math.Max(20, (int)Math.Round(milliseconds * scale));
    }
}
