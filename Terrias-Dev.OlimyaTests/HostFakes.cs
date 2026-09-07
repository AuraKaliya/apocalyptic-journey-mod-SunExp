using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Hooks;
using Terrias.Dll.Network;
using Witch.Core;

// Narrow models of the verified native 1.0.24831968 contracts. Production role
// hooks, services, API adapter, scripts and RPC handler run unchanged against them.
public interface IStatusManager
{
    string InstanceId { get; }
    int CurHp { get; set; }
    int Defend { get; set; }
    object fatherObject { get; }
    string Role { get; set; }
    RoleTable? Wallet { get; }
    Dictionary<string, int> Buffs { get; }
    void AddBuff(string id, int amount);
    void RemoveBuff(string id);
}

public sealed class Status : IStatusManager
{
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public int CurHp { get; set; } = 80;
    public int Defend { get; set; }
    public bool Downed { get; set; }
    public object fatherObject { get; init; } = new object();
    public string Role { get; set; } = OlimyaIds.Career;
    public RoleTable? Wallet { get; set; }
    public Dictionary<string, int> Buffs { get; } = new();
    public void AddBuff(string id, int amount) => Buffs[id] = Math.Min(1, Buffs.GetValueOrDefault(id) + amount);
    public void RemoveBuff(string id) => Buffs.Remove(id);
}

public sealed class Enemy { }
public sealed class CustomDamageType { public bool ignoreDefend; }
public sealed class FightPlayer
{
    public static FightPlayer? Instance;
    public IStatusManager Status = null!;
}

public sealed class RoleTable
{
    public static RoleTable? Instance;
    public int money = 100;
    public int MoneyMultiplier = 100;
    public Dictionary<string, string> SpecialVarMap = new();
    public string Career = OlimyaIds.Career;
    public Action? OnMoneyChanged;
    public int Money
    {
        get => money;
        set
        {
            Host.Before("RoleTable.set_Money", this, value);
            if (value != money)
            {
                money = (int)Math.Clamp(value > money ? (long)money + ((long)value - money) * MoneyMultiplier / 100 : value, 0, 2147483646L);
                Host.Before("RoleTable.OnPropertyChanged", this, "Money");
                OnMoneyChanged?.Invoke();
            }
            Host.After("RoleTable.set_Money", this, value);
        }
    }
}

public sealed class ScriptExecutor
{
    public IStatusManager Self = null!;
    public IStatusManager? Target;
    public List<IStatusManager> Object = new();
    public Dictionary<string, string> Vars = new();
    public Dictionary<string, Delegate> ScriptDict = new();
    public void SetStatus(string mode) { Object.Clear(); Object.Add(Self); }
    public void ChangeMoney(string amount)
    {
        if (Self.Wallet == null) return; // Native receiving endpoint requires a player wallet.
        var oldRole = RoleTable.Instance;
        var oldPlayer = FightPlayer.Instance;
        RoleTable.Instance = Self.Wallet;
        FightPlayer.Instance = new FightPlayer { Status = Self };
        try { Self.Wallet.Money += int.Parse(amount); Host.GoldRecipients.Add(Self.InstanceId); }
        finally { RoleTable.Instance = oldRole; FightPlayer.Instance = oldPlayer; }
    }
}

internal static class Host
{
    public static readonly Dictionary<string, Action<ModHookContext>> BeforeHooks = new(), AfterHooks = new();
    public static readonly Dictionary<string, IStatusManager> Statuses = new();
    public static readonly Dictionary<string, string> CompanionOwners = new();
    public static readonly List<string> GoldRecipients = new(), Warnings = new(), Errors = new();
    public static TerriasBattleLifecycleSubscription Battle = null!;
    public static string? Form;
    public static bool Server = true, ClientOnly;
    public static string Sender = "player-a";
    public static int Epoch = 1, Cooldown;
    public static int Sent;

