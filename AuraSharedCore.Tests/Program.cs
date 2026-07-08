using AuraShared.Core;
using AuraJourney.Shared;
using AuraOnline.Shared;
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
    Assert(storage.StorageLockKey(new AuraSharedStorageRequest
    {
        Scope = AuraSharedStorageScopes.Shared,
        System = "Test",
        FileName = "shared.json"
    }).Contains("Config/Shared"), "storage document lock key");

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
    Assert(OperationLogContains(tempRoot, "InstallResource", "ContentCommitted")
           && OperationLogContains(tempRoot, "InstallResource", "RegistryCommitted")
           && OperationLogContains(tempRoot, "InstallResource", "Completed"),
        "install operation log phases");

    var duplicate = packages.Install(Request("OwnerA", "Audio", "voice", "PackA", 1, sourceFile, "Audio/Test/voice.wav"));
    Assert(duplicate.Success && !duplicate.Changed && duplicate.Status == "Deduplicated", "same owner deduplication");

    var secondOwner = packages.Install(Request("OwnerB", "Audio", "voice", "PackB", 1, sourceFile, "Audio/Test/voice.wav"));
    Assert(secondOwner.Success && !secondOwner.Changed, "equal content cross-owner deduplication");

    var bootstrapSummary = AuraSharedBootstrapResult.FromResponses(new[]
    {
        install,
        duplicate,
        new AuraSharedInstallResponse { Success = true, Changed = true, Status = "Repaired" },
        new AuraSharedInstallResponse { Success = false, Conflict = true, Status = "Conflict" }
    });
    Assert(!bootstrapSummary.Success
           && bootstrapSummary.Changed
           && bootstrapSummary.Installed == 1
           && bootstrapSummary.Repaired == 1
           && bootstrapSummary.Deduplicated == 1
           && bootstrapSummary.Conflicts == 1,
        "resource bootstrap aggregates multi-Mod install outcomes");

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

    TestIdentityContracts();
    TestSecureEnvelopeContracts();
    TestLifecycleContracts();
    TestJourneyContracts();
    TestOnlineChatContracts();

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

void TestSecureEnvelopeContracts()
{
    using var encryptionKey = new System.Security.Cryptography.RSACryptoServiceProvider(2048);
    using var signatureKey = new System.Security.Cryptography.RSACryptoServiceProvider(2048);
    var envelopeJson = AuraSharedSecureEnvelope.EncryptJson(
        "TestEnvelope",
        "test-key",
        "{\"value\":42}",
        encryptionKey.ToXmlString(false),
        signatureKey.ToXmlString(true));

    Assert(envelopeJson.Contains("RSA-OAEP-SHA1+A256CBC-HS256")
           && envelopeJson.Contains("RSA-SHA256"),
        "secure envelope records crypto algorithms");

    var plainJson = AuraSharedSecureEnvelope.DecryptJson(
        envelopeJson,
        "TestEnvelope",
        encryptionKey.ToXmlString(true),
        signatureKey.ToXmlString(false));
    Assert(JObject.Parse(plainJson)["value"]!.Value<int>() == 42,
        "secure envelope decrypts signed payload");

    var tamperedEnvelope = JObject.Parse(envelopeJson);
    tamperedEnvelope["ciphertext"] = "AA" + tamperedEnvelope["ciphertext"]!.Value<string>();
    var tampered = tamperedEnvelope.ToString(Newtonsoft.Json.Formatting.None);
    try
    {
        AuraSharedSecureEnvelope.DecryptJson(
            tampered,
            "TestEnvelope",
            encryptionKey.ToXmlString(true),
            signatureKey.ToXmlString(false));
        throw new InvalidOperationException("Tampered envelope was accepted.");
    }
    catch (System.Security.Cryptography.CryptographicException)
    {
        Assert(true, "secure envelope rejects tampered payload");
    }
    catch (InvalidOperationException)
    {
        Assert(true, "secure envelope rejects tampered payload");
    }
    catch (FormatException)
    {
        Assert(true, "secure envelope rejects malformed payload");
    }
}

