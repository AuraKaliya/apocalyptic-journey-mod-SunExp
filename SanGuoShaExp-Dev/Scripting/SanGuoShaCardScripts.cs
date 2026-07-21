using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Data.Save;
using SanGuoShaExp.Dll.GameApi;
using SanGuoShaExp.Dll.Hooks;
using SanGuoShaExp.Dll.Infrastructure;

namespace SanGuoShaExp.Dll.Scripting;

public static class SanGuoShaCardScripts
{
    private const string LinkageResolving = "SanGuoShaExpLinkageResolving";
    private const string SomethingFromNothingId = "SanGuoShaExp_sanguosha_wuzhong_shengyou";
    private const string HiddenMilitaryPackId = "SanGuoShaExp_sanguosha_cardpack_military";

    private static readonly HashSet<string> ShaIds = new(StringComparer.Ordinal)
    {
        "sha",
        "huosha",
        "leisha",
        "SanGuoShaExp_sanguosha_sha",
        "SanGuoShaExp_sanguosha_huosha",
        "SanGuoShaExp_sanguosha_leisha"
    };

    public static void Init(ScriptExecutor self, string id)
    {
        InitCore(self, id);
    }

    // The game refreshes target-dependent damage previews only when the InitScript
    // text contains "Damage". Keep this public entry point as the CSV marker while
    // sharing the actual initialization logic with non-damage cards.
    public static void InitDamage(ScriptExecutor self, string id)
    {
        InitCore(self, id);
    }

