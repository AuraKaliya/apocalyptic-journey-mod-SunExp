using System;
using System.Collections.Generic;
using AuraTooling.Shared;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules.Contracts;
using UnityEngine;

namespace AuraToolsExp.Dll.Modules;

internal sealed class AuraToolSharedExtensionAdapter : IAuraToolModule
{
    private static readonly HashSet<string> KnownCategories = new(
        new[]
        {
            "gameplay",
            "presentation",
            "records",
            "multiplayer",
            "intelligence",
            "system",
            AuraToolExtensionProtocol.DefaultCategoryId
        },
        StringComparer.Ordinal);

    private readonly IAuraToolExtensionProvider provider;

    public AuraToolSharedExtensionAdapter(AuraToolExtensionRegistration registration)
    {
        if (registration == null)
        {
            throw new ArgumentNullException(nameof(registration));
        }

        provider = registration.Provider;
        var source = registration.Descriptor;
        Descriptor = new AuraToolModuleDescriptor
        {
            ModuleId = source.QualifiedModuleId,
            CategoryId = KnownCategories.Contains(source.CategoryId)
                ? source.CategoryId
                : AuraToolExtensionProtocol.DefaultCategoryId,
            Order = source.Order,
            InitializationOrder = 10000 + source.Order,
            DisplayName = source.DisplayName,
            Description = source.Description,
            IconKey = source.IconKey,
            SearchTerms = source.SearchTerms,
            HasSettingsPage = source.HasSettingsPage,
            Experimental = source.Experimental,
            RequiresRestartWhenChanged = source.RequiresRestartWhenChanged,
            Visible = true
        };
    }

    public AuraToolModuleDescriptor Descriptor { get; }

    public void Initialize(AuraToolModuleContext context)
    {
    }

    public AuraToolModuleState SnapshotState()
    {
        var state = provider.SnapshotState()
                    ?? throw new InvalidOperationException(
                        "Extension returned no state.");
        return new AuraToolModuleState
        {
            ModuleId = Descriptor.ModuleId,
            Revision = state.Revision,
            ConfiguredEnabled = state.ConfiguredEnabled,
            EffectiveEnabled = state.EffectiveEnabled,
            Availability = MapAvailability(state.Availability),
            Summary = TrimStateText(state.Summary, 120),
            Attention = TrimStateText(state.Attention, 180),
            ItemCount = state.ItemCount.HasValue
                ? Math.Max(0, state.ItemCount.Value)
                : null
        };
    }

    public AuraToolOperationResult SetEnabled(bool enabled)
    {
        var result = provider.SetEnabled(enabled);
        return result == null
            ? AuraToolOperationResult.Fail("扩展工具没有返回启停结果。")
            : new AuraToolOperationResult
            {
                Success = result.Success,
                Message = result.Message ?? ""
            };
    }

    public void ApplyCurrentConfiguration()
    {
    }

    public IAuraToolSettingsPage? CreateSettingsPage()
    {
        return Descriptor.HasSettingsPage
            ? new SharedExtensionSettingsPage(Descriptor.ModuleId, provider)
            : null;
    }

    private static AuraToolModuleAvailability MapAvailability(
        AuraToolExtensionAvailability availability)
    {
        return availability switch
        {
            AuraToolExtensionAvailability.Ready => AuraToolModuleAvailability.Ready,
            AuraToolExtensionAvailability.Disabled => AuraToolModuleAvailability.Disabled,
            AuraToolExtensionAvailability.Unavailable => AuraToolModuleAvailability.Unavailable,
            AuraToolExtensionAvailability.Degraded => AuraToolModuleAvailability.Degraded,
            AuraToolExtensionAvailability.Busy => AuraToolModuleAvailability.Busy,
            AuraToolExtensionAvailability.RestartRequired => AuraToolModuleAvailability.RestartRequired,
            _ => AuraToolModuleAvailability.Degraded
        };
    }

    private static string TrimStateText(string value, int maximum)
    {
        var text = (value ?? "").Trim();
        return text.Length <= maximum ? text : text.Substring(0, maximum);
    }

    private sealed class SharedExtensionSettingsPage : IAuraToolSettingsPage
    {
        private readonly IAuraToolExtensionProvider provider;

        public SharedExtensionSettingsPage(
            string moduleId,
            IAuraToolExtensionProvider provider)
        {
            ModuleId = moduleId;
            this.provider = provider;
        }

        public string ModuleId { get; }

        public void Build(AuraToolSettingsPageContext context)
        {
            try
            {
                provider.ShowSettings(context.Parent);
            }
            catch (Exception ex)
            {
                AuraToolsLog.Error(
                    "[ToolExtensions] settings page failed: " + ModuleId,
                    ex);
            }
        }

        public void Activate()
        {
        }

        public void Deactivate()
        {
        }

        public void Dispose()
        {
        }
    }
}
