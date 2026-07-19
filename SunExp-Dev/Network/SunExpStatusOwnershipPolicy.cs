using System;

namespace SunExp.Dll.Network;

/// <summary>
/// Validates a status owner against the sender identity bound by the server
/// receive context. Native player statuses commonly use the player id as
/// their status id; RoleStatusMap remains the fallback for mapped statuses.
/// </summary>
public static class SunExpStatusOwnershipPolicy
{
    public static bool SenderOwnsStatus(
        string playerId,
        string ownerStatusId,
        out string detail)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(ownerStatusId))
        {
            detail = "missing player or status id";
            return false;
        }

        if (string.Equals(playerId, ownerStatusId, StringComparison.Ordinal))
        {
            detail = "direct player-status identity";
            return true;
        }

        try
        {
            var ownership = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            if (ownership == null)
            {
                detail = "role-status map unavailable";
                return false;
            }

            if (!ownership.TryGetValue(playerId, out var statuses) || statuses == null)
            {
                detail = "sender missing from role-status map";
                return false;
            }

            if (!statuses.Contains(ownerStatusId))
            {
                detail = "status missing from sender mapping";
                return false;
            }

            detail = "role-status map";
            return true;
        }
        catch (Exception ex)
        {
            detail = "ownership lookup failed: " + ex.GetType().Name;
            return false;
        }
    }

    public static bool TryResolveOwningPlayerId(string ownerStatusId, out string playerId)
    {
        playerId = "";
        if (string.IsNullOrWhiteSpace(ownerStatusId))
        {
            return false;
        }

        foreach (var lobbyPlayerId in SunExpNetworkRuntime.LobbyPlayerIds())
        {
            if (string.Equals(lobbyPlayerId, ownerStatusId, StringComparison.Ordinal))
            {
                playerId = lobbyPlayerId;
                return true;
            }
        }

        try
        {
            var ownership = Singleton<TempDataManager>.Instance?.RoleStatusMap;
            if (ownership == null)
            {
                return false;
            }

            foreach (var pair in ownership)
            {
                if (pair.Value != null && pair.Value.Contains(ownerStatusId))
                {
                    playerId = pair.Key ?? "";
                    return !string.IsNullOrWhiteSpace(playerId);
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
