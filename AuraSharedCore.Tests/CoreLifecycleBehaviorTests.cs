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
    public static void TestLifecycleContracts()
    {
        AuraFeatureSwitchRuntime.RegisterFeature("OwnerA", "FeatureA", defaultEnabled: true, "test");
        Assert(AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature default enabled");
        AuraFeatureSwitchRuntime.SetLocalOverride("ToolA", "OwnerA", "FeatureA", false);
        Assert(!AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature effective override disabled");
        AuraFeatureSwitchRuntime.SetLocalOverride("ToolA", "OwnerA", "FeatureA", true);
        Assert(AuraFeatureSwitchRuntime.IsEnabled("OwnerA", "FeatureA"), "feature effective override enabled");
    
        AuraLifecycleOperationLedger.ClearScopePrefix("test-battle:");
    
        AuraLifecycleSessionRuntime.EndBattleSession();
        var firstEpoch = AuraLifecycleSessionRuntime.RestartBattleSession();
        var secondEpoch = AuraLifecycleSessionRuntime.RestartBattleSession();
        Assert(secondEpoch > firstEpoch,
            "RestartBattleSession should advance the epoch even while the previous battle session is active");
        Assert(AuraLifecycleSessionRuntime.IsBattleSessionActive,
            "restarted battle session should remain active");
        Assert(AuraLifecycleSessionRuntime.TryBeginBattleRestart(out var interruptedEpoch)
               && interruptedEpoch == secondEpoch,
            "battle restart boundary should atomically interrupt the current session");
        Assert(!AuraLifecycleSessionRuntime.IsBattleSessionActive
               && !AuraLifecycleSessionRuntime.TryBeginBattleRestart(out _),
            "battle restart boundary should be emitted once per active session");
        var rebuiltEpoch = AuraLifecycleSessionRuntime.RestartBattleSession();
        Assert(rebuiltEpoch == interruptedEpoch + 1 && AuraLifecycleSessionRuntime.IsBattleSessionActive,
            "rebuilt battle should receive exactly one new session epoch");
        AuraLifecycleSessionRuntime.EndBattleSession();
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
    
    public static void TestIdentityContracts()
    {
        Assert(AuraSharedIdentity.NormalizeRoleId("1") == "career_1", "short numeric career id normalizes");
        Assert(AuraSharedIdentity.NormalizeRoleId("*Terrias_wuna_wuna") == "Terrias_wuna_wuna", "mod role id trims legacy star prefix");
        Assert(AuraSharedIdentity.IsRuntimeNumericId("76561198326385152"), "long numeric runtime owner id detected");
        Assert(!AuraSharedIdentity.IsUsableRoleId("76561198326385152"), "long numeric runtime owner id is not a role");
        Assert(AuraSharedIdentity.SelectRoleId("76561198326385152", "Terrias_wuna_wuna") == "Terrias_wuna_wuna",
            "role selector falls back past runtime owner id");
        Assert(AuraSharedIdentity.SelectRoleId("wuna", "Terrias_wuna_wuna") == "wuna",
            "role selector preserves usable short mod role id");
    
        Assert(AuraSharedContentId.Matches("careercard_*8", "careercard_8", knownPrefixes: new[] { "careercard_" }),
            "content id matcher accepts internal table protocol markers");
        Assert(AuraSharedContentId.Matches("8", "careercard_8", knownPrefixes: new[] { "careercard_" }),
            "content id matcher accepts deterministic official short ids");
        Assert(AuraSharedContentId.Matches("solar_prayer", "Terrias_solar_prayer", "Terrias"),
            "content id matcher accepts owner-scoped short ids");
    
        var uniqueShort = AuraSharedContentId.Resolve(
            "solar_prayer",
            new[] { "Terrias_solar_prayer", "Other_card" },
            "Terrias");
        Assert(uniqueShort.Success && uniqueShort.ResolvedId == "Terrias_solar_prayer",
            "content id resolver returns the unique owner-scoped full id");
        var ambiguousShort = AuraSharedContentId.Resolve(
            "prayer",
            new[] { "Terrias_solar_prayer", "Other_lunar_prayer" });
        Assert(!ambiguousShort.Success && ambiguousShort.Kind == AuraSharedContentIdResolutionKind.Ambiguous,
            "content id resolver rejects colliding short ids");
    
        var resourceCandidates = AuraSharedResourceReference.BuildCandidates(
            "CG/AuraToolsExp/Roles/career_7/skill_cg_1.png",
            new AuraSharedResourceAlias("CG/Roles/", "CG/AuraToolsExp/Roles/"));
        Assert(resourceCandidates.Count == 2
               && resourceCandidates[0] == "CG/AuraToolsExp/Roles/career_7/skill_cg_1.png"
               && resourceCandidates[1] == "CG/Roles/career_7/skill_cg_1.png",
            "resource compatibility keeps the declared path first and adds a bidirectional alias fallback");
    }
    
    public static void TestJourneyContracts()
    {
        var context = new AuraJourneyConditionContext
        {
            RoleIds = new List<string> { "Terrias_wuna_wuna", "SanGuoShaExp_shenzhugeliang" },
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
            new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.AnyRole, Value = "Terrias_wuna_wuna" },
            new AuraJourneyCondition { Kind = AuraJourneyConditionKinds.PlayerCountAtLeast, Number = 2 }
        }, context), "journey condition evaluator");
    
        var request = new AuraJourneyCommitRequest
        {
            JourneyId = "Terrias.SolarMemory",
            OwnerModId = "Terrias",
            Action = "SelectNode",
            NodeId = "memory_1",
            Message = "selected first memory",
            Mutation = new AuraJourneyMutation
            {
                Run = new AuraJourneyRunBinding
                {
                    RunId = "solar-memory-run",
                    NativeModeKey = "Terrias_SolarMemoryMode",
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
            JourneyId = "Terrias.SolarMemory",
            OwnerModId = "Terrias",
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
            MapId = "Terrias_terrias_solar_memory_black_sun_after",
            FallbackMapId = "solar_memory_black_sun_after",
            NodeId = "Terrias_terrias_Sub_solar_memory_black_sun_after",
            Type = AuraJourneyNodeKinds.Event,
            Note = "普通事件",
            Level = "-1",
            DicePolicy = AuraJourneyDicePolicies.Default
        }, id => id == "Terrias_terrias_solar_memory_black_sun_after"
            ? new Dictionary<string, string> { ["Id"] = "old", ["Type"] = "", ["Note"] = "", ["Level"] = "" }
            : null);
    
        Assert(projection.Valid
               && projection.Data["Id"] == "Terrias_terrias_solar_memory_black_sun_after"
               && projection.Data["Type"] == AuraJourneyNodeKinds.Event
               && projection.Data["NodeId"] == "Terrias_terrias_Sub_solar_memory_black_sun_after"
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
    
    public static void TestOnlineChatContracts()
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
}
