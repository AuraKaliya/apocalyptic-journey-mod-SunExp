using System;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class WunaScripts
{
    private const string GraveSongCardId = "SunExp_wuna_wuna_grave_song";

    public static void InitCareer(ScriptExecutor self)
    {
        try
        {
            PlayerApi.SetGameVar(SunExpIds.WunaActive, "1");
            PlayerApi.SetSkillTime(SunExpIds.WunaWhiteSunPrayerCardId, 0);
            PlayerApi.SetSkillTime(GraveSongCardId, 0);
            ExecutorApi.SetVar(self, "SunExpWunaRadianceDone", "0");
            ExecutorApi.SetVar(self, "SunExpWunaPrevEnemyBurn", "0");
            AttachOrbitFire(self, "InitCareer");

            var token = (DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpWunaCareerToken", "0")) + 1).ToString();
            self.SetStatus("Self");
            var fightStartRegistered = ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
            {
                if (!ExecutorApi.IsHookTokenActive(self, "SunExpWunaCareerToken", token))
                {
                    return;
                }

                RestorePersistentEmber(self);
                ExecutorApi.SetVar(self, "SunExpWunaRadianceDone", "0");
                ExecutorApi.SetVar(self, "SunExpWunaPrevEnemyBurn", EnemyBurnTotal(self));
                WunaRoundRadianceState.ResetFight(self.Self);
                AttachOrbitFire(self, "FightStart");
            }), "wuna_career");
            var startRoundRegistered = ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "SunExpWunaCareerToken", token))
                {
                    StartRound(self);
                }
            }), "wuna_career");
            var burnChangeRegistered = ExecutorApi.TryAddEvent(self, "buff_burnOnLevelChange", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "SunExpWunaCareerToken", token))
                {
                    TryGainRadianceFromEnemyBurn(self);
                }
            }), "wuna_career");

            ExecutorApi.TryAddEvent(self, "Win", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "SunExpWunaCareerToken", token))
                {
                    SaveAndClearCareerHook(self);
                }
            }), "wuna_career");
            ExecutorApi.TryAddEvent(self, "Escape", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "SunExpWunaCareerToken", token))
                {
                    SaveAndClearCareerHook(self);
                }
            }), "wuna_career");

            if (fightStartRegistered && startRoundRegistered && burnChangeRegistered)
            {
                ExecutorApi.SetVar(self, "SunExpWunaCareerHook", "1");
                ExecutorApi.SetVar(self, "SunExpWunaCareerToken", token);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Wuna InitCareer failed", ex);
        }
    }

    public static void Init(ScriptExecutor self, string id)
    {
        try
        {
            ExecutorApi.SetBaseScript(self, "CommonCardItem");
            if (id == "*wuna_white_sun_prayer")
            {
                self.AddDescription("1", "Value", "5");
            }
            else if (id == "*wuna_grave_song")
            {
                self.AddDescription("1", "Buff", "30");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Wuna Init failed: " + id, ex);
        }
    }

    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "*wuna_white_sun_prayer":
                    UseWhiteSunPrayer(self);
                    break;
                case "*wuna_grave_song":
                    UseGraveSong(self);
                    break;
                case "*wuna_coronation_token":
                    self.SetStatus("Self");
                    self.AddBuff(SunExpIds.SolarRadiance, "2");
                    self.AddBuff(SunExpIds.SolarCrown, "2");
                    break;
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Wuna Use failed: " + id, ex);
        }
    }

    private static void UseWhiteSunPrayer(ScriptExecutor self)
    {
        if (!IsWunaRuntimeActive())
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u4e4c\u5a1c\u6280\u80fd\u5df2\u88ab\u5f53\u524d\u5316\u8eab\u8986\u76d6\u3002");
            return;
        }

        if (PolymorphCooldownService.TryUseSharedSkill(self, "Wuna.WhiteSunPrayer"))
        {
            return;
        }

        var cooldown = PlayerApi.GetSkillTime(SunExpIds.WunaWhiteSunPrayerCardId);
        if (cooldown > 0)
        {
            PlayerApi.ShowCaption("白曜圣祷尚未冷却。");
            return;
        }

        self.SetStatus("Self");
        AttachOrbitFire(self, "WhiteSunPrayer", "Skill");
        AudioApi.PlayWhiteSunPrayer();
        var grant = WunaCardGrantService.GrantCoronationTokenToHand(self, SunExpIds.WunaCoronationTokenCardId);
        if (!grant.Success)
        {
            SunExpLog.Warn("Wuna coronation token grant failed: step=" + grant.FailureStep + ", error=" + grant.FailureReason);
        }

        var handTagRequested = SunExpCardTagService.RequestBurnoutAndWhiteRadianceForFriendlyHands(self, "Wuna.WhiteSunPrayer");
        SunExpLog.Info("Wuna white sun prayer hand tag requested=" + handTagRequested);
        if (!PolymorphCooldownService.MarkSkillUsed(self, "Wuna.WhiteSunPrayer"))
        {
            PlayerApi.SetSkillTime(SunExpIds.WunaWhiteSunPrayerCardId, 5);
        }
    }

    private static void UseGraveSong(ScriptExecutor self)
    {
        if (!IsWunaRuntimeActive())
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u4e4c\u5a1c\u6280\u80fd\u5df2\u88ab\u5f53\u524d\u5316\u8eab\u8986\u76d6\u3002");
            return;
        }

        if (PolymorphCooldownService.TryUseSharedSkill(self, "Wuna.GraveSong"))
        {
            return;
        }

        var cooldown = PlayerApi.GetSkillTime(GraveSongCardId);
        if (cooldown > 0)
        {
            PlayerApi.ShowCaption("圣庭墓曲尚未冷却。");
            return;
        }

        var ember = ExecutorApi.SelfBuffLevel(self, SunExpIds.Ember);
        if (ember <= 30)
        {
            PlayerApi.ShowCaption("余烬不足。");
            return;
        }

        var burn = ember / 2;
        AudioApi.PlayGraveSong();
        self.SetStatus("Self");
        AttachOrbitFire(self, "GraveSong", "Skill");
        BuffApi.ClearEmberDamageBonus(self, self.Self);
        self.RemoveBuff(SunExpIds.Ember);
        BuffApi.OnEmberConsumed(self, self.Self, ember);
        SetPersistentEmber(self, 0);
        if (!PolymorphCooldownService.MarkSkillUsed(self, "Wuna.GraveSong"))
        {
            PlayerApi.SetSkillTime(GraveSongCardId, 4);
        }
        if (burn > 0)
        {
            self.SetStatus("All");
            self.AddBuff(SunExpIds.Burn, burn.ToString());
        }
        self.SetStatus("Self");
        self.AddBuff(SunExpIds.EmberCloak, "1");
        ExecutorApi.TriggerBurnAll(self);
    }

    private static void StartRound(ScriptExecutor self)
    {
        if (!IsWunaRuntimeActive())
        {
            return;
        }

        AttachOrbitFire(self, "StartRound");
        WunaRoundRadianceState.AdvanceLocalRound(self.Self);
        if (!PolymorphCooldownService.IsActive(self.Self))
        {
            TickSkillTimes();
        }
        ExecutorApi.SetVar(self, "SunExpWunaRadianceDone", "0");

        var emberGain = AllBurnTotal(self) / 2;
        if (emberGain > 0)
        {
            AddEmber(self, emberGain);
        }

        ExecutorApi.SetVar(self, "SunExpWunaPrevEnemyBurn", EnemyBurnTotal(self));
    }

    private static int SavePersistentEmber(ScriptExecutor self)
    {
        return BuffApi.SavePersistentEmber(self, self?.Self);
    }

    private static void SaveAndClearCareerHook(ScriptExecutor self)
    {
        SavePersistentEmber(self);
        ExecutorApi.ClearHook(self, "SunExpWunaCareerHook", "SunExpWunaCareerToken");
    }

    private static void TickSkillTimes()
    {
        TickSkillTime(SunExpIds.WunaWhiteSunPrayerCardId);
        TickSkillTime(GraveSongCardId);
    }

    private static void TickSkillTime(string key)
    {
        var current = PlayerApi.GetSkillTime(key);
        if (current > 0)
        {
            PlayerApi.SetSkillTime(key, current - 1);
        }
    }

    private static int EnemyBurnTotal(ScriptExecutor self)
    {
        return ExecutorApi.EnemyTargets(self).Sum(target => ExecutorApi.StatusBuffLevel(target, SunExpIds.Burn));
    }

    private static int AllBurnTotal(ScriptExecutor self)
    {
        return EnemyBurnTotal(self) + ExecutorApi.SelfBuffLevel(self, SunExpIds.Burn);
    }

    private static int AddEmber(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        self.SetStatus("Self");
        self.AddBuff(SunExpIds.Ember, amount.ToString());
        var level = ClampEmber(self);
        BuffApi.SyncEmberDamageBonus(self, self.Self);
        return level;
    }

    private static int ClampEmber(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return 0;
        }

        var ember = self.Self.GetBuff(SunExpIds.Ember);
        if (ember?.buffConfig == null)
        {
            return 0;
        }

        var level = ember.buffConfig.Level;
        if (level > 99)
        {
            ember.buffConfig.Level = 99;
            BuffApi.SyncEmberDamageBonus(self, self.Self);
            SetPersistentEmber(self, 99);
            return 99;
        }

        if (level <= 0)
        {
            BuffApi.ClearEmberDamageBonus(self, self.Self);
            self.SetStatus("Self");
            self.RemoveBuff(SunExpIds.Ember);
            SetPersistentEmber(self, 0);
            return 0;
        }

        SetPersistentEmber(self, level);
        return level;
    }

    private static int RestorePersistentEmber(ScriptExecutor self)
    {
        if (self?.Self == null)
        {
            return 0;
        }

        var stored = GetPersistentEmber(self);
        self.SetStatus("Self");
        if (ExecutorApi.SelfBuffLevel(self, SunExpIds.Ember) > 0)
        {
            BuffApi.ClearEmberDamageBonus(self, self.Self);
            self.RemoveBuff(SunExpIds.Ember);
        }

        if (stored > 0)
        {
            self.AddBuff(SunExpIds.Ember, stored.ToString());
            BuffApi.SyncEmberDamageBonus(self, self.Self);
        }
        else
        {
            BuffApi.ClearEmberDamageBonus(self, self.Self);
        }

        return stored;
    }

    private static bool TryGainRadianceFromEnemyBurn(ScriptExecutor self)
    {
        if (!IsWunaRuntimeActive())
        {
            return false;
        }

        if (self?.Self == null)
        {
            return false;
        }

        var current = EnemyBurnTotal(self);
        var previous = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "SunExpWunaPrevEnemyBurn", current.ToString()));
        ExecutorApi.SetVar(self, "SunExpWunaPrevEnemyBurn", current);
        if (current <= previous
            || !WunaRoundRadianceState.TryMarkTriggered(self.Self, "WunaScripts.TryGainRadianceFromEnemyBurn"))
        {
            return false;
        }

        self.SetStatus("Self");
        self.AddBuff(SunExpIds.SolarRadiance, "1");
        ExecutorApi.SetVar(self, "SunExpWunaRadianceDone", "1");
        return true;
    }

    private static int GetPersistentEmber(ScriptExecutor self)
    {
        return WunaEmberSyncService.GetStored(self?.Self);
    }

    private static int SetPersistentEmber(ScriptExecutor self, int value)
    {
        var level = Math.Max(0, Math.Min(99, value));
        WunaEmberSyncService.CommitLocal(self?.Self, level, "WunaScripts.SetPersistentEmber");
        return level;
    }

    private static bool IsWunaRuntimeActive()
    {
        return !PolymorphStateStore.IsLocalRoleSuppressed("wuna") && BuffApi.IsWunaActive();
    }

    private static void AttachOrbitFire(ScriptExecutor self, string source, string action = "")
    {
        WunaVisualApi.AttachOrbitFire(self, action, "WunaScripts." + source);
    }
}
