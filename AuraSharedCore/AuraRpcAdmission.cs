using System;
using System.Collections.Generic;

namespace AuraShared.Core;

public enum AuraRpcPublication { MemberRequest, HostOnly }

// Only explicitly registered Aura messages are admitted. Game and foreign MOD
// messages retain their original receive path.
public static class AuraRpcAdmission
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> OwnedAssemblies = new(StringComparer.OrdinalIgnoreCase);
    public static void RegisterAssembly(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName.Contains(",")) throw new ArgumentException("A simple assembly name is required.");
        lock (Gate) OwnedAssemblies.Add(assemblyName.Trim());
    }
    private static readonly Dictionary<string, AuraRpcPublication> Policies = new(StringComparer.Ordinal);
    private static readonly HashSet<string> BattleMessages = new(StringComparer.Ordinal);
    private static readonly HashSet<string> SettlementMessages = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Action<string, AuraRpcSender>> Handlers = new(StringComparer.Ordinal);
    public static void RegisterHandler<T>(Action<T, AuraRpcSender> handler)
    {
        var key = WireKey(typeof(T).AssemblyQualifiedName ?? "");
        lock (Gate)
        {
            if (!Policies.ContainsKey(key)) throw new InvalidOperationException("RPC admission must be registered first.");
            Handlers[key] = (json, sender) => handler(Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json)!, sender);
        }
    }
    public static bool TryHandle(string wireType, string json, AuraRpcSender sender)
    {
        Action<string, AuraRpcSender>? handler;
        lock (Gate) Handlers.TryGetValue(WireKey(wireType), out handler);
        if (handler == null) return false;
        handler(json, sender);
        return true;
    }

    public static void Register<T>(AuraRpcPublication publication, bool battleScoped = false, bool allowSettledResult = false)
        => Register(typeof(T), publication, battleScoped, allowSettledResult);

    public static void Register(Type type, AuraRpcPublication publication, bool battleScoped = false, bool allowSettledResult = false)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (publication != AuraRpcPublication.MemberRequest && publication != AuraRpcPublication.HostOnly)
            throw new ArgumentOutOfRangeException(nameof(publication));
        if (allowSettledResult && (!battleScoped || publication != AuraRpcPublication.HostOnly))
            throw new ArgumentException("Only host battle results may drain after settlement.");
        RegisterAssembly(type.Assembly.GetName().Name ?? "");
        var key = WireKey(type.AssemblyQualifiedName ?? "");
        lock (Gate)
        {
            if (Policies.TryGetValue(key, out var existing) && existing != publication)
                throw new InvalidOperationException("Conflicting RPC publication policy: " + key);
            Policies[key] = publication;
            if (battleScoped) BattleMessages.Add(key);
            if (allowSettledResult)
            {
                SettlementMessages.Add(key);
            }
        }
    }

    public static bool TryAuthorize(string wireType, AuraRpcSender sender, out bool owned, out string rejection)
    {
        var key = WireKey(wireType);
        lock (Gate)
        {
            owned = Policies.ContainsKey(key) || IsAuraType(key);
            rejection = "";
            if (!owned) return true;
            if (!Policies.TryGetValue(key, out var policy)) rejection = "unregistered Aura RPC";
            else if (sender == null || !sender.IsAvailable || !sender.IsLobbyMember) rejection = "unbound or non-member connection";
            else if (policy == AuraRpcPublication.HostOnly && !sender.IsLobbyHost) rejection = "host publication required";
            return rejection.Length == 0;
        }
    }

    public static bool IsBattleScoped(string wireType)
    {
        lock (Gate) return BattleMessages.Contains(WireKey(wireType));
    }
    public static bool AllowsSettledResult(string wireType)
    {
        lock (Gate) return SettlementMessages.Contains(WireKey(wireType));
    }

    public static bool IsRegistered(Type type)
    {
        lock (Gate) return Policies.ContainsKey(WireKey(type.AssemblyQualifiedName ?? ""));
    }

    private static bool IsAuraType(string key)
    {
        var comma = key.IndexOf(',');
        var assembly = comma < 0 ? "" : key.Substring(comma + 1).Trim();
        return OwnedAssemblies.Contains(assembly);
    }

    private static string WireKey(string value)
    {
        var parts = (value ?? "").Split(',');
        return parts.Length < 2 ? (value ?? "").Trim() : parts[0].Trim() + ", " + parts[1].Trim();
    }
}

public static class AuraRpcReceiveContext
{
    [ThreadStatic] private static AuraRpcSender? current;
    public static AuraRpcSender Sender => current ?? AuraRpcSender.Unbound;

    public static IDisposable Enter(AuraRpcSender sender) => new Scope(sender);

    private sealed class Scope : IDisposable
    {
        private readonly AuraRpcSender? previous = current;
        private bool disposed;
        public Scope(AuraRpcSender sender) { current = sender ?? AuraRpcSender.Unbound; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            current = previous;
        }
    }
}
