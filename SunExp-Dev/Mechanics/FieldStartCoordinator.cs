using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public enum FieldStartSourceCategory
{
    DifficultyPool = 100,
    Blessing = 200,
    Relic = 300,
    Other = 400
}

public sealed class FieldStartContext
{
    public FieldStartContext(ScriptExecutor? executor, string source)
    {
        Executor = executor;
        Source = source ?? "";
    }

    public ScriptExecutor? Executor { get; }

    public string Source { get; }
}

public sealed class FieldStartGrant
{
    public FieldStartGrant(string sourceId, SunExpFieldId field, int stacks, int order = 0)
    {
        SourceId = sourceId ?? "";
        Field = field;
        Stacks = Math.Max(0, stacks);
        Order = order;
    }

    public string SourceId { get; }

    public SunExpFieldId Field { get; }

    public int Stacks { get; }

    public int Order { get; }
}

public sealed class FieldStartSourceProvider
{
    public FieldStartSourceProvider(
        string id,
        FieldStartSourceCategory category,
        int order,
        Func<FieldStartContext, IEnumerable<FieldStartGrant>> provide)
    {
        Id = id ?? "";
        Category = category;
        Order = order;
        Provide = provide ?? throw new ArgumentNullException(nameof(provide));
    }

    public string Id { get; }

    public FieldStartSourceCategory Category { get; }

    public int Order { get; }

    public Func<FieldStartContext, IEnumerable<FieldStartGrant>> Provide { get; }
}

public sealed class FieldStartResolution
{
    public FieldStartResolution(SunExpFieldId field, int stacks, IReadOnlyList<string> appliedSources)
    {
        Field = field;
        Stacks = Math.Max(0, stacks);
        AppliedSources = appliedSources ?? Array.Empty<string>();
    }

    public SunExpFieldId Field { get; }

    public int Stacks { get; }

    public IReadOnlyList<string> AppliedSources { get; }

    public bool IsActive => Field != SunExpFieldId.None && Stacks > 0;
}

public static class FieldStartCoordinator
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, FieldStartSourceProvider> Providers =
        new(StringComparer.Ordinal);
    private static bool builtInsRegistered;

    public static void RegisterProvider(FieldStartSourceProvider provider)
    {
        if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
        {
            return;
        }

        lock (Gate)
        {
            Providers[provider.Id] = provider;
        }
    }

    public static bool ResolveAndCommit(ScriptExecutor? executor, string source)
    {
        if (!FieldApi.IsAuthoritativeFieldWriter())
        {
            return false;
        }

        EnsureBuiltInProvidersRegistered();
        var statusId = string.IsNullOrWhiteSpace(executor?.Self?.InstanceId)
            ? "local"
            : executor!.Self.InstanceId;
        if (!AuraLifecycleOperationLedger.TryClaimBattleOperation(
                SunExpIds.ModId,
                "FieldStartCoordinator",
                "ResolveAndCommit",
                statusId,
                "opening-field",
                "coordinated-field"))
        {
            return false;
        }

        var resolution = Resolve(new FieldStartContext(executor, source));
        if (!resolution.IsActive)
        {
            SunExpLog.Debug("[FieldStartCoordinator] no opening field source; source=" + (source ?? ""));
            return false;
        }

        var changed = FieldApi.CommitOpeningField(
            resolution.Field,
            resolution.Stacks,
            "FieldStartCoordinator:" + (source ?? ""));
        SunExpLog.Debug("[FieldStartCoordinator] committed field="
            + FieldEffectRegistry.FieldSlug(resolution.Field)
            + ", stacks="
            + resolution.Stacks
            + ", sources="
            + string.Join(" -> ", resolution.AppliedSources)
            + ", changed="
            + changed);
        return changed;
    }

    public static FieldStartResolution Resolve(FieldStartContext context)
    {
        EnsureBuiltInProvidersRegistered();
        var field = SunExpFieldId.None;
        var stacks = 0;
        var appliedSources = new List<string>();

        foreach (var provider in ProviderSnapshot())
        {
            FieldStartGrant[] grants;
            try
            {
                grants = (provider.Provide(context) ?? Array.Empty<FieldStartGrant>())
                    .Where(grant => grant != null)
                    .OrderBy(grant => grant.Order)
                    .ThenBy(grant => grant.SourceId, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("[FieldStartCoordinator] provider failed: id="
                    + provider.Id
                    + ", error="
                    + ex.Message);
                continue;
            }

            foreach (var grant in grants)
            {
                if (grant.Field == SunExpFieldId.None || grant.Stacks <= 0)
                {
                    continue;
                }

                stacks = field == grant.Field ? stacks + grant.Stacks : grant.Stacks;
                field = grant.Field;
                stacks = Math.Min(FieldEffectRegistry.MaxStacks(field), stacks);
                appliedSources.Add(provider.Category + ":" + grant.SourceId);
            }
        }

        return new FieldStartResolution(field, stacks, appliedSources);
    }

    private static void EnsureBuiltInProvidersRegistered()
    {
        lock (Gate)
        {
            if (builtInsRegistered)
            {
                return;
            }

            builtInsRegistered = true;
            Providers["difficulty-field-pool"] = new FieldStartSourceProvider(
                "difficulty-field-pool",
                FieldStartSourceCategory.DifficultyPool,
                0,
                context => DifficultyGrant(context.Source));
            Providers["familiar-blessing-fields"] = new FieldStartSourceProvider(
                "familiar-blessing-fields",
                FieldStartSourceCategory.Blessing,
                0,
                _ => FamiliarBlessingEffectRuntime.OpeningFieldGrants());
            Providers["relic-fields"] = new FieldStartSourceProvider(
                "relic-fields",
                FieldStartSourceCategory.Relic,
                0,
                _ => RelicFieldStartSourceService.OpeningFieldGrants());
        }
    }

    private static IEnumerable<FieldStartGrant> DifficultyGrant(string source)
    {
        var selected = DifficultyFieldPoolService.DrawEqualCandidate(source);
        if (selected == null)
        {
            return Array.Empty<FieldStartGrant>();
        }

        return new[]
        {
            new FieldStartGrant(
                "difficulty." + selected.HardTagId,
                selected.Field,
                selected.Stacks)
        };
    }

    private static FieldStartSourceProvider[] ProviderSnapshot()
    {
        lock (Gate)
        {
            return Providers.Values
                .OrderBy(provider => provider.Category)
                .ThenBy(provider => provider.Order)
                .ThenBy(provider => provider.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
