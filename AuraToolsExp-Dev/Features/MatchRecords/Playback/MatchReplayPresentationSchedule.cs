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
    internal static int Gap(string mode) => ActionGap(mode);

    internal static List<long> Build(IReadOnlyList<MatchReplayEvent> source, string mode)
    {
        var result = new List<long>(source?.Count ?? 0);
        long clock = 0;
        foreach (var item in source ?? Array.Empty<MatchReplayEvent>())
        {
            result.Add(clock);
            switch (item.Kind)
            {
                case MatchReplayEventKinds.TurnFrame:
                    clock += Scale(120, mode);
                    break;
                case MatchReplayEventKinds.ActionFrame:
                    clock += Duration(item, mode) + ActionGap(mode);
                    break;
            }
        }

        return result;
    }

    internal static int Duration(MatchReplayEvent item, string mode)
    {
        if (item?.Kind != MatchReplayEventKinds.ActionFrame)
        {
            return 0;
        }

        var recorded = item.ActionFrame?.DurationMilliseconds ?? 0;
        return Scale(Math.Max(360, Math.Min(1200, recorded)), mode);
    }

    private static int ActionGap(string mode)
    {
        if (string.Equals(mode, MatchReplayPresentationModes.Compact, StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        return string.Equals(mode, MatchReplayPresentationModes.Showcase, StringComparison.OrdinalIgnoreCase)
            ? 140
            : 60;
    }

    private static int Scale(int milliseconds, string mode)
    {
        var scale = string.Equals(mode, MatchReplayPresentationModes.Compact, StringComparison.OrdinalIgnoreCase)
            ? 0.62
            : string.Equals(mode, MatchReplayPresentationModes.Showcase, StringComparison.OrdinalIgnoreCase)
                ? 1.25
                : 1d;
        return Math.Max(20, (int)Math.Round(milliseconds * scale));
    }
}
