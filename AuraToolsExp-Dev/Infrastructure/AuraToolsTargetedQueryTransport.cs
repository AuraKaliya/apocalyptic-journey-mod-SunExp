using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using Network.Query;
using Witch.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public static class AuraToolsTargetedQueryTransport
{
    private static readonly FieldInfo? PendingQueriesField = typeof(PlayerManager)
        .GetField("pendingQueries", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? NextQueryIdField = typeof(PlayerManager)
        .GetField("nextQueryId", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? RpcQueryMethod = typeof(PlayerManager)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .FirstOrDefault(method => string.Equals(method.Name, "RpcQuery", StringComparison.Ordinal)
                                  && method.GetParameters().Length == 2
                                  && typeof(NetworkConnection).IsAssignableFrom(method.GetParameters()[0].ParameterType)
                                  && typeof(QueryBase).IsAssignableFrom(method.GetParameters()[1].ParameterType));

    public static bool TryRegister<T>(
        PlayerManager? manager,
        QueryBase<T>? query,
        Action<T>? callback,
        out uint queryId,
        out string rejection)
    {
        queryId = 0;
        rejection = "";
        if (manager == null || query == null || callback == null)
        {
            rejection = "targeted query registration input unavailable";
            return false;
        }

        if (PendingQueriesField == null || NextQueryIdField == null || RpcQueryMethod == null)
        {
            rejection = "native targeted query metadata unavailable";
            return false;
        }

        try
        {
            if (PendingQueriesField.GetValue(manager) is not IDictionary<uint, QueryBase> pending)
            {
                rejection = "native targeted query callback store unavailable";
                return false;
            }

            var candidate = Convert.ToUInt32(NextQueryIdField.GetValue(manager));
            if (candidate == 0)
            {
                candidate = 1;
            }

            for (var attempt = 0; attempt < 1024; attempt++)
            {
                if (candidate != 0 && !pending.ContainsKey(candidate))
                {
                    query.QueryId = candidate;
                    query.Callback = callback;
                    pending[candidate] = query;
                    queryId = candidate;
                    NextQueryIdField.SetValue(manager, candidate == uint.MaxValue ? 1u : candidate + 1u);
                    return true;
                }

                candidate = candidate == uint.MaxValue ? 1u : candidate + 1u;
            }

            rejection = "native targeted query callback ids exhausted";
            return false;
        }
        catch (Exception ex)
        {
            rejection = "native targeted query registration failed: " + ex.Message;
            return false;
        }
    }

    public static void RemovePending(PlayerManager? manager, uint queryId)
    {
        if (manager == null || queryId == 0 || PendingQueriesField == null)
        {
            return;
        }

        try
        {
            if (PendingQueriesField.GetValue(manager) is IDictionary<uint, QueryBase> pending)
            {
                pending.Remove(queryId);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[TargetedQuery] pending callback cleanup failed: " + ex.Message);
        }
    }

    public static bool TrySend(
        string requesterPlayerId,
        uint queryId,
        QueryBase response,
        out string rejection)
    {
        rejection = "";
        if (string.IsNullOrWhiteSpace(requesterPlayerId) || queryId == 0 || response == null)
        {
            rejection = "targeted query response identity unavailable";
            return false;
        }

        if (RpcQueryMethod == null)
        {
            rejection = "native targeted query response metadata unavailable";
            return false;
        }

        try
        {
            var player = GameServer.Instance?.LobbyInfo?.AddedPlayers?.FirstOrDefault(value =>
                value != null
                && string.Equals(value.Id, requesterPlayerId, StringComparison.Ordinal));
            var connection = player?.Connection;
            var targetManager = connection?.identity?.GetComponent<PlayerManager>();
            if (connection == null || targetManager == null)
            {
                rejection = "requester network connection unavailable";
                return false;
            }

            response.QueryId = queryId;
            RpcQueryMethod.Invoke(targetManager, new object[] { connection, response });
            return true;
        }
        catch (Exception ex)
        {
            rejection = "native targeted query response failed: " + (ex.InnerException?.Message ?? ex.Message);
            return false;
        }
    }
}
