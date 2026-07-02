using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class PolymorphCooldownService
{
    public const int SkillCooldownRounds = 1;

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, int> Cooldowns = new(StringComparer.Ordinal);

    public static bool IsActive(IStatusManager? ownerStatus)
    {
        return ownerStatus != null
            && BuffApi.Has(ownerStatus, SunExpIds.PolymorphTraitBuffId)
            && PolymorphStateStore.ActiveFor(ownerStatus) != null;
    }

    public static int Current(IStatusManager? ownerStatus)
    {
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            if (!Cooldowns.TryGetValue(owner, out var cooldown))
            {
                cooldown = 0;
                Cooldowns[owner] = cooldown;
            }

            return cooldown;
        }
    }

    public static void ApplyToCurrentRole(ScriptExecutor? self, string source)
    {
        var owner = self?.Self;
        var cooldown = Current(owner);
        RoleSkillApi.SetCurrentCareerSkillTimes(cooldown);
        RefreshSkillUi(self, source);
    }

    public static bool TryUseSharedSkill(ScriptExecutor? self, string source)
    {
        if (!IsActive(self?.Self))
        {
            return false;
        }

        var cooldown = Current(self?.Self);
        if (cooldown > 0)
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u6280\u80fd\u5c1a\u672a\u51b7\u5374\u3002");
            ApplyToCurrentRole(self, source + ".blocked");
            return true;
        }

        return false;
    }

    public static bool MarkSkillUsed(ScriptExecutor? self, string source)
    {
        if (!IsActive(self?.Self))
        {
            return false;
        }

        Set(self?.Self, SkillCooldownRounds);
        ApplyToCurrentRole(self, source + ".used");
        SunExpPerformanceCounters.Record("Polymorph.CooldownSkillUsed");
        return true;
    }

    public static bool ShouldCaptureSkillUse(SkillItem? skillItem, string source)
    {
        try
        {
            if (skillItem?.dataConfig == null || skillItem.scriptExecutor is not ScriptExecutor self)
            {
                return false;
            }

            if (!IsActive(self.Self) || !RoleSkillApi.IsCurrentCareerSkill(skillItem.dataConfig))
            {
                return false;
            }

            if (Current(self.Self) > 0)
            {
                ApplyToCurrentRole(self, source + ".blocked");
                return false;
            }

            return skillItem.TryUse();
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[Polymorph] skill capture skipped from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static bool MarkSkillItemUsed(SkillItem? skillItem, string source)
    {
        if (skillItem?.scriptExecutor is not ScriptExecutor self)
        {
            return false;
        }

        var id = RoleSkillApi.NormalizeSkillId(CardConfigApi.Id(skillItem.dataConfig));
        return MarkSkillUsed(self, source + ":" + id);
    }

    public static void TickRound(ScriptExecutor? self, string source)
    {
        if (!IsActive(self?.Self))
        {
            return;
        }

        var owner = self?.Self;
        var current = Current(owner);
        if (current > 0)
        {
            Set(owner, current - 1);
        }

        ApplyToCurrentRole(self, source + ".tick");
        SunExpPerformanceCounters.Record("Polymorph.CooldownTick");
    }

    public static void Clear(IStatusManager? ownerStatus)
    {
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            Cooldowns.Remove(owner);
        }
    }

    public static void ClearAll()
    {
        lock (SyncRoot)
        {
            Cooldowns.Clear();
        }
    }

    private static void Set(IStatusManager? ownerStatus, int value)
    {
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            Cooldowns[owner] = Math.Max(0, Math.Min(SkillCooldownRounds, value));
        }
    }

    private static void RefreshSkillUi(ScriptExecutor? self, string source)
    {
        try
        {
            self?.UpdateSkillTime();
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[Polymorph] cooldown UI refresh skipped from " + source + ": " + ex.Message);
        }
    }

    private static string OwnerKey(IStatusManager? ownerStatus)
    {
        var owner = ownerStatus?.InstanceId ?? PlayerApi.LocalPlayerStatusId();
        return string.IsNullOrWhiteSpace(owner) ? "local" : owner;
    }
}
