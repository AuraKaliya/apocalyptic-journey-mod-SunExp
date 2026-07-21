using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

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

        var careerExecutor = TryRunCurrentCareerScript(self, role);
        if (IsLoneer(role.Id))
        {
            LoneerMiracleService.PreparePolymorphEntry(careerExecutor ?? self);
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

        TerriasLog.Info("[Polymorph] runtime entered: owner=" + state.OwnerStatusId
            + ", role=" + role.Id
            + ", supportedPassive=" + IsSupportedPassiveRole(role.Id)
            + ", careerScript=" + (careerExecutor != null));
        TerriasPerformanceCounters.Record("Polymorph.RuntimeEntered");
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
            ClearAttachment(attachment, source, endCombat: true);
        }

        TerriasPerformanceCounters.Record("Polymorph.RuntimeCleared");
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
            ClearAttachment(attachment, source, endCombat: false);
        }
    }

    public static bool RestoreOriginalCareerRuntime(PolymorphState state, string source)
    {
        if (state?.OriginalCareer == null || !IsSupportedPassiveRole(state.OriginalCareerId))
        {
            return false;
        }

        var owner = FightPlayer.Instance?.Status;
        if (owner == null || (!string.IsNullOrWhiteSpace(state.OwnerStatusId)
            && !string.Equals(owner.InstanceId, state.OwnerStatusId, StringComparison.Ordinal)))
        {
            TerriasLog.Warn("[Polymorph] original career runtime restore skipped from " + source
                + ": owner status unavailable.");
            return false;
        }

        var executor = TryRunCareerScript(owner, state.OriginalCareer, state.OriginalCareerId);
        if (executor == null)
        {
            return false;
        }

        if (IsLoneer(state.OriginalCareerId))
        {
            LoneerMiracleService.ResumeAfterPolymorph(executor);
        }

        TerriasLog.Info("[Polymorph] original career runtime restored from " + source
            + ": role=" + state.OriginalCareerId + ".");
        return true;
    }

    private static void ClearAttachment(PolymorphRuntimeAttachment attachment, string source, bool endCombat)
    {
        try
        {
            var executor = attachment.RuntimeCareer?.scriptExecutor as ScriptExecutor;
            if (IsWuna(attachment.RoleId))
            {
                BuffApi.SavePersistentEmber(executor, executor?.Self);
                ExecutorApi.ClearHook(executor, "TerriasWunaCareerHook", "TerriasWunaCareerToken");
            }
            else if (IsLoneer(attachment.RoleId))
            {
                if (endCombat && executor != null)
                {
                    LoneerMiracleService.EndCombatCleanup(executor);
                }

                LoneerMiracleService.DetachCareerRuntime(executor);
            }

            executor?.Clear();
            TerriasLog.Info("[Polymorph] runtime cleared from " + source + ": role=" + attachment.RoleId);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[Polymorph] runtime cleanup failed from " + source + ": " + ex.Message);
        }
    }

    private static ScriptExecutor? TryRunCurrentCareerScript(ScriptExecutor self, PolymorphRoleSpec role)
    {
        return TryRunCareerScript(self.Self, RoleTable.Instance?.Career, role.Id);
    }

    private static ScriptExecutor? TryRunCareerScript(IStatusManager? owner, DataConfig? career, string roleId)
    {
        try
        {
            var executor = career?.scriptExecutor;
            if (executor == null || owner == null)
            {
                return null;
            }

            executor.Clear();
            executor.Self = owner;
            executor.Object.Clear();
            executor.Object.Add(owner);
            executor.RunScript("SkillScript");
            TerriasLog.Info("[Polymorph] career script ran for role: " + roleId);
            return executor as ScriptExecutor;
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[Polymorph] career script failed for " + roleId + ": " + ex.Message);
            return null;
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
            || string.Equals(normalized, "Terrias_wuna_wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_wuna", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoneer(string roleId)
    {
        var normalized = NormalizeRoleId(roleId);
        return string.Equals(normalized, "loneer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Terrias_loneer_loneer", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_loneer", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoleId(string roleId)
    {
        return TerriasContentIdCompatibility.Canonicalize((roleId ?? "").Trim().TrimStart('*'));
    }
}
