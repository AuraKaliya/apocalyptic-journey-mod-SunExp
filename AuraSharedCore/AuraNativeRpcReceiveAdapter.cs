using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Mirror;
using Mirror.RemoteCalls;
using Newtonsoft.Json.Linq;

namespace AuraShared.Core;

// The game's ModHookContext does not expose the connection or cancellation.
// Preserve the real receive connection at Mirror's existing delegate boundary.
public static class AuraNativeRpcReceiveAdapter
{
    private static readonly Dictionary<ushort, Lease> Leases = new();
    private static readonly Dictionary<string, Dictionary<string, Func<NetworkBehaviour, NetworkReader, NetworkConnectionToClient, bool>>> CommandGuards = new(StringComparer.Ordinal);
    public static void RegisterCommandGuard(string ownerId, string method, Func<NetworkBehaviour, NetworkReader, NetworkConnectionToClient, bool> guard)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || guard == null) throw new ArgumentException("A guard owner and callback are required.");
        if (!CommandGuards.TryGetValue(method, out var guards)) CommandGuards[method] = guards = new(StringComparer.Ordinal);
        guards[ownerId] = guard;
        InstallDelegate(method, (original, target, reader, sender) =>
        {
            foreach (var check in CommandGuards[method].Values)
            {
                var position = reader.Position;
                bool allowed;
                try { allowed = check(target, reader, sender); }
                finally { reader.Position = position; }
                if (!allowed) return;
            }
            original(target, reader, sender);
        });
    }
    private static readonly string[] Commands =
    {
        "System.Void PlayerManager::CmdReceiveRpcCommand(Network.Command.RpcCommandBase)",
        "System.Void PlayerManager::CmdReceiveRpcCommandExcludeOwner(Network.Command.RpcCommandBase)"
    };

    internal static void Install()
    {
        RuntimeHelpers.RunClassConstructor(typeof(PlayerManager).TypeHandle);
        var before = new Dictionary<ushort, Lease>(Leases);
        try
        {
        foreach (var name in Commands)
            InstallDelegate(name, (original, target, reader, connection) => Receive(original, target, reader, connection));
        foreach (var name in new[] { "System.Void PlayerManager::RpcReceiveRpcCommand(Network.Command.RpcCommandBase)",
                     "System.Void PlayerManager::RpcReceiveRpcCommandExcludeOwner(Network.Command.RpcCommandBase)" })
            InstallDelegate(name, ReceivePublication);
        }
        catch
        {
            foreach (var pair in new Dictionary<ushort, Lease>(Leases))
                if (!before.TryGetValue(pair.Key, out var previous) || !ReferenceEquals(previous, pair.Value)) pair.Value.Restore();
            Leases.Clear(); foreach (var pair in before) Leases.Add(pair.Key, pair.Value);
            throw;
        }
    }

    internal static void InstallDelegate(string method, Action<RemoteCallDelegate, NetworkBehaviour, NetworkReader, NetworkConnectionToClient> receive)
    {
        var hash = unchecked((ushort)method.GetStableHashCode());
        if (Leases.TryGetValue(hash, out var installed))
        {
            if (installed.IsCurrent) return;
        }
        var field = typeof(RemoteProcedureCalls).GetField("remoteCallDelegates", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("Mirror RPC delegate inventory is unavailable.");
        var delegates = field.GetValue(null) as IDictionary
            ?? throw new InvalidOperationException("Mirror RPC delegate inventory is incompatible.");
        var invoker = delegates[hash] ?? throw new MissingMethodException("Native RPC is not registered: " + method);
        var function = invoker.GetType().GetField("function", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingFieldException("Mirror invoker function is unavailable.");
        var original = function.GetValue(invoker) as RemoteCallDelegate
            ?? throw new InvalidOperationException("Native RPC delegate is incompatible: " + method);
        RemoteCallDelegate wrapper = (target, reader, connection) => receive(original, target, reader, connection);
        var lease = new Lease(hash, invoker, function, original, wrapper);
        function.SetValue(invoker, wrapper);
        if (!lease.IsCurrent) throw new InvalidOperationException("Native RPC wrapper installation failed: " + method);
        Leases[hash] = lease;
    }

    private static void Receive(RemoteCallDelegate original, NetworkBehaviour target, NetworkReader reader, NetworkConnectionToClient connection)
    {
        var position = reader.Position;
        var json = reader.ReadString();
        reader.Position = position;
        var sender = FromConnection(connection, "Mirror.ReceiveRpc");
        var wireType = "";
        try
        {
            var envelope = JObject.Parse(json ?? "{}");
            wireType = (string?)envelope["$type"] ?? "";
            if (!AuraRpcAdmission.TryAuthorize(wireType, sender, out var owned, out var rejection))
            {
                Reject(wireType, sender, rejection);
                return;
            }
            if (owned && Encoding.UTF8.GetByteCount(json ?? "") > AuraSharedPayloadBudget.DefaultSoftLimitBytes)
            {
                Reject(wireType, sender, "payload budget exceeded");
                return;
            }
            if (owned && AuraRpcAdmission.IsBattleScoped(wireType)
                && !MatchesBattle(wireType, (string?)envelope["NetworkBattleId"] ?? ""))
            {
                Reject(wireType, sender, "battle identity missing or stale");
                return;
            }
        }
        catch (Newtonsoft.Json.JsonException)
        {
            Reject("malformed", sender, "invalid RPC JSON");
            return;
        }
        using (AuraRpcReceiveContext.Enter(sender))
        {
            if (AuraRpcAdmission.TryHandle(wireType, json ?? "{}", sender)) return;
            original(target, reader, connection);
        }
    }

    private static void ReceivePublication(RemoteCallDelegate original, NetworkBehaviour target, NetworkReader reader, NetworkConnectionToClient connection)
    {
        var position = reader.Position;
        var json = reader.ReadString(); reader.Position = position;
        try
        {
            var envelope = JObject.Parse(json ?? "{}");
            var wireType = (string?)envelope["$type"] ?? "";
            if (AuraRpcAdmission.IsBattleScoped(wireType) && !MatchesBattle(wireType, (string?)envelope["NetworkBattleId"] ?? "")) return;
        }
        catch (Newtonsoft.Json.JsonException) { return; }
        original(target, reader, connection);
    }

    private static bool MatchesBattle(string type, string id) => AuraRpcAdmission.AllowsSettledResult(type)
        ? AuraNetworkIdentityRuntime.MatchesBattleResult(id) : AuraNetworkIdentityRuntime.MatchesBattle(id);

    internal static AuraRpcSender FromConnection(NetworkConnectionToClient? connection, string source)
    {
        if (connection == null || !NetworkServer.active
            || !NetworkServer.connections.TryGetValue(connection.connectionId, out var live)
            || !ReferenceEquals(live, connection) || connection.identity == null)
            return AuraRpcSender.Unbound;
        var manager = connection.identity.GetComponent<PlayerManager>();
        if (manager == null || !ReferenceEquals(manager.connectionToClient, connection)) return AuraRpcSender.Unbound;
        var id = (manager.PlayerId ?? "").Trim();
        var players = GameServer.Instance?.LobbyInfo?.AddedPlayers;
        var member = id.Length > 0 && players != null && players.Exists(player => player != null && player.Id == id && ReferenceEquals(player.Connection, connection));
        // A remote client choosing the host's textual id must not become host.
        var host = member && ReferenceEquals(connection, NetworkServer.localConnection);
        return new AuraRpcSender(id, manager.playerInfo?.Name ?? "", member, host, source, id.Length > 0, connection.connectionId);
    }

    private static void Reject(string type, AuraRpcSender sender, string reason)
        => AuraSharedLog.Warn("RpcAuthority", "Rejected " + type + "; sender=" + sender.PlayerId + "; reason=" + reason);

    private sealed class Lease
    {
        private readonly ushort hash;
        private readonly object invoker;
        private readonly FieldInfo field;
        private readonly RemoteCallDelegate original;
        private readonly RemoteCallDelegate wrapper;
        internal Lease(ushort hash, object invoker, FieldInfo field, RemoteCallDelegate original, RemoteCallDelegate wrapper)
        { this.hash = hash; this.invoker = invoker; this.field = field; this.original = original; this.wrapper = wrapper; }
        internal bool IsCurrent => Equals(RemoteProcedureCalls.GetDelegate(hash), wrapper);
        internal void Restore() { if (IsCurrent) field.SetValue(invoker, original); }
    }
}
