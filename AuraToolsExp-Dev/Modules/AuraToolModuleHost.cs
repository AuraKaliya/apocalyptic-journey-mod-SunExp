using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using AuraTooling.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules.Contracts;
using Witch.Mod;

namespace AuraToolsExp.Dll.Modules;

public static class AuraToolModuleHost
{
    private static bool initialized;
    private static readonly List<IDisposable> ConfigSubscriptions = new();
    private static readonly HashSet<string> ExternalModuleIds =
        new(StringComparer.Ordinal);

    public static AuraToolModuleCatalog Catalog { get; private set; } =
        new(Array.Empty<IAuraToolModule>());

    public static AuraToolModuleStateStore States { get; } = new();

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        Catalog = new AuraToolModuleCatalog(AuraToolsBuiltInModules.Create());
        var context = new AuraToolModuleContext(modConfig);
        foreach (var module in Catalog.Modules)
        {
            AuraSharedHooks.RunStep(
                "tool module " + module.Descriptor.ModuleId,
                () =>
                {
                    module.Initialize(context);
                    module.ApplyCurrentConfiguration();
                    States.Publish(module.SnapshotState());
                    if (module.Descriptor.Visible)
                    {
                        var moduleId = module.Descriptor.ModuleId;
                        ConfigSubscriptions.Add(
                            AuraToolsConfigService.SubscribeModule(
                                moduleId,
                                () => RefreshState(moduleId)));
                    }
                },
                (step, ex) =>
                {
                    AuraToolsLog.Error("Initialization step failed: " + step, ex);
                    States.Publish(new AuraToolModuleState
                    {
                        ModuleId = module.Descriptor.ModuleId,
                        ConfiguredEnabled = false,
                        EffectiveEnabled = false,
                        Availability = AuraToolModuleAvailability.Unavailable,
                        Summary = "初始化失败",
                        Attention = ex.Message
                    });
                });
        }

        AuraToolExtensionRegistry.Changed += OnSharedExtensionsChanged;
        AuraToolExtensionRegistry.StateChanged += OnSharedExtensionStateChanged;
        RefreshSharedExtensions();
        initialized = true;
    }

    public static AuraToolOperationResult SetEnabled(string moduleId, bool enabled)
    {
        if (!Catalog.TryGet(moduleId, out var module))
        {
            return AuraToolOperationResult.Fail("未找到工具模块：" + moduleId);
        }
        if (AuraToolsConfigService.IsModuleConfigReadOnly(moduleId))
        {
            return AuraToolOperationResult.Fail(
                "该模块配置来自更新版本，当前为只读状态。");
        }

        try
        {
            var result = module.SetEnabled(enabled);
            States.Publish(module.SnapshotState());
            return result;
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[Modules] failed to change " + moduleId, ex);
            PublishFailureState(module, ex.Message);
            return AuraToolOperationResult.Fail(ex.Message);
        }
    }

    public static AuraToolModuleState RefreshState(string moduleId)
    {
        if (!Catalog.TryGet(moduleId, out var module))
        {
            return new AuraToolModuleState
            {
                ModuleId = moduleId ?? "",
                Availability = AuraToolModuleAvailability.Unavailable,
                Summary = "模块不存在"
            };
        }

        try
        {
            return States.Publish(module.SnapshotState());
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn(
                "[Modules] failed to snapshot " + moduleId + ": " + ex.Message);
            return PublishFailureState(module, ex.Message);
        }
    }

    private static AuraToolModuleState PublishFailureState(
        IAuraToolModule module,
        string message)
    {
        var configured = false;
        if (States.TryGet(module.Descriptor.ModuleId, out var existing))
        {
            configured = existing.ConfiguredEnabled;
        }

        return States.Publish(new AuraToolModuleState
        {
            ModuleId = module.Descriptor.ModuleId,
            ConfiguredEnabled = configured,
            EffectiveEnabled = false,
            Availability = AuraToolModuleAvailability.Degraded,
            Summary = "状态读取失败",
            Attention = message ?? ""
        });
    }

    private static void OnSharedExtensionsChanged(long revision)
    {
        if (!AuraSharedFrameScheduler.RunOnceNextFrame(
                new AuraSharedFrameActionRequest
                {
                    OwnerId = AuraToolsIds.ModId,
                    Key = "tool-extension-registry-refresh",
                    Source = "AuraTools.ToolExtensions.RegistryChanged",
                    Action = RefreshSharedExtensions
                }))
        {
            RefreshSharedExtensions();
        }
    }

    private static void RefreshSharedExtensions()
    {
        try
        {
            var adapters = AuraToolExtensionRegistry.Snapshot()
                .Select(registration =>
                    (IAuraToolModule)new AuraToolSharedExtensionAdapter(registration))
                .ToArray();
            var nextIds = new HashSet<string>(
                adapters.Select(module => module.Descriptor.ModuleId),
                StringComparer.Ordinal);
            foreach (var removed in ExternalModuleIds.Where(id => !nextIds.Contains(id)).ToArray())
            {
                States.Remove(removed);
            }

            ExternalModuleIds.Clear();
            foreach (var moduleId in nextIds)
            {
                ExternalModuleIds.Add(moduleId);
            }
            Catalog.ReplaceExternal(adapters);
            foreach (var adapter in adapters)
            {
                RefreshState(adapter.Descriptor.ModuleId);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Error("[ToolExtensions] registry refresh failed", ex);
        }
    }

    private static void OnSharedExtensionStateChanged(
        string qualifiedModuleId,
        long stateRevision)
    {
        if (!AuraSharedFrameScheduler.RunOnceNextFrame(
                new AuraSharedFrameActionRequest
                {
                    OwnerId = AuraToolsIds.ModId,
                    Key = "tool-extension-state:" + qualifiedModuleId,
                    Source = "AuraTools.ToolExtensions.StateChanged",
                    Action = () => RefreshState(qualifiedModuleId)
                }))
        {
            RefreshState(qualifiedModuleId);
        }
    }
}
