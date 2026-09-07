using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.Application;
using Terrias.Dll.Contracts;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using Terrias.Dll.Network;
using Terrias.Dll.Scripting;
using Witch.Core;

var assertions = 0;
Host.Reset();
var wallet = RoleTable.Instance!;
var notifications = 0;
wallet.OnMoneyChanged = () => notifications++;
wallet.Money += 1;
wallet.Money += 1;
Check(wallet.Money == 103 && notifications == 2 && wallet.SpecialVarMap[OlimyaIds.IncomeRemainder] == "0",
    "two one-coin receipts manufacture one coin without an extra wallet notification");
wallet.Money -= 1; wallet.Money -= 1;
Check(wallet.Money == 102 && wallet.SpecialVarMap[OlimyaIds.SpendingRemainder] == "0",
    "two one-coin payments manufacture one coin independently of income");
wallet.Money = wallet.Money;
Check(wallet.Money == 102 && notifications == 4, "assigning the same balance is not an income event");

Host.Reset();
wallet = RoleTable.Instance!;
wallet.Money += 1;
wallet.Money -= 2;
Check(wallet.Money == 100 && wallet.SpecialVarMap[OlimyaIds.IncomeRemainder] == "1",
    "spending-generated money does not consume an income remainder or recursively manufacture coins");
wallet.Money += 1;
Check(wallet.Money == 102, "the retained income remainder applies to a later real receipt");
Host.Form = "career_other";
wallet.Money += 100; wallet.Money -= 20;
Check(wallet.Money == 182, "changing away from Olimya disables income and spending manufacture");
Host.Form = OlimyaIds.Career;
wallet.Money += 2;
Check(wallet.Money == 185, "returning to Olimya resumes manufacture without retroactive credit");
wallet.Career = "career_other";
Host.Form = null;
wallet.Money += 2;
Host.Form = OlimyaIds.Career;
wallet.Money += 2;
Check(wallet.Money == 190, "another career gains the passive only while actually transformed into Olimya");

Host.Reset();
wallet = RoleTable.Instance!;
wallet.MoneyMultiplier = 200;
wallet.Money += 10;
Check(wallet.Money == 130 && wallet.MoneyMultiplier == 200,
    "normal income receives the native multiplier; the manufactured half is exact and does not alter that multiplier");
wallet.money = 100;
wallet.MoneyMultiplier = 100;
var reentered = false;
wallet.OnMoneyChanged = () => { if (!reentered) { reentered = true; wallet.Money += 2; } };
wallet.Money += 2;
Check(wallet.Money == 106, "nested legitimate income is counted once without recounting the parent transaction");

Host.Reset();
wallet = RoleTable.Instance!;
Host.Before("RoleTable.Init", wallet);
wallet.Money += 100;
Host.After("RoleTable.Init", wallet);
Check(wallet.Money == 200 && wallet.SpecialVarMap.Count == 0, "initial balance setup grants no manufacture or remainder");
wallet.Money += 1;
var restored = new RoleTable { money = wallet.money, SpecialVarMap = new Dictionary<string, string>(wallet.SpecialVarMap) };
RoleTable.Instance = restored;
FightPlayer.Instance!.Status.Role = OlimyaIds.Career;
restored.Money += 1;
Check(restored.Money == 203, "restored balances are a baseline while saved remainders survive the adventure");
FightPlayer.Instance = null;
restored.Money -= 2;
Check(restored.Money == 202, "Olimya's spending passive also works outside combat");
var foreignWallet = new RoleTable();
foreignWallet.Money += 2;
Check(foreignWallet.Money == 102 && restored.Money == 202, "a different role table does not receive the local career passive");
restored.money = 2147483645;
restored.SpecialVarMap[OlimyaIds.IncomeRemainder] = "1";
restored.Money += 1;
Check(restored.Money == 2147483646, "manufacture respects the host wallet limit without integer wrapping");

