using System;
using AuraToolsExp.Dll.Config;

namespace AuraToolsExp.Dll.Modules;

internal static class AuraToolsMatchRecordModulePolicy
{
    public static void SetDamageStatistics(
        MatchRecordSettings records,
        bool enabled)
    {
        if (records == null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        if (enabled && !records.Enabled)
        {
            records.Replay.Enabled = false;
        }
        records.Statistics.Enabled = enabled;
        records.Enabled = records.Statistics.Enabled || records.Replay.Enabled;
    }

    public static void SetBattleReplay(
        MatchRecordSettings records,
        bool enabled)
    {
        if (records == null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        if (enabled && !records.Enabled)
        {
            records.Statistics.Enabled = false;
        }
        records.Replay.Enabled = enabled;
        records.Enabled = records.Statistics.Enabled || records.Replay.Enabled;
    }
}
