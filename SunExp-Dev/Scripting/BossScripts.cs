using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class BossScripts
{
    public static void InitEnemy(ScriptExecutor self, string bossId)
    {
        try
        {
            if (bossId == "second_sun_last_day")
            {
                PlayerApi.SetGameVar(SunExpIds.SolarFinaleSecondSunDefeatedKey, "0");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss enemy init failed: " + bossId, ex);
        }
    }

    public static void InitCard(ScriptExecutor self, string cardId)
    {
        try
        {
            var spec = Spec(cardId);
            self.Vars["CD"] = spec.Cooldown.ToString();
            self.Vars["priority"] = spec.Priority.ToString();
            self.AddDescription("1", spec.DescriptionType, spec.DescriptionValue.ToString());
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss card init failed: " + cardId, ex);
        }
    }

    public static void Target(ScriptExecutor self, string target)
    {
        try
        {
            self.SetStatus(string.IsNullOrWhiteSpace(target) ? "Target" : target);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss card target failed: " + target, ex);
        }
    }

    public static void UseCard(ScriptExecutor self, string cardId)
    {
        try
        {
            switch (cardId)
            {
                case "mirror_calibration":
                    self.SetStatus("All");
                    self.AddBuff(SunExpIds.Burn, "5");
                    self.SetStatus("Self");
                    self.ChangeDefence("10");
                    break;
                case "orbit_refraction":
                    ExecutorApi.DealDamage(self, 20);
                    self.AddBuff(SunExpIds.Burn, "10");
                    break;
                case "last_day_morning_prayer":
                    self.SetStatus("All");
                    self.AddBuff(SunExpIds.Burn, "5");
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.GatheredFlame, "10");
                    break;
                case "last_day_noon_burn":
                    ExecutorApi.DealDamage(self, 18);
                    self.AddBuff("buff_weak", "2");
                    break;
                case "saint_purification":
                    ExecutorApi.DealDamage(self, 14);
                    self.AddBuff(SunExpIds.BodyBurn, "2");
                    break;
                case "saint_return_to_court":
                    MoveSavedNameToNameless();
                    ExecutorApi.DealDamage(self, 12);
                    break;
                default:
                    ExecutorApi.DealDamage(self, 10);
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Boss card use failed: " + cardId, ex);
        }
    }

    private static BossCardSpec Spec(string cardId)
    {
        return cardId switch
        {
            "mirror_calibration" => new BossCardSpec(0, 1, "Buff", 5),
            "orbit_refraction" => new BossCardSpec(2, 2, "Damage", 20),
            "last_day_morning_prayer" => new BossCardSpec(0, 1, "Buff", 5),
            "last_day_noon_burn" => new BossCardSpec(1, 2, "Damage", 18),
            "saint_purification" => new BossCardSpec(0, 1, "Damage", 14),
            "saint_return_to_court" => new BossCardSpec(2, 2, "Damage", 12),
            _ => new BossCardSpec(0, 1, "Damage", 10)
        };
    }

    private static void MoveSavedNameToNameless()
    {
        var saved = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleSavedNamesKey, "0")));
        if (saved <= 0)
        {
            return;
        }

        var nameless = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, "0")));
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleSavedNamesKey, (saved - 1).ToString());
        PlayerApi.SetGameVar(SunExpIds.SolarFinaleNamelessNamesKey, (nameless + 1).ToString());
    }

    private readonly struct BossCardSpec
    {
        public BossCardSpec(int cooldown, int priority, string descriptionType, int descriptionValue)
        {
            Cooldown = cooldown;
            Priority = priority;
            DescriptionType = descriptionType;
            DescriptionValue = descriptionValue;
        }

        public int Cooldown { get; }

        public int Priority { get; }

        public string DescriptionType { get; }

        public int DescriptionValue { get; }
    }
}
