using System;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Scripting;

public static class WunaScripts
{
    private const string GraveSongCardId = "Terrias_wuna_wuna_grave_song";

    public static void InitCareer(ScriptExecutor self)
    {
        try
        {
            PlayerApi.SetGameVar(TerriasIds.WunaActive, "1");
            PlayerApi.SetSkillTime(TerriasIds.WunaWhiteSunPrayerCardId, 0);
            PlayerApi.SetSkillTime(GraveSongCardId, 0);
            ExecutorApi.SetVar(self, "TerriasWunaRadianceDone", "0");
            ExecutorApi.SetVar(self, "TerriasWunaPrevEnemyBurn", "0");
            AttachOrbitFire(self, "InitCareer");

            var token = ExecutorApi.RegisterHook(self, "TerriasWunaCareerHook", "TerriasWunaCareerToken");
            if (token == null)
            {
                return;
            }

            self.SetStatus("Self");
            var fightStartRegistered = ExecutorApi.TryAddEvent(self, "FightStart", new Action(() =>
            {
                if (!ExecutorApi.IsHookTokenActive(self, "TerriasWunaCareerToken", token))
                {
                    return;
                }

                ExecutorApi.SetVar(self, "TerriasWunaRadianceDone", "0");
                ExecutorApi.SetVar(self, "TerriasWunaPrevEnemyBurn", EnemyBurnTotal(self));
                WunaRoundRadianceState.ResetFight(self.Self);
                AttachOrbitFire(self, "FightStart");
                RegisterEnemyBurnListeners(self, token);
            }), "wuna_career");
            var startRoundRegistered = ExecutorApi.TryAddEvent(self, "StartRound", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "TerriasWunaCareerToken", token))
                {
                    StartRound(self);
                    RegisterEnemyBurnListeners(self, token);
                }
            }), "wuna_career");
            var actionRegistered = ExecutorApi.TryAddEvent(self, "Action", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "TerriasWunaCareerToken", token))
                {
                    RegisterEnemyBurnListeners(self, token);
                }
            }), "wuna_career");

            ExecutorApi.TryAddEvent(self, "Win", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "TerriasWunaCareerToken", token))
                {
                    SaveAndClearCareerHook(self);
                }
            }), "wuna_career");
            ExecutorApi.TryAddEvent(self, "Escape", new Action(() =>
            {
                if (ExecutorApi.IsHookTokenActive(self, "TerriasWunaCareerToken", token))
                {
                    SaveAndClearCareerHook(self);
                }
            }), "wuna_career");

            if (fightStartRegistered && startRoundRegistered && actionRegistered)
            {
                RegisterEnemyBurnListeners(self, token);
                return;
            }

            ExecutorApi.ClearHook(self, "TerriasWunaCareerHook", "TerriasWunaCareerToken");
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Wuna InitCareer failed", ex);
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
            TerriasLog.Error("Wuna Init failed: " + id, ex);
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
                    self.AddBuff(TerriasIds.SolarRadiance, "2");
                    self.AddBuff(TerriasIds.SolarCrown, "2");
                    break;
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("Wuna Use failed: " + id, ex);
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

        var cooldown = PlayerApi.GetSkillTime(TerriasIds.WunaWhiteSunPrayerCardId);
        if (cooldown > 0)
        {
            PlayerApi.ShowCaption("白曜圣祷尚未冷却。");
            return;
        }

        self.SetStatus("Self");
        AttachOrbitFire(self, "WhiteSunPrayer", "Skill");
        AudioApi.PlayWhiteSunPrayer();
        var grant = WunaCardGrantService.GrantCoronationTokenToHand(self, TerriasIds.WunaCoronationTokenCardId);
        if (!grant.Success)
        {
            TerriasLog.Warn("Wuna coronation token grant failed: step=" + grant.FailureStep + ", error=" + grant.FailureReason);
        }

        var handTagRequested = TerriasCardTagService.RequestBurnoutAndWhiteRadianceForFriendlyHands(self, "Wuna.WhiteSunPrayer");
        TerriasLog.Info("Wuna white sun prayer hand tag requested=" + handTagRequested);
        if (!PolymorphCooldownService.MarkSkillUsed(self, "Wuna.WhiteSunPrayer"))
        {
            PlayerApi.SetSkillTime(TerriasIds.WunaWhiteSunPrayerCardId, 5);
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

        var ember = ExecutorApi.SelfBuffLevel(self, TerriasIds.Ember);
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
        self.RemoveBuff(TerriasIds.Ember);
        BuffApi.OnEmberConsumed(self, self.Self, ember);
        if (!PolymorphCooldownService.MarkSkillUsed(self, "Wuna.GraveSong"))
        {
            PlayerApi.SetSkillTime(GraveSongCardId, 4);
        }
        if (burn > 0)
        {
            self.SetStatus("All");
            self.AddBuff(TerriasIds.Burn, burn.ToString());
        }
        self.SetStatus("Self");
        self.AddBuff(TerriasIds.EmberCloak, "1");
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
        ExecutorApi.SetVar(self, "TerriasWunaRadianceDone", "0");

        var emberGain = AllBurnTotal(self) / 2;
        if (emberGain > 0)
        {
            AddEmber(self, emberGain);
        }

        ExecutorApi.SetVar(self, "TerriasWunaPrevEnemyBurn", EnemyBurnTotal(self));
    }

    private static int SavePersistentEmber(ScriptExecutor self)
    {
        return BuffApi.SavePersistentEmber(self, self?.Self);
    }

    private static void SaveAndClearCareerHook(ScriptExecutor self)
    {
        SavePersistentEmber(self);
        ExecutorApi.ClearHook(self, "TerriasWunaCareerHook", "TerriasWunaCareerToken");
    }

    private static void TickSkillTimes()
    {
        TickSkillTime(TerriasIds.WunaWhiteSunPrayerCardId);
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
        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            return ExecutorApi.EnemyTargets(self).Sum(target => ExecutorApi.StatusBuffLevel(target, TerriasIds.Burn));
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("WunaRadiance.EnemyBurnTotal", start);
        }
    }

    private static int AllBurnTotal(ScriptExecutor self)
    {
        return EnemyBurnTotal(self) + ExecutorApi.SelfBuffLevel(self, TerriasIds.Burn);
    }

    private static int RegisterEnemyBurnListeners(ScriptExecutor self, string token)
    {
        if (self == null || !ExecutorApi.IsHookTokenActive(self, "TerriasWunaCareerToken", token))
        {
            return 0;
        }

        try
        {
            var registered = 0;
            foreach (var target in ExecutorApi.EnemyTargets(self))
            {
                var targetId = target?.InstanceId;
                if (string.IsNullOrWhiteSpace(targetId))
                {
                    continue;
                }

                var listenerKey = "TerriasWunaBurnListener_" + targetId + "_" + token;
                if (ExecutorApi.GetVar(self, listenerKey, "0") == "1")
                {
                    continue;
                }

                ExecutorApi.SetVar(self, listenerKey, "1");
                EventCenter.Instance.AddEventListener(
                    TerriasIds.Burn + "OnLevelChange" + targetId,
                    new Action(() => OnEnemyBurnChanged(self, token)),
                    self,
                    EventDispose.OnFightEnd);
                registered++;
            }

            if (registered > 0)
            {
                TerriasLog.Debug("[WunaRadiance] registered enemy burn listeners: count=" + registered + ".");
            }

            return registered;
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[WunaRadiance] enemy burn listener registration skipped: " + ex.Message);
            return 0;
        }
    }

    private static void OnEnemyBurnChanged(ScriptExecutor self, string token)
    {
        if (self?.Self == null || !ExecutorApi.IsHookTokenActive(self, "TerriasWunaCareerToken", token))
        {
            return;
        }

        var ownerId = self.Self.InstanceId;
        var enqueued = TerriasFrameDispatcher.RunOnceNextFrame(
            "WunaRadiance.BurnChanged." + ownerId + "." + token,
            () =>
            {
                var start = TerriasPerformanceCounters.Timestamp();
                try
                {
                    if (ExecutorApi.IsHookTokenActive(self, "TerriasWunaCareerToken", token))
                    {
                        TryGainRadianceFromEnemyBurn(self);
                    }
                }
                finally
                {
                    TerriasPerformanceCounters.RecordDuration("WunaRadiance.BurnChanged.Action", start);
                }
            });
        TerriasPerformanceCounters.Record(enqueued
            ? "WunaRadiance.BurnChanged.Enqueued"
            : "WunaRadiance.BurnChanged.Deduped");
    }

    private static int AddEmber(ScriptExecutor self, int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        self.SetStatus("Self");
        self.AddBuff(TerriasIds.Ember, amount.ToString());
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

        var ember = self.Self.GetBuff(TerriasIds.Ember);
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
            self.RemoveBuff(TerriasIds.Ember);
            SetPersistentEmber(self, 0);
            return 0;
        }

        SetPersistentEmber(self, level);
        return level;
    }

    private static bool TryGainRadianceFromEnemyBurn(ScriptExecutor self)
    {
        var start = TerriasPerformanceCounters.Timestamp();
        try
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
            var previous = DictionaryUtil.ParseInt(ExecutorApi.GetVar(self, "TerriasWunaPrevEnemyBurn", current.ToString()));
            ExecutorApi.SetVar(self, "TerriasWunaPrevEnemyBurn", current);
            if (current <= previous
                || !WunaRoundRadianceState.TryMarkTriggered(self.Self, "WunaScripts.TryGainRadianceFromEnemyBurn"))
            {
                return false;
            }

            self.SetStatus("Self");
            var addBuffStart = TerriasPerformanceCounters.Timestamp();
            try
            {
                self.AddBuff(TerriasIds.SolarRadiance, "1");
            }
            finally
            {
                TerriasPerformanceCounters.RecordDuration("WunaRadiance.AddSolarRadiance", addBuffStart);
            }

            ExecutorApi.SetVar(self, "TerriasWunaRadianceDone", "1");
            return true;
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("WunaRadiance.TryGainRadianceFromEnemyBurn", start);
        }
    }

    private static int SetPersistentEmber(ScriptExecutor self, int value)
    {
        var level = Math.Max(0, Math.Min(99, value));
        EmberAdventureStateService.CommitLocal(self?.Self, level, "WunaScripts.SetPersistentEmber");
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
