namespace Terrias.Dll.GameApi;

/// <summary>
/// Resolves the player id used only by the native ScriptExecutor locality
/// table. This is an adapter identity and is not the companion's semantic
/// summoner/owner.
/// </summary>
public static class CompanionExecutionRouteApi
{
    public static string ResolveAuthoritativePlayerId(string semanticOwnerPlayerId = "")
    {
        var playerManagerId = PlayerManager.Instance?.PlayerId ?? "";
        if (!string.IsNullOrWhiteSpace(playerManagerId)
            && (PlayerManager.Instance?.isServer == true || FightManager.Instance?.isServer == true))
        {
            return playerManagerId;
        }

        var roleQueue = FightManager.Instance?.roleQueue;
        var hostRoleId = roleQueue != null && roleQueue.Count > 0
            ? roleQueue[0]?.InstanceId ?? ""
            : "";
        if (!string.IsNullOrWhiteSpace(hostRoleId))
        {
            return hostRoleId;
        }

        var roleId = RoleTable.Instance?.Id ?? "";
        if (!string.IsNullOrWhiteSpace(roleId))
        {
            return roleId;
        }

        return semanticOwnerPlayerId ?? "";
    }
}