    public static void Before(string key, object target, params object[] args)
    { if (BeforeHooks.TryGetValue(key, out var hook)) hook(new ModHookContext { Target = target, Arguments = args }); }
    public static void After(string key, object target, params object[] args)
    { if (AfterHooks.TryGetValue(key, out var hook)) hook(new ModHookContext { Target = target, Arguments = args }); }
    public static Status Player(string id, string role = OlimyaIds.Career)
    {
        var body = new FightPlayer();
        var status = new Status { InstanceId = id, Role = role, Wallet = new RoleTable { Career = role }, fatherObject = body };
        body.Status = status;
        Statuses[id] = status;
        return status;
    }
    public static void Reset()
    {
        Terrias.Dll.Application.OlimyaRoleApplication.EndBattle();
        BeforeHooks.Clear(); AfterHooks.Clear(); Statuses.Clear(); CompanionOwners.Clear();
        GoldRecipients.Clear(); Warnings.Clear(); Errors.Clear(); Form = null;
        Server = true; ClientOnly = false; Sender = "player-a"; Epoch++; Cooldown = 0; Sent = 0;
        AuraShared.Core.AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation = true;
        Witch.UI.Window.FightUI.IsReset = false;
        var player = Player("player-a");
        FightPlayer.Instance = (FightPlayer)player.fatherObject;
        RoleTable.Instance = player.Wallet;
        OlimyaRuntime.Initialize(new Witch.Mod.ModConfig());
        OlimyaNetworkAdapter.Initialize();
        Battle.BattleOpening!(new ModHookContext());
    }
    public static void Hit(IStatusManager target, IStatusManager source, int amount, bool bypassShield = false, Action? duringHurt = null)
    {
        var type = new CustomDamageType { ignoreDefend = bypassShield };
        var args = new object[] { target, amount, new object(), source, amount, "Normal", "test-card" };
        Before("CustomDamageType.ApplyDamage", type, args);
        var shield = bypassShield ? 0 : Math.Min(Math.Max(0, target.Defend), Math.Max(0, amount));
        target.Defend -= shield;
        target.CurHp = Math.Max(0, target.CurHp - Math.Max(0, amount - shield));
        duringHurt?.Invoke();
        Before("CustomDamageType.ShowDamage", type, target, amount, new object(), source, amount);
        After("CustomDamageType.ApplyDamage", type, args);
    }
    public static void StartTurn() => Battle.PlayerTurnEntering!(new ModHookContext());
    public static ScriptExecutor Skill(IStatusManager? target = null) => new() { Self = FightPlayer.Instance!.Status, Target = target };
}

namespace Witch.Core { public sealed class ModHookContext { public object? Target; public object[] Arguments = Array.Empty<object>(); } }
namespace Witch.Mod { public sealed class ModConfig { } }
namespace Witch.UI { public sealed class Placeholder { } }
namespace Witch.UI.Window { public static class FightUI { public static bool IsReset; } }
namespace AuraShared.Core { public static class AuraBattleLifecycleStateRuntime { public static bool AcceptsCombatPresentation = true; } }
namespace Network.Command { public abstract class RpcCommandBase { public abstract void CmdExecute(); public abstract void RpcExecute(); } }

