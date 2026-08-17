using System;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using AuraToolsExp.Dll.Modules.Contracts;
using Witch.Mod;

namespace AuraToolsExp.Dll.Modules;

public static class AuraToolModuleHost
{
    private static bool initialized;

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

        initialized = true;
    }

    public static AuraToolOperationResult SetEnabled(string moduleId, bool enabled)
    {
        if (!Catalog.TryGet(moduleId, out var module))
        {
            return AuraToolOperationResult.Fail("未找到工具模块：" + moduleId);
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
}
