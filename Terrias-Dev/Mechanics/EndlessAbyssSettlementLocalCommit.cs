using System;

namespace Terrias.Dll.Mechanics;

public sealed class EndlessAbyssSettlementLocalCommit
{
    private string token = "";
    private bool clientCommit;

    public bool IsArmed => token.Length > 0;

    public void Arm(string? settlementToken, bool isClientOnly)
    {
        var normalized = (settlementToken ?? "").Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        token = normalized;
        clientCommit = isClientOnly;
    }

    public bool TryTakeBeforeNetworkTeardown(out string settlementToken)
    {
        settlementToken = token;
        var shouldSend = clientCommit && settlementToken.Length > 0;
        Clear();
        return shouldSend;
    }

    public void Clear()
    {
        token = "";
        clientCommit = false;
    }
}