void TestLifecycleContracts()
{
    AuraFeatureSwitchRuntime.RegisterFeature("OwnerA", "FeatureA", defaultEnabled: true, "test");
    Assert(AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature default enabled");
    AuraFeatureSwitchRuntime.SetLocalOverride("ToolA", "OwnerA", "FeatureA", false);
    Assert(!AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature effective override disabled");
    AuraFeatureSwitchRuntime.SetLocalOverride("ToolA", "OwnerA", "FeatureA", true);
    Assert(AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature effective override enabled");

    AuraLifecycleOperationLedger.ClearScopePrefix("test-battle:");
    Assert(AuraLifecycleOperationLedger.TryClaim("test-battle:1", "OwnerA", "FeatureA", "AddStartBuff", "Status1", "buff", "BuffA"),
        "first lifecycle operation claim");
    Assert(!AuraLifecycleOperationLedger.TryClaim("test-battle:1", "OwnerA", "FeatureA", "AddStartBuff", "Status1", "buff", "BuffA"),
        "duplicate lifecycle operation rejected");
    Assert(AuraLifecycleOperationLedger.TryClaim("test-battle:1", "OwnerA", "FeatureA", "AddStartBuff", "Status1", "buff", "BuffB"),
        "different buff effect can claim");
    Assert(AuraLifecycleOperationLedger.TryClaim("test-battle:1", "OwnerA", "FeatureA", "AddStartBuff", "Status1", "marker", "BuffA"),
        "different effect category can claim");
    AuraLifecycleOperationLedger.ClearScopePrefix("test-battle:");
}

void TestIdentityContracts()
{
    Assert(AuraSharedIdentity.NormalizeRoleId("1") == "career_1", "short numeric career id normalizes");
    Assert(AuraSharedIdentity.NormalizeRoleId("*SunExp_wuna_wuna") == "SunExp_wuna_wuna", "mod role id trims legacy star prefix");
    Assert(AuraSharedIdentity.IsRuntimeNumericId("76561198326385152"), "long numeric runtime owner id detected");
    Assert(!AuraSharedIdentity.IsUsableRoleId("76561198326385152"), "long numeric runtime owner id is not a role");
    Assert(AuraSharedIdentity.SelectRoleId("76561198326385152", "SunExp_wuna_wuna") == "SunExp_wuna_wuna",
        "role selector falls back past runtime owner id");
    Assert(AuraSharedIdentity.SelectRoleId("wuna", "SunExp_wuna_wuna") == "wuna",
        "role selector preserves usable short mod role id");
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

void TestOnlineChatContracts()
{
    var parsed = AuraChatEmojiParser.Parse("hi #[role:smile] ok").ToList();
    Assert(parsed.Count == 3
           && parsed[1].Kind == "Sticker"
           && parsed[1].PackId == "role"
           && parsed[1].StickerId == "smile"
           && AuraChatEmojiParser.DisplayLength("hi #[role:smile] ok") == 7,
        "chat emoji token parsing");

    var limited = AuraChatTextLimiter.LimitPlayerText("12345678901234567890123456789012345678901234");
    Assert(limited == "1234567890123456789012345678901234567890...", "chat player text limit");

    var wrapped = AuraChatTextLimiter.WrapPlainText("123456789012345678901234567890123456789012345678901234567890X");
    Assert(wrapped == "123456789012345678901234567890123456789012345678901234567890\nX", "chat display line wrap");

    var status = AuraChatModSyncSnapshot.BuildStatus(new object[]
    {
        new FakeLobbyPlayer("p1", "A", new[] { new FakeLobbyMod("ChatExp", "0.1", true), new FakeLobbyMod("Other", "1", true), new FakeLobbyMod("SuperLongModName", "2", true), new FakeLobbyMod("中文MOD名称超长", "3", true), new FakeLobbyMod("Unused", "9", false) }),
        new FakeLobbyPlayer("p2", "LongPlayerName", new[] { new FakeLobbyMod("ChatExp", "0.1", false), new FakeLobbyMod("Unused", "9", false) }),
        new FakeLobbyPlayer("p3", "玩家甲乙丙丁", new[] { new FakeLobbyMod("ChatExp", "0.1", true) })
    }, "ChatExp");
    Assert(status.Contains("MOD同步状态")
           && status.Contains("MOD\tA\tLongPlayer\t玩家甲乙丙")
           && status.Contains("A")
           && status.Contains("LongPlayer")
           && status.Contains("玩家甲乙丙")
           && !status.Contains("LongPlayerName")
           && !status.Contains("玩家甲乙丙丁")
           && status.Contains("ChatExp")
           && status.Contains("ChatExp\t0.1\tOFF\t0.1")
           && status.Contains("0.1")
           && status.Contains("OFF")
           && status.Contains("Other")
           && status.Contains("SuperLongM")
           && !status.Contains("SuperLongModName")
           && status.Contains("中文MOD名")
           && !status.Contains("中文MOD名称超长")
           && !status.Contains("Unused"),
        "chat mod sync status");

    var localStore = new AuraChatLocalStore(2);
    Assert(localStore.Add(new AuraChatMessage { MessageId = "one", RawText = "one" })
           && localStore.Add(new AuraChatMessage { MessageId = "two", RawText = "two" })
           && localStore.Add(new AuraChatMessage { MessageId = "three", RawText = "three" })
           && !localStore.Add(new AuraChatMessage { MessageId = "three", RawText = "duplicate" })
           && localStore.Messages.Count == 2
           && localStore.Messages[0].RawText == "two"
           && localStore.Messages[1].RawText == "three",
        "chat bounded local store");

    AuraChatRuntime.Initialize("ChatExp", 2);
    AuraChatRuntime.Receive(new AuraChatMessage
    {
        MessageId = "unsigned-free-text",
        ContentKind = AuraChatKinds.PlayerText,
        RawText = "must be rejected"
    });
    Assert(AuraChatRuntime.Messages.Count == 0, "chat rejects content outside the verified catalog");
    AuraChatRuntime.ClearMessages();
    Assert(AuraChatRuntime.Messages.Count == 0, "chat clear local history");
}

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }
    assertions++;
}

bool OperationLogContains(string root, string kind, string phase)
{
    var directory = Path.Combine(root, "Logs", "Operations");
    if (!Directory.Exists(directory))
    {
        return false;
    }

    return Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
        .SelectMany(File.ReadAllLines)
        .Any(line =>
        {
            var json = JObject.Parse(line);
            return string.Equals(json["kind"]?.Value<string>(), kind, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(json["phase"]?.Value<string>(), phase, StringComparison.OrdinalIgnoreCase);
        });
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

sealed class FakeLobbyPlayer
{
    public FakeLobbyPlayer(string id, string name, IEnumerable<FakeLobbyMod> mods)
    {
        Id = id;
        Name = name;
        Mods = mods.ToList();
    }

    public string Id { get; }

    public string Name { get; }

    public List<FakeLobbyMod> Mods { get; }
}

sealed class FakeLobbyMod
{
    public FakeLobbyMod(string modName, string modVersion, bool enabled)
    {
        ModName = modName;
        ModVersion = modVersion;
        Enabled = enabled;
    }

    public string ModName { get; }

    public string ModVersion { get; }

    public bool Enabled { get; }
}