Host.Reset();
var player = FightPlayer.Instance!.Status;
var enemy = Enemy("enemy");
player.Defend = 10;
Host.Hit(player, enemy, 10);
Check(player.CurHp == 80 && player.Defend == 0, "absorbed damage generates no Dreamweaver shield");
Host.Hit(player, enemy, 12);
Check(player.CurHp == 68 && player.Defend == 12, "actual HP loss generates an equal shield after one damage instance");
Host.Hit(player, enemy, 15);
Check(player.CurHp == 65 && player.Defend == 3, "a mixed hit grants shield only for HP lost beyond the previous shield");
player.CurHp -= 5;
Check(player.Defend == 3, "a direct HP payment outside damage produces no shield");
Host.Form = "career_other";
Host.Hit(player, enemy, 5, bypassShield: true);
Check(player.Defend == 3, "changing away disables Dreamweaver immediately");
Host.Form = OlimyaIds.Career;
Host.Hit(player, enemy, 5, bypassShield: true);
Check(player.Defend == 8, "shield-ignoring damage can trigger Dreamweaver for its actual HP loss");
Host.StartTurn();
Check(player.Defend == 0, "old shields are cleared before the native start-of-turn effects");
Host.Hit(player, enemy, 4, bypassShield: true);
Check(player.Defend == 4, "a later start-of-turn damage effect creates a new shield that remains");
Witch.UI.Window.FightUI.IsReset = true;
Host.StartTurn();
Check(player.Defend == 4, "a reset-only player-turn UI refresh cannot clear a new shield");
Witch.UI.Window.FightUI.IsReset = false;
player.Defend = 0;
player.CurHp = 5;
Host.Hit(player, enemy, 100, bypassShield: true);
Check(player.CurHp == 0 && player.Defend == 0, "a lethal hit is not prevented by a shield granted after damage");

Host.Reset();
var downed = (Status)FightPlayer.Instance!.Status;
downed.Downed = true;
downed.CurHp = 10;
var rescuer = Host.Player("rescuer");
var rescueType = new CustomDamageType { ignoreDefend = true };
var rescueArgs = new object[] { downed, 70, new object(), rescuer, 70, "True", "rescue" };
Host.Before("CustomDamageType.ApplyDamage", rescueType, rescueArgs);
downed.CurHp = 80;
downed.Downed = false;
Host.Before("CustomDamageType.ShowDamage", rescueType, downed, 70, new object(), rescuer, 70);
Host.After("CustomDamageType.ApplyDamage", rescueType, rescueArgs);
Check(downed.Defend == 0, "a first-death rescue converted into healing is not mistaken for HP damage");

Host.Reset();
player = FightPlayer.Instance!.Status;
enemy = Enemy("nested-enemy");
Host.Hit(player, enemy, 5, duringHurt: () => Host.Hit(player, enemy, 3));
Check(player.CurHp == 72 && player.Defend == 8, "nested damage frames award each HP loss once");

Host.Reset();
player = FightPlayer.Instance!.Status;
enemy = Enemy("gold-target", hp: 25, shield: 10);
var skill = Host.Skill(enemy);
OlimyaScripts.InitCareer(skill);
OlimyaScripts.Init(skill);
Check(Host.Cooldown == 0 && skill.Vars["BaseScript"] == "AttackCardItem" && skill.Vars["CanSelf"] == "False",
    "the single skill starts ready and targets enemies");
OlimyaScripts.Use(skill);
Check(Host.Cooldown == 3 && enemy.Buffs[OlimyaIds.Goldenized] == 1 && skill.Target == enemy && skill.Object.Count == 1,
    "Golden Touch creates one mark, preserves its selected target, and starts the three-turn cooldown");
var walletBefore = RoleTable.Instance!.Money;
Host.Hit(enemy, player, 100);
Check(RoleTable.Instance.Money == walletBefore + 52 && Host.GoldRecipients.Count == 1,
    "a marked hit grants only 35 actual HP-plus-shield loss, then Olimya manufactures 17 coins");
Host.Before("CustomDamageType.ShowDamage", new CustomDamageType(), enemy, 100, new object(), player, 100);
Check(Host.GoldRecipients.Count == 1, "a replicated damage presentation outside an application frame never awards gold twice");
Host.Form = "career_other";
Host.StartTurn();
Check(!enemy.Buffs.ContainsKey(OlimyaIds.Goldenized) && Host.Cooldown == 3,
    "marks expire on the caster's next turn after changing form, while her cooldown remains frozen");
Host.Form = OlimyaIds.Career;
Host.StartTurn();
Check(Host.Cooldown == 2, "the active career resumes cooldown progression");

Host.Reset();
player = FightPlayer.Instance!.Status;
enemy = Enemy("cooperative-enemy", hp: 1000);
enemy.AddBuff(OlimyaIds.Goldenized, 1);
var ally = Host.Player("player-b", "career_other");
Host.Hit(enemy, ally, 12);
Check(ally.Wallet!.Money == 112 && RoleTable.Instance!.Money == 100,
    "another player receives their own hit income rather than paying the caster");
