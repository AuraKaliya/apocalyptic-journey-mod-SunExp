using System;
using System.Linq;
using SanGuoShaExp.Dll.GameApi;
using SanGuoShaExp.Dll.Infrastructure;

namespace SanGuoShaExp.Dll.Scripting;

public static class SanGuoShaCardScripts
{
    private const int WineDamageBonus = 14;

    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, IsAttackCard(id) ? "AttackCardItem" : "CommonCardItem", canSelf: !IsAttackCard(id));
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
            switch (id)
            {
                case "sha":
                    UseSha(
                        self,
                        HasRelic(SanGuoShaExpIds.VermilionFanRelic) ? "Fire" : "Normal",
                        14 + Fission(self) * 3,
                        8,
                        burn: HasRelic(SanGuoShaExpIds.VermilionFanRelic) ? 12 : 0);
                    break;
                case "shan":
                    self.SetStatus("Self");
                    self.ChangeDefence((14 + Fission(self) * 2).ToString());
                    self.AddBuff(SanGuoShaExpIds.Resilient, "1");
                    if (self.Self != null && self.Self.CurHp < self.Self.MaxHp)
                    {
                        self.DrawCount("1");
                    }
                    break;
                case "tao":
                    self.SetStatus("Self");
                    self.ChangeHp((16 + Fission(self) * 3 + LowHealthBonus(self, 8)).ToString());
                    break;
                case "jiu":
                    self.SetStatus("Self");
                    self.AddBuff(SanGuoShaExpIds.Wine, "2");
                    break;
                case "juedou":
                    UseDuel(self);
                    break;
                case "wuzhong_shengyou":
                    self.DrawCount("3");
                    self.SetStatus("Self");
                    self.ChangePower("1");
                    break;
                case "guohe_chaiqiao":
                    UseDismantle(self);
                    break;
                case "shunshou_qianyang":
                    self.SetStatus("Target");
                    self.Damage("12");
                    self.AddBuff(SanGuoShaExpIds.Weakness, "4");
                    self.SetStatus("Self");
                    self.DrawCount("2");
                    break;
                case "nanman_ruqin":
                    var nanmanCombo = Combo(self);
                    self.SetStatus("AllTarget");
                    self.Damage(nanmanCombo ? "30" : "22");
                    if (nanmanCombo)
                    {
                        self.Damage("8", "True");
                    }
                    break;
                case "wanjian_qifa":
                    self.SetStatus("AllTarget");
                    self.Damage("18");
                    self.SetStatus("Self");
                    self.ChangeDefence("18");
                    if (Combo(self))
                    {
                        self.DrawCount("1");
                    }
                    break;
                case "taoyuan_jieyi":
                    self.SetStatus("Self");
                    self.ChangeHp("28");
                    self.RemoveBadBuff("2");
                    if (self.Self != null && self.Self.CurHp * 100 <= self.Self.MaxHp * 35)
                    {
                        self.DrawCount("2");
                    }
                    break;
                case "wuxie_keji":
                    self.SetStatus("Self");
                    self.RemoveAllBadBuff("0");
                    self.ChangeDefence("12");
                    self.AddBuff(SanGuoShaExpIds.Impregnable, "3");
                    break;
                case "shandian":
                    UseLightning(self);
                    break;
                case "wugu_fengdeng":
                    self.DrawCount((Math.Min(6, 4 + Fission(self))).ToString());
                    self.SetStatus("Self");
                    self.ChangePower("1");
                    break;
                case "huosha":
                    UseSha(self, "Fire", 12 + Fission(self) * 2, 0, burn: 8);
                    break;
                case "leisha":
                    UseThunderSha(self);
                    break;
                case "tiesuo_lianhuan":
                    self.SetStatus("AllTarget");
                    self.AddBuff(SanGuoShaExpIds.Chain, "3");
                    break;
                case "huogong":
                    UseFireAttack(self);
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
                    ExecutorApi.SetVar(self, "SanGuoShaAncientScimitar", "1");
                    break;
                case "lebu_sishu":
                    self.SetStatus("Target");
                    self.AddBuff(SanGuoShaExpIds.TimeStop, "1");
                    break;
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("SanGuoSha card use failed: " + id, ex);
        }
    }

    private static void AddDescriptions(ScriptExecutor self, string id)
    {
        switch (id)
        {
            case "sha":
                self.AddDescription("1", "Damage", 14 + Fission(self) * 3);
                break;
            case "shan":
                self.AddDescription("1", "Defence", 14 + Fission(self) * 2);
                break;
            case "tao":
                self.AddDescription("1", "Hp", 16 + Fission(self) * 3);
                break;
            case "huosha":
            case "leisha":
                self.AddDescription("1", "Damage", 12 + Fission(self) * 2);
                break;
            case "juedou":
                self.AddDescription("1", "Damage", 32);
                break;
            case "nanman_ruqin":
                self.AddDescription("1", "Damage", 22);
                break;
            case "wanjian_qifa":
                self.AddDescription("1", "Damage", 18);
                self.AddDescription("2", "Defence", 18);
                break;
            case "shandian":
                self.AddDescription("1", "Damage", 45);
                break;
        }
    }

    private static bool IsAttackCard(string id)
    {
        switch (id)
        {
            case "sha":
            case "juedou":
            case "shunshou_qianyang":
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

    private static void UseSha(ScriptExecutor self, string damageType, int baseDamage, int comboBonus, int burn = 0)
    {
        var damage = baseDamage + ConsumeWineBonus(self);
        var combo = Combo(self);
        if (combo)
        {
            damage += comboBonus;
        }

        if (HasRelic(SanGuoShaExpIds.VermilionFanRelic) && damageType == "Fire")
        {
            damage += 2;
            burn += 1;
        }

        if (ConsumeAncientScimitar(self))
        {
            var target = self.Target;
            damage += target != null && target.Defend <= 0 ? 24 : 4;
            if (target != null && target.Defend > 0)
            {
                self.SetStatus("Target");
                self.ChangeDefence((-Math.Min(20, target.Defend)).ToString());
            }
        }

        self.SetStatus("Target");
        if (HasRelic(SanGuoShaExpIds.FangtianHalberdRelic) && self.HandCard.Count <= 2)
        {
            self.SetStatus("AllTarget");
            self.Damage(Math.Max(1, damage * 80 / 100).ToString(), damageType);
        }
        else
        {
            self.Damage(damage.ToString(), damageType);
        }

        var primaryTarget = self.Target;
        if (HasRelic(SanGuoShaExpIds.GreenDragonBladeRelic) && primaryTarget != null && primaryTarget.CurHp > 0)
        {
            self.SetStatusById(primaryTarget.InstanceId);
            self.Damage("10", damageType);
        }

        if (burn > 0)
        {
            self.SetStatus("Target");
            self.AddBuff(SanGuoShaExpIds.Burn, (burn + (damage > baseDamage ? 2 : 0)).ToString());
            if (combo)
            {
                self.RunImmediately(SanGuoShaExpIds.Burn, "StartRound");
            }
        }

        SplashChain(self, damage, damageType);
    }

    private static void UseThunderSha(ScriptExecutor self)
    {
        var damage = 12 + Fission(self) * 2 + ConsumeWineBonus(self);
        self.SetStatus("Target");
        self.Damage(damage.ToString(), "Thunder");
        if (Check(self, 50))
        {
            self.Damage("18", "Thunder");
            self.AddBuff(SanGuoShaExpIds.Weakness, "4");
        }

        SplashChain(self, damage, "Thunder");
    }

    private static void UseDuel(ScriptExecutor self)
    {
        var before = self.Target?.CurHp ?? 0;
        self.SetStatus("Target");
        self.Damage(Combo(self) ? "48" : "32");
        if (self.Target != null && self.Target.CurHp > 0 && before > 0)
        {
            self.SetStatus("Self");
            self.ChangeHp("-6");
        }
    }

    private static void UseDismantle(ScriptExecutor self)
    {
        var target = self.Target;
        self.SetStatus("Target");
        if (target != null && target.Defend > 0)
        {
            self.ChangeDefence((-target.Defend).ToString());
        }

        self.RemoveBadBuff("2", "true");
        self.Damage("12");
    }

    private static void UseLightning(ScriptExecutor self)
    {
        if (Check(self, 50))
        {
            self.SetStatus("AllRandomTarget1");
            self.Damage("45", "Thunder");
            self.SetStatus("AllTarget");
            self.AddBuff(SanGuoShaExpIds.Weakness, "3");
        }
        else
        {
            self.SetStatus("Self");
            self.Damage("10", "Thunder");
        }
    }

    private static void UseFireAttack(ScriptExecutor self)
    {
        var hasFuel = self.HandCard.Any(card => card?.dataConfig?.data != null
            && card.dataConfig.data.GetValueOrDefault("Id", "").Contains("huosha"))
            || BuffLevel(self, SanGuoShaExpIds.Wine) > 0;
        var damage = hasFuel ? 32 : 16;
        self.SetStatus("Target");
        self.Damage(damage.ToString(), "Fire");
        self.AddBuff(SanGuoShaExpIds.Burn, hasFuel ? "10" : "3");
        if (Combo(self))
        {
            self.SetStatus("Self");
            self.DrawCount("1");
        }

        SplashChain(self, damage, "Fire");
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
            ExecutorApi.AddStatusBuff(self, target, SanGuoShaExpIds.Chain, 0);
            self.SetStatusById(target.InstanceId);
            self.Damage(splash.ToString(), damageType);
        }
    }

    private static int ConsumeWineBonus(ScriptExecutor self)
    {
        var level = BuffLevel(self, SanGuoShaExpIds.Wine);
        if (level <= 0)
        {
            return 0;
        }

        self.SetStatus("Self");
        self.RemoveBuff(SanGuoShaExpIds.Wine);
        return level * WineDamageBonus;
    }

    private static bool ConsumeAncientScimitar(ScriptExecutor self)
    {
        if (ExecutorApi.GetVar(self, "SanGuoShaAncientScimitar", "0") != "1")
        {
            return false;
        }

        ExecutorApi.SetVar(self, "SanGuoShaAncientScimitar", "0");
        return true;
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

    private static bool Check(ScriptExecutor self, int threshold)
    {
        try
        {
            return self.CheckDice.Roll().Value > threshold;
        }
        catch
        {
            return false;
        }
    }

    private static int Fission(ScriptExecutor self)
    {
        return DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "ThisCount", "0"));
    }

    private static int LowHealthBonus(ScriptExecutor self, int bonus)
    {
        return self.Self != null && self.Self.CurHp * 2 < self.Self.MaxHp ? bonus : 0;
    }

    private static int BuffLevel(ScriptExecutor self, string buffId)
    {
        return BuffLevel(self.Self, buffId);
    }

    private static int BuffLevel(IStatusManager? target, string buffId)
    {
        return target?.GetBuff(buffId)?.buffConfig?.Level ?? 0;
    }

    private static bool HasRelic(string relicId)
    {
        return RoleTable.Instance?.relicList?.Any(relic => relic?.data != null && relic.data.GetValueOrDefault("Id", "") == relicId) == true;
    }
}
