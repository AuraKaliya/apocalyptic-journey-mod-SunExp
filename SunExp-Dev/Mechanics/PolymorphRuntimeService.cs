using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public sealed class PolymorphRuntimeAttachment
{
    public PolymorphRuntimeAttachment(string ownerStatusId, string roleId, DataConfig? runtimeCareer)
    {
        OwnerStatusId = ownerStatusId ?? "";
        RoleId = roleId ?? "";
        RuntimeCareer = runtimeCareer;
    }

    public string OwnerStatusId { get; }

    public string RoleId { get; }

    public DataConfig? RuntimeCareer { get; }
}

public static class PolymorphRuntimeService
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, PolymorphRuntimeAttachment> Attachments = new(StringComparer.Ordinal);

    public static bool Enter(ScriptExecutor self, PolymorphRoleSpec role, PolymorphState state)
    {
        if (self?.Self == null || role == null || state == null)
        {
            return false;
        }

        ClearOwner(state.OwnerStatusId, "PolymorphRuntimeService.Enter");

        var ranCareerScript = TryRunCurrentCareerScript(self, role);
        if (IsLoneer(role.Id))
        {
            LoneerMiracleService.OnFightStart(self);
        }

        RoleSkillApi.EnsureCurrentCareerSkillTimes();
        PolymorphCooldownService.PrepareCurrentRoleEntry(self.Self, role.Id, "PolymorphRuntimeService.Enter");
        RoleSkillApi.RefreshFightSkills("PolymorphRuntimeService.Enter:" + role.Id);
        RoleSkillApi.LogCurrentSkillDiagnostics("PolymorphRuntimeService.Enter:" + role.Id);

        lock (SyncRoot)
        {
            Attachments[state.OwnerStatusId] = new PolymorphRuntimeAttachment(
                state.OwnerStatusId,
                role.Id,
                RoleTable.Instance?.Career);
        }

        SunExpLog.Info("[Polymorph] runtime entered: owner=" + state.OwnerStatusId
            + ", role=" + role.Id
            + ", supportedPassive=" + IsSupportedPassiveRole(role.Id)
            + ", careerScript=" + ranCareerScript);
        SunExpPerformanceCounters.Record("Polymorph.RuntimeEntered");
        return true;
    }

    public static void ClearAll(string source)
    {
        PolymorphRuntimeAttachment[] attachments;
        lock (SyncRoot)
        {
            if (Attachments.Count == 0)
            {
                return;
            }

            attachments = new PolymorphRuntimeAttachment[Attachments.Count];
            Attachments.Values.CopyTo(attachments, 0);
            Attachments.Clear();
        }

        foreach (var attachment in attachments)
        {
            ClearAttachment(attachment, source);
        }

        SunExpPerformanceCounters.Record("Polymorph.RuntimeCleared");
    }

    public static void ClearOwner(string ownerStatusId, string source)
    {
        PolymorphRuntimeAttachment? attachment = null;
        lock (SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(ownerStatusId) && Attachments.TryGetValue(ownerStatusId, out attachment))
            {
                Attachments.Remove(ownerStatusId);
            }
        }

        if (attachment != null)
        {
            ClearAttachment(attachment, source);
        }
    }

    private static void ClearAttachment(PolymorphRuntimeAttachment attachment, string source)
    {
        try
        {
            var executor = attachment.RuntimeCareer?.scriptExecutor as ScriptExecutor;
            if (IsWuna(attachment.RoleId))
            {
                BuffApi.SavePersistentEmber(executor, executor?.Self);
                ExecutorApi.ClearHook(executor, "SunExpWunaCareerHook", "SunExpWunaCareerToken");
            }
            else if (IsLoneer(attachment.RoleId))
            {
                if (executor != null)
                {
                    LoneerMiracleService.EndCombatCleanup(executor);
                }

                ExecutorApi.ClearHook(executor, "SunExpLoneerCareerHook", "SunExpLoneerCareerToken");
            }

            executor?.Clear();
            SunExpLog.Info("[Polymorph] runtime cleared from " + source + ": role=" + attachment.RoleId);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Polymorph] runtime cleanup failed from " + source + ": " + ex.Message);
        }
    }

    private static bool TryRunCurrentCareerScript(ScriptExecutor self, PolymorphRoleSpec role)
    {
        try
        {
            var executor = RoleTable.Instance?.Career?.scriptExecutor;
            if (executor == null)
            {
                return false;
            }

            executor.Clear();
            executor.Self = self.Self;
            executor.Object.Clear();
            executor.Object.Add(self.Self);
            executor.RunScript("SkillScript");
            SunExpLog.Info("[Polymorph] career script ran for role: " + role.Id);
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Polymorph] career script failed for " + role.Id + ": " + ex.Message);
            return false;
        }
    }

    private static bool IsSupportedPassiveRole(string roleId)
    {
        return IsWuna(roleId) || IsLoneer(roleId);
    }

    private static bool IsWuna(string roleId)
    {
        var normalized = NormalizeRoleId(roleId);
        return string.Equals(normalized, "wuna", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "SunExp_wuna_wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_wuna", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoneer(string roleId)
    {
        var normalized = NormalizeRoleId(roleId);
        return string.Equals(normalized, "loneer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "SunExp_loneer_loneer", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_loneer", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoleId(string roleId)
    {
        return (roleId ?? "").Trim().TrimStart('*');
    }
}
