using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraOnline.Shared;

public sealed class AuraLobbyModState
{
    public string MatchKey { get; internal set; } = "";
    public string ModVersion { get; internal set; } = "";
    public bool Enabled { get; internal set; }
}

public sealed class AuraLobbyPlayerState
{
    public string PlayerId { get; internal set; } = "";
    public string PlayerName { get; internal set; } = "";
    public string GameVersion { get; internal set; } = "";
    public string RoleId { get; internal set; } = "";
    public bool RoleSynced { get; internal set; }
    public bool Ready { get; internal set; }
    public bool IsHost { get; internal set; }
    public bool IsLocal { get; internal set; }
    public IReadOnlyList<AuraLobbyModState> Mods { get; internal set; } =
        Array.Empty<AuraLobbyModState>();
}

public sealed class AuraLobbySnapshot
{
    public long Revision { get; internal set; }
    public string Fingerprint { get; internal set; } = "";
    public string LocalPlayerId { get; internal set; } = "";
    public string HostPlayerId { get; internal set; } = "";
    public GameEntryUI? Entry { get; internal set; }
    public IReadOnlyList<AuraLobbyPlayerState> Players { get; internal set; } =
        Array.Empty<AuraLobbyPlayerState>();
    public AuraChatModSyncState ModSyncState { get; internal set; } = new();
}

public static class AuraLobbySnapshotRuntime
{
    private const string RuntimeOwnerId = "AuraOnline.LobbySnapshot";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Handler> Handlers =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> RolesByPlayer =
        new(StringComparer.OrdinalIgnoreCase);
    private static Handler[] handlerSnapshot = Array.Empty<Handler>();
    private static AuraLobbySnapshot current = new();
    private static bool initialized;
    private static long revision;
    private static long handlerGeneration;

    public static AuraLobbySnapshot Current
    {
        get
        {
            lock (Gate) return current;
        }
    }

    public static IDisposable Register(
        ModConfig modConfig,
        string ownerModId,
        string handlerId,
        Action<AuraLobbySnapshot> changed,
        Action<string>? info = null,
        Action<string>? warn = null)
    {
        if (modConfig == null || changed == null)
        {
            return EmptyDisposable.Instance;
        }

        var id = (ownerModId ?? "").Trim()
                 + ":"
                 + (handlerId ?? "").Trim();
        if (id == ":")
        {
            return EmptyDisposable.Instance;
        }

        lock (Gate)
        {
            EnsureInitializedNoLock(modConfig, info, warn);
            if (Handlers.TryGetValue(id, out var existing))
            {
                if (!existing.Matches(changed))
                {
                    warn?.Invoke(
                        "[LobbySnapshot] handler identity conflict: "
                        + id);
                    return EmptyDisposable.Instance;
                }

                existing.AddLease();
                return new Subscription(id, existing.Generation);
            }

            var generation = ++handlerGeneration;
            Handlers[id] = new Handler(
                id,
                generation,
                changed,
                warn);
            RebuildSnapshotNoLock();
            return new Subscription(id, generation);
        }
    }

    private static void EnsureInitializedNoLock(
        ModConfig modConfig,
        Action<string>? info,
        Action<string>? warn)
    {
        if (initialized) return;
        initialized = true;
        var hooks = new AuraHookRegistry(
            modConfig,
            RuntimeOwnerId,
            info,
            warn);
        hooks.AfterRouted(
            "GameEntryUI.UpdateLobby",
            UpdateLobby,
            "UpdateLobby");
        hooks.AfterRouted(
            "GameEntryUI.ChangeRole",
            UpdateRole,
            "ChangeRole");
        hooks.AfterRouted(
            "GameEntryUI.SetReady",
            UpdateReady,
            "SetReady");
        hooks.AfterRouted(
            "GameEntryUI.Init",
            InitializeEntry,
            "Entry.Init");
        hooks.AfterRouted(
            "GameEntryUI.Outlobby",
            _ => Clear("Outlobby"),
            "Entry.Outlobby");
        hooks.AfterRouted(
            "GameEntryUI.ReturnHouse",
            _ => Clear("ReturnHouse"),
            "Entry.ReturnHouse");
        hooks.BeforeRouted(
            "GameEntryUI.StartGame",
            _ => Clear("StartGame"),
            "Entry.StartGame");
    }