    private static void InitCore(ScriptExecutor self, string id)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, IsTargetCard(id) ? "AttackCardItem" : "CommonCardItem", canSelf: CanTargetSelf(id));
            AddDescriptions(self, id);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("SanGuoSha card init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            UseCore(self, id, false);
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("SanGuoSha card use failed: " + id, ex);
        }
    }

    public static void ResolveLinkage(ScriptExecutor self)
    {
        if (!SanGuoShaCombatRuntime.IsCombatActive)
        {
            return;
        }

        if (ExecutorApi.GetVar(self, LinkageResolving, "0") == "1")
        {
            return;
        }

        ExecutorApi.SetVar(self, LinkageResolving, "1");
        try
        {
            var usedPile = UsedPile(self, out var excludedCurrent);
            var card = usedPile.LastOrDefault(candidate =>
                candidate?.data != null && HasTag(candidate, SanGuoShaExpIds.LinkageTag));
            var selectedId = card?.data?.GetValueOrDefault("Id", card.InstanceID) ?? "";

            SanGuoShaExpLog.Info(
                "Linkage resolve: source=" + (self.dataConfig?.data?.GetValueOrDefault("Id", "") ?? "")
                + ", usedPile=" + usedPile.Count
                + ", excludedCurrent=" + excludedCurrent
                + ", selected=" + selectedId);

            if (card != null)
            {
                var localId = LocalId(card.data.GetValueOrDefault("Id", card.InstanceID));
                if (!string.IsNullOrWhiteSpace(localId))
                {
                    UseCore(self, localId, true);
                }
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Resolve linkage failed", ex);
        }
        finally
        {
            ExecutorApi.SetVar(self, LinkageResolving, "0");
        }
    }

    public static void ResolveSnatch(ScriptExecutor self)
    {
        try
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive)
            {
                return;
            }

            if (self.HandCard == null || self.HandCard.Count == 0 || !TargetsContainFriend(self))
            {
                return;
            }

            var index = UnityEngine.Random.Range(0, self.HandCard.Count);
            var card = self.HandCard[index];
            var config = card?.dataConfig;
            if (card == null || config == null)
            {
                return;
            }

            card.Burning(0f);
            self.CreateCard(CopyCardConfig(config));
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Resolve snatch failed", ex);
        }
    }

    private static void UseCore(ScriptExecutor self, string id, bool fromLinkage)
    {
        if (!SanGuoShaCombatRuntime.IsCombatActive)
        {
            return;
        }

        switch (id)
        {
            case "sha":
                UseSha(self, "Normal", 12, 0);
                break;
            case "shan":
                UseShan(self);
                break;
            case "tao":
                UseTao(self);
                break;
            case "jiu":
                UseJiu(self);
                break;
            case "juedou":
                UseDuel(self);
                break;
            case "wuzhong_shengyou":
                UseSomethingFromNothing(self);
                break;
            case "guohe_chaiqiao":
                UseDismantle(self);
                break;
            case "shunshou_qianyang":
                UseSnatchCard(self);
                break;
            case "nanman_ruqin":
                UseBarbarianInvasion(self);
                break;
            case "wanjian_qifa":
                UseArrowBarrage(self);
                break;
            case "taoyuan_jieyi":
                UsePeachGarden(self);
                break;
            case "wuxie_keji":
                UseNullification(self);
                break;
            case "shandian":
                self.SetStatus("Self");
                self.AddBuff(SanGuoShaExpIds.Lightning, "1");
                break;
            case "wugu_fengdeng":
                UseHarvest(self);
                break;
            case "huosha":
                UseSha(self, "Fire", 12, 8);
                break;
            case "leisha":
                UseSha(self, "Thunder", 12, 0);
                break;
            case "tiesuo_lianhuan":
                self.SetStatus("AllTarget");
                self.AddBuff(SanGuoShaExpIds.Chain, "3");
                break;
            case "huogong":
                var fireAttackDamage = CalculateRawDamage(self, 16, 0);
                self.SetStatus("Target");
                self.Damage(fireAttackDamage.ToString(), "Fire");
                self.AddBuff(SanGuoShaExpIds.Burn, "3");
                break;
            case "bingliang_cunduan":
                self.SetStatus("Target");
                self.AddBuff(SanGuoShaExpIds.SupplyShortage, "1");
                self.AddBuff(SanGuoShaExpIds.Weak, "4");
                self.AddBuff(SanGuoShaExpIds.Weakness, "4");
                break;
            case "tengjia":
                self.SetStatus("Self");
                self.ChangeDefence("28");
                self.AddBuff(SanGuoShaExpIds.VineArmor, "2");
                self.AddBuff(SanGuoShaExpIds.Resilient, "8");
                break;
            case "guding_dao":
                self.SetStatus("Self");
                self.AddBuff(SanGuoShaExpIds.KeenEdge, "4");
                break;
            case "lebu_sishu":
                self.SetStatus("Target");
                self.AddBuff(SanGuoShaExpIds.TimeStop, "1");
                break;
        }

        if (!fromLinkage)
        {
            TriggerEngravings(self);
        }
    }

    private static void UseSha(ScriptExecutor self, string damageType, int baseDamage, int burn)
    {
        var target = self.Target;
        if (damageType == "Normal" && HasRelic(SanGuoShaExpIds.VermilionFanRelic))
        {
            damageType = "Fire";
        }

        if (damageType == "Fire" && HasRelic(SanGuoShaExpIds.VermilionFanRelic))
        {
            baseDamage += 2;
            burn += 1;
        }

        var damage = CalculateRawDamage(self, baseDamage, PeekWineBonus(self));
        ConsumeWine(self);

        if (HasRelic(SanGuoShaExpIds.FangtianHalberdRelic) && (self.HandCard?.Count ?? 0) <= 2)
        {
            var groupDamage = Math.Max(1, damage * 80 / 100);
            self.SetStatus("AllTarget");
            self.Damage(groupDamage.ToString(), damageType);
            if (burn > 0)
            {
                self.SetStatus("AllTarget");
                self.AddBuff(SanGuoShaExpIds.Burn, burn.ToString());
            }

            self.SetStatus("Self");
            self.AddBuff(SanGuoShaExpIds.KillIntent, "1");
            return;
        }

        self.SetStatus("Target");
        self.Damage(damage.ToString(), damageType);

        if (HasRelic(SanGuoShaExpIds.GreenDragonBladeRelic) && target != null && target.CurHp > 0)
        {
            self.SetStatusById(target.InstanceId);
            self.Damage("10", damageType);
        }

        if (Combo(self) && target != null)
        {
            var extra = Math.Max(0, Revelation(self) * Math.Max(1, target.MaxHp) / 100);
            if (extra > 0)
            {
                self.SetStatusById(target.InstanceId);
                self.Damage(extra.ToString(), damageType);
            }
        }

        if (burn > 0)
        {
            self.SetStatus("Target");
            self.AddBuff(SanGuoShaExpIds.Burn, burn.ToString());
        }

        self.SetStatus("Self");
        self.AddBuff(SanGuoShaExpIds.KillIntent, "1");
        SplashChain(self, damage, damageType);
    }

    private static void UseShan(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SanGuoShaExpIds.Dodge, "1");
        if (Combo(self))
        {
            self.AddBuff(SanGuoShaExpIds.Resilient, Math.Max(0, Revelation(self) * 2).ToString());
        }
    }

    private static void UseTao(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.ChangeHp("10");
        if (Combo(self))
        {
            var extra = Math.Max(0, (self.Self?.MaxHp ?? 0) * Revelation(self) * 2 / 100);
            if (extra > 0)
            {
                self.ChangeHp(extra.ToString());
            }
        }
    }

    private static void UseJiu(ScriptExecutor self)
    {
        self.SetStatus("Self");
        self.AddBuff(SanGuoShaExpIds.Wine, "2");
        if (Combo(self))
        {
            self.AddBuff(SanGuoShaExpIds.KeenEdge, Math.Max(0, Revelation(self)).ToString());
        }
    }

    private static void UseDuel(ScriptExecutor self)
    {
        var repeats = 1 + CountShaInHand(self);
        var totalDamage = 0;
        var wineBonus = PeekWineBonus(self);
        ConsumeWine(self);
        for (var i = 0; i < repeats; i++)
        {
            var damage = CalculateRawDamage(self, 30, i == 0 ? wineBonus : 0);
            totalDamage += damage;
            self.SetStatus("Target");
            self.Damage(damage.ToString());
        }

        self.SetStatus("Self");
        self.AddBuff(SanGuoShaExpIds.KillIntent, "1");

        if (Combo(self) && totalDamage > 0)
        {
            self.SetStatus("AllRandomTarget1");
            self.Damage(totalDamage.ToString());
        }
    }

    private static void UseSomethingFromNothing(ScriptExecutor self)
    {
        var pool = SelectedCardPackCards()
            .Cast<IDataConfig>()
            .ToList();

        if (pool.Count > 0)
        {
            var generation = SanGuoShaCombatRuntime.Generation;
            self.PackToDeckAction("1", pool, selected =>
            {
                if (!SanGuoShaCombatRuntime.IsCurrentGeneration(generation))
                {
                    return;
                }

                var card = selected?.FirstOrDefault() as DataConfig;
                if (card != null)
                {
                    self.CreateCard(CopyCardConfig(card));
                }

                self.SetStatus("Self");
                self.ChangePower("2");
            });
            return;
        }

        self.SetStatus("Self");
        self.ChangePower("2");
    }

    private static void UseDismantle(ScriptExecutor self)
    {
        var target = self.Target;
        var removed = 0;
        if (target != null && target.Defend > 0)
        {
            removed += target.Defend;
            self.SetStatusById(target.InstanceId);
            self.ChangeDefence((-target.Defend).ToString());
        }

        removed += RemoveBuffs(target, positiveOnly: true, randomOne: false);

        if (Combo(self) && removed > 0)
        {
            self.SetStatus("Self");
            self.ChangeDefence(removed.ToString());
        }
    }

    private static void UseSnatchCard(ScriptExecutor self)
    {
        var target = self.Target;
        var stolen = RandomBuff(target);
        if (stolen.BuffId != null && target != null)
        {
            self.SetStatusById(target.InstanceId);
            self.RemoveBuff(stolen.BuffId);
            self.SetStatus("Self");
            self.AddBuff(stolen.BuffId, Math.Max(1, stolen.Level).ToString());
        }

        if (Combo(self))
        {
            self.SetStatus("Self");
            self.ChangePower("1");
        }
    }

    private static void UseBarbarianInvasion(ScriptExecutor self)
    {
        var damage = CalculateRawDamage(self, 20, 0);
        self.SetStatus("AllTarget");
        self.Damage(damage.ToString());
        self.SetStatus("Self");
        self.AddBuff(SanGuoShaExpIds.KillIntent, "1");
        ResolveMilitaryTactic(self, new[] { "sha", "wuxie_keji" }, damage);
    }

    private static void UseArrowBarrage(ScriptExecutor self)
    {
        var damage = CalculateRawDamage(self, 20, 0);
        self.SetStatus("AllTarget");
        self.Damage(damage.ToString());
        self.SetStatus("Self");
        self.AddBuff(SanGuoShaExpIds.KillIntent, "1");
        ResolveMilitaryTactic(self, new[] { "shan", "wuxie_keji" }, damage);
    }

    private static void UsePeachGarden(ScriptExecutor self)
    {
        self.SetStatus("All");
        foreach (var target in self.Object?.Where(target => target != null).ToList() ?? new List<IStatusManager>())
        {
            self.SetStatusById(target.InstanceId);
            self.ChangeHp(Math.Max(1, 1 + target.MaxHp / 3).ToString());
        }

        if (Combo(self) && self.Self != null)
        {
            self.SetStatus("Self");
            self.ChangeMaxHp(Math.Max(1, self.Self.MaxHp / 10).ToString());
        }
    }

    private static void UseNullification(ScriptExecutor self)
    {
        self.SetStatus("Target");
        self.DesEnemyAction();
    }

    private static void UseHarvest(ScriptExecutor self)
    {
        self.SetStatus("AllFriends");
        var friendCount = Math.Max(1, self.Object?.Count ?? 1);
        self.DrawCount(friendCount.ToString());

        var burnCount = Math.Max(0, friendCount - 1);
        if (burnCount <= 0 || self.HandCard == null || self.HandCard.Count == 0)
        {
            return;
        }

        var generation = SanGuoShaCombatRuntime.Generation;
        self.ChooseCardToAction(Math.Min(burnCount, self.HandCard.Count).ToString(), selected =>
        {
            if (!SanGuoShaCombatRuntime.IsCurrentGeneration(generation))
            {
                return;
            }

            var burned = selected?.Where(card => card != null).ToList();
            if (burned == null || burned.Count == 0)
            {
                return;
            }

            var reward = burned[UnityEngine.Random.Range(0, burned.Count)]?.dataConfig;
            foreach (var card in burned)
            {
                card.Burning(0f);
            }

            if (reward == null)
            {
                return;
            }

            var friends = FriendlyTargets(self);
            foreach (var target in friends)
            {
                var randomReward = burned[UnityEngine.Random.Range(0, burned.Count)]?.dataConfig ?? reward;
                CreateCardForTarget(self, target, randomReward);
            }
        });
    }

    private static void ResolveMilitaryTactic(ScriptExecutor self, string[] allowedLocalIds, int damage)
    {
        self.SetStatus("AllFriendsExSelf");
        var friends = self.Object?.Where(target => target != null).ToList();
        if (friends == null || friends.Count == 0 || self.HandCard == null || self.HandCard.Count == 0)
        {
            return;
        }

        var allowed = new HashSet<string>(allowedLocalIds, StringComparer.Ordinal);
        var response = self.HandCard.FirstOrDefault(card => allowed.Contains(LocalId(card?.dataConfig?.data.GetValueOrDefault("Id", ""))));
        if (response == null)
        {
            foreach (var friend in friends)
            {
                self.SetStatusById(friend.InstanceId);
                self.Damage(damage.ToString());
            }

            return;
        }

        response.Burning(0f);
    }

    private static void TriggerEngravings(ScriptExecutor self)
    {
        var data = self.dataConfig?.data;
        if (data == null)
        {
            return;
        }

        if (HasTag(data, SanGuoShaExpIds.SnatchTag))
        {
            ResolveSnatch(self);
        }

        if (HasTag(data, SanGuoShaExpIds.LinkageTag))
        {
            ResolveLinkage(self);
        }
    }

    private static void AddDescriptions(ScriptExecutor self, string id)
    {
        switch (id)
        {
            case "sha":
            case "huosha":
            case "leisha":
                self.AddDescription("1", "Damage", CalculateRawDamage(self, 12, PeekWineBonus(self)));
                break;
            case "tao":
                self.AddDescription("1", "Hp", 10);
                break;
            case "juedou":
                self.AddDescription("1", "Damage", CalculateRawDamage(self, 30, PeekWineBonus(self)));
                break;
            case "nanman_ruqin":
            case "wanjian_qifa":
                self.AddDescription("1", "Damage", CalculateRawDamage(self, 20, 0));
                break;
            case "huogong":
                self.AddDescription("1", "Damage", CalculateRawDamage(self, 16, 0));
                break;
        }
    }

    private static bool IsTargetCard(string id)
    {
        switch (id)
        {
            case "sha":
            case "juedou":
            case "guohe_chaiqiao":
            case "shunshou_qianyang":
            case "wuxie_keji":
            case "huosha":
            case "leisha":
            case "huogong":
            case "bingliang_cunduan":
            case "lebu_sishu":
                return true;
            default:
                return false;
        }
    }

    private static bool CanTargetSelf(string id)
    {
        return id == "sha" || id == "juedou" || id == "guohe_chaiqiao" || id == "shunshou_qianyang";
    }

    private static int PeekWineBonus(ScriptExecutor self)
    {
        return Math.Max(0, BuffLevel(self.Self, SanGuoShaExpIds.Wine)) * 10;
    }

    private static void ConsumeWine(ScriptExecutor self)
    {
        if (BuffLevel(self.Self, SanGuoShaExpIds.Wine) <= 0)
        {
            return;
        }

        self.SetStatus("Self");
        self.RemoveBuff(SanGuoShaExpIds.Wine);
    }

    private static int CalculateRawDamage(ScriptExecutor self, int baseDamage, int wineBonus)
    {
        var killIntentMultiplier = 1 + Math.Max(0, BuffLevel(self.Self, SanGuoShaExpIds.KillIntent));
        return Math.Max(0, baseDamage + Math.Max(0, wineBonus)) * killIntentMultiplier;
    }

    private static int CountShaInHand(ScriptExecutor self)
    {
        return self.HandCard?.Count(card => ShaIds.Contains(LocalId(card?.dataConfig?.data.GetValueOrDefault("Id", "")))) ?? 0;
    }

    private static int RemoveBuffs(IStatusManager? target, bool positiveOnly, bool randomOne)
    {
        var buffs = BuffEntries(target)
            .Where(buff => buff.BuffId != null && (!positiveOnly || buff.IsPositive))
            .ToList();
        if (buffs.Count == 0 || target == null)
        {
            return 0;
        }

        if (randomOne)
        {
            buffs = new List<BuffEntry> { buffs[UnityEngine.Random.Range(0, buffs.Count)] };
        }

        var removed = 0;
        foreach (var buff in buffs)
        {
            removed += Math.Max(1, buff.Level);
            target.RemoveBuff(buff.BuffId);
        }

        return removed;
    }

    private static BuffEntry RandomBuff(IStatusManager? target)
    {
        var buffs = BuffEntries(target).Where(buff => buff.BuffId != null).ToList();
        return buffs.Count == 0 ? default : buffs[UnityEngine.Random.Range(0, buffs.Count)];
    }

    private static IEnumerable<BuffEntry> BuffEntries(IStatusManager? target)
    {
        if (target == null)
        {
            yield break;
        }

        IEnumerable? raw = null;
        try
        {
            raw = target.GetBuffs();
        }
        catch
        {
            yield break;
        }

        if (raw == null)
        {
            yield break;
        }

        foreach (var buff in raw)
        {
            var config = GetMemberValue(buff, "buffConfig");
            var level = DictionaryUtil.ParseInt(Convert.ToString(GetMemberValue(config, "Level")), 1);
            var dataConfig = GetMemberValue(config, "dataConfig");
            var data = GetMemberValue(dataConfig, "data") as IDictionary<string, string>;
            var id = data?.GetValueOrDefault("Id", "");
            var type = data?.GetValueOrDefault("Type", "");
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return new BuffEntry(id!, level, type == "\u6b63\u9762");
            }
        }
    }

    private static object? GetMemberValue(object? instance, string name)
    {
        if (instance == null)
        {
            return null;
        }

        var type = instance.GetType();
        return type.GetProperty(name)?.GetValue(instance) ?? type.GetField(name)?.GetValue(instance);
    }

    private static List<DataConfig> SelectedCardPackCards()
    {
        try
        {
            var manager = Singleton<GameConfigManager>.Instance;
            var rows = AuraGameDataHostApi.CopyTableForHostInterop(DataType.Card);
            if (rows == null || rows.Count == 0)
            {
                return new List<DataConfig>();
            }

            var selectedRows = manager.CardPackCheck(rows)
                .Where(IsSelectableCardPackRow)
                .GroupBy(row => row["Id"], StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(row => row["Id"], StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new List<DataConfig>(selectedRows.Count);
            foreach (var row in selectedRows)
            {
                try
                {
                    var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, row["Id"]);
                    var materialized = handle == null
                        ? null
                        : AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest { Definition = handle }).Instance as DataConfig;
                    if (materialized != null)
                    {
                        result.Add(materialized);
                    }
                }
                catch (Exception ex)
                {
                    SanGuoShaExpLog.Debug("Skipped invalid selected-pack card " + row["Id"] + ": " + ex.Message);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("Failed to build selected card-pack pool", ex);
            return new List<DataConfig>();
        }
    }

    private static bool IsSelectableCardPackRow(Dictionary<string, string> row)
    {
        if (row == null
            || !row.TryGetValue("Id", out var id)
            || string.IsNullOrWhiteSpace(id)
            || id.StartsWith("*", StringComparison.Ordinal)
            || string.Equals(id, SomethingFromNothingId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(LocalId(id), "wuzhong_shengyou", StringComparison.OrdinalIgnoreCase)
            || Singleton<GameRuntimeData>.Instance.IsLocked(id))
        {
            return false;
        }

        if (row.TryGetValue("PackBelong", out var packBelong)
            && string.Equals(packBelong, HiddenMilitaryPackId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tags = row.GetValueOrDefault("Tag", "");
        var type = row.GetValueOrDefault("Type", "");
        return tags.IndexOf("Curse", StringComparison.OrdinalIgnoreCase) < 0
            && type != "诅咒";
    }

    private static List<DataConfig> UsedPile(ScriptExecutor self, out bool excludedCurrent)
    {
        var raw = new List<DataConfig>();
        try
        {
            if (self.UsedCard != null)
            {
                foreach (var item in self.UsedCard)
                {
                    if (item is DataConfig dataConfig)
                    {
                        raw.Add(dataConfig);
                    }
                    else
                    {
                        var config = item?.GetType().GetProperty("dataConfig")?.GetValue(item) as DataConfig;
                        if (config != null)
                        {
                            raw.Add(config);
                        }
                    }
                }
            }
        }
        catch
        {
            // Discard-pile APIs vary between game versions; fall back below.
        }

        if (raw.Count == 0 && FightCardManager.Instance?.usedCardList != null)
        {
            raw.AddRange(FightCardManager.Instance.usedCardList.Where(card => card != null));
        }

        excludedCurrent = ExcludeCurrentUsedCard(raw, self.dataConfig);
        return raw.Select(CopyCardConfig).ToList();
    }

    private static bool ExcludeCurrentUsedCard(List<DataConfig> usedPile, IDataConfig? current)
    {
        if (current == null || usedPile.Count == 0)
        {
            return false;
        }

        for (var index = usedPile.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(usedPile[index], current))
            {
                usedPile.RemoveAt(index);
                return true;
            }
        }

        // Normally the current DataConfig reference is preserved. Online card-use
        // deserialization may clone it, so only remove the newest matching row when
        // the game throws cards before UseScript. Earlier same-id copies remain valid.
        if (GameSaveManager.GetValue<bool>(GameVar.LateThrow))
        {
            return false;
        }

        var currentId = current.data?.GetValueOrDefault("Id", current.InstanceID) ?? current.InstanceID;
        var newest = usedPile[usedPile.Count - 1];
        var newestId = newest.data?.GetValueOrDefault("Id", newest.InstanceID) ?? newest.InstanceID;
        if (!string.Equals(currentId, newestId, StringComparison.Ordinal))
        {
            return false;
        }

        usedPile.RemoveAt(usedPile.Count - 1);
        return true;
    }

    private static DataConfig CopyCardConfig(DataConfig card)
    {
        var cardId = card.data.GetValueOrDefault("Id", card.InstanceID);
        var handle = AuraGameDataHostApi.ResolveHandle(DataType.Card, cardId);
        return handle != null
               && AuraGameDataHostApi.Materialize(new AuraGameDataMaterializeRequest { Definition = handle }).Instance is DataConfig copy
            ? copy
            : throw new InvalidOperationException("Registered card definition is unavailable: " + cardId);
    }

    private static List<IStatusManager> FriendlyTargets(ScriptExecutor self)
    {
        self.SetStatus("AllFriends");
        return self.Object?
            .Where(target => target != null)
            .OrderBy(target => target.InstanceId, StringComparer.Ordinal)
            .ToList() ?? new List<IStatusManager>();
    }

    private static void CreateCardForTarget(ScriptExecutor self, IStatusManager target, DataConfig card)
    {
        try
        {
            self.SetStatusById(target.InstanceId);
            self.CreateCard(CopyCardConfig(card));
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Warn("Harvest reward delivery failed: target=" + target.InstanceId + ", error=" + ex.Message);
        }
    }

    private static bool TargetsContainFriend(ScriptExecutor self)
    {
        var selfId = self.Self?.InstanceId;
        if (selfId == null)
        {
            return false;
        }

        var targets = self.Object?.Where(target => target != null).ToList();
        return targets != null && targets.Any(target => target.InstanceId == selfId || !IsEnemy(self, target));
    }

    private static bool IsEnemy(ScriptExecutor self, IStatusManager target)
    {
        var enemies = ExecutorApi.EnemyTargets(self);
        return enemies.Any(enemy => enemy.InstanceId == target.InstanceId);
    }

    private static bool Combo(ScriptExecutor self)
    {
        try
        {
            return self.ComboCheck();
        }
        catch
        {
            return false;
        }
    }

    private static int Revelation(ScriptExecutor self)
    {
        return BuffLevel(self.Self, "buff_revelation");
    }

    private static int BuffLevel(IStatusManager? target, string buffId)
    {
        return target?.GetBuff(buffId)?.buffConfig?.Level ?? 0;
    }

    private static bool HasTag(DataConfig card, string tag)
    {
        return HasTag(card.data, tag);
    }

    private static bool HasTag(IDictionary<string, string> data, string tag)
    {
        var tags = data.GetValueOrDefault("Tag", "");
        return tags.Split(',').Any(value => value.Trim() == tag);
    }

    private static bool HasRelic(string fullRelicId)
    {
        var expected = NormalizeLocalId(fullRelicId);
        if (string.IsNullOrWhiteSpace(expected) || RoleTable.Instance?.relicList == null)
        {
            return false;
        }

        return RoleTable.Instance.relicList.Any(relic =>
        {
            var id = relic?.data?.GetValueOrDefault("Id", relic.InstanceID) ?? relic?.InstanceID ?? "";
            return string.Equals(NormalizeLocalId(id), expected, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string LocalId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "";
        }

        const string prefix = "SanGuoShaExp_sanguosha_";
        var value = id!;
        return value.StartsWith(prefix, StringComparison.Ordinal) ? value.Substring(prefix.Length) : value;
    }

    private static string NormalizeLocalId(string? id)
    {
        return LocalId(id).TrimStart('*');
    }

    private static void SplashChain(ScriptExecutor self, int sourceDamage, string damageType)
    {
        if (damageType != "Fire" && damageType != "Thunder")
        {
            return;
        }

        var splash = Math.Max(1, sourceDamage * 60 / 100);
        var chained = ExecutorApi.EnemyTargets(self)
            .Where(target => target != null && target.InstanceId != self.Target?.InstanceId && BuffLevel(target, SanGuoShaExpIds.Chain) > 0)
            .ToList();
        foreach (var target in chained)
        {
            self.SetStatusById(target.InstanceId);
            self.RemoveBuff(SanGuoShaExpIds.Chain);
            self.Damage(splash.ToString(), damageType);
        }
    }

    private readonly struct BuffEntry
    {
        public BuffEntry(string buffId, int level, bool isPositive)
        {
            BuffId = buffId;
            Level = level;
            IsPositive = isPositive;
        }

        public string BuffId { get; }
        public int Level { get; }
        public bool IsPositive { get; }
    }
}
