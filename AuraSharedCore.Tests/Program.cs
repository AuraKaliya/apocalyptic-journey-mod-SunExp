using AuraShared.Core;
using AuraJourney.Shared;
using Newtonsoft.Json.Linq;

var assertions = 0;
var tempRoot = Path.Combine(Path.GetTempPath(), "AuraSharedCore.Tests", Guid.NewGuid().ToString("N"));
var sourceRoot = Path.Combine(Path.GetTempPath(), "AuraSharedCore.Tests.Sources", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
Directory.CreateDirectory(sourceRoot);

try
{
    using var storage = new AuraSharedStorageCoordinator(tempRoot);
    var packages = new AuraSharedPackageCoordinator(storage);

    var first = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json",
        WriterId = "TestAuthority",
        AuthorityId = "TestAuthority",
        ExpectedRevision = 0,
        PayloadJson = "{\"value\":1}"
    });
    Assert(first.Success && first.Revision == 1, "first shared write");

    var snapshot = storage.Read(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json"
    });
    Assert(snapshot.Success && snapshot.Found && JObject.Parse(snapshot.PayloadJson)["value"]!.Value<int>() == 1, "shared snapshot read");

    var unauthorized = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json",
        WriterId = "OtherWriter",
        ExpectedRevision = 1,
        PayloadJson = "{\"value\":2}"
    });
    Assert(!unauthorized.Success && unauthorized.Conflict, "shared authority rejection");

    var stale = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json",
        WriterId = "TestAuthority",
        ExpectedRevision = 0,
        PayloadJson = "{\"value\":2}"
    });
    Assert(!stale.Success && stale.Conflict && stale.Revision == 1, "revision conflict");

    var wrongOwner = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Owner,
        System = "Test",
        OwnerModId = "OwnerA",
        FileName = "owner.json",
        WriterId = "OwnerB",
        ExpectedRevision = 0,
        PayloadJson = "{}"
    });
    Assert(!wrongOwner.Success && wrongOwner.Conflict, "owner writer isolation");

    var invalidDocumentPath = Path.Combine(tempRoot, "Config", "Shared", "Invalid", "raw.json");
    Directory.CreateDirectory(Path.GetDirectoryName(invalidDocumentPath)!);
    File.WriteAllText(invalidDocumentPath, "{\"oldRawFormat\":true}");
    var replacedInvalid = storage.Write(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Invalid",
        FileName = "raw.json",
        WriterId = "RepairAuthority",
        ExpectedRevision = 0,
        PayloadJson = "{\"valid\":true}"
    });
    Assert(replacedInvalid.Success
           && Directory.EnumerateFiles(Path.Combine(tempRoot, "Backups", "Storage", "Invalid"), "*.invalid", SearchOption.AllDirectories).Any(),
        "invalid document quarantine");

    var sourceFile = Path.Combine(sourceRoot, "audio.wav");
    File.WriteAllText(sourceFile, "v1");
    var install = packages.Install(Request("OwnerA", "Audio", "voice", "PackA", 1, sourceFile, "Audio/Test/voice.wav"));
    Assert(install.Success && install.Changed && install.Status == "Installed", "file resource install");

    var duplicate = packages.Install(Request("OwnerA", "Audio", "voice", "PackA", 1, sourceFile, "Audio/Test/voice.wav"));
    Assert(duplicate.Success && !duplicate.Changed && duplicate.Status == "Deduplicated", "same owner deduplication");

    var secondOwner = packages.Install(Request("OwnerB", "Audio", "voice", "PackB", 1, sourceFile, "Audio/Test/voice.wav"));
    Assert(secondOwner.Success && !secondOwner.Changed, "equal content cross-owner deduplication");

    File.WriteAllText(sourceFile, "different owner content");
    var crossOwnerConflict = packages.Install(Request("OwnerC", "Audio", "voice", "PackC", 2, sourceFile, "Audio/Test/voice.wav"));
    Assert(!crossOwnerConflict.Success && crossOwnerConflict.Conflict, "cross-owner content conflict");

    var updateSource = Path.Combine(sourceRoot, "update.wav");
    File.WriteAllText(updateSource, "one");
    Assert(packages.Install(Request("OwnerA", "Audio", "update", "PackUpdate", 1, updateSource, "Audio/Test/update.wav")).Success, "update baseline");
    File.WriteAllText(updateSource, "two");
    var sameVersionConflict = packages.Install(Request("OwnerA", "Audio", "update", "PackUpdate", 1, updateSource, "Audio/Test/update.wav"));
    Assert(!sameVersionConflict.Success && sameVersionConflict.Conflict, "same version content conflict");
    var updated = packages.Install(Request("OwnerA", "Audio", "update", "PackUpdate", 2, updateSource, "Audio/Test/update.wav"));
    Assert(updated.Success && updated.Changed && updated.Status == "Updated", "higher version update");

    var directorySource = Path.Combine(sourceRoot, "skin");
    Directory.CreateDirectory(Path.Combine(directorySource, "Idle"));
    File.WriteAllText(Path.Combine(directorySource, "skin.json"), "{}");
    File.WriteAllText(Path.Combine(directorySource, "Idle", "Idle_00.png"), "frame");
    var directoryInstall = packages.Install(new AuraSharedInstallRequest
    {
        OwnerModId = "OwnerA",
        System = "Skin",
        LogicalId = "career::skin",
        PackageId = "SkinPack",
        PackageVersion = 1,
        Kind = AuraSharedResourceKinds.Directory,
        SourcePath = directorySource,
        DestinationRelativePath = "Skins/career/skin"
    });
    Assert(directoryInstall.Success && File.Exists(Path.Combine(tempRoot, "Skins", "career", "skin", "Idle", "Idle_00.png")), "directory install");

    using var storage2 = new AuraSharedStorageCoordinator(tempRoot);
    await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(() => Increment(index % 2 == 0 ? storage2 : storage))));
    var counter = storage.Read(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Concurrency",
        FileName = "counter.json"
    });
    Assert(counter.Success && JObject.Parse(counter.PayloadJson)["value"]!.Value<int>() == 20, "two coordinator CAS concurrency");

    TestRecovery(storage, packages);
    Assert(true, "transaction recovery");

    TestJourneyContracts();

    Console.WriteLine($"AuraSharedCore tests passed: {assertions} assertions.");
}
finally
{
    TryDelete(tempRoot);
    TryDelete(sourceRoot);
}