    private static void InitializeEntry(ModHookContext context)
    {
        RolesByPlayer.Clear();
        Publish(new AuraLobbySnapshot
        {
            Entry = context.Target as GameEntryUI
        });
    }

    private static void UpdateLobby(ModHookContext context)
    {
        var entry = context.Target as GameEntryUI;
        var players = ExtractPlayers(context.Arguments);
        var localId = ResolveLocalPlayerId(players);
        var modState = AuraChatModSyncSnapshot.BuildState(
            players,
            "",
            localId);
        var ready = ReadReadyMap(entry);
        var projected = modState.Players.Select((player, index) =>
        {
            var raw = players.FirstOrDefault(value => string.Equals(
                Read(value, "Id"),
                player.PlayerId,
                StringComparison.OrdinalIgnoreCase));
            return new AuraLobbyPlayerState
            {
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                GameVersion = Read(raw, "Version"),
                RoleId = RolesByPlayer.TryGetValue(
                    player.PlayerId,
                    out var role)
                    ? role
                    : "",
                RoleSynced = ReadBool(raw, "IsSyncedRole"),
                Ready = ready.TryGetValue(
                    player.PlayerId,
                    out var isReady)
                    && isReady,
                IsHost = index == 0,
                IsLocal = string.Equals(
                    player.PlayerId,
                    localId,
                    StringComparison.OrdinalIgnoreCase),
                Mods = player.Mods.Select(mod => new AuraLobbyModState
                {
                    MatchKey = mod.MatchKey,
                    ModVersion = mod.ModVersion,
                    Enabled = mod.Enabled
                }).ToArray()
            };
        }).ToArray();
        Publish(new AuraLobbySnapshot
        {
            Entry = entry,
            LocalPlayerId = localId,
            HostPlayerId = modState.HostPlayerId,
            Players = projected,
            ModSyncState = modState
        });
    }

    private static void UpdateRole(ModHookContext context)
    {
        var data = context.Arguments?.OfType<DataConfig>().FirstOrDefault();
        var playerId = context.Arguments?.OfType<string>().FirstOrDefault()
                       ?? "";
        var roleId = data?.data != null
                     && data.data.TryGetValue("Id", out var value)
            ? value ?? ""
            : "";
        if (playerId.Length == 0 || roleId.Length == 0) return;
        RolesByPlayer[playerId] = roleId;
        MutatePlayers(player =>
        {
            if (string.Equals(
                    player.PlayerId,
                    playerId,
                    StringComparison.OrdinalIgnoreCase))
            {
                player.RoleId = roleId;
            }
        });
    }

    private static void UpdateReady(ModHookContext context)
    {
        var entry = context.Target as GameEntryUI ?? Current.Entry;
        var ready = ReadReadyMap(entry);
        MutatePlayers(player =>
        {
            player.Ready = ready.TryGetValue(
                player.PlayerId,
                out var value)
                           && value;
        });
    }

    private static void MutatePlayers(Action<AuraLobbyPlayerState> mutation)
    {
        AuraLobbySnapshot source;
        lock (Gate) source = current;
        var players = source.Players.Select(ClonePlayer).ToArray();
        for (var i = 0; i < players.Length; i++) mutation(players[i]);
        Publish(new AuraLobbySnapshot
        {
            Entry = source.Entry,
            LocalPlayerId = source.LocalPlayerId,
            HostPlayerId = source.HostPlayerId,
            Players = players,
            ModSyncState = source.ModSyncState
        });
    }

    private static void Clear(string source)
    {
        RolesByPlayer.Clear();
        Publish(new AuraLobbySnapshot());
    }

    private static void Publish(AuraLobbySnapshot value)
    {
        Handler[] handlers;
        var fingerprint = Fingerprint(value);
        lock (Gate)
        {
            if (string.Equals(
                    current.Fingerprint,
                    fingerprint,
                    StringComparison.Ordinal))
            {
                return;
            }

            value.Fingerprint = fingerprint;
            value.Revision = ++revision;
            current = value;
            handlers = handlerSnapshot;
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            handlers[i].Invoke(value);
        }
    }

