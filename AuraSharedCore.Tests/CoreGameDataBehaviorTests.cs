using AuraShared.Core;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraOnline.Shared;
using AuraDirector.Shared;
using AuraRole.Shared;
using AuraGameData.Shared;
using Newtonsoft.Json.Linq;
internal static partial class CoreTestSuite
{
    public static void TestGameDataCatalog()
    {
        AuraSharedConfigStore.ResetGameDataTestStore();
        var source = new FakeGameDataSource(
            new AuraGameDataDefinition
            {
                Key = new AuraGameDataKey("Card", "card_a"),
                OwnerModId = "BaseGame",
                WriterId = AuraGameDataConstants.RegistryAuthorityId,
                SourceKind = AuraGameDataSourceKinds.Native,
                Fields = new Dictionary<string, string> { ["Id"] = "card_a", ["Name"] = "Native" }
            },
            new AuraGameDataDefinition
            {
                Key = new AuraGameDataKey("Card", "card_overlay"),
                OwnerModId = "BaseGame",
                WriterId = AuraGameDataConstants.RegistryAuthorityId,
                SourceKind = AuraGameDataSourceKinds.Native,
                Fields = new Dictionary<string, string>
                {
                    ["Id"] = "card_overlay",
                    ["Name"] = "Native Overlay Base",
                    ["Cost"] = "2"
                }
            });
        AuraGameDataCatalogRuntime.ConfigureSource(source);
    
        var ownerRule = AuraGameDataCatalogRuntime.RegisterOwnerRules("ModOwner", new[]
        {
            new AuraGameDataOwnerRule
            {
                OwnerModId = "ModOwner",
                WriterId = "ModOwner",
                IdPrefix = "card_"
            }
        });
        var nativeOwned = AuraGameDataCatalogRuntime.Query(new AuraGameDataQuery
        {
            DataType = "Card",
            CandidateIds = new List<string> { "card_a" },
            IncludeAllCandidates = true
        }).Items.FirstOrDefault(value => value.SourceKind == AuraGameDataSourceKinds.Native);
        Assert(ownerRule.Success && nativeOwned?.OwnerModId == "ModOwner",
            "game data v5 owner rules assign provenance without copying native rows");
    
        var overlay = AuraGameDataCatalogRuntime.Register("OverlayMod", new AuraGameDataDefinition
        {
            Key = new AuraGameDataKey("Card", "card_overlay"),
            OwnerModId = "OverlayMod",
            WriterId = "OverlayMod",
            SourceKind = AuraGameDataSourceKinds.Registered,
            StorageKind = AuraGameDataStorageKinds.Overlay,
            Fields = new Dictionary<string, string> { ["Name"] = "Overlay" },
            RemoveFields = new List<string> { "Cost" }
        });
        var overlaid = AuraGameDataCatalogRuntime.Resolve("Card", new[] { "card_overlay" });
        Assert(overlay.Success
               && overlaid?.Fields["Name"] == "Overlay"
               && !overlaid.Fields.ContainsKey("Cost"),
            "game data v5 overlays merge once during compilation");
        var overlayHandle = overlay.Handle;
        source.Invalidate();
        AuraGameDataCatalogRuntime.Rebuild();
        Assert(overlayHandle != null
               && !AuraGameDataCatalogRuntime.ValidateHandle(overlayHandle, out _),
            "game data handles become stale after a catalog generation change");
    
        var rejectedV4 = AuraGameDataCatalogRuntime.Register("ModA", new AuraGameDataDefinition
        {
            SchemaVersion = 4,
            Key = new AuraGameDataKey("Card", "card_a"),
            OwnerModId = "ModA",
            WriterId = "ModA"
        });
        Assert(!rejectedV4.Success && rejectedV4.Message.Contains("schemaVersion 5"), "game data rejects non-v5 registration");
    
        var registered = AuraGameDataCatalogRuntime.Register("ModA", new AuraGameDataDefinition
        {
            Key = new AuraGameDataKey("Card", "card_a"),
            OwnerModId = "ModA",
            WriterId = "ModA",
            SourceKind = AuraGameDataSourceKinds.Registered,
            Fields = new Dictionary<string, string> { ["Id"] = "card_a", ["Name"] = "Registered" }
        });
        Assert(registered.Success && registered.Handle != null, "game data registers owner-qualified v5 definition");
    
        var effective = AuraGameDataCatalogRuntime.Resolve("Card", new[] { "card_a" });
        Assert(effective != null
               && effective.SourceKind == AuraGameDataSourceKinds.Registered
               && effective.Fields["Name"] == "Registered",
            "game data uses centralized source search order");
        var captureCount = source.CaptureCount;
        AuraGameDataDiagnostics.Reset();
        for (var index = 0; index < 1000; index++)
        {
            Assert(AuraGameDataCatalogRuntime.TryGet("Card", "card_a", out _), "game data indexed point lookup resolves");
        }
        Assert(source.CaptureCount == captureCount, "game data hot point lookups never recapture native tables");
        var diagnostics = AuraGameDataDiagnostics.Snapshot();
        Assert(diagnostics.PointLookups == 1000
               && diagnostics.PointHits == 1000
               && diagnostics.NativeCaptures == 0
               && diagnostics.CatalogBuilds == 0,
            "game data diagnostics prove hot point lookups are pure snapshot reads");
        AuraGameDataCatalogRuntime.TryGet("Card", "card_a", out _);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var lookupBenchmark = System.Diagnostics.Stopwatch.StartNew();
        for (var index = 0; index < 10_000; index++)
        {
            AuraGameDataCatalogRuntime.TryGet("Card", "card_a", out _);
        }
        lookupBenchmark.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert(allocatedBytes <= 1024,
            "game data performs ten thousand hot point lookups without meaningful allocation");
        Assert(lookupBenchmark.ElapsedMilliseconds < 250,
            "game data performs ten thousand hot point lookups within the regression budget");
        Assert(AuraGameDataCatalogRuntime.TryResolveUniqueType("card_a", out var resolvedType)
               && resolvedType == "Card",
            "game data unique-type index resolves without table probes");
        var tableA = AuraGameDataCatalogRuntime.GetTable("Card");
        var tableB = AuraGameDataCatalogRuntime.GetTable("Card");
        Assert(ReferenceEquals(tableA, tableB), "game data table view is stable within one catalog epoch");
    
        var foreignPatch = AuraGameDataCatalogRuntime.Patch(
            "OtherMod",
            new AuraGameDataKey("Card", "card_a"),
            "ModA",
            new AuraGameDataPatch { SetFields = new Dictionary<string, string> { ["Name"] = "Foreign" } },
            registered.Handle!.Revision);
        Assert(!foreignPatch.Success && foreignPatch.Conflict, "game data rejects foreign definition patch");
    
        Assert(!AuraGameDataFieldPolicy.IsScriptField("Description")
               && !AuraGameDataFieldPolicy.IsScriptField("Description_zh-Hant")
               && !AuraGameDataFieldPolicy.IsScriptField("Description1")
               && AuraGameDataFieldPolicy.IsScriptField("UseScript")
               && AuraGameDataFieldPolicy.IsScriptField("ChoiceScript1"),
            "game data distinguishes description fields from executable script columns");
    
        var descriptionPatch = AuraGameDataCatalogRuntime.Patch(
            "ModA",
            new AuraGameDataKey("Card", "card_a"),
            "ModA",
            new AuraGameDataPatch { SetFields = new Dictionary<string, string> { ["Description"] = "Localized effect" } },
            registered.Handle.Revision);
        Assert(descriptionPatch.Success, "game data permits runtime description patch");
    
        var scriptPatch = AuraGameDataCatalogRuntime.Patch(
            "ModA",
            new AuraGameDataKey("Card", "card_a"),
            "ModA",
            new AuraGameDataPatch { SetFields = new Dictionary<string, string> { ["UseScript"] = "unsafe" } },
            descriptionPatch.Handle!.Revision);
        Assert(!scriptPatch.Success && scriptPatch.Message.Contains("registration-time"), "game data blocks runtime script patch");
    
        var numberedScriptPatch = AuraGameDataCatalogRuntime.Patch(
            "ModA",
            new AuraGameDataKey("Card", "card_a"),
            "ModA",
            new AuraGameDataPatch { SetFields = new Dictionary<string, string> { ["ChoiceScript1"] = "unsafe" } },
            descriptionPatch.Handle.Revision);
        Assert(!numberedScriptPatch.Success && numberedScriptPatch.Message.Contains("registration-time"),
            "game data blocks numbered runtime script patch");
    
        var retired = AuraGameDataCatalogRuntime.Retire(
            "ModA",
            new AuraGameDataKey("Card", "card_a"),
            "ModA",
            descriptionPatch.Handle.Revision);
        var history = AuraGameDataCatalogRuntime.QueryHistory(new AuraGameDataQuery { DataType = "Card" });
        Assert(retired.Success && history.Items.Count == 1 && history.Items[0].Retired,
            "game data keeps retired definitions in independent history view");
    
        var lastGood = AuraGameDataCatalogRuntime.AcquireSnapshot();
        var delayed = new DelayedGameDataSource(new AuraGameDataDefinition
        {
            Key = new AuraGameDataKey("Buff", "field_buff"),
            OwnerModId = "DelayedMod",
            WriterId = AuraGameDataConstants.RegistryAuthorityId,
            SourceKind = AuraGameDataSourceKinds.Native,
            Fields = new Dictionary<string, string> { ["Id"] = "field_buff", ["Name"] = "Field" }
        });
        AuraGameDataCatalogRuntime.ConfigureSource(delayed, rebuildImmediately: false);
        AuraGameDataCatalogRuntime.Rebuild();
        Assert(AuraGameDataCatalogRuntime.State == AuraGameDataCatalogState.AwaitingNativeCapture
               && ReferenceEquals(AuraGameDataCatalogRuntime.AcquireSnapshot(), lastGood)
               && AuraGameDataCatalogRuntime.AcquireSnapshot().Version.NativeReady,
            "game data rejects incomplete native generations and preserves the last-good snapshot");
    
        delayed.CompleteCapture();
        AuraGameDataCatalogRuntime.Rebuild();
        var completed = AuraGameDataCatalogRuntime.AcquireSnapshot();
        Assert(AuraGameDataCatalogRuntime.State == AuraGameDataCatalogState.Ready
               && completed.Version.NativeReady
               && completed.TryGet("Buff", "field_buff", out _),
            "game data publishes a completed native generation after deferred capture");
    }
}
