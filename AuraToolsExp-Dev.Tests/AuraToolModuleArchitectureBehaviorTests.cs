using AuraToolsExp.Dll.Modules;
using AuraToolsExp.Dll.Modules.Contracts;

internal static partial class AuraToolsTestSuite
{
    public static void TestAuraToolModuleCatalogAndStateStore()
    {
        Assert(AuraToolModuleIds.Persisted.Length == 22
               && AuraToolModuleIds.Persisted.Distinct(StringComparer.Ordinal).Count() == 22
               && AuraToolModuleIds.Persisted.Contains(AuraToolModuleIds.FeastCg)
               && AuraToolModuleIds.Persisted.Contains(AuraToolModuleIds.Voice)
               && AuraToolModuleIds.Persisted.Contains(AuraToolModuleIds.CardVisual)
               && AuraToolModuleIds.Persisted.Contains(AuraToolModuleIds.PresetLibrary)
               && AuraToolModuleIds.Persisted.Contains(AuraToolModuleIds.ModHealth)
               && AuraToolModuleIds.Persisted.Contains(AuraToolModuleIds.LobbyStatus)
               && AuraToolModuleIds.Persisted.Contains(AuraToolModuleIds.AdventureArchive),
            "tool module config inventory contains twenty-two unique persisted module ids including voice and card visuals");
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
        var dependencyBlocked = store.Publish(new AuraToolModuleState
        {
            ModuleId = "module.first",
            ConfiguredEnabled = false,
            EffectiveEnabled = false,
            Availability = AuraToolModuleAvailability.Disabled,
            Summary = "关闭",
            EnableControlInteractable = false
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
               && dependencyBlocked.Revision > unchanged.Revision
               && enabled.Revision > dependencyBlocked.Revision
               && changes == 3,
            "tool module state store publishes dependency-control changes but suppresses identical snapshots");

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
        TestSharedToolExtensionRegistryAndAdapter();
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

        var batchedFirst = 0;
        var batchedSecond = 0;
        long batchedRevision = 0;
        using var batchedFirstSubscription = AuraToolsExp.Dll.Config
            .AuraToolConfigChangeBus.Subscribe("module.batch.first", change =>
            {
                batchedFirst++;
                batchedRevision = change.Revision;
            });
        using var batchedSecondSubscription = AuraToolsExp.Dll.Config
            .AuraToolConfigChangeBus.Subscribe("module.batch.second", _ => batchedSecond++);
        using (AuraToolsExp.Dll.Config.AuraToolConfigChangeBus.BeginBatch())
        {
            AuraToolsExp.Dll.Config.AuraToolConfigChangeBus.Publish("module.batch.first", 4);
            AuraToolsExp.Dll.Config.AuraToolConfigChangeBus.Publish("module.batch.first", 9);
            AuraToolsExp.Dll.Config.AuraToolConfigChangeBus.Publish("module.batch.second", 5);
            Assert(batchedFirst == 0 && batchedSecond == 0,
                "module config batch suppresses partial configuration notifications");
        }
        Assert(batchedFirst == 1 && batchedSecond == 1 && batchedRevision == 9,
            "module config batch publishes one final notification per changed module");

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

        AuraShared.Core.AuraSharedConfigStore.SetForTests(
            AuraToolsExp.Dll.Infrastructure.AuraToolsIds.ModId,
            AuraToolsExp.Dll.Config.AuraToolModuleConfigStore.ConfigSystem,
            AuraToolsExp.Dll.Config.AuraToolModuleConfigStore.FileName(
                AuraToolModuleIds.CardRefresh),
            new AuraToolsExp.Dll.Config.AuraToolModuleConfigDocument<
                AuraToolsExp.Dll.Config.CardRefreshSettings>
            {
                SchemaVersion = 2,
                ModuleId = AuraToolModuleIds.CardRefresh,
                Settings = new AuraToolsExp.Dll.Config.CardRefreshSettings
                {
                    Enabled = true
                }
            },
            revision: 7,
            schemaVersion: 2);
        store.Reset();
        loaded = store.Load(
            AuraToolModuleIds.CardRefresh,
            new AuraToolsExp.Dll.Config.CardRefreshSettings
            {
                Enabled = false
            },
            out migrated);
        Assert(!migrated
               && !loaded.Enabled
               && !store.Save(
                   AuraToolModuleIds.CardRefresh,
                   new AuraToolsExp.Dll.Config.CardRefreshSettings
                   {
                       Enabled = true
                   },
                   out _),
            "newer module config schemas fall back safely and cannot be overwritten");

        AuraShared.Core.AuraSharedConfigStore.SetForTests(
            AuraToolsExp.Dll.Infrastructure.AuraToolsIds.ModId,
            AuraToolsExp.Dll.Config.AuraToolModuleConfigStore.ConfigSystem,
            AuraToolsExp.Dll.Config.AuraToolModuleConfigStore.FileName(
                AuraToolModuleIds.PixelEmoji),
            new AuraToolsExp.Dll.Config.AuraToolModuleConfigDocument<
                AuraToolsExp.Dll.Config.AuraToolsPixelEmojiSettings>
            {
                ModuleId = AuraToolModuleIds.PixelEmoji,
                Settings = new AuraToolsExp.Dll.Config.AuraToolsPixelEmojiSettings
                {
                    SchemaVersion =
                        AuraToolsExp.Dll.Config.AuraToolsPixelEmojiSettings
                            .CurrentSchemaVersion + 1
                }
            },
            revision: 8,
            schemaVersion: 1);
        store.Reset();
        var pixelSettings = store.Load(
            AuraToolModuleIds.PixelEmoji,
            new AuraToolsExp.Dll.Config.AuraToolsPixelEmojiSettings(),
            out migrated);
        Assert(!migrated
               && pixelSettings.SchemaVersion
               == AuraToolsExp.Dll.Config.AuraToolsPixelEmojiSettings
                   .CurrentSchemaVersion
               && !store.Save(
                   AuraToolModuleIds.PixelEmoji,
                   pixelSettings,
                   out _),
            "newer feature-owned settings schemas remain read-only even inside a current module envelope");
        Assert(store.IsReadOnly(AuraToolModuleIds.PixelEmoji),
            "module configuration exposes its forward-schema read-only state to the host and settings router");
    }

    private static void TestSharedToolExtensionRegistryAndAdapter()
    {
        AuraTooling.Shared.AuraToolExtensionRegistry.ClearForTests();
        var changed = 0;
        void OnChanged(long revision)
        {
            if (revision > 0)
            {
                changed++;
            }
        }
        AuraTooling.Shared.AuraToolExtensionRegistry.Changed += OnChanged;
        try
        {
            var provider = new FakeSharedExtensionProvider();
            var registered = AuraTooling.Shared.AuraToolExtensionRegistry.Register(
                "ExampleTools",
                provider);
            Assert(registered.Success
                   && !registered.AlreadyRegistered
                   && registered.Handle != null
                   && changed == 1,
                "shared tool registry accepts one owner-qualified compatible provider");

            var duplicate = AuraTooling.Shared.AuraToolExtensionRegistry.Register(
                "ExampleTools",
                provider);
            Assert(duplicate.Success
                   && duplicate.AlreadyRegistered
                   && AuraTooling.Shared.AuraToolExtensionRegistry.Snapshot().Count == 1,
                "shared tool registry treats repeat registration by the same provider as idempotent");

            var conflicting = AuraTooling.Shared.AuraToolExtensionRegistry.Register(
                "ExampleTools",
                new FakeSharedExtensionProvider());
            Assert(!conflicting.Success,
                "shared tool registry rejects a different provider claiming the same identity");

            var wrongOwner = AuraTooling.Shared.AuraToolExtensionRegistry.Register(
                "OtherOwner",
                provider);
            Assert(!wrongOwner.Success,
                "shared tool registry rejects owner identity supplied by another mod");

            var registration = AuraTooling.Shared.AuraToolExtensionRegistry.Snapshot()[0];
            var adapter = new AuraToolSharedExtensionAdapter(registration);
            Assert(adapter.Descriptor.ModuleId == "ExampleTools:sample-tool"
                   && adapter.Descriptor.CategoryId == "extensions"
                   && adapter.Descriptor.HasSettingsPage,
                "AuraTools adapter maps unknown extension categories to the extension shelf");
            var state = adapter.SnapshotState();
            Assert(state.ConfiguredEnabled
                   && state.EffectiveEnabled
                   && state.Availability == AuraToolModuleAvailability.Ready
                   && state.Summary == "扩展就绪",
                "AuraTools adapter projects shared extension state into the internal module contract");
            Assert(adapter.SetEnabled(false).Success
                   && !adapter.SnapshotState().ConfiguredEnabled,
                "AuraTools adapter delegates the extension master switch to its owner provider");

            var page = adapter.CreateSettingsPage();
            Assert(page != null, "shared extension exposes a routed settings page");
            page!.Build(new AuraToolSettingsPageContext(new UnityEngine.Transform()));
            Assert(provider.SettingsOpened == 1,
                "shared extension settings page remains provider-owned");

            var catalog = new AuraToolModuleCatalog(Array.Empty<IAuraToolModule>());
            catalog.ReplaceExternal(new IAuraToolModule[] { adapter });
            Assert(catalog.VisibleModules.Count == 1
                   && catalog.TryGet(adapter.Descriptor.ModuleId, out _),
                "tool module catalog accepts a late shared extension projection");
            catalog.ReplaceExternal(Array.Empty<IAuraToolModule>());
            Assert(catalog.VisibleModules.Count == 0,
                "tool module catalog removes unregistered shared extensions");

            duplicate.Handle!.Dispose();
            Assert(AuraTooling.Shared.AuraToolExtensionRegistry.Snapshot().Count == 1,
                "disposing a duplicate extension lease preserves the original owner lease");
            registered.Handle!.Dispose();
            Assert(AuraTooling.Shared.AuraToolExtensionRegistry.Snapshot().Count == 0
                   && changed == 2,
                "disposing the owner handle unregisters the extension and advances the registry revision");
        }
        finally
        {
            AuraTooling.Shared.AuraToolExtensionRegistry.Changed -= OnChanged;
            AuraTooling.Shared.AuraToolExtensionRegistry.ClearForTests();
        }
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

    private sealed class FakeSharedExtensionProvider :
        AuraTooling.Shared.IAuraToolExtensionProvider
    {
        private bool enabled = true;

        public int SettingsOpened { get; private set; }

        public AuraTooling.Shared.AuraToolExtensionDescriptor Descriptor { get; } =
            new()
            {
                OwnerModId = "ExampleTools",
                ModuleId = "sample-tool",
                CategoryId = "custom-category",
                DisplayName = "示例扩展工具",
                Description = "用于验证共享扩展协议。",
                HasSettingsPage = true,
                SearchTerms = new[] { "sample", "扩展" }
            };

        public AuraTooling.Shared.AuraToolExtensionState SnapshotState()
        {
            return new AuraTooling.Shared.AuraToolExtensionState
            {
                Revision = 1,
                ConfiguredEnabled = enabled,
                EffectiveEnabled = enabled,
                Availability = enabled
                    ? AuraTooling.Shared.AuraToolExtensionAvailability.Ready
                    : AuraTooling.Shared.AuraToolExtensionAvailability.Disabled,
                Summary = enabled ? "扩展就绪" : "扩展已关闭"
            };
        }

        public AuraTooling.Shared.AuraToolExtensionOperationResult SetEnabled(
            bool value)
        {
            enabled = value;
            return AuraTooling.Shared.AuraToolExtensionOperationResult.Ok();
        }

        public void ShowSettings(UnityEngine.Transform parent)
        {
            SettingsOpened++;
        }
    }
}
