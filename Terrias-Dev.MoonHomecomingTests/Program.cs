using System;
using System.Linq;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Scripting;

var assertions = 0;

World.Reset();
var player = FightPlayer.Instance!;
var first = Card("moon_chronicle_i");
Check(player.MaxPowerCount == 3, "initializing or refreshing a Chronicle does not grant its draw reward");
Draw(first);
MoonHomecomingScripts.Init(first, "moon_chronicle_i");
Check(player.MaxPowerCount == 4 && player.CurPowerCount == 4, "first Chronicle raises the cap and fills the new point exactly once");
Draw(first);
Check(player.MaxPowerCount == 5 && player.CurPowerCount == 5, "discarding and drawing the same Chronicle again can grant another point");
player.Status.CurHp = 40;
Draw(Card("moon_chronicle_ii"));
Check(player.Status.MaxHp == 105 && player.Status.CurHp == 45 && World.SavedMaximum == 105 && World.ExplicitHeals == 0,
    "Chronicle II saves adventure growth and relies on native HP delta recovery without a second heal");
var third = Card("moon_chronicle_iii");
Draw(third); Draw(third);
Check(player.Status.Defend == 20, "Chronicle III grants its proportional shield on each genuine draw");
MoonHomecomingMechanics.EndBattle();
MoonHomecomingMechanics.EndBattle();
Check(player.MaxPowerCount == 3 && player.CurPowerCount == 3 && World.SavedMaximum == 105,
    "repeated battle teardown removes temporary power once and retains adventure HP");

World.Reset();
player = FightPlayer.Instance!;
Draw(Card("moon_chronicle_i"));
player.MaxPowerCount += 2;
player.CurPowerCount = 0;
MoonHomecomingMechanics.EndBattle();
Check(player.MaxPowerCount == 5 && player.CurPowerCount == 0, "cleanup keeps unrelated cap bonuses and clamps spent current power");
Draw(Card("moon_chronicle_i"));
FightPlayer.Instance = new FightPlayer();
MoonHomecomingMechanics.EndBattle();
Check(FightPlayer.Instance.MaxPowerCount == 3, "an old actor's pending cleanup cannot subtract from a new battle actor");

World.Reset();
player = FightPlayer.Instance!;
Use(Card("frostmoon_new_god"));
Use(Card("frostmoon_new_god"));
Check(World.Domains == 2 && player.Status.Buffs[MoonHomecomingIds.FrostmoonMarrow] == 1,
    "replaying the new god can restore the domain but does not stack Marrow");
player.Status.MaxHp = 199;
player.Status.CurHp = 40;
World.Enemies.AddRange(new[] { new Status(), new Status(), new Status() });
World.OnElement = (source, target) => MoonHomecomingMechanics.ResolveReactionGrowth(source.Self, ElementalReactionType.Bloom);
Use(Card("flower_sea_moon_night"));
Check(World.AppliedElements == 3 && player.Status.MaxHp == 207 && World.SavedMaximum == 207 && player.Status.Defend == 41,
    "Flower Sea applies to each target, receives three compounding reaction rewards and shields after growth");
MoonHomecomingMechanics.ResolveReactionGrowth(player.Status, ElementalReactionType.Hyperbloom);
var remote = new Status();
remote.Buffs[MoonHomecomingIds.FrostmoonMarrow] = 1;
MoonHomecomingMechanics.ResolveReactionGrowth(remote, ElementalReactionType.Bloom);
Check(player.Status.MaxHp == 207 && remote.MaxHp == 100 && World.SavedMaximum == 207,
    "unrelated reactions and a different player's reactions cannot mutate this player's adventure");
player.Status.Buffs.Remove(MoonHomecomingIds.FrostmoonMarrow);
MoonHomecomingMechanics.ResolveReactionGrowth(player.Status, ElementalReactionType.ElectroCharged);
Check(player.Status.MaxHp == 207, "removing Marrow immediately stops its reaction reward");

World.Reset();
player = FightPlayer.Instance!;
World.Hand.AddRange(new[] { new CardConfig(MoonHomecomingIds.ChronicleI), new CardConfig(MoonHomecomingIds.ChronicleI), new CardConfig(MoonHomecomingIds.ChronicleIII) });
var homecoming = Card("moon_homecoming_night");
MoonHomecomingMechanics.PrepareHomecoming(homecoming);
World.OnDraw = _ =>
{
    var gained = Card("moon_chronicle_ii");
    World.Hand.Add(gained.dataConfig);
    Draw(gained);
};
Use(homecoming); Use(homecoming);
Check(player.CurPowerCount == 7 && World.Draws == 2 && player.Status.ExtraUses == 2
    && !player.Status.Buffs.ContainsKey(TerriasIds.GravityRipple) && World.Hand.Count == 5,
    "repeated Homecoming uses share the captured chapters and do not consume them or enable a newly drawn chapter mid-action");
