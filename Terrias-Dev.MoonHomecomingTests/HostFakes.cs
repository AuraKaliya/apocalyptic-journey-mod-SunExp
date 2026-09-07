using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

// These adapters model the checked 1.0.24831968 host contracts. The tests execute
// the production scripts, mechanics, hand adapter and rules, without Unity UI.
public interface IStatusManager
{
    string InstanceId { get; }
    int MaxHp { get; set; }
    int CurHp { get; set; }
    int Defend { get; set; }
    Dictionary<string, int> Buffs { get; }
    int ExtraUses { get; set; }
}

public sealed class Status : IStatusManager
{
    private int maxHp = 100;
    public string InstanceId { get; } = Guid.NewGuid().ToString();
    public int MaxHp { get => maxHp; set { CurHp = Math.Min(value, CurHp + Math.Max(0, value - maxHp)); maxHp = value; } }
    public int CurHp { get; set; } = 100;
    public int Defend { get; set; }
    public Dictionary<string, int> Buffs { get; } = new();
    public int ExtraUses { get; set; }
}

public sealed class FightPlayer
{
    public static FightPlayer? Instance { get; set; }
    public IStatusManager Status { get; set; } = new Status();
    private int maximum = 3;
    public int MaxPowerCount { get => maximum; set { CurPowerCount += value - maximum; maximum = Math.Max(0, value); } }
    public int CurPowerCount { get; set; } = 3;
}

public interface IDataConfig
{
    string InstanceID { get; }
    IReadOnlyDictionary<string, string> data { get; }
    Dictionary<string, string> Vars { get; }
}

public sealed class CardConfig : IDataConfig
{
    public CardConfig(string id, int cost = 0)
    {
        data = new ReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["Id"] = id, ["Expend"] = cost.ToString() });
    }
    public string InstanceID { get; } = Guid.NewGuid().ToString();
    public IReadOnlyDictionary<string, string> data { get; }
    public Dictionary<string, string> Vars { get; } = new();
}

public sealed class ScriptExecutor
{
    public ScriptExecutor(IDataConfig card) { dataConfig = card; Self = FightPlayer.Instance!.Status; }
    public IStatusManager Self { get; set; }
    public IDataConfig dataConfig { get; }
    public Dictionary<string, string> Vars => dataConfig.Vars;
    public Dictionary<string, Delegate> ScriptDict { get; } = new();
    public void SetStatus(string _) { }
    public void AddBuff(string id, string value) { Self.Buffs[id] = Math.Min(id == Terrias.Dll.Infrastructure.MoonHomecomingIds.FrostmoonMarrow ? 1 : int.MaxValue, Self.Buffs.GetValueOrDefault(id) + int.Parse(value)); }
    public void ChangeDynamicVar(string name, string value) { if (name != "UseCount") throw new Exception(name); Self.ExtraUses += int.Parse(value); }
    public void BurnCardByData(IDataConfig card) { if (!World.FailBurn) World.Hand.Remove(card); }
}

internal static class World
{
    public static List<IDataConfig> Hand = new();
    public static List<IDataConfig> Discard = new();
    public static List<IStatusManager> Enemies = new();
    public static List<Selection> Selections = new();
    public static List<string> Errors = new();
    public static Action<ScriptExecutor>? OnDraw;
    public static Action<ScriptExecutor, IStatusManager>? OnElement;
    public static int Draws, Gold, Truth, SavedMaximum, ExplicitHeals, AppliedElements, RandomIndex, Domains;
    public static bool FailBurn;
    public static void Reset()
    {
        Terrias.Dll.Mechanics.MoonHomecomingMechanics.EndBattle();
        FightPlayer.Instance = new FightPlayer();
        Hand.Clear(); Discard.Clear(); Enemies.Clear(); Selections.Clear(); Errors.Clear();
        OnDraw = null; OnElement = null;
        Draws = Gold = Truth = ExplicitHeals = AppliedElements = RandomIndex = Domains = 0;
        SavedMaximum = 100; FailBurn = false;
        AuraShared.Core.AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation = true;
        AuraShared.Core.AuraBattleLifecycleRouter.CurrentBattleSessionId++;
    }
    public sealed record Selection(IReadOnlyList<IDataConfig> Cards, Action<IDataConfig> Choose, Action? Cancel);
}