var spirit = new Status { InstanceId = "spirit-b", Role = "spirit" };
Host.Statuses[spirit.InstanceId] = spirit;
Host.CompanionOwners[spirit.InstanceId] = ally.InstanceId;
Host.Hit(enemy, spirit, 8);
Check(ally.Wallet.Money == 120 && Host.GoldRecipients.Last() == "player-b", "a companion's income belongs to its semantic owner");
var environmental = Enemy("unowned-source");
Host.Hit(enemy, environmental, 7);
Check(ally.Wallet.Money == 120 && RoleTable.Instance!.Money == 100, "unowned non-player sources cannot manufacture a player payout");

Host.Reset();
enemy = Enemy("refresh-target");
var ownerB = Host.Player("player-b");
var markA = Command("player-a", enemy.InstanceId, 1);
var markB = Command("player-b", enemy.InstanceId, 1);
Check(OlimyaRoleApplication.HandleAuthoritative(markA, true), "the owner can apply the first mark");
Check(!OlimyaRoleApplication.HandleAuthoritative(markA, true), "duplicate commands are rejected");
Check(OlimyaRoleApplication.HandleAuthoritative(markB, true) && enemy.Buffs[OlimyaIds.Goldenized] == 1,
    "a second caster refreshes the single mark without stacking");
var endA = Command("player-a", "", 2, OlimyaGoldenizationCommandKind.OwnerTurnStarted);
OlimyaRoleApplication.HandleAuthoritative(endA, true);
Check(enemy.Buffs.ContainsKey(OlimyaIds.Goldenized), "the earlier caster's turn does not clear a refreshed mark");
OlimyaRoleApplication.HandleAuthoritative(Command("player-b", "", 2, OlimyaGoldenizationCommandKind.OwnerTurnStarted), true);
Check(!enemy.Buffs.ContainsKey(OlimyaIds.Goldenized), "the current caster's turn clears the mark");
Check(!OlimyaRoleApplication.HandleAuthoritative(Command("player-a", enemy.InstanceId, 3), false), "unowned requests are rejected");
Host.Epoch++;
Check(!OlimyaRoleApplication.HandleAuthoritative(Command("player-a", enemy.InstanceId, 4, epoch: Host.Epoch - 1), true),
    "prior-battle commands cannot mutate the next battle");
Host.Battle.BattleInitializing!(new ModHookContext());
Check(!OlimyaRoleApplication.HandleAuthoritative(Command("player-a", enemy.InstanceId, 5), true),
    "even a matching epoch cannot apply a mark while the next battle is still materializing");
Host.Battle.BattleOpening!(new ModHookContext());
Check(OlimyaRoleApplication.HandleAuthoritative(Command("player-a", enemy.InstanceId, 6), true),
    "normal mark requests resume after battle opening");

Host.Reset();
enemy = Enemy("rpc-enemy");
Host.ClientOnly = true; Host.Server = false;
OlimyaScripts.Use(Host.Skill(enemy));
Check(Host.Sent == 1 && enemy.Buffs.ContainsKey(OlimyaIds.Goldenized), "the client routes its mark through the bound server command");
Host.Server = true;
var forged = new RpcOlimyaGoldenization { Command = Command("player-a", enemy.InstanceId, 10) };
forged.BindServerSender(new TerriasRpcSender { PlayerId = "player-other", IsAvailable = true, IsLobbyMember = true });
forged.CmdExecute();
Check(Host.Warnings.Count == 1, "the RPC refuses a payload owner that differs from its bound sender");

Check(!OlimyaRules.IsOlimya("fake_olimya") && OlimyaRules.IsOlimya("OLIMYA"), "role identity uses canonical aliases, not substring matches");
Console.WriteLine($"Olimya production behavior passed: {assertions} assertions.");

Status Enemy(string id, int hp = 80, int shield = 0)
{
    var enemy = new Status { InstanceId = id, fatherObject = new Enemy(), Role = "enemy", CurHp = hp, Defend = shield };
    Host.Statuses[id] = enemy;
    return enemy;
}
OlimyaGoldenizationCommand Command(string owner, string target, long sequence,
    OlimyaGoldenizationCommandKind kind = OlimyaGoldenizationCommandKind.Apply, int? epoch = null)
    => new() { OwnerStatusId = owner, TargetStatusId = target, Sequence = sequence, Kind = kind, BattleEpoch = epoch ?? Host.Epoch, Token = Guid.NewGuid().ToString("N") };
void Check(bool condition, string message)
{
    if (Host.Errors.Count != 0) throw new Exception(string.Join("; ", Host.Errors));
    if (!condition) throw new Exception(message);
    assertions++;
}
