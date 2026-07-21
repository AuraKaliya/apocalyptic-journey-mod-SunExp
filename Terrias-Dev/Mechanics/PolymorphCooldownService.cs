using System;
using System.Collections.Generic;
using System.Reflection;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class PolymorphCooldownService
{
    private const int CrossFormEntryCooldown = 1;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, CrossFormSkillUse> SkillUses = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, int>>> RoleCooldowns = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, EntryCooldownOverlay> EntryCooldownOverlays = new(StringComparer.Ordinal);

    public static bool IsActive(IStatusManager? ownerStatus)
    {
        return ownerStatus != null
            && BuffApi.Has(ownerStatus, TerriasIds.PolymorphTraitBuffId)
            && PolymorphStateStore.ActiveFor(ownerStatus) != null;
    }

    public static void ApplyToCurrentRole(ScriptExecutor? self, string source)
    {
        RefreshSkillUi(self, source);
    }

    public static void CaptureCurrentRole(IStatusManager? ownerStatus, string source)
    {
        var active = PolymorphStateStore.ActiveFor(ownerStatus);
        if (active == null)
        {
            return;
        }

        var owner = OwnerKey(ownerStatus);
        var normalizedRole = NormalizeRoleId(active.RoleId);
        EntryCooldownOverlay? overlay = null;
        lock (SyncRoot)
        {
            if (EntryCooldownOverlays.TryGetValue(owner, out var currentOverlay)
                && string.Equals(currentOverlay.RoleId, normalizedRole, StringComparison.OrdinalIgnoreCase))
            {
                overlay = currentOverlay.Copy();
            }
        }

        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        var ignoredSynthetic = 0;
        foreach (var skillId in RoleSkillApi.CurrentCareerSkillIds())
        {
            var actual = Math.Max(0, PlayerApi.GetSkillTime(skillId));
            if (overlay != null && overlay.SyntheticSkillIds.Contains(skillId))
            {
                var baseline = overlay.BaselineCooldowns.TryGetValue(skillId, out var saved)
                    ? Math.Max(0, saved)
                    : 0;
                values[skillId] = actual <= CrossFormEntryCooldown ? baseline : actual;
                ignoredSynthetic++;
            }
            else
            {
                values[skillId] = actual;
            }
        }

        if (values.Count == 0)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!RoleCooldowns.TryGetValue(owner, out var roles))
            {
                roles = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                RoleCooldowns[owner] = roles;
            }

            roles[normalizedRole] = values;
        }

        TerriasLog.Debug("[Polymorph] captured role cooldowns from " + source
            + ": role=" + active.RoleId
            + ", ignoredEntryCooldowns=" + ignoredSynthetic
            + ", values=" + FormatCooldowns(values) + ".");
    }

    public static void PrepareCurrentRoleEntry(IStatusManager? ownerStatus, string roleId, string source)
    {
        var owner = OwnerKey(ownerStatus);
        var normalizedRole = NormalizeRoleId(roleId);
        Dictionary<string, int>? saved = null;
        lock (SyncRoot)
        {
            EntryCooldownOverlays.Remove(owner);
            if (RoleCooldowns.TryGetValue(owner, out var roles)
                && roles.TryGetValue(normalizedRole, out var previous))
            {
                saved = new Dictionary<string, int>(previous, StringComparer.Ordinal);
            }
        }

        // Career scripts may seed a non-zero initial cooldown. Polymorph forms
        // start ready on their first visit; revisits restore only cooldowns
        // produced while that same form was active.
        RoleSkillApi.SetCurrentCareerSkillTimes(0);
        var realCooldowns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var skillId in RoleSkillApi.CurrentCareerSkillIds())
        {
            var cooldown = saved != null && saved.TryGetValue(skillId, out var actual)
                ? Math.Max(0, actual)
                : 0;
            PlayerApi.SetSkillTime(skillId, cooldown);
            realCooldowns[skillId] = cooldown;
        }

        lock (SyncRoot)
        {
            if (!RoleCooldowns.TryGetValue(owner, out var roles))
            {
                roles = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                RoleCooldowns[owner] = roles;
            }

            roles[normalizedRole] = new Dictionary<string, int>(realCooldowns, StringComparer.Ordinal);
        }

        var entryCooldownRequired = HasDifferentFormSkillUseThisRound(ownerStatus, out var usedRole);
        var entryCooldownCount = entryCooldownRequired
            ? ApplyEntryCooldownFloor(ownerStatus, normalizedRole, realCooldowns, source + ".entry")
            : 0;

        TerriasLog.InfoAlways("[PolymorphCooldown] prepared role from " + source
            + ": role=" + roleId
            + ", firstEntry=" + (saved == null)
            + ", entryCooldownRequired=" + entryCooldownRequired
            + ", entryCooldownCount=" + entryCooldownCount
            + ", usedRole=" + usedRole
            + ", realValues=" + FormatCooldowns(realCooldowns) + ".");
    }

    public static bool MarkSkillUsed(ScriptExecutor? self, string source, string? skillId = null)
    {
        if (!IsActive(self?.Self))
        {
            return false;
        }

        MarkCrossFormSkillUse(self?.Self);
        ConsumeEntryCooldown(self?.Self, skillId);
        CaptureCurrentRole(self?.Self, source + ".capture");
        ApplyToCurrentRole(self, source + ".used");
        var active = PolymorphStateStore.ActiveFor(self?.Self);
        TerriasLog.InfoAlways("[PolymorphCooldown] skill use committed from " + source
            + ": role=" + active?.RoleId
            + ", skill=" + RoleSkillApi.NormalizeSkillId(skillId)
            + ", round=" + CurrentRoundKey(self?.Self) + ".");
        TerriasPerformanceCounters.Record("Polymorph.CrossFormSkillUsed");
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

            return skillItem.TryUse();
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[Polymorph] skill capture skipped from " + source + ": " + ex.Message);
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
        return MarkSkillUsed(self, source + ":" + id, id);
    }

    public static void TickRound(ScriptExecutor? self, string source)
    {
        if (!IsActive(self?.Self))
        {
            return;
        }

        ReleaseEntryCooldown(self?.Self, source + ".release");
        AdvanceFallbackRound(self?.Self);
        PruneExpiredUse(self?.Self);
        CaptureCurrentRole(self?.Self, source + ".capture");
        ApplyToCurrentRole(self, source + ".tick");
        TerriasPerformanceCounters.Record("Polymorph.CrossFormSkillRoundObserved");
    }

    public static void Clear(IStatusManager? ownerStatus)
    {
        var owner = OwnerKey(ownerStatus);
        lock (SyncRoot)
        {
            SkillUses.Remove(owner);
            RoleCooldowns.Remove(owner);
            EntryCooldownOverlays.Remove(owner);
        }
    }

    public static void ClearAll()
    {
        lock (SyncRoot)
        {
            SkillUses.Clear();
            RoleCooldowns.Clear();
            EntryCooldownOverlays.Clear();
        }
    }

    private static bool HasDifferentFormSkillUseThisRound(IStatusManager? ownerStatus, out string previousRole)
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

    private static int ApplyEntryCooldownFloor(
        IStatusManager? ownerStatus,
        string normalizedRole,
        Dictionary<string, int> realCooldowns,
        string source)
    {
        var syntheticSkillIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skillId in RoleSkillApi.CurrentCareerSkillIds())
        {
            var cooldown = realCooldowns.TryGetValue(skillId, out var current)
                ? Math.Max(0, current)
                : 0;
            var presented = Math.Max(cooldown, CrossFormEntryCooldown);
            if (presented != cooldown)
            {
                syntheticSkillIds.Add(skillId);
            }

            PlayerApi.SetSkillTime(skillId, presented);
        }

        if (syntheticSkillIds.Count > 0)
        {
            lock (SyncRoot)
            {
                EntryCooldownOverlays[OwnerKey(ownerStatus)] = new EntryCooldownOverlay(
                    normalizedRole,
                    realCooldowns,
                    syntheticSkillIds);
            }
        }

        TerriasLog.Debug("[Polymorph] applied one-time entry cooldown from " + source
            + ": role=" + normalizedRole
            + ", skills=" + string.Join("|", syntheticSkillIds) + ".");
        return syntheticSkillIds.Count;
    }

    private static void ConsumeEntryCooldown(IStatusManager? ownerStatus, string? skillId)
    {
        var owner = OwnerKey(ownerStatus);
        var normalizedSkill = RoleSkillApi.NormalizeSkillId(skillId);
        lock (SyncRoot)
        {
            if (!EntryCooldownOverlays.TryGetValue(owner, out var overlay))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(normalizedSkill))
            {
                EntryCooldownOverlays.Remove(owner);
                return;
            }

            overlay.SyntheticSkillIds.Remove(normalizedSkill);
            if (overlay.SyntheticSkillIds.Count == 0)
            {
                EntryCooldownOverlays.Remove(owner);
            }
        }
    }

    private static void ReleaseEntryCooldown(IStatusManager? ownerStatus, string source)
    {
        var owner = OwnerKey(ownerStatus);
        EntryCooldownOverlay? overlay = null;
        lock (SyncRoot)
        {
            if (EntryCooldownOverlays.TryGetValue(owner, out var current))
            {
                overlay = current.Copy();
                EntryCooldownOverlays.Remove(owner);
            }
        }

        if (overlay == null)
        {
            return;
        }

        foreach (var skillId in overlay.SyntheticSkillIds)
        {
            var actual = Math.Max(0, PlayerApi.GetSkillTime(skillId));
            if (actual <= CrossFormEntryCooldown)
            {
                PlayerApi.SetSkillTime(skillId,
                    overlay.BaselineCooldowns.TryGetValue(skillId, out var baseline)
                        ? Math.Max(0, baseline)
                        : 0);
            }
        }

        TerriasLog.Debug("[Polymorph] released one-time entry cooldown from " + source
            + ": role=" + overlay.RoleId + ".");
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

        return "local:" + PlayerApi.GetGameVar(PlayerApi.ScopedGameVarKey("TerriasPolymorphCrossFormRound", ownerStatus), "0");
    }

    private static void AdvanceFallbackRound(IStatusManager? ownerStatus)
    {
        if (ReadFirstInt(FightManager.Instance, "Round", "round", "RoundIndex", "roundIndex", "Turn", "turn", "TurnIndex", "turnIndex") > 0)
        {
            return;
        }

        var key = PlayerApi.ScopedGameVarKey("TerriasPolymorphCrossFormRound", ownerStatus);
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
            TerriasLog.Debug("[Polymorph] cooldown UI refresh skipped from " + source + ": " + ex.Message);
        }
    }

    private static string OwnerKey(IStatusManager? ownerStatus)
    {
        var owner = ownerStatus?.InstanceId ?? PlayerApi.LocalPlayerStatusId();
        return string.IsNullOrWhiteSpace(owner) ? "local" : owner;
    }

    private static string FormatCooldowns(Dictionary<string, int> values)
    {
        var parts = new List<string>(values.Count);
        foreach (var pair in values)
        {
            parts.Add(pair.Key + "=" + pair.Value);
        }

        return string.Join("|", parts);
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

    private sealed class EntryCooldownOverlay
    {
        public EntryCooldownOverlay(
            string roleId,
            IDictionary<string, int> baselineCooldowns,
            IEnumerable<string> syntheticSkillIds)
        {
            RoleId = roleId ?? "";
            BaselineCooldowns = new Dictionary<string, int>(baselineCooldowns, StringComparer.Ordinal);
            SyntheticSkillIds = new HashSet<string>(syntheticSkillIds, StringComparer.Ordinal);
        }

        public string RoleId { get; }

        public Dictionary<string, int> BaselineCooldowns { get; }

        public HashSet<string> SyntheticSkillIds { get; }

        public EntryCooldownOverlay Copy()
        {
            return new EntryCooldownOverlay(RoleId, BaselineCooldowns, SyntheticSkillIds);
        }
    }
}