return;

AuraSharedInstallRequest Request(string owner, string system, string id, string package, long version, string source, string destination)
{
    return new AuraSharedInstallRequest
    {
        OwnerModId = owner,
        System = system,
        LogicalId = id,
        PackageId = package,
        PackageVersion = version,
        Kind = AuraSharedResourceKinds.File,
        SourcePath = source,
        DestinationRelativePath = destination
    };
}

void Increment(AuraSharedStorageCoordinator coordinator)
{
    for (var attempt = 0; attempt < 200; attempt++)
    {
        var read = coordinator.Read(new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Shared,
            System = "Concurrency",
            FileName = "counter.json"
        });
        var value = read.Found ? JObject.Parse(read.PayloadJson)["value"]!.Value<int>() : 0;
        var write = coordinator.Write(new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Shared,
            System = "Concurrency",
            FileName = "counter.json",
            WriterId = "Counter",
            AuthorityId = "Counter",
            ExpectedRevision = read.Revision,
            PayloadJson = "{\"value\":" + (value + 1) + "}"
        });
        if (write.Success)
        {
            return;
        }
        if (!write.Conflict)
        {
            throw new InvalidOperationException(write.Message);
        }
    }
    throw new TimeoutException("CAS increment did not converge.");
}

void TestRecovery(AuraSharedStorageCoordinator coordinator, AuraSharedPackageCoordinator packageCoordinator)
{
    var destination = Path.Combine(tempRoot, "Audio", "Recovery", "file.wav");
    var backup = Path.Combine(tempRoot, "Backups", "Recovery", "old.wav");
    var staging = Path.Combine(tempRoot, "Cache", "Packages", "recovery-test");
    var registry = Path.Combine(tempRoot, "Registries", "Recovery", "resources.json");
    var registryBackup = Path.Combine(staging, "registry.backup.json");
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
    Directory.CreateDirectory(staging);
    Directory.CreateDirectory(Path.GetDirectoryName(registry)!);
    File.WriteAllText(destination, "new");
    File.WriteAllText(backup, "old");
    File.WriteAllText(registry, "{\"new\":true}");
    File.WriteAllText(registryBackup, "{\"old\":true}");

    var journal = new AuraSharedTransactionJournal
    {
        TransactionId = "recovery-test",
        State = "ContentCommitted",
        DestinationPath = destination,
        BackupPath = backup,
        StagingPath = staging,
        RegistryPath = registry,
        RegistryBackupPath = registryBackup,
        DestinationExisted = true,
        RegistryExisted = true,
        Kind = AuraSharedResourceKinds.File
    };
    coordinator.WriteRawJsonAtomic(Path.Combine(tempRoot, "Transactions", "recovery-test.json"), journal, false);
    var recovered = packageCoordinator.RecoverTransactions();
    if (recovered != 1 || File.ReadAllText(destination) != "old" || !File.ReadAllText(registry).Contains("old"))
    {
        throw new InvalidOperationException("Interrupted transaction was not restored.");
    }
}