MoonHomecomingMechanics.PrepareHomecoming(homecoming);
Use(homecoming);
Check(player.Status.Buffs[TerriasIds.GravityRipple] == 5, "a later action captures the now-held second chapter");
World.Reset();
Use(Card("moon_homecoming_night"));
Check(World.Draws == 0 && FightPlayer.Instance!.Status.ExtraUses == 0, "Homecoming with no Chronicles grants no conditional rewards");
Use(Card("kuutar_morning_mist"));
Check(World.Draws == 1 && FightPlayer.Instance!.Status.Buffs[TerriasIds.GravityRipple] == 3,
    "Kuutar grants one draw and three Ripples before the separate native action-after resolution");

World.Reset();
player = FightPlayer.Instance!;
player.Status.MaxHp = 200; player.Status.CurHp = 20;
var expensive = new CardConfig("test_expensive", 3);
var zero = new CardConfig("test_zero", 0);
World.Hand.AddRange(new[] { expensive, zero });
var offering = Card("moon_offering");
Check(offering.Vars["Usable"] == "1", "Offering is usable with another hand card");
Use(offering); Use(offering);
Check(World.Selections.Count == 1, "repeated Offering serializes selection prompts");
expensive.Vars["ExCost"] = "-1";
World.Selections[0].Choose(expensive);
Check(player.Status.CurHp == 60 && World.Selections.Count == 2 && World.Selections[1].Cards.Count == 1
    && ReferenceEquals(World.Selections[1].Cards[0], zero),
    "Offering reads cost at sacrifice time and the next prompt excludes the already burned card");
World.Selections[1].Choose(zero);
Check(player.Status.CurHp == 60 && World.Hand.Count == 0 && World.ExplicitHeals == 1,
    "a zero-cost offering is burned without inventing a minimum heal");
MoonHomecomingScripts.Init(offering, "moon_offering");
Check(offering.Vars["Usable"] == "0", "an empty hand disables Offering");

World.Reset();
player = FightPlayer.Instance!;
player.Status.CurHp = 20;
var failed = new CardConfig("test_failed", 2);
World.Hand.Add(failed);
Use(Card("moon_offering"));
World.FailBurn = true;
World.Selections[0].Choose(failed);
Check(player.Status.CurHp == 20 && World.Hand.Contains(failed), "a failed sacrifice gives no healing");
World.FailBurn = false;
Use(Card("moon_offering"));
var expired = World.Selections.Last();
World.Reset();
World.Hand.Add(failed);
Use(Card("moon_offering"));
expired.Choose(failed);
Check(World.Hand.Contains(failed) && World.Selections.Count == 1 && World.ExplicitHeals == 0,
    "an old battle's delayed callback neither burns new-battle cards nor drains its selection queue");
World.Selections[0].Cancel!();
Use(Card("moon_offering"));
Check(World.Selections.Count == 2, "cancelled selection releases the queue for a subsequent offering");

World.Reset();
Use(Card("new_moon_blessing")); Use(Card("new_moon_blessing"));
Check(World.Gold == 60 && World.Truth == 180, "repeated blessing effects each grant their full currency rewards");
foreach (var index in new[] { 0, 0, 1, 2 }) { World.RandomIndex = index; Use(Card("luonnotar")); }
Check(World.Discard.Count == 4 && World.Discard.Count(card => card.data["Id"] == MoonHomecomingIds.ChronicleI) == 2
    && FightPlayer.Instance!.Status.Buffs["buff_rebirth"] == 120 && World.Hand.Count == 0
    && FightPlayer.Instance.MaxPowerCount == 3 && World.SavedMaximum == 100,
    "Luonnotar permits duplicate Chronicles in discard and does not trigger their draw rewards or add adventure cards");

World.Reset();
var foreign = Card("new_moon_blessing");
foreign.Self = new Status();
Use(foreign);
AuraBattleLifecycleStateRuntime.AcceptsCombatPresentation = false;
Use(Card("new_moon_blessing"));
Draw(Card("moon_chronicle_ii"));
Check(World.Gold == 0 && World.Truth == 0 && World.SavedMaximum == 100,
    "foreign-owner and terminal calls cannot award local persistent resources");

Console.WriteLine($"Moon Homecoming production behavior passed: {assertions} assertions.");

ScriptExecutor Card(string id)
{
    var self = new ScriptExecutor(new CardConfig("Terrias_terrias_" + id));
    MoonHomecomingScripts.Init(self, id);
    return self;
}
void Use(ScriptExecutor self) => ((Action<ScriptExecutor>)self.ScriptDict["UseScript"])(self);
void Draw(ScriptExecutor self) => ((Action<ScriptExecutor>)self.ScriptDict["DrawScript"])(self);
void Check(bool condition, string message)
{
    if (World.Errors.Count > 0) throw new Exception("Production script failed: " + string.Join("; ", World.Errors));
    if (!condition) throw new Exception(message);
    assertions++;
}
