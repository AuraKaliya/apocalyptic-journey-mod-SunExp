using System;
using AuraShared.Core;
using Network.Query;
namespace AuraToolsExp.Dll.Infrastructure;

// Public compatibility facade; callback ownership and native transport are shared.
public static class AuraToolsTargetedQueryTransport
{
    public static bool TryRegister<T>(PlayerManager? manager, QueryBase<T>? query, Action<T>? callback, out uint queryId, out string rejection) =>
        AuraNativeTargetedQueryTransport.TryRegister(manager, query, callback, out queryId, out rejection);
    public static void RemovePending(PlayerManager? manager, uint queryId) => AuraNativeTargetedQueryTransport.RemovePending(manager, queryId);
    public static bool TrySend(string requesterPlayerId, uint queryId, QueryBase response, out string rejection) =>
        AuraNativeTargetedQueryTransport.TrySend(requesterPlayerId, queryId, response, out rejection);
}