void TestJourneyContracts()
{
    var context = new AuraJourneyConditionContext
    {
        RoleIds = new List<string> { "SunExp_wuna_wuna", "SanGuoShaExp_shenzhugeliang" },
        PlayerCount = 2,
        Flags = new Dictionary<string, bool> { ["solar_memory_unlocked"] = true },
        Values = new Dictionary<string, string> { ["route"] = "sun" },
        Counters = new Dictionary<string, int> { ["embers"] = 3 }
    };

    Assert(AuraJourneyConditionEvaluator.EvaluateAll(new[]
    {
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.Flag, Key = "solar_memory_unlocked" },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.Equals, Key = "route", Value = "sun" },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.MinCounter, Key = "embers", Number = 2 },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.AnyRole, Value = "SunExp_wuna_wuna" },
        new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.PlayerCountAtLeast, Number = 2 }
    }, context), "journey condition evaluator");

    var request = new AuraJourneyCommitRequest
    {
        JourneyId = "SunExp.SolarMemory",
        OwnerModId = "SunExp",
        Action = "SelectNode",
        NodeId = "memory_1",
        Message = "selected first memory",
        Mutation = new AuraJourneyMutation
        {
            Run = new AuraJourneyRunBinding
            {
                RunId = "solar-memory-run",
                NativeModeKey = "SunExp_SolarMemoryMode",
                NativeModeValue = "1",
                StartedUtc = DateTime.UnixEpoch.ToString("O")
            },
            ActiveNodeId = "memory_1",
            SelectRouteId = "solar_route",
            SetFlags = new Dictionary<string, bool> { ["entered"] = true },
            AddCounters = new Dictionary<string, int> { ["embers"] = 1 }
        }
    };

    var state = AuraJourneyStateReducer.Apply(null, request, DateTime.UnixEpoch);
    Assert(state.Version == 1
           && state.Run.RunId == "solar-memory-run"
           && state.ActiveNodeId == "memory_1"
           && state.SelectedRouteIds.Contains("solar_route")
           && state.Flags["entered"]
           && state.Counters["embers"] == 1
           && state.Events.Count == 1,
        "journey state reducer first event");

    var next = AuraJourneyStateReducer.Apply(state, new AuraJourneyCommitRequest
    {
        JourneyId = "SunExp.SolarMemory",
        OwnerModId = "SunExp",
        Action = "CompleteNode",
        NodeId = "memory_1",
        Mutation = new AuraJourneyMutation
        {
            CompleteNodeId = "memory_1",
            AddCounters = new Dictionary<string, int> { ["embers"] = 2 }
        }
    }, DateTime.UnixEpoch.AddSeconds(1));

    Assert(next.Version == 2
           && next.CompletedNodeIds.Contains("memory_1")
           && next.Counters["embers"] == 3
           && next.Events.Count == 2,
        "journey state reducer append event");

    var projection = AuraJourneyMapNodeDataBuilder.Build(new AuraJourneyMapNodeSpec
    {
        MapId = "SunExp_sunexp_solar_memory_black_sun_after",
        FallbackMapId = "solar_memory_black_sun_after",
        NodeId = "SunExp_sunexp_Sub_solar_memory_black_sun_after",
        Type = AuraJourneyNodeKinds.Event,
        Note = "普通事件",
        Level = "-1",
        DicePolicy = AuraJourneyDicePolicies.Default
    }, id => id == "SunExp_sunexp_solar_memory_black_sun_after"
        ? new Dictionary<string, string> { ["Id"] = "old", ["Type"] = "", ["Note"] = "", ["Level"] = "" }
        : null);

    Assert(projection.Valid
           && projection.Data["Id"] == "SunExp_sunexp_solar_memory_black_sun_after"
           && projection.Data["Type"] == AuraJourneyNodeKinds.Event
           && projection.Data["NodeId"] == "SunExp_sunexp_Sub_solar_memory_black_sun_after"
           && projection.Data["Level"] == "-1"
           && projection.DicePolicy == AuraJourneyDicePolicies.Default,
        "journey map node projection fills native fields");

    var maps = new[] { "wrong", "Breaks_keep", "old" };
    var mapData = new[] { "wrong_event", "Breaks_keep_data", "old_node" };
    var repair = AuraJourneySyncProjection.Repair(maps, mapData, new[]
    {
        new AuraJourneySlotRule
        {
            SlotIndex = 0,
            MapNode = new AuraJourneyMapNodeSpec
            {
                MapId = "fixed_event_map",
                NodeId = "fixed_event_node",
                Type = AuraJourneyNodeKinds.Event
            }
        },
        new AuraJourneySlotRule
        {
            SlotIndex = 1,
            ReplacementPolicy = AuraJourneyReplacementPolicies.PreserveBreak,
            MapNode = new AuraJourneyMapNodeSpec
            {
                MapId = "should_not_replace",
                NodeId = "should_not_replace_node"
            }
        }
    });

    Assert(repair.Changed
           && repair.Repaired == 1
           && repair.Preserved == 1
           && maps[0] == "fixed_event_map"
           && mapData[0] == "fixed_event_node"
           && maps[1] == "Breaks_keep",
        "journey sync projection repairs fixed slots and preserves break nodes");

    AuraJourneyMapIdAliasRegistry.RegisterPrefixAlias("test.full-to-short", "TestMod_full_", "");
    var aliases = AuraJourneyMapIdAliasRegistry.Expand("*TestMod_full_map_1");
    Assert(aliases.Contains("*TestMod_full_map_1")
           && aliases.Contains("TestMod_full_map_1")
           && aliases.Contains("map_1"),
        "journey map id alias registry expands registered prefixes without shared content rules");
}

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }
    assertions++;
}

void TryDelete(string path)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
    catch
    {
    }
}
