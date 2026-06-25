using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Scripting;

public static class RelicScripts
{
    public static void Fight(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "morning_shard":
                    ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
                    {
                        self.SetStatus("Self");
                        self.AddBuff(SunExpIds.SolarRadiance, "2");
                        UpdateRelicShow(self);
                    }), "morning_shard");
                    break;
                case "ember_cloak_lining":
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
                    break;
                case "sun_orbit_mirror":
                    RegisterSunOrbitMirror(self);
                    break;
                case "sun_bottle":
                    ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
                    {
                        var target = ExecutorApi.RandomEnemyTarget(self, true);
                        if (ExecutorApi.TriggerBurn(self, target))
                        {
                            UpdateRelicShow(self);
                        }
                    }), "sun_bottle");
                    break;
                case "solar_phase_dial":
                    RegisterSolarPhaseDial(self);
                    break;
                case "miniature_sunwheel":
                    ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
                    {
                        if (ExecutorApi.SelfBuffLevel(self, SunExpIds.ScorchingCanopy) <= 0)
                        {
                            return;
                        }

                        var total = BuffApi.NegativeTotal(self.Self);
                        if (total <= 0)
                        {
                            return;
                        }

                        self.SetStatus("Self");
                        self.AddBuff(SunExpIds.GatheredFlame, total.ToString());
                        UpdateRelicShow(self);
                    }), "miniature_sunwheel");
                    break;
                case "blazing_crown_heart":
                    RegisterBlazingCrownHeart(self);
                    break;
                case "solar_prism":
                    RegisterSolarPrism(self);
                    break;
                case "coronation_throne":
                    RegisterCoronationThrone(self);
                    break;
                case "gathered_flame_charm":
                    RegisterGatheredFlameCharm(self);
                    break;
                case "ash_charm":
                    RegisterAshCharm(self);
                    break;
                case "blazing_sundial":
                    RegisterBlazingSundial(self);
                    break;
                case "burning_calamity_wind_belt":
                    RegisterBurningCalamityWindBelt(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Relic Fight failed: " + id, ex);
        }
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

            if (ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance) > 0)
            {
                ExecutorApi.AddBurnToRandomEnemy(self, 2);
            }
            else
            {
                self.SetStatus("Self");
                self.AddBuff(SunExpIds.SolarRadiance, "2");
            }
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
            self.AddBuff(SunExpIds.SolarRadiance, "4");
            self.AddBuff(SunExpIds.SolarCrown, "1");
            ExecutorApi.ApplyFieldBuff(self, "scorching_canopy", 2);
        }), "blazing_crown_heart");
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            var burn = ExecutorApi.SelfBuffLevel(self, SunExpIds.SolarRadiance);
            if (burn <= 0)
            {
                return;
            }

            foreach (var target in ExecutorApi.EnemyTargets(self))
            {
                ExecutorApi.AddStatusBuff(self, target, SunExpIds.Burn, burn);
            }

            UpdateRelicShow(self);
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
        ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
        {
            var burn = ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
            if (burn <= 0)
            {
                return;
            }

            var removed = (burn + 1) / 2;
            ExecutorApi.RemoveBuffStacks(self, self.Self, SunExpIds.Burn, removed);
            self.SetStatus("Self");
            self.AddBuff(SunExpIds.GatheredFlame, removed.ToString());
            self.ChangeDefence(removed.ToString());
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