namespace Terrias.Dll.Infrastructure
{
    public static class TerriasLog
    {
        public static void Warn(string text) => Host.Warnings.Add(text);
        public static void Error(string text, Exception error) => Host.Errors.Add(text + ": " + error.Message);
    }
}
namespace Terrias.Dll.GameApi
{
    public static class StatusApi
    {
        public static bool IsAlive(IStatusManager? status) => status?.CurHp > 0 && !(status is Status value && value.Downed);
        public static IStatusManager? FindById(string id) => Host.Statuses.GetValueOrDefault(id);
        public static bool TryAddShield(IStatusManager status, int amount) { status.Defend += amount; return true; }
    }
    public static class PlayerApi
    {
        public static string GetCurrentCareerId() => RoleTable.Instance?.Career ?? "";
        public static string LocalPlayerStatusId() => FightPlayer.Instance?.Status.InstanceId ?? "";
        public static void SetSkillTime(string id, int value) => Host.Cooldown = value;
        public static int GetSkillTime(string id) => Host.Cooldown;
        public static void ShowCaption(string value) { }
    }
    public static class BuffApi { public static int Level(IStatusManager target, string id) => target.Buffs.GetValueOrDefault(id); }
    public static class DamageApi
    {
        public static ScriptExecutor CreateCardSourceExecutor(IStatusManager source, string card, string origin) => new() { Self = source };
    }
    public static class TargetApi
    {
        public static IStatusManager? PrimaryTarget(ScriptExecutor self) => self.Target;
        public static IReadOnlyList<IStatusManager> OpposingSideTargets(ScriptExecutor self, IStatusManager source) => Host.Statuses.Values.Where(status => status.fatherObject is Enemy).ToArray();
        public static void SetStatusForTarget(ScriptExecutor self, IStatusManager? target, string fallback) { self.Target = target; self.Object = target == null ? new() : new() { target }; }
    }
    public static class ExecutorApi
    {
        public static void SetBaseScript(ScriptExecutor self, string type, bool canSelf) { self.Vars["BaseScript"] = type; self.Vars["CanSelf"] = canSelf.ToString(); }
    }
    public static class ScriptDelegateApi
    {
        public static void BindParameterized(ScriptExecutor self, string name, string id, Action<ScriptExecutor, string> action) => self.ScriptDict[name] = new Action<ScriptExecutor>(current => action(current, id));
    }
}
namespace Terrias.Dll.Mechanics
{
    public static class PolymorphStateStore
    {
        public static string EffectiveCombatRoleIdFor(IStatusManager? status) => status == null ? "" : ReferenceEquals(status, FightPlayer.Instance?.Status) ? Host.Form ?? RoleTable.Instance?.Career ?? status.Role : status.Role;
    }
    public static class CompanionAuthorityService
    {
        public static int BattleEpoch => Host.Epoch;
        public static bool IsAuthoritative() => Host.Server;
    }
    public sealed class CompanionIdentity { public string SemanticOwnerStatusId = ""; }
    public static class CompanionOwnershipService
    {
        public static CompanionIdentity? Find(string id) => Host.CompanionOwners.TryGetValue(id, out var owner) ? new() { SemanticOwnerStatusId = owner } : null;
    }
    public sealed class ChildState { public string OwnerStatusId = ""; }
    public static class SpiritStateStore { public static ChildState? Find(string id) => null; }
    public static class ProjectionStateStore { public static ChildState? Find(string id) => null; }
}
namespace Terrias.Dll.Hooks
{
    public static class TerriasHookRegistry
    {
        public static bool Before(Witch.Mod.ModConfig config, string target, Action<ModHookContext> handler, string owner) { Host.BeforeHooks[target] = handler; return true; }
        public static bool After(Witch.Mod.ModConfig config, string target, Action<ModHookContext> handler, string owner) { Host.AfterHooks[target] = handler; return true; }
    }
    public sealed class TerriasBattleLifecycleSubscription
    {
        public Action<ModHookContext>? PlayerTurnEntering, BattleInitializing, BattleRestarting, BattleEnded, BattleOpening;
    }
    public static class TerriasBattleLifecycleRouter
    { public static void Register(string name, TerriasBattleLifecycleSubscription subscription) => Host.Battle = subscription; }
}
namespace Terrias.Dll.Contracts
{
    public sealed class TerriasRpcSender
    {
        public static readonly TerriasRpcSender Unbound = new();
        public string PlayerId = "";
        public bool IsAvailable, IsLobbyMember;
    }
}
namespace Terrias.Dll.Network
{
    using Terrias.Dll.Contracts;
    public interface ITerriasServerBoundRpcCommand { void BindServerSender(TerriasRpcSender sender); }
    public static class TerriasRpcAuthorityRuntime
    { public static TerriasRpcSender CreateLocalServerSender(string source) => new() { PlayerId = Host.Sender, IsAvailable = true, IsLobbyMember = true }; }
    public static class TerriasStatusOwnershipPolicy
    { public static bool SenderOwnsStatus(string player, string status, out string detail) { detail = ""; return player == status; } }
    public static class TerriasNetworkRuntime
    {
        public static bool IsClientOnly() => Host.ClientOnly;
        public static bool NetworkActive() => Host.ClientOnly || Host.Statuses.Values.Count(status => status.Wallet != null) > 1;
        public static bool Send(global::Network.Command.RpcCommandBase command, string source)
        {
            Host.Sent++;
            var server = Host.Server;
            Host.Server = true;
            ((ITerriasServerBoundRpcCommand)command).BindServerSender(TerriasRpcAuthorityRuntime.CreateLocalServerSender(source));
            try { command.CmdExecute(); } finally { Host.Server = server; }
            return true;
        }
    }
}