    private static string Fingerprint(AuraLobbySnapshot snapshot)
    {
        var entryId = snapshot.Entry == null
            ? 0
            : snapshot.Entry.GetInstanceID();
        return entryId
               + "\n"
               + string.Join(
            "\n",
            snapshot.Players.Select(player =>
                player.PlayerId
                + "|" + player.PlayerName
                + "|" + player.GameVersion
                + "|" + player.RoleId
                + "|" + player.RoleSynced
                + "|" + player.Ready
                + "|" + string.Join(",", player.Mods
                    .OrderBy(mod => mod.MatchKey, StringComparer.Ordinal)
                    .Select(mod => mod.MatchKey
                                   + ":" + mod.ModVersion
                                   + ":" + mod.Enabled))));
    }

    private static string ResolveLocalPlayerId(IReadOnlyList<object> players)
    {
        var manager = PlayerManager.Instance;
        var isClient = manager != null && !manager.isServer;
        var playerId = (manager?.PlayerId ?? "").Trim();
        if (IsUsableLocalPlayerId(players, playerId, isClient))
        {
            return playerId;
        }

        var configPlayerId =
            (Singleton<GameConfigManager>.Instance?.PlayerId ?? "").Trim();
        if (IsUsableLocalPlayerId(players, configPlayerId, isClient))
        {
            return configPlayerId;
        }

        var managerName = (manager?.playerInfo?.Name ?? "").Trim();
        var byManagerName = ResolvePlayerIdByName(players, managerName);
        if (IsUsableLocalPlayerId(players, byManagerName, isClient))
        {
            return byManagerName;
        }

        var configName =
            (Singleton<GameConfigManager>.Instance?.PlayerName ?? "").Trim();
        var byConfigName = ResolvePlayerIdByName(players, configName);
        if (IsUsableLocalPlayerId(players, byConfigName, isClient))
        {
            return byConfigName;
        }

        var steamPlayerId = ResolveSteamPlayerId();
        if (IsUsableLocalPlayerId(players, steamPlayerId, isClient))
        {
            return steamPlayerId;
        }

        if (isClient)
        {
            var nonHostId = ResolveOnlyNonHostPlayerId(players);
            if (nonHostId.Length > 0) return nonHostId;
        }

        return "";
    }

    private static bool IsUsableLocalPlayerId(
        IReadOnlyList<object> players,
        string playerId,
        bool isClient)
    {
        if (!ContainsPlayerId(players, playerId)) return false;
        return !isClient
               || !IsLobbyHostPlayerId(players, playerId)
               || CountDistinctPlayerIds(players) <= 1;
    }

