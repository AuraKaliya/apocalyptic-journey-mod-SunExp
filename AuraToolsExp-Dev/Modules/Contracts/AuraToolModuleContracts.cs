using System;
using System.Collections.Generic;
using UnityEngine;
using Witch.Mod;

namespace AuraToolsExp.Dll.Modules.Contracts;

public enum AuraToolModuleAvailability
{
    Ready,
    Disabled,
    Unavailable,
    Degraded,
    Busy,
    RestartRequired
}

public sealed class AuraToolModuleDescriptor
{
    public string ModuleId { get; set; } = "";

    public string CategoryId { get; set; } = "";

    public int Order { get; set; }

    public int InitializationOrder { get; set; }

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public string IconKey { get; set; } = "";

    public IReadOnlyList<string> SearchTerms { get; set; } = Array.Empty<string>();

    public bool HasSettingsPage { get; set; }

    public bool Experimental { get; set; }

    public bool RequiresRestartWhenChanged { get; set; }

    public bool Visible { get; set; } = true;
}

public sealed class AuraToolModuleState
{
    public string ModuleId { get; set; } = "";

    public long Revision { get; set; }

    public bool ConfiguredEnabled { get; set; }

    public bool EffectiveEnabled { get; set; }

    public AuraToolModuleAvailability Availability { get; set; }

    public string Summary { get; set; } = "";

    public string Attention { get; set; } = "";

    public int? ItemCount { get; set; }

    internal AuraToolModuleState CloneWithRevision(long revision)
    {
        return new AuraToolModuleState
        {
            ModuleId = ModuleId,
            Revision = revision,
            ConfiguredEnabled = ConfiguredEnabled,
            EffectiveEnabled = EffectiveEnabled,
            Availability = Availability,
            Summary = Summary,
            Attention = Attention,
            ItemCount = ItemCount
        };
    }

    internal bool SameVisibleState(AuraToolModuleState other)
    {
        return other != null
               && ConfiguredEnabled == other.ConfiguredEnabled
               && EffectiveEnabled == other.EffectiveEnabled
               && Availability == other.Availability
               && string.Equals(Summary, other.Summary, StringComparison.Ordinal)
               && string.Equals(Attention, other.Attention, StringComparison.Ordinal)
               && ItemCount == other.ItemCount;
    }
}

public sealed class AuraToolOperationResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";

    public static AuraToolOperationResult Ok(string message = "") => new()
    {
        Success = true,
        Message = message ?? ""
    };

    public static AuraToolOperationResult Fail(string message) => new()
    {
        Success = false,
        Message = message ?? ""
    };
}

public sealed class AuraToolModuleContext
{
    public AuraToolModuleContext(ModConfig modConfig)
    {
        ModConfig = modConfig ?? throw new ArgumentNullException(nameof(modConfig));
    }

    public ModConfig ModConfig { get; }
}

public sealed class AuraToolSettingsPageContext
{
    public AuraToolSettingsPageContext(Transform parent, Action? closed = null)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        Closed = closed;
    }

    public Transform Parent { get; }

    public Action? Closed { get; }
}

public interface IAuraToolSettingsPage : IDisposable
{
    string ModuleId { get; }

    void Build(AuraToolSettingsPageContext context);

    void Activate();

    void Deactivate();
}

public interface IAuraToolModule
{
    AuraToolModuleDescriptor Descriptor { get; }

    void Initialize(AuraToolModuleContext context);

    AuraToolModuleState SnapshotState();

    AuraToolOperationResult SetEnabled(bool enabled);

    void ApplyCurrentConfiguration();

    IAuraToolSettingsPage? CreateSettingsPage();
}
