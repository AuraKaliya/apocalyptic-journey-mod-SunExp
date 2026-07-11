using System;
using System.Collections.Generic;
using System.Reflection;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class PolymorphCooldownService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, CrossFormSkillUse> SkillUses = new(StringComparer.Ordinal);

    public static bool IsActive(IStatusManager? ownerStatus)
    {
        return ownerStatus != null
            && BuffApi.Has(ownerStatus, SunExpIds.PolymorphTraitBuffId)
            && PolymorphStateStore.ActiveFor(ownerStatus) != null;
    }

    public static void ApplyToCurrentRole(ScriptExecutor? self, string source)
    {
        RefreshSkillUi(self, source);
    }

    public static bool TryUseSharedSkill(ScriptExecutor? self, string source)
    {
        if (!IsActive(self?.Self))
        {
            return false;
        }

        if (IsCrossFormLocked(self?.Self, out var previousRole))
        {
            PlayerApi.ShowCaption("\u767e\u53d8\uff1a\u672c\u56de\u5408\u5df2\u4f7f\u7528\u5176\u4ed6\u5316\u8eab\u7684\u6280\u80fd\u3002");
            ApplyToCurrentRole(self, source + ".blocked");
            SunExpLog.Debug("[Polymorph] cross-form skill use blocked from " + source
                + "; previousRole=" + previousRole + ".");
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

        MarkCrossFormSkillUse(self?.Self);
        ApplyToCurrentRole(self, source + ".used");
        SunExpPerformanceCounters.Record("Polymorph.CrossFormSkillUsed");
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

            if (IsCrossFormLocked(self.Self, out _))
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

        AdvanceFallbackRound(self?.Self);
        ApplyToCurrentRole(self, source + ".tick");
        PruneExpiredUse(self?.Self);
        SunExpPerformanceCounters.Record("Polymorph.CrossFormSkillRoundObserved");
    }

    public static void Clear(IStatusManager? ownerStatus)
    {
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            SkillUses.Remove(owner);
        }
    }

    public static void ClearAll()
    {
        lock (SyncRoot)
        {
            SkillUses.Clear();
        }
    }

    private static bool IsCrossFormLocked(IStatusManager? ownerStatus, out string previousRole)
    {
        previousRole = "";
        var active = PolymorphStateStore.ActiveFor(ownerStatus);
        if (active == null)
        {
            return false;
        }

        var owner = OwnerKey(ownerStatus);
        var round = CurrentRoundKey(ownerStatus);
        lock (SyncRoot)
        {
            if (!SkillUses.TryGetValue(owner, out var used)
                || !string.Equals(used.RoundKey, round, StringComparison.Ordinal))
            {
                return false;
            }

            previousRole = used.RoleId;
            return !RoleMatches(used.RoleId, active.RoleId);
        }
    }

    private static void MarkCrossFormSkillUse(IStatusManager? ownerStatus)
    {
        var active = PolymorphStateStore.ActiveFor(ownerStatus);
        if (active == null)
        {
            return;
        }

        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            SkillUses[owner] = new CrossFormSkillUse(CurrentRoundKey(ownerStatus), active.RoleId);
        }
    }

    private static void PruneExpiredUse(IStatusManager? ownerStatus)
    {
        var owner = OwnerKey(ownerStatus);
        var round = CurrentRoundKey(ownerStatus);
        lock (SyncRoot)
        {
            if (SkillUses.TryGetValue(owner, out var used)
                && !string.Equals(used.RoundKey, round, StringComparison.Ordinal))
            {
                SkillUses.Remove(owner);
            }
        }
    }

    private static string CurrentRoundKey(IStatusManager? ownerStatus)
    {
        var reflected = ReadFirstInt(FightManager.Instance, "Round", "round", "RoundIndex", "roundIndex", "Turn", "turn", "TurnIndex", "turnIndex");
        if (reflected > 0)
        {
            return "fight:" + reflected;
        }

        return "local:" + PlayerApi.GetGameVar(PlayerApi.ScopedGameVarKey("SunExpPolymorphCrossFormRound", ownerStatus), "0");
    }

    private static void AdvanceFallbackRound(IStatusManager? ownerStatus)
    {
        if (ReadFirstInt(FightManager.Instance, "Round", "round", "RoundIndex", "roundIndex", "Turn", "turn", "TurnIndex", "turnIndex") > 0)
        {
            return;
        }

        var key = PlayerApi.ScopedGameVarKey("SunExpPolymorphCrossFormRound", ownerStatus);
        var next = DictionaryUtil.ParseInt(PlayerApi.GetGameVar(key), 0) + 1;
        PlayerApi.SetGameVar(key, next.ToString());
    }

    private static int ReadFirstInt(object? target, params string[] names)
    {
        if (target == null)
        {
            return 0;
        }

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in names)
        {
            try
            {
                var type = target.GetType();
                var value = type.GetProperty(name, Flags)?.GetValue(target)
                            ?? type.GetField(name, Flags)?.GetValue(target);
                if (value is int parsed && parsed > 0)
                {
                    return parsed;
                }

                if (int.TryParse(Convert.ToString(value), out parsed) && parsed > 0)
                {
                    return parsed;
                }
            }
            catch
            {
                // Reflection only improves round precision; the scoped fallback stays safe.
            }
        }

        return 0;
    }

    private static bool RoleMatches(string left, string right)
    {
        var first = NormalizeRoleId(left);
        var second = NormalizeRoleId(right);
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase)
            || first.EndsWith("_" + second, StringComparison.OrdinalIgnoreCase)
            || second.EndsWith("_" + first, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoleId(string roleId)
    {
        return (roleId ?? "").Trim().TrimStart('*');
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

    private sealed class CrossFormSkillUse
    {
        public CrossFormSkillUse(string roundKey, string roleId)
        {
            RoundKey = roundKey ?? "";
            RoleId = roleId ?? "";
        }

        public string RoundKey { get; }

        public string RoleId { get; }
    }
}