    private static bool ContainsPlayerId(
        IReadOnlyList<object> players,
        string playerId)
    {
        return playerId.Length > 0
               && players.Any(player => string.Equals(
                   Read(player, "Id"),
                   playerId,
                   StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLobbyHostPlayerId(
        IReadOnlyList<object> players,
        string playerId)
    {
        var hostId = players
            .Select(player => Read(player, "Id"))
            .FirstOrDefault(id => id.Length > 0) ?? "";
        return playerId.Length > 0
               && string.Equals(
                   hostId,
                   playerId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static int CountDistinctPlayerIds(
        IReadOnlyList<object> players)
    {
        return players
            .Select(player => Read(player, "Id"))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static string ResolvePlayerIdByName(
        IReadOnlyList<object> players,
        string playerName)
    {
        if (playerName.Length == 0) return "";
        var matches = players
            .Where(player => string.Equals(
                Read(player, "Name"),
                playerName,
                StringComparison.OrdinalIgnoreCase))
            .Select(player => Read(player, "Id"))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return matches.Count == 1 ? matches[0] : "";
    }

    private static string ResolveOnlyNonHostPlayerId(
        IReadOnlyList<object> players)
    {
        var ids = players
            .Select(player => Read(player, "Id"))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return ids.Count == 2 ? ids[1] : "";
    }

    private static string ResolveSteamPlayerId()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? steamUserType;
            try { steamUserType = assembly.GetType("Steamworks.SteamUser"); }
            catch { continue; }
            var method = steamUserType?.GetMethod(
                "GetSteamID",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null) continue;
            try { return SteamIdToString(method.Invoke(null, null)); }
            catch { return ""; }
        }
        return "";
    }

    private static string SteamIdToString(object? value)
    {
        if (value == null) return "";
        var text = (value.ToString() ?? "").Trim();
        if (text.Length > 0 && text.All(char.IsDigit)) return text;
        var type = value.GetType();
        foreach (var name in new[] { "m_SteamID", "SteamID", "steamID" })
        {
            try
            {
                var member = type.GetField(
                                 name,
                                 BindingFlags.Public
                                 | BindingFlags.NonPublic
                                 | BindingFlags.Instance)?.GetValue(value)
                             ?? type.GetProperty(
                                 name,
                                 BindingFlags.Public
                                 | BindingFlags.NonPublic
                                 | BindingFlags.Instance)?.GetValue(value);
                var memberText = Convert.ToString(member)?.Trim() ?? "";
                if (memberText.Length > 0
                    && memberText.All(char.IsDigit))
                {
                    return memberText;
                }
            }
            catch
            {
            }
        }
        return "";
    }

    private static List<object> ExtractPlayers(IReadOnlyList<object>? arguments)
    {
        foreach (var argument in arguments ?? Array.Empty<object>())
        {
            if (argument is string || argument is not IEnumerable enumerable)
            {
                continue;
            }

            var values = enumerable.Cast<object>()
                .Where(value => value != null)
                .ToList();
            if (values.Any(value =>
                    !string.IsNullOrWhiteSpace(Read(value, "Id"))))
            {
                return values;
            }
        }

        return new List<object>();
    }

    private static Dictionary<string, bool> ReadReadyMap(GameEntryUI? entry)
    {
        try
        {
            return typeof(GameEntryUI).GetField(
                       "Ready",
                       BindingFlags.Instance
                       | BindingFlags.NonPublic)?.GetValue(entry)
                   as Dictionary<string, bool>
                   ?? new Dictionary<string, bool>(
                       StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, bool>(
                StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Read(object? target, string name)
    {
        if (target == null) return "";
        try
        {
            var type = target.GetType();
            return Convert.ToString(
                       type.GetField(
                               name,
                               BindingFlags.Public
                               | BindingFlags.NonPublic
                               | BindingFlags.Instance)?.GetValue(target)
                       ?? type.GetProperty(
                               name,
                               BindingFlags.Public
                               | BindingFlags.NonPublic
                               | BindingFlags.Instance)?.GetValue(target))?
                       .Trim()
                   ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool ReadBool(object? target, string name)
    {
        return bool.TryParse(Read(target, name), out var value) && value;
    }

    private static AuraLobbyPlayerState ClonePlayer(
        AuraLobbyPlayerState value)
    {
        return new AuraLobbyPlayerState
        {
            PlayerId = value.PlayerId,
            PlayerName = value.PlayerName,
            GameVersion = value.GameVersion,
            RoleId = value.RoleId,
            RoleSynced = value.RoleSynced,
            Ready = value.Ready,
            IsHost = value.IsHost,
            IsLocal = value.IsLocal,
            Mods = value.Mods
        };
    }

    private static void RebuildSnapshotNoLock()
    {
        handlerSnapshot = Handlers.Values
            .OrderBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class Handler
    {
        private readonly Action<AuraLobbySnapshot> changed;
        private readonly Action<string>? warn;

        public Handler(
            string id,
            long generation,
            Action<AuraLobbySnapshot> changed,
            Action<string>? warn)
        {
            Id = id;
            Generation = generation;
            this.changed = changed;
            this.warn = warn;
        }

        public string Id { get; }
        public long Generation { get; }
        public int LeaseCount { get; private set; } = 1;

        public bool Matches(Action<AuraLobbySnapshot> handler)
        {
            return changed.Equals(handler);
        }

        public void AddLease()
        {
            LeaseCount++;
        }

        public bool ReleaseLease()
        {
            LeaseCount--;
            return LeaseCount <= 0;
        }

        public void Invoke(AuraLobbySnapshot snapshot)
        {
            try { changed(snapshot); }
            catch (Exception ex)
            {
                warn?.Invoke(
                    "[LobbySnapshot] subscriber failed: "
                    + Id
                    + " -> "
                    + ex.Message);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private string id;
        private readonly long generation;

        public Subscription(string id, long generation)
        {
            this.id = id;
            this.generation = generation;
        }

        public void Dispose()
        {
            if (id.Length == 0) return;
            lock (Gate)
            {
                if (Handlers.TryGetValue(id, out var handler)
                    && handler.Generation == generation)
                {
                    if (handler.ReleaseLease())
                    {
                        Handlers.Remove(id);
                        RebuildSnapshotNoLock();
                    }
                }
            }
            id = "";
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
