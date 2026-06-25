using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class DamageMeterAuthorityPolicy
{
    public static bool TryBindReporter(
        DamageEvent? candidate,
        AuraToolsRpcSender? sender,
        out DamageEvent bound,
        out string rejection)
    {
        bound = new DamageEvent();
        if (!RequireLobbyMember(sender, out rejection))
        {
            return false;
        }

        if (candidate == null)
        {
            rejection = "empty damage event";
            return false;
        }

        bound = candidate.Copy();
        bound.ReporterPlayerId = sender!.PlayerId;
        return true;
    }

    public static bool RequireHostControl(AuraToolsRpcSender? sender, out string rejection)
    {
        if (!RequireLobbyMember(sender, out rejection))
        {
            return false;
        }

        if (!sender!.IsLobbyHost)
        {
            rejection = "control issuer is not host";
            return false;
        }

        rejection = "";
        return true;
    }

    public static bool RequireLobbyMember(AuraToolsRpcSender? sender, out string rejection)
    {
        if (sender == null || !sender.IsAvailable)
        {
            rejection = "missing server sender";
            return false;
        }

        if (!sender.IsLobbyMember)
        {
            rejection = "sender not in lobby";
            return false;
        }

        rejection = "";
        return true;
    }
}
