using System;

namespace AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal sealed class MatchReplayTerminalGate
{
    internal bool SettlementPrepared { get; private set; }

    internal bool TerminalFrameSealed { get; private set; }

    internal string Result { get; private set; } = "";

    internal void Prepare(string result)
    {
        SettlementPrepared = true;
        if (Result.Length == 0 || string.Equals(Result, "Unknown", StringComparison.Ordinal))
        {
            Result = Normalize(result);
        }
    }

    internal void SealTerminalFrame(string result)
    {
        Prepare(result);
        TerminalFrameSealed = true;
    }

    internal bool CanFinalize => SettlementPrepared && TerminalFrameSealed;

    internal void Reset()
    {
        SettlementPrepared = false;
        TerminalFrameSealed = false;
        Result = "";
    }

    private static string Normalize(string result)
    {
        return string.IsNullOrWhiteSpace(result) ? "Unknown" : result.Trim();
    }
}