namespace UnityEngine { public static class Random { public static int Range(int minimum, int maximum) => World.RandomIndex; } }
namespace AuraShared.Core
{
    public static class AuraBattleLifecycleStateRuntime { public static bool AcceptsCombatPresentation = true; }
    public static class AuraBattleLifecycleRouter { public static long CurrentBattleSessionId = 1; }
    public sealed class AuraCombatCardZoneSnapshotOptions
    {
        public bool IncludeFightUiActive, IncludeFightUiWait, IncludeExecutorHand, IncludeExecutorWait;
    }
    public sealed class AuraCombatCardReference { public IDataConfig? Config; }
    public sealed class AuraCombatCardZoneSnapshot
    {
        public AuraCombatCardReference[] Cards = Array.Empty<AuraCombatCardReference>();
        public static AuraCombatCardZoneSnapshot Capture(ScriptExecutor? self, AuraCombatCardZoneSnapshotOptions options)
            => new() { Cards = World.Hand.Select(card => new AuraCombatCardReference { Config = card }).ToArray() };
    }
}
namespace AuraCombatAi.Shared
{
    public enum CombatPromptKind { BurnCards }
    public enum CombatPromptZone { Hand }
    public sealed class CombatInteractionHint { public string OwnerModId = "", Purpose = ""; public CombatPromptKind Kind; public CombatPromptZone Zone; public bool Forced; }
}
namespace Terrias.Dll.Infrastructure
{
    public static class TerriasIds { public const string ModId = "Terrias", GravityRipple = "Terrias_terrias_gravity_ripple"; }
    public enum TerriasFieldId { MoonDomain }
    public static class DictionaryUtil
    {
        public static void Set(IDictionary<string, string> vars, string key, string value) => vars[key] = value;
        public static int GetInt(IDictionary<string, string> vars, string key) => vars.TryGetValue(key, out var value) && int.TryParse(value, out var number) ? number : 0;
    }
    public static class TerriasLog
    {
        public static void Error(string text, Exception error) => World.Errors.Add(text + ": " + error.Message);
        public static void Warn(string text) => World.Errors.Add(text);
    }
}
namespace Terrias.Dll.Mechanics
{
    public enum ElementalReactionType { None, Melt, Vaporize, Overloaded, Superconduct, ElectroCharged, Freeze, Swirl, Crystallize, Burning, Bloom, Quicken, Burgeon, Hyperbloom }
    public enum ElementalType { Dendro }
    public static class FieldActivationIntentCatalog { public const string FrostmoonNewGodIntent = "MoonHomecoming.FrostmoonNewGod"; }
    public static class ElementalReactionService
    {
        public static void Apply(ScriptExecutor self, IStatusManager target, ElementalType element, string source) { World.AppliedElements++; World.OnElement?.Invoke(self, target); }
    }
}
namespace Terrias.Dll.GameApi
{
    public static class ExecutorApi { public static void SetBaseScript(ScriptExecutor self, string type) => self.Vars["BaseScript"] = type; }
    public static class ScriptDelegateApi
    {
        public static void BindParameterized(ScriptExecutor self, string script, string id, Action<ScriptExecutor, string> handler) => self.ScriptDict[script] = new Action<ScriptExecutor>(current => handler(current, id));
    }
    public static class CardConfigApi
    {
        public static string Id(IDataConfig card) => card.data["Id"];
        public static int CurrentCost(IDataConfig card) => Math.Max(0, int.Parse(card.data["Expend"]) + int.Parse(card.Vars.GetValueOrDefault("ExCost", "0")));
    }
    public static class CardSelectionApi
    {
        public static bool SelectOneFromCards(ScriptExecutor self, IReadOnlyList<IDataConfig> cards, Func<IDataConfig, bool> predicate, Action<IDataConfig> selected, string caption, Action? onCancelled = null, AuraCombatAi.Shared.CombatInteractionHint? interactionHint = null)
        { World.Selections.Add(new World.Selection(cards.Where(predicate).ToArray(), selected, onCancelled)); return true; }
    }
    public static class StatusApi
    {
        public static bool IsAlive(IStatusManager? status) => status?.CurHp > 0;
        public static int MaxHp(IStatusManager? status) => status?.MaxHp ?? 0;
        public static bool TryAddShield(IStatusManager status, int amount) { status.Defend += amount; return true; }
        public static bool TryHeal(IStatusManager status, int amount) { if (amount <= 0) return false; World.ExplicitHeals++; status.CurHp = Math.Min(status.MaxHp, status.CurHp + amount); return true; }
    }
    public static class PlayerMaxHpApi
    {
        public static bool TrySetNativeMaxHp(IStatusManager status, int maximum, bool persistRole, string source)
        { status.MaxHp = maximum; if (persistRole) World.SavedMaximum = maximum; return true; }
    }
    public static class PlayerPowerApi
    {
        public static bool TryChangeMaxPower(int amount) { FightPlayer.Instance!.MaxPowerCount += amount; return true; }
        public static bool TryGainPower(int amount) { FightPlayer.Instance!.CurPowerCount += amount; return true; }
        public static bool TrySetPower(int amount) { FightPlayer.Instance!.CurPowerCount = Math.Max(0, amount); return true; }
    }
    public static class PlayerApi { public static bool AddMoney(int amount) { World.Gold += amount; return true; } }
    public static class TruthCurrencyApi { public static void Refund(int amount) => World.Truth += amount; }
    public static class BuffApi { public static int Level(IStatusManager status, string id) => status.Buffs.GetValueOrDefault(id); }
    public static class FieldApi { public static void ActivateField(ScriptExecutor self, Infrastructure.TerriasFieldId field, int amount, string source) => World.Domains++; }
    public static class TargetApi { public static IReadOnlyList<IStatusManager> OpposingSideTargets(ScriptExecutor self, IStatusManager source) => World.Enemies; }
    public static class CombatCardApi
    {
        public static bool TryDrawPlayerCards(ScriptExecutor self, int count, string source) { World.Draws += count; for (var i = 0; i < count; i++) World.OnDraw?.Invoke(self); return true; }
    }
    public static class CardApi
    {
        public static bool AddCardToDiscardPile(ScriptExecutor self, string id) { World.Discard.Add(new CardConfig(id)); return true; }
    }
}
