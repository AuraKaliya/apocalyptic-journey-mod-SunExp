using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class RelicScripts
{
    private static readonly Dictionary<string, Action<ScriptExecutor>> FightHandlers = new(StringComparer.Ordinal)
    {
        ["morning_shard"] = RegisterMorningShard,
        ["ember_cloak_lining"] = RegisterEmberCloakLining,
        ["sun_orbit_mirror"] = RegisterSunOrbitMirror,
        ["sun_bottle"] = RegisterSunBottle,
        ["solar_phase_dial"] = RegisterSolarPhaseDial,
        ["miniature_sunwheel"] = RegisterMiniatureSunwheel,
        ["blazing_crown_heart"] = RegisterBlazingCrownHeart,
        ["solar_prism"] = RegisterSolarPrism,
        ["coronation_throne"] = RegisterCoronationThrone,
        ["gathered_flame_charm"] = RegisterGatheredFlameCharm,
        ["ash_charm"] = RegisterAshCharm,
        ["blazing_sundial"] = RegisterBlazingSundial,
        ["burning_calamity_wind_belt"] = RegisterBurningCalamityWindBelt
    };

    public static void Fight(ScriptExecutor self, string id)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(id) && FightHandlers.TryGetValue(id, out var handler))
            {
                handler(self);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Relic Fight failed: " + id, ex);
        }
    }

    private static void RegisterMorningShard(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
        {
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.SolarRadiance, "2");
            UpdateRelicShow(self);
        }), "morning_shard");
    }

    private static void RegisterEmberCloakLining(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            var burn = ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
            if (burn <= 0)
            {
                return;
            }

            ExecutorApi.RemoveBuffStacks(self, self.Self, SunExpIds.Burn, 1);
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.GatheredFlame, "2");
        }), "ember_cloak_lining");
    }

    private static void RegisterSunBottle(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            var target = ExecutorApi.RandomEnemyTarget(self, true);
            if (ExecutorApi.TriggerBurn(self, target))
            {
                UpdateRelicShow(self);
            }
        }), "sun_bottle");
    }

    private static void RegisterMiniatureSunwheel(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            var changed = false;
            var total = BuffApi.NegativeTotal(self.Self);
            if (total > 0)
            {
                self.SetStatus("Self");
                self.AddBuff(SunExpIds.GatheredFlame, total.ToString());
                changed = true;
            }

            var burn = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            if (burn > 0)
            {
                foreach (var target in ExecutorApi.EnemyTargets(self))
                {
                    ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, burn);
                    changed = true;
                }
            }

            if (changed)
            {
                UpdateRelicShow(self);
            }
        }), "miniature_sunwheel");
    }

    private static void RegisterSunOrbitMirror(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
        {
            ExecutorApi.SetVar(self, "ThisCount", "0");
            UpdateRelicShow(self);
        }), "sun_orbit_mirror");
        ExecutorApi.TryAddEvent(self, "Action", new Action(() =>
        {
            var count = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "ThisCount", "0")) + 1;
            ExecutorApi.SetVar(self, "ThisCount", count);
            UpdateRelicShow(self);
            if (count % 3 != 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.AddBuff(SunExpIds.GatheredFlame, "1");
            ExecutorApi.AddBurnToRandomEnemy(self, 3);
            UpdateRelicShow(self);
        }), "sun_orbit_mirror");
    }

    private static void RegisterSolarPhaseDial(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            var level = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            if (level >= 4)
            {
                self.SetStatus("Self");
                self.DrawCount("1");
            }

            if (level >= 8)
            {
                self.SetStatus("Self");
                self.ChangePower("1");
            }

            if (level >= 12)
            {
                ExecutorApi.TriggerBurnAll(self);
            }

            UpdateRelicShow(self);
        }), "solar_phase_dial");
    }

    private static void RegisterBlazingCrownHeart(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
        {
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.SolarRadiance, "8");
            ExecutorApi.ApplyFieldBuff(self, "scorching_canopy", 2);
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.SolarCrown, "1");
        }), "blazing_crown_heart");
    }

    private static void RegisterSolarPrism(ScriptExecutor self)
    {
        void Reset()
        {
            ExecutorApi.SetVar(self, "SunExpPrismDone", "0");
            ExecutorApi.SetVar(self, "SunExpPrismLastRadiance", ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance));
            UpdateRelicShow(self);
        }

        void Check()
        {
            var current = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            var last = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpPrismLastRadiance", current.ToString()));
            if (ExecutorApi.GetVar(self, "SunExpPrismDone", "0") == "0" && current > last)
            {
                self.SetStatus("Self");
                self.AddBuff("buff_elements", "1");
                ExecutorApi.SetVar(self, "SunExpPrismDone", "1");
                UpdateRelicShow(self);
                current = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            }

            ExecutorApi.SetVar(self, "SunExpPrismLastRadiance", current);
        }

        ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
        {
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.SolarRadiance, "1");
            Reset();
        }), "solar_prism");
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(Reset), "solar_prism");
        ExecutorApi.TryAddEvent(self, "Action", new Action(Check), "solar_prism");
    }

    private static void RegisterCoronationThrone(ScriptExecutor self)
    {
        void Reset()
        {
            ExecutorApi.SetVar(self, "SunExpCradleDone", "0");
            ExecutorApi.SetVar(self, "SunExpCradleLastCrown", "0");
            UpdateRelicShow(self);
        }

        void Check()
        {
            var current = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarCrown);
            var last = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpCradleLastCrown", "0"));
            if (ExecutorApi.GetVar(self, "SunExpCradleDone", "0") == "0" && current > last)
            {
                self.SetStatus("Self");
                self.DrawCount("2");
                self.ChangePower("2");
                ExecutorApi.SetVar(self, "SunExpCradleDone", "1");
                UpdateRelicShow(self);
            }

            ExecutorApi.SetVar(self, "SunExpCradleLastCrown", current);
        }

        ExecutorApi.TryAddEvent(self, "FightStart", new Action(Reset), "coronation_throne");
        ExecutorApi.TryAddEvent(self, "SunExp_sunexp_solar_crownOnLevelChange", new Action(Check), "coronation_throne");
        ExecutorApi.TryAddEvent(self, "solar_crownOnLevelChange", new Action(Check), "coronation_throne");
        ExecutorApi.TryAddEvent(self, "Action", new Action(Check), "coronation_throne");
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(Check), "coronation_throne");
    }

    private static void RegisterGatheredFlameCharm(ScriptExecutor self)
    {
        void Reset()
        {
            ExecutorApi.SetVar(self, "SunExpMoltenCharmLastBurn", ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn));
            UpdateRelicShow(self);
        }

        void Check()
        {
            var current = ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
            var last = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpMoltenCharmLastBurn", current.ToString()));
            if (current > last)
            {
                self.SetStatus("Self");
                self.AddBuff(SunExpIds.GatheredFlame, (current - last).ToString());
                UpdateRelicShow(self);
                current = ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
            }

            ExecutorApi.SetVar(self, "SunExpMoltenCharmLastBurn", current);
        }

        ExecutorApi.TryAddEvent(self, "FightStart", new Action(Reset), "gathered_flame_charm");
        ExecutorApi.TryAddEvent(self, "buff_burnOnLevelChange", new Action(Check), "gathered_flame_charm");
        ExecutorApi.TryAddEvent(self, "Action", new Action(Check), "gathered_flame_charm");
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(Check), "gathered_flame_charm");
    }

    private static void RegisterAshCharm(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "EndRound", new Action(() =>
        {
            var burn = ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
            if (burn <= 0)
            {
                return;
            }

            self.SetStatus("Self");
            self.AddBuff(SunExpIds.Ember, burn.ToString());
            self.ChangeDefence(burn.ToString());
            UpdateRelicShow(self);
        }), "ash_charm");
    }

    private static void RegisterBlazingSundial(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            var applied = 0;
            foreach (var target in ExecutorApi.EnemyTargets(self))
            {
                if (ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn) <= 0)
                {
                    continue;
                }

                ExecutorApi.AddStatusBuff(self, target, "buff_weak", 1);
                ExecutorApi.AddStatusBuff(self, target, "buff_rotten", 1);
                applied++;
                if (applied >= 4)
                {
                    break;
                }
            }

            if (applied > 0)
            {
                UpdateRelicShow(self);
            }
        }), "blazing_sundial");
    }

    private static void RegisterBurningCalamityWindBelt(ScriptExecutor self)
    {
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            var targets = ExecutorApi.EnemyTargets(self);
            if (targets.Count < 2)
            {
                return;
            }

            var burning = targets.Where(target => ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn) > 0).Take(4).ToList();
            if (burning.Count == 0)
            {
                return;
            }

            foreach (var source in burning)
            {
                var choices = targets.Where(target => target.InstanceId != source.InstanceId).ToList();
                if (choices.Count == 0)
                {
                    continue;
                }

                var target = choices[UnityEngine.Random.Range(0, choices.Count)];
                ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, 3);
            }

            UpdateRelicShow(self);
        }), "burning_calamity_wind_belt");
    }

    private static void UpdateRelicShow(ScriptExecutor self)
    {
        self?.UpdateRelicShow();
    }
}
