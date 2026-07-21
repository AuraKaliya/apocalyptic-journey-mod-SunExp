using System;
using System.Linq;
using System.Reflection;
using Mirror;
using Network.Query;
using Witch.Core;
using Witch.Mod;

namespace AuraToolsExp.Dll.Infrastructure;

public interface IAuraToolsImmediateQuery
{
    bool ResponseDispatched { get; set; }

    void BindServerRequester(string playerId);
}

public static class AuraToolsTargetedQueryTransport
{
    private static readonly MethodInfo? RpcQueryMethod = typeof(PlayerManager)
        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .FirstOrDefault(method => string.Equals(method.Name, "RpcQuery", StringComparison.Ordinal)
                                  && method.GetParameters().Length == 2);
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        AuraToolsHookRegistry.Before(
            modConfig,
            "PlayerManager.UserCode_CmdQuery__QueryBase__NetworkConnectionToClient",
            DispatchImmediateQuery,
            "TargetedQuery");
    }

    public static bool Send<T>(PlayerManager? manager, QueryBase<T> query, Action<T> callback, string source)
    {
        if (manager == null || query == null || callback == null)
        {
            AuraToolsLog.Warn("[TargetedQuery] send skipped: source=" + source + "; missing input.");
            return false;
        }

        try
        {
            manager.SendQuery(query, callback);
            return true;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[TargetedQuery] send failed: source=" + source + "; error=" + ex.Message);
            return false;
        }
    }

    private static void DispatchImmediateQuery(ModHookContext context)
    {
        if (context.Target is not PlayerManager manager
            || context.Arguments == null
            || context.Arguments.OfType<QueryBase>().FirstOrDefault() is not QueryBase query
            || query is not IAuraToolsImmediateQuery immediate
            || immediate.ResponseDispatched)
        {
            return;
        }

        var connection = context.Arguments.OfType<NetworkConnectionToClient>().FirstOrDefault();
        if (connection == null || RpcQueryMethod == null)
        {
            AuraToolsLog.Warn("[TargetedQuery] immediate response unavailable: target RPC metadata missing.");
            return;
        }

        try
        {
            immediate.BindServerRequester(manager.PlayerId ?? "");
            query.CmdExecute();
            immediate.ResponseDispatched = true;
            RpcQueryMethod.Invoke(manager, new object[] { connection, query });
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[TargetedQuery] immediate response failed: "
                              + (ex.InnerException?.Message ?? ex.Message));
        }
    }
}
