using AuraToolsExp.Dll.Modules;
using AuraToolsExp.Dll.Modules.Contracts;

internal static partial class AuraToolsTestSuite
{
    public static void TestAuraToolModuleCatalogAndStateStore()
    {
        Assert(AuraToolModuleIds.Persisted.Length == 15
               && AuraToolModuleIds.Persisted.Distinct(StringComparer.Ordinal).Count() == 15,
            "tool module config inventory contains fifteen unique persisted module ids");
        var second = new FakeModule("module.second", "presentation", 20, visible: true);
        var first = new FakeModule("module.first", "gameplay", 10, visible: true);
        var hidden = new FakeModule("module.hidden", "system", 1, visible: false);
        var catalog = new AuraToolModuleCatalog(new IAuraToolModule[]
        {
            second,
            hidden,
            first
        });

        Assert(catalog.Modules.Count == 3
               && catalog.VisibleModules.Count == 2
               && catalog.VisibleModules[0].Descriptor.ModuleId == "module.first"
               && catalog.VisibleModules[1].Descriptor.ModuleId == "module.second",
            "tool module catalog validates visibility and deterministic UI order");
        Assert(catalog.TryGet("module.hidden", out var resolved)
               && ReferenceEquals(resolved, hidden),
            "tool module catalog resolves hidden runtime modules by stable id");

        var duplicateRejected = false;
        try
        {
            _ = new AuraToolModuleCatalog(new IAuraToolModule[]
            {
                first,
                new FakeModule("module.first", "records", 30, visible: true)
            });
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        Assert(duplicateRejected, "tool module catalog rejects duplicate stable ids");

        var store = new AuraToolModuleStateStore();
        var changes = 0;
        store.Changed += _ => changes++;
        var initial = store.Publish(new AuraToolModuleState
        {
            ModuleId = "module.first",
            ConfiguredEnabled = false,
            EffectiveEnabled = false,
            Availability = AuraToolModuleAvailability.Disabled,
            Summary = "关闭"
        });
        var unchanged = store.Publish(new AuraToolModuleState
        {
            ModuleId = "module.first",
            ConfiguredEnabled = false,
            EffectiveEnabled = false,
            Availability = AuraToolModuleAvailability.Disabled,
            Summary = "关闭"
        });
        var enabled = store.Publish(new AuraToolModuleState
        {
            ModuleId = "module.first",
            ConfiguredEnabled = true,
            EffectiveEnabled = true,
            Availability = AuraToolModuleAvailability.Ready,
            Summary = "就绪"
        });

        Assert(initial.Revision == unchanged.Revision
               && enabled.Revision > unchanged.Revision
               && changes == 2,
            "tool module state store only publishes observable state changes");

        var records = new AuraToolsExp.Dll.Config.MatchRecordSettings();
        AuraToolsMatchRecordModulePolicy.SetBattleReplay(records, true);
        Assert(records.Enabled
               && records.Replay.Enabled
               && !records.Statistics.Enabled,
            "enabling replay from a disabled legacy parent does not implicitly enable DPT");
        AuraToolsMatchRecordModulePolicy.SetDamageStatistics(records, true);
        Assert(records.Enabled
               && records.Replay.Enabled
               && records.Statistics.Enabled,
            "DPT and replay can be enabled independently at the same time");
        AuraToolsMatchRecordModulePolicy.SetBattleReplay(records, false);
        Assert(records.Enabled
               && !records.Replay.Enabled
               && records.Statistics.Enabled,
            "disabling replay keeps the DPT module parent gate active");
        AuraToolsMatchRecordModulePolicy.SetDamageStatistics(records, false);
        Assert(!records.Enabled
               && !records.Replay.Enabled
               && !records.Statistics.Enabled,
            "disabling the final record module closes the legacy parent gate");

        TestAuraToolModuleConfigIsolation();
    }

    private static void TestAuraToolModuleConfigIsolation()
    {
        var firstChanges = 0;
        var secondChanges = 0;
        using var firstSubscription = AuraToolsExp.Dll.Config
            .AuraToolConfigChangeBus.Subscribe(
                "module.first",
                change =>
                {
                    if (change.Revision == 3)
                    {
                        firstChanges++;
                    }
                });
        using var secondSubscription = AuraToolsExp.Dll.Config
            .AuraToolConfigChangeBus.Subscribe(
                "module.second",
                _ => secondChanges++);
        AuraToolsExp.Dll.Config.AuraToolConfigChangeBus.Publish(
            "module.first",
            3);
        Assert(firstChanges == 1 && secondChanges == 0,
            "module config change bus only wakes subscribers for the changed module");

        AuraShared.Core.AuraSharedConfigStore.ResetForTests();
        var store = new AuraToolsExp.Dll.Config.AuraToolModuleConfigStore();
        var fallback = new AuraToolsExp.Dll.Config.CardRefreshSettings
        {
            Enabled = true
        };
        var loaded = store.Load(
            AuraToolModuleIds.CardRefresh,
            fallback,
            out var migrated);
        Assert(migrated && loaded.Enabled,
            "missing module config migrates from the supplied legacy aggregate slice");
        Assert(store.Save(
                   AuraToolModuleIds.CardRefresh,
                   new AuraToolsExp.Dll.Config.CardRefreshSettings
                   {
                       Enabled = false
                   },
                   out var revision)
               && revision > 0,
            "module config store persists an owner-qualified module document");
        store.Reset();
        loaded = store.Load(
            AuraToolModuleIds.CardRefresh,
            fallback,
            out migrated);
        Assert(!migrated && !loaded.Enabled,
            "persisted module config wins over the legacy aggregate fallback");
    }

    private sealed class FakeModule : IAuraToolModule
    {
        public FakeModule(
            string moduleId,
            string categoryId,
            int order,
            bool visible)
        {
            Descriptor = new AuraToolModuleDescriptor
            {
                ModuleId = moduleId,
                CategoryId = categoryId,
                DisplayName = moduleId,
                Order = order,
                InitializationOrder = order,
                Visible = visible
            };
        }

        public AuraToolModuleDescriptor Descriptor { get; }

        public void Initialize(AuraToolModuleContext context)
        {
        }

        public AuraToolModuleState SnapshotState()
        {
            return new AuraToolModuleState
            {
                ModuleId = Descriptor.ModuleId,
                Availability = AuraToolModuleAvailability.Disabled
            };
        }

        public AuraToolOperationResult SetEnabled(bool enabled) =>
            AuraToolOperationResult.Ok();

        public void ApplyCurrentConfiguration()
        {
        }

        public IAuraToolSettingsPage? CreateSettingsPage() => null;
    }
}
