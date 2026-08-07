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
    public static void TestObjectPoolContracts()
    {
        var pool = new AuraSharedObjectPool<string, PoolValue>(2, value => value.IsValid);
        var first = new PoolValue("first");
        var second = new PoolValue("second");
        var overflow = new PoolValue("overflow");
        Assert(pool.Release("common", first), "object pool accepts first value");
        Assert(!pool.Release("common", first), "object pool rejects duplicate idle instances");
        Assert(pool.Release("common", second), "object pool accepts value up to capacity");
        Assert(!pool.Release("common", overflow), "object pool rejects values over per-key capacity");
        Assert(pool.Count("common") == 2, "object pool reports per-key count");
        Assert(pool.TryAcquire("common", out var acquired) && ReferenceEquals(acquired, second), "object pool acquires in LIFO order");
    
        acquired!.IsValid = false;
        Assert(pool.Release("attack", acquired) == false, "object pool rejects invalid values");
        first.IsValid = false;
        Assert(!pool.TryAcquire("common", out _), "object pool discards invalid idle values");
    
        var disposable = new PoolValue("dispose");
        Assert(pool.Release("attack", disposable), "object pool keeps keys isolated");
        var disposed = new List<string>();
        pool.Clear(value => disposed.Add(value.Name));
        Assert(disposed.SequenceEqual(new[] { "dispose" }) && pool.Count("attack") == 0, "object pool clear disposes idle values and removes buckets");
    }
    
    public static void TestModeContracts()
    {
        Assert(AuraModePolicyEvaluator.EvaluateStarterDeckMutation(null, "Tool").Allowed,
            "mode policy inherits host behavior when no semantic mode is active");
    
        var snapshot = new AuraActiveModeSnapshot
        {
            Status = AuraModeStates.Active,
            ModeId = "Content:challenge",
            OwnerModId = "Content",
            ResolvedPolicies = new AuraModePolicies
            {
                StarterDeck = new AuraModeStarterDeckPolicy
                {
                    MutationAuthority = AuraModeStarterDeckAuthorities.ModeOwnerExclusive,
                    ProviderId = "Content"
                }
            }
        };
        Assert(AuraModePolicyEvaluator.EvaluateStarterDeckMutation(snapshot, "Content").Allowed
               && !AuraModePolicyEvaluator.EvaluateStarterDeckMutation(snapshot, "Tool").Allowed,
            "mode-owner-exclusive starter deck policy is evaluated without content semantics");
    
        snapshot.ResolvedPolicies.StarterDeck.MutationAuthority = AuraModeStarterDeckAuthorities.OfficialOnly;
        Assert(!AuraModePolicyEvaluator.EvaluateStarterDeckMutation(snapshot, "Content").Allowed,
            "official-only starter deck policy rejects every external provider");
    
        Assert(AuraModeOutcomeRuntime.Publish(new AuraModeOutcomeSnapshot
            {
                OwnerModId = "Content",
                ModeId = "Content:challenge",
                RunId = "run-a",
                OutcomeId = "outcome-a",
                Status = AuraModeOutcomeStates.Completed,
                Source = "authoritative settlement"
            }),
            "mode outcome accepts a complete authoritative handoff");
        Assert(AuraModeOutcomeRuntime.TryReadRecent(
                   "Content:challenge",
                   "run-a",
                   TimeSpan.FromSeconds(30),
                   out var completedOutcome)
               && completedOutcome.IsCompleted
               && completedOutcome.Sequence > 0,
            "mode outcome resolves a matching recent run");
        Assert(!AuraModeOutcomeRuntime.TryReadRecent(
                "Content:challenge",
                "run-b",
                TimeSpan.FromSeconds(30),
                out _),
            "mode outcome rejects a different run id");
        Assert(AuraModeOutcomeRuntime.Clear("Content", "Content:challenge", "run-a")
               && !AuraModeOutcomeRuntime.TryReadRecent(
                   "Content:challenge",
                   "run-a",
                   TimeSpan.FromSeconds(30),
                   out _),
            "mode outcome conditional clear removes the handoff");
    }
    
    public static void TestDirectorContracts()
    {
        var request = DirectorRequest(2);
        var first = AuraDirectorPlanCompiler.Compile(request);
        var second = AuraDirectorPlanCompiler.Compile(DirectorRequest(2));
        Assert(first.Success && first.Descriptor != null && first.Cues.Count == 8, "director compiles four cues per actor");
        Assert(first.Descriptor!.Actors.Select(actor => actor.ActorKey).SequenceEqual(new[] { "player-a", "e0" }),
            "director side strategy groups friendly actors before hostile actors");
        var portraits = first.Cues.Where(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide).ToArray();
        Assert(Math.Abs(portraits[0].StartSeconds - AuraDirectorPlanCompiler.OpeningDelaySeconds) < 0.001d
               && Math.Abs(first.Descriptor.DurationSeconds - 2.8d) < 0.001d,
            "director side strategy delays the opening by 0.3 seconds and includes it in plan duration");
        Assert(portraits[0].Direction == AuraDirectorDirection.RightToLeft
               && Math.Abs(portraits[0].StartXRatio - 1.15d) < 0.001d
               && Math.Abs(portraits[0].FocusXRatio - 1d / 3d) < 0.001d
               && Math.Abs(portraits[0].EndXRatio + 0.15d) < 0.001d,
            "director sends friendly portraits from screen right through the left third");
        Assert(portraits[1].Direction == AuraDirectorDirection.LeftToRight
               && Math.Abs(portraits[1].StartXRatio + 0.15d) < 0.001d
               && Math.Abs(portraits[1].FocusXRatio - 2d / 3d) < 0.001d
               && Math.Abs(portraits[1].EndXRatio - 1.15d) < 0.001d,
            "director mirrors hostile portraits through the right third");
        var enemyCast = AuraDirectorPlanCompiler.Compile(DirectorRequest(4));
        Assert(enemyCast.Success
               && enemyCast.Cues
                   .Where(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide)
                   .Skip(1)
                   .All(cue => cue.Direction == AuraDirectorDirection.LeftToRight
                               && Math.Abs(cue.FocusXRatio - 2d / 3d) < 0.001d),
            "director gives every hostile actor the same mirrored route");
        var mixedCast = DirectorRequest(4);
        mixedCast.Actors[2].Side = AuraDirectorActorSide.Friendly;
        var groupedMixedCast = AuraDirectorPlanCompiler.Compile(mixedCast);
        Assert(groupedMixedCast.Success
               && groupedMixedCast.Descriptor!.Actors.Select(actor => actor.ActorKey)
                   .SequenceEqual(new[] { "player-a", "e1", "e0", "e2" }),
            "director preserves source order within stable friendly and hostile groups");
        Assert(first.Descriptor.PlanHash == second.Descriptor!.PlanHash,
            "director plan hash is deterministic");
        Assert(first.Envelope != null
               && first.Envelope.ContractId == AuraDirectorProtocol.ContractId
               && first.Envelope.SchemaVersion == AuraDirectorProtocol.CurrentSchemaVersion
               && first.Envelope.Cues.Count == first.Cues.Count,
            "director emits a self-contained versioned plan envelope");
    
        var legacy = DirectorRequest(2);
        legacy.SchemaVersion = AuraDirectorProtocol.MinimumSupportedSchemaVersion;
        legacy.MinimumReaderSchemaVersion = AuraDirectorProtocol.MinimumSupportedSchemaVersion;
        Assert(AuraDirectorPlanCompiler.Compile(legacy).Success,
            "director accepts the supported legacy schema");
    
        var future = DirectorRequest(2);
        future.SchemaVersion = AuraDirectorProtocol.CurrentSchemaVersion + 1;
        Assert(AuraDirectorPlanCompiler.Compile(future).RejectionCode == "schema-version-unsupported",
            "director rejects future schemas it cannot interpret");
    
        var extensionA = DirectorRequest(2);
        extensionA.Extensions["z"] = "last";
        extensionA.Extensions["a"] = "first";
        var extensionB = DirectorRequest(2);
        extensionB.Extensions["a"] = "first";
        extensionB.Extensions["z"] = "last";
        Assert(AuraDirectorPlanCompiler.Compile(extensionA).Descriptor!.PlanHash
               == AuraDirectorPlanCompiler.Compile(extensionB).Descriptor!.PlanHash,
            "director hashes bounded extensions in deterministic key order");
    
        var oversizedExtensions = DirectorRequest(2);
        for (var i = 0; i <= AuraDirectorPlanCompiler.MaximumExtensionCount; i++)
        {
            oversizedExtensions.Extensions["key-" + i] = "value";
        }
        Assert(AuraDirectorPlanCompiler.Compile(oversizedExtensions).RejectionCode == "extensions-invalid",
            "director rejects oversized extension maps");
    
        var reversedSides = DirectorRequest(2);
        reversedSides.Actors.Reverse();
        var regrouped = AuraDirectorPlanCompiler.Compile(reversedSides);
        Assert(regrouped.Success
               && regrouped.Descriptor!.Actors.Select(actor => actor.ActorKey).SequenceEqual(new[] { "player-a", "e0" })
               && regrouped.Descriptor.PlanHash == first.Descriptor.PlanHash,
            "director side grouping canonicalizes cross-side caller order");
    
        var originalEnemyOrder = AuraDirectorPlanCompiler.Compile(DirectorRequest(3));
        var changedEnemyOrder = DirectorRequest(3);
        (changedEnemyOrder.Actors[1], changedEnemyOrder.Actors[2]) =
            (changedEnemyOrder.Actors[2], changedEnemyOrder.Actors[1]);
        var changed = AuraDirectorPlanCompiler.Compile(changedEnemyOrder);
        Assert(changed.Success && changed.Descriptor!.PlanHash != originalEnemyOrder.Descriptor!.PlanHash,
            "director preserves and hashes caller order within one side");
    
        var alternating = DirectorRequest(2);
        alternating.Actors.Reverse();
        alternating.Strategy = new AuraDirectorStrategyRef
        {
            StrategyId = AuraDirectorPlanCompiler.AlternatingPortraitStrategyId,
            StrategyVersion = AuraDirectorPlanCompiler.AlternatingPortraitStrategyVersion,
            ProfileId = AuraDirectorPlanCompiler.DefaultOpeningProfileId
        };
        var alternatingPlan = AuraDirectorPlanCompiler.Compile(alternating);
        var alternatingPortraits = alternatingPlan.Cues
            .Where(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide)
            .ToArray();
        Assert(alternatingPlan.Success
               && alternatingPlan.Descriptor!.Actors.Select(actor => actor.ActorKey).SequenceEqual(new[] { "e0", "player-a" })
               && alternatingPortraits[0].StartSeconds == 0d
               && alternatingPortraits[0].Direction == AuraDirectorDirection.RightToLeft
               && alternatingPortraits[1].Direction == AuraDirectorDirection.LeftToRight
               && alternatingPortraits.All(cue => Math.Abs(cue.FocusXRatio - 0.5d) < 0.001d),
            "director retains the explicit alternating portrait v1 strategy");
    
        var compact = AuraDirectorPlanCompiler.Compile(DirectorRequest(9));
        var compactPortrait = compact.Cues.First(cue => cue.CueKind == AuraDirectorCueKind.PortraitSlide);
        Assert(compact.Success && compactPortrait.EnterSeconds == 0.25d && compactPortrait.HoldSeconds == 0.15d,
            "director uses compact timing beyond eight actors");
    
        var duplicate = DirectorRequest(2);
        duplicate.Actors[1].ActorKey = duplicate.Actors[0].ActorKey;
        Assert(AuraDirectorPlanCompiler.Compile(duplicate).RejectionCode == "actor-key-duplicate",
            "director rejects duplicate battle actor identities");
    
        var overLimit = DirectorRequest(AuraDirectorPlanCompiler.MaximumActorCount + 1);
        Assert(AuraDirectorPlanCompiler.Compile(overLimit).RejectionCode == "actors-over-limit",
            "director fails open instead of truncating oversized casts");
    
        var state = new AuraDirectorSessionStateMachine();
        Assert(state.TryAdvance(AuraDirectorSessionState.Preparing)
               && !state.TryAdvance(AuraDirectorSessionState.Playing)
               && state.TryBeginRelease("test-abort")
               && !state.TryBeginRelease("duplicate")
               && state.TryMarkReleased()
               && state.IsReleased
               && state.ReleaseReason == "test-abort",
            "director session release is ordered and idempotent");
        Assert(typeof(IAuraDirectorNativeStartHold).GetProperty(nameof(IAuraDirectorNativeStartHold.NativeTarget)) != null
               && typeof(IAuraDirectorNativeStartHoldSink).GetMethod(nameof(IAuraDirectorNativeStartHoldSink.TryAccept)) != null,
            "director exposes a backend-independent native start hold contract");
        Assert(typeof(IAuraDirectorStartGateProvider).GetMethod(nameof(IAuraDirectorStartGateProvider.Install)) != null
               && typeof(IAuraDirectorRequestSource).GetMethod(nameof(IAuraDirectorRequestSource.BuildRequest)) != null,
            "director exposes provider and local request-source contracts");
    
        var layout = AuraDirectorPortraitLayout.Calculate(
            1080d,
            0.13d,
            -0.75d,
            -1.5d,
            0.75d,
            1.5d);
        Assert(Math.Abs(layout.BarHeight - 140.4d) < 0.001d
               && Math.Abs(layout.DisplayHeight - 779.2d) < 0.001d
               && Math.Abs((1080d - layout.BarHeight * 2d - layout.DisplayHeight) * 0.5d
                           - AuraDirectorPortraitLayout.VerticalInsetPixels) < 0.001d,
            "director portrait visible bounds keep ten pixels from expanded letterbox edges");
    
        var shifted = AuraDirectorPortraitLayout.Calculate(
            1080d,
            0.13d,
            -0.2d,
            -0.8d,
            1.8d,
            1.2d);
        Assert(Math.Abs(shifted.SourceCenterX - 0.8d) < 0.001d
               && Math.Abs(shifted.SourceCenterY - 0.2d) < 0.001d
               && Math.Abs(shifted.DisplayHeight - layout.DisplayHeight) < 0.001d,
            "director portrait layout recenters asymmetric sprite mesh bounds");
    
        var rightOutside = AuraDirectorPortraitLayout.ResolveAnchoredX(1.15d, 1920d, 2200d);
        var leftOutside = AuraDirectorPortraitLayout.ResolveAnchoredX(-0.15d, 1920d, 2200d);
        Assert(rightOutside >= 2070d && leftOutside <= -2070d,
            "director keeps height-priority wide portraits fully outside before and after slides");
    }
    
    public static AuraDirectorRequest DirectorRequest(int actorCount)
    {
        var request = new AuraDirectorRequest
        {
            ContractId = AuraDirectorProtocol.ContractId,
            SchemaVersion = AuraDirectorProtocol.CurrentSchemaVersion,
            MinimumReaderSchemaVersion = AuraDirectorProtocol.MinimumSupportedSchemaVersion,
            OwnerModId = "Tests",
            RequestId = "opening",
            BattleSessionId = 7
        };
        for (var i = 0; i < actorCount; i++)
        {
            var player = i == 0;
            request.Actors.Add(new AuraDirectorActorRef
            {
                ActorKey = player ? "player-a" : "e" + (i - 1),
                ActorKind = player ? AuraDirectorActorKind.Player : AuraDirectorActorKind.Enemy,
                Side = player ? AuraDirectorActorSide.Friendly : AuraDirectorActorSide.Hostile,
                OwnerPlayerId = player ? "player-a" : "",
                ContentOwnerModId = "Tests",
                ContentId = player ? "role-a" : "enemy-" + (i - 1),
                Resource = new AuraDirectorResourceRef
                {
                    ProviderId = "aura.cg",
                    OwnerModId = "Tests",
                    ResourceId = player ? "role-a-portrait" : "enemy-" + (i - 1) + "-portrait"
                }
            });
        }
        return request;
    }
    
    public static AuraSharedInstallRequest Request(string owner, string system, string id, string package, long version, string source, string destination)
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
    
    public static void TestAuthoritativeSyncContracts()
    {
        var domain = AuraAuthoritativeSyncRuntime.RegisterDomain(new AuraAuthoritativeSyncDomainOptions
        {
            OwnerModId = "Tests",
            DomainId = "sender-scoped-" + Guid.NewGuid().ToString("N"),
            SnapshotRequestThrottleSeconds = 0.05d,
            MaxResolvedTokens = 16
        });
    
        Assert(domain.TryClaimToken("player-a", 17), "first sender token claim");
        Assert(domain.TryClaimToken("player-b", 17), "same token from another sender must not collide");
        Assert(!domain.TryClaimToken("player-a", 17), "same sender token replay must be rejected");
    
        Assert(domain.TryBeginSnapshotRequest(), "first snapshot request is accepted");
        Assert(!domain.TryBeginSnapshotRequest(), "snapshot request is throttled inside the configured window");
        Assert(domain.AcceptRemoteSnapshotSession(4), "first remote session is accepted");
        var sessionBeforeReset = domain.CurrentSession;
        domain.ResetSession();
        Assert(domain.CurrentSession > sessionBeforeReset,
            "sync lifecycle reset advances the local session");
        Assert(domain.TryClaimToken("player-a", 17),
            "sync lifecycle reset releases sender-scoped replay claims");
        Assert(domain.TryBeginSnapshotRequest(),
            "sync lifecycle reset releases snapshot request throttling");
        Assert(!domain.AcceptRemoteSnapshotSession(4)
               && domain.AcceptRemoteSnapshotSession(5),
            "sync lifecycle reset requires a fresh remote session");
    
        for (var token = 1; token <= domain.MaxResolvedTokens + 1; token++)
        {
            Assert(domain.TryClaimToken("bounded-sender", token),
                "bounded sender token claim " + token);
        }
        Assert(domain.TryClaimToken("bounded-sender", 1),
            "bounded replay ledger evicts its oldest sender token");
    
        Assert(AuraSharedPayloadBudget.TryMeasureUtf8Json(new { text = "payload" }, out var bytes, out _)
               && bytes > 0,
            "payload budget measures serialized UTF-8 bytes");
        Assert(!AuraSharedPayloadBudget.FitsSoftLimit(new { text = new string('x', 512) }, 32, out _, out _),
            "payload budget rejects oversized serialized payloads");
    }
}
