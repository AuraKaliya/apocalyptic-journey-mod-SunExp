using AuraCg.Shared;

var assertions = 0;

var entry = new AuraCgRegistryEntry
{
    OwnerModId = "Terrias",
    CgId = "solar-prayer",
    Kind = "skill",
    TargetRoleIds = new List<string> { "wuna" },
    CardIds = new List<string> { "*sun_card" },
    Priority = 42,
    Media = new AuraCgMediaSpec
    {
        Type = SkillCgMediaTypes.Sequence,
        Resource = "cg/sequence",
        FallbackImage = "cg/fallback.png",
        BundlePath = "visual.bundle",
        BundleAssetPrefix = "cg/frames",
        FrameSeconds = 0.09f,
        AlphaMode = "blackKey",
        KeyThreshold = 0.03f,
        KeySoftness = 0.08f,
        FlashAtSeconds = 0.4f,
        FlashDuration = 0.2f,
        FlashMode = "screen",
        FlashStartFrame = 2,
        FlashEndFrame = 5,
        FlashPulseEveryFrames = 2,
        FlashStrength = 0.8f
    },
    DefaultPresentation = new AuraCgPresentationSpec
    {
        FadeIn = 0.2f,
        Hold = 1.1f,
        FadeOut = 0.3f,
        Mode = "slide",
        Fit = "cover",
        FocusX = 0.4f,
        FocusY = 0.6f,
        SafeScale = 1.2f
    }
};

Assert(AuraCgRegistryQueryService.IsRegisteredEntry(entry, "skill"), "registered sequence entry");
Assert(!AuraCgRegistryQueryService.IsRegisteredEntry(entry, "cardUse"), "kind mismatch rejected");
entry.Enabled = false;
Assert(!AuraCgRegistryQueryService.IsRegisteredEntry(entry, "skill"), "disabled entry rejected");
entry.Enabled = true;
entry.Media.Type = "video";
Assert(!AuraCgRegistryQueryService.IsRegisteredEntry(entry, "skill"), "unsupported media rejected");
entry.Media.Type = SkillCgMediaTypes.Sequence;

Assert(AuraCgRegistryQueryService.MatchesRole(entry, ""), "empty role keeps existing wildcard behavior");
Assert(AuraCgTargetMatcher.MatchesRole(entry, "WUNA"), "role match ignores case");
Assert(AuraCgTargetMatcher.MatchesRole(entry, "Terrias_wuna_wuna"), "owner-scoped short role id matches full runtime role id");
entry.TargetRoleIds = new List<string> { "Terrias_wuna_wuna", "wuna" };
Assert(AuraCgTargetMatcher.MatchesRole(entry, "Terrias_wuna_wuna"),
    "canonical and short aliases may both resolve to one runtime role");
entry.TargetRoleIds = new List<string> { "wuna" };
Assert(!AuraCgTargetMatcher.MatchesRole(entry, "loneer"), "other role rejected");
entry.TargetRoleIds = new List<string> { "*" };
Assert(AuraCgTargetMatcher.MatchesRole(entry, "loneer"), "role wildcard accepted");
entry.TargetRoleIds = new List<string> { "wuna" };

Assert(AuraCgRegistryQueryService.MatchesCard(entry, "sun_card"), "leading-star card identity matches");
Assert(AuraCgRegistryQueryService.MatchesCard(entry, "*sun_card"), "exact decorated card identity matches");
entry.CardIds = new List<string> { "careercard_*8" };
Assert(AuraCgRegistryQueryService.MatchesCard(entry, "careercard_8"), "internal table marker card identity matches");
entry.CardIds = new List<string> { "solar_prayer" };
Assert(AuraCgRegistryQueryService.MatchesCard(entry, "Terrias_solar_prayer"), "owner-scoped short card identity matches");
entry.CardIds = new List<string> { "*sun_card" };

var selectionCandidates = new[] { "first", "second", "third" };
Assert(AuraCgCandidateSelector.Select(selectionCandidates, AuraCgSelectionModes.Priority, "role", 99) == "first",
    "priority selection uses the catalog order");
Assert(AuraCgCandidateSelector.Select(selectionCandidates, AuraCgSelectionModes.Sequential, "role", 1) == "first"
       && AuraCgCandidateSelector.Select(selectionCandidates, AuraCgSelectionModes.Sequential, "role", 2) == "second"
       && AuraCgCandidateSelector.Select(selectionCandidates, AuraCgSelectionModes.Sequential, "role", 4) == "first",
    "sequential selection advances and wraps through every enabled candidate");
var randomSelection = AuraCgCandidateSelector.Select(selectionCandidates, AuraCgSelectionModes.Random, "role", 12);
Assert(randomSelection != null
       && randomSelection == AuraCgCandidateSelector.Select(selectionCandidates, AuraCgSelectionModes.Random, "role", 12),
    "random selection is stable for the same role action sequence");
Assert(!AuraCgRegistryQueryService.MatchesCard(entry, "other"), "other card rejected");
entry.CardIds = new List<string> { "*" };
Assert(AuraCgRegistryQueryService.MatchesCard(entry, "other"), "card wildcard accepted");
entry.CardIds = new List<string> { "*sun_card" };

var context = new SkillCgTriggerContext
{
    Action = "future-action",
    CardId = "sun_card",
    OwnerRoleId = "wuna",
    OwnerInstanceId = "status-1",
    ActionSequence = 17,
    EventToken = "event-17"
};
Assert(AuraCgRegistryQueryService.MatchesTrigger(entry, "skill", context, consumerCanPlay: true), "matching trigger accepted");
Assert(!AuraCgRegistryQueryService.MatchesTrigger(entry, "skill", context, consumerCanPlay: false), "consumer activation enforced");
Assert(AuraCgRegistryQueryService.MatchesAction("future-action"), "action remains forward-compatible");
Assert(AuraCgRegistryQueryService.ResolveImageResource(entry) == "cg/sequence", "primary resource selected");
entry.Media.Resource = "";
Assert(AuraCgRegistryQueryService.ResolveImageResource(entry) == "cg/fallback.png", "fallback resource selected");
entry.Media.Resource = "cg/sequence";

var request = AuraCgRegistryQueryService.CreateRequest(entry, "cg/sequence", "D:/cg/sequence", context, disableSync: true, createdAt: 12.5f);
Assert(request.ProviderId == "Terrias.SkillCG.solar-prayer" && request.OwnerModId == "Terrias", "request provider identity");
Assert(request.CardId == "sun_card" && request.OwnerInstanceId == "status-1", "request trigger identity");
Assert(request.MediaType == SkillCgMediaTypes.Sequence && request.BundlePath == "visual.bundle", "request media contract");
Assert(request.Priority == 42 && request.FitMode == "cover", "request priority and presentation");
Assert(request.CreatedAt == 12.5f && request.DisableSync, "request clock injection and sync policy");

var providerCoordinator = new AuraCgProviderCoordinator((source, providerId, ownerModId, priority, trigger) =>
{
    var shape = source as TestProviderRequest;
    return shape == null
        ? null
        : new SkillCgRequest
        {
            ProviderId = providerId,
            OwnerModId = ownerModId,
            CardId = trigger.CardId,
            ActionSequence = shape.ActionSequence,
            Priority = priority,
            ImagePath = shape.ImagePath
        };
});
Assert(providerCoordinator.Register(null).Status == AuraCgProviderRegistrationStatus.NullProvider, "provider coordinator rejects null registration");
Assert(providerCoordinator.Register(new EmptyIdCgProvider()).Status == AuraCgProviderRegistrationStatus.EmptyProviderId, "provider coordinator rejects empty provider ids");
Assert(providerCoordinator.Register(new TestCgProvider("shared", "OwnerA", 2, 7, "low.png")).Status == AuraCgProviderRegistrationStatus.Registered, "provider coordinator registers reflected providers");
Assert(providerCoordinator.Register(new TestCgProvider("shared", "OwnerA", 4, 7, "replacement.png")).Status == AuraCgProviderRegistrationStatus.Registered
       && providerCoordinator.ProviderCount == 1,
    "provider registration replaces the same owner-qualified identity");
Assert(providerCoordinator.Register(new TestCgProvider("high", "OwnerB", 9, 7, "high.png")).Status == AuraCgProviderRegistrationStatus.Registered, "provider coordinator accepts a second identity");
Assert(providerCoordinator.Register(new TestCgProvider("earlier", "OwnerC", 1, 2, "earlier.png")).Status == AuraCgProviderRegistrationStatus.Registered, "provider coordinator accepts earlier action providers");
var providerFailures = new List<AuraCgProviderBuildFailure>();
providerCoordinator.Register(new ThrowingCgProvider());
var providerRequests = providerCoordinator.BuildRequests(context, providerFailures.Add);
Assert(providerRequests.Count == 3
       && providerRequests[0].ImagePath == "earlier.png"
       && providerRequests[1].ImagePath == "high.png"
       && providerRequests[2].ImagePath == "replacement.png",
    "provider coordinator owns deterministic action-priority ordering and duplicate replacement");
Assert(providerFailures.Count == 1 && providerFailures[0].ProviderId == "throwing", "provider reflection failures are isolated and reported");

var resolverEnabled = false;
var registeredResolver = new AuraCgRegisteredRequestResolver(
    owner => string.Equals(owner, entry.OwnerModId, StringComparison.OrdinalIgnoreCase)
        ? new List<AuraCgRegistryEntry> { entry }
        : Array.Empty<AuraCgRegistryEntry>(),
    _ => resolverEnabled,
    (owner, resource) => "D:/shared/" + owner + "/" + resource,
    () => 19.25f,
    null,
    "skill",
    "cardUse",
    160);
var registeredEvent = new SkillCgNetworkEvent
{
    OwnerModId = entry.OwnerModId,
    CgId = entry.CgId,
    ProviderId = entry.OwnerModId + ".SkillCG." + entry.CgId,
    CardId = "sun_card",
    OwnerInstanceId = "status-network",
    ActionSequence = 33,
    EventToken = "event-network",
    IssuerPlayerId = "player-1",
    SkillCgPlayId = "play-1"
};
var hostResolved = registeredResolver.ResolveNetworkRequest(registeredEvent, requireLocalActivation: false);
Assert(hostResolved != null
       && hostResolved.CreatedAt == 19.25f
       && hostResolved.IssuerPlayerId == "player-1"
       && hostResolved.SkillCgPlayId == "play-1",
    "registered resolver validates host identity without applying recipient-local activation");
Assert(registeredResolver.ResolveNetworkRequest(registeredEvent, requireLocalActivation: true) == null, "registered resolver enforces recipient-local activation");
resolverEnabled = true;
Assert(registeredResolver.ResolveNetworkRequest(registeredEvent, requireLocalActivation: true) != null, "registered resolver admits locally enabled network playback");
registeredEvent.ProviderId = "Other.SkillCG." + entry.CgId;
Assert(registeredResolver.ResolveNetworkRequest(registeredEvent, requireLocalActivation: false) == null, "registered resolver rejects provider identity substitution");
registeredEvent.ProviderId = entry.OwnerModId + ".SkillCG." + entry.CgId;
Assert(AuraCgRegisteredRequestResolver.MediaExists(entry.Media.Type, "missing", entry.Media.BundlePath), "registered bundled media resolves without exposing transport paths");

var slideSize = AuraCgPresentationMath.CalculateSlideImageSize(200f, 100f, 1000f, 1000f);
Assert(Near(slideSize.X, 1700f) && Near(slideSize.Y, 850f), "slide layout preserves aspect at the configured viewport height ratio");
var landscapeCover = AuraCgPresentationMath.CalculateCoverImageSize(1600f, 900f, 1200f, 900f, 1f);
Assert(Near(landscapeCover.X, 1600f) && Near(landscapeCover.Y, 900f), "landscape cover fills viewport height");
var portraitCover = AuraCgPresentationMath.CalculateCoverImageSize(500f, 1000f, 1200f, 900f, 1f);
Assert(Near(portraitCover.X, 1200f) && Near(portraitCover.Y, 2400f), "portrait cover fills viewport width");
var coverOffset = AuraCgPresentationMath.CalculateCoverImageOffset(1600f, 1200f, 1200f, 900f, 0f, 1f);
Assert(Near(coverOffset.X, 200f) && Near(coverOffset.Y, 150f), "cover focus maps to bounded overflow offset");
Assert(Near(AuraCgPresentationMath.EvaluateSlideXRatio(0f), 1.18f)
       && Near(AuraCgPresentationMath.EvaluateSlideXRatio(0.5f), 0.5f)
       && Near(AuraCgPresentationMath.EvaluateSlideXRatio(1f), -0.18f),
    "slide trajectory preserves start center and end anchors");
Assert(Near(AuraCgPresentationMath.EvaluateSlideAlpha(1.05f), 0f)
       && Near(AuraCgPresentationMath.EvaluateSlideAlpha(0.5f), 1f)
       && Near(AuraCgPresentationMath.EvaluateSlideAlpha(-0.05f), 0f),
    "slide alpha preserves edge fades and opaque center");
Assert(Near(AuraCgPresentationMath.ScreenBwPulse(0), 1f)
       && Near(AuraCgPresentationMath.ScreenBwPulse(6), 0.16f)
       && Near(AuraCgPresentationMath.ScreenBwPulse(99), 0.08f),
    "screen black-white pulse keeps its bounded decay table");

var playbackCoordinator = new AuraCgPlaybackCoordinator();
Assert(playbackCoordinator.TryEnqueue(null, 1f, 2, 0.5f, out _) == AuraCgPlaybackEnqueueResult.Invalid, "playback coordinator rejects null requests");
Assert(playbackCoordinator.TryEnqueue(new SkillCgRequest(), 1f, 2, 0.5f, out _) == AuraCgPlaybackEnqueueResult.EmptyMedia, "playback coordinator rejects empty media");
var queuedOldest = PlaybackRequest("oldest", actionSequence: 1, priority: 5, createdAt: 9f);
var queuedSecond = PlaybackRequest("second", actionSequence: 1, priority: 5, createdAt: 9f);
var queuedNewest = PlaybackRequest("newest", actionSequence: 1, priority: 5, createdAt: 9f);
Assert(playbackCoordinator.TryEnqueue(queuedOldest, 10f, 2, 0.5f, out var firstDrop) == AuraCgPlaybackEnqueueResult.Accepted && firstDrop == 0, "first playback request is admitted");
Assert(playbackCoordinator.TryEnqueue(queuedSecond, 10f, 2, 0.5f, out _) == AuraCgPlaybackEnqueueResult.Accepted, "second playback request is admitted");
Assert(playbackCoordinator.TryEnqueue(queuedNewest, 10f, 2, 0.5f, out var boundedDrop) == AuraCgPlaybackEnqueueResult.Accepted
       && boundedDrop == 1
       && playbackCoordinator.QueueCount == 2,
    "playback queue remains bounded and drops the oldest equal-priority request");
Assert(playbackCoordinator.TryEnqueue(queuedNewest, 10.25f, 2, 0.5f, out _) == AuraCgPlaybackEnqueueResult.Duplicate, "playback duplicate window suppresses repeated media");
Assert(playbackCoordinator.TryEnqueue(queuedOldest, 10.75f, 3, 0.5f, out _) == AuraCgPlaybackEnqueueResult.Accepted, "expired playback duplicate keys can be admitted again");
Assert(playbackCoordinator.TryBegin(out var playbackGeneration) && playbackCoordinator.IsPlaying, "playback coordinator claims one active generation");
Assert(!playbackCoordinator.TryBegin(out _), "parallel playback loops are rejected");
Assert(playbackCoordinator.TryTakeNext(playbackGeneration, 10f, 2f, out var firstPlayback, out var staleBeforeFirst)
       && staleBeforeFirst == 0
       && ReferenceEquals(firstPlayback, queuedSecond),
    "playback queue preserves action priority and enqueue order");
Assert(playbackCoordinator.Complete(playbackGeneration) && !playbackCoordinator.IsPlaying, "playback completion releases the active generation");

var staleCoordinator = new AuraCgPlaybackCoordinator();
var staleRequest = PlaybackRequest("stale", actionSequence: 1, priority: 1, createdAt: 1f);
var freshRequest = PlaybackRequest("fresh", actionSequence: 2, priority: 1, createdAt: 9f);
staleCoordinator.TryEnqueue(staleRequest, 9f, 4, 0.5f, out _);
staleCoordinator.TryEnqueue(freshRequest, 9f, 4, 0.5f, out _);
Assert(staleCoordinator.TryBegin(out var staleGeneration), "stale playback batch begins");
Assert(staleCoordinator.TryTakeNext(staleGeneration, 10f, 2f, out var freshPlayback, out var staleSkipped)
       && staleSkipped == 1
       && ReferenceEquals(freshPlayback, freshRequest),
    "stale playback requests are skipped before returning current work");
staleCoordinator.Clear();
Assert(!staleCoordinator.IsCurrent(staleGeneration)
       && !staleCoordinator.Complete(staleGeneration)
       && staleCoordinator.QueueCount == 0
       && staleCoordinator.RecentKeyCount == 0,
    "playback clear invalidates the active generation and all queue-owned state");

const int maxIdentifier = 16;
var networkEvent = new SkillCgNetworkEvent
{
    OwnerModId = "Terrias",
    CgId = "solar",
    ProviderId = "provider",
    CardId = "card",
    OwnerInstanceId = "status"
};
Assert(AuraCgNetworkPolicy.HasValidEventIdentity(networkEvent, maxIdentifier), "bounded event identity");
networkEvent.ProviderId = new string('x', maxIdentifier + 1);
Assert(!AuraCgNetworkPolicy.HasValidEventIdentity(networkEvent, maxIdentifier), "oversized event identity rejected");
networkEvent.ProviderId = "provider";

var playback = new SkillCgPlaybackSnapshot
{
    IssuerPlayerId = " player ",
    SkillCgPlayId = " play ",
    OwnerStatusId = " status ",
    CardId = " card ",
    ActionSequence = 91,
    FightToken = " fight ",
    Events = new List<SkillCgNetworkEvent> { networkEvent }
};
Assert(AuraCgNetworkPolicy.HasValidPlaybackShape(playback, 4, maxIdentifier), "playback envelope shape accepted");
playback.Events.Add(new SkillCgNetworkEvent());
Assert(!AuraCgNetworkPolicy.HasValidPlaybackShape(playback, 1, maxIdentifier), "playback event budget enforced");
playback.Events.RemoveAt(1);
AuraCgNetworkPolicy.NormalizePlaybackSnapshot(playback);
Assert(playback.IssuerPlayerId == "player" && playback.SkillCgPlayId == "play", "playback identity normalized");
Assert(networkEvent.IssuerPlayerId == "player" && networkEvent.OwnerInstanceId == "status", "event authority identity projected");
Assert(networkEvent.EventToken == "play" && networkEvent.ActionSequence == 91, "event sequence projected");
Assert(AuraCgNetworkPolicy.PlaybackKey(" player ", " play ") == "player|play", "playback key normalized");
Assert(AuraCgNetworkPolicy.PlaybackKey("", "play") == "", "incomplete playback key rejected");

var claims = new AuraCgPlaybackClaimStore(2);
Assert(claims.TryClaim("p", "1", out var firstKey) && firstKey == "p|1", "first playback claim");
Assert(claims.TryClaim("p", "2", out _), "second playback claim");
Assert(!claims.TryClaim("p", "2", out _), "duplicate playback rejected");
Assert(claims.TryClaim("p", "3", out _) && claims.Count == 2, "claim store remains bounded");
Assert(claims.TryClaim("p", "1", out _), "oldest playback claim evicted");
claims.Clear();
Assert(claims.Count == 0 && claims.TryClaim("p", "2", out _), "fight cleanup resets claims");

var networkSession = new AuraCgNetworkSessionState(2);
networkSession.SetFightToken(" fight-1 ");
Assert(networkSession.FightToken == "fight-1", "network session normalizes fight token");
Assert(Near(AuraCgNetworkSessionState.NormalizeReuseWindow(0.1f), 0.35f)
       && Near(AuraCgNetworkSessionState.NormalizeReuseWindow(3f), 2f),
    "network action reuse window remains bounded");
var localPlayA = networkSession.ReuseOrCreateLocalPlayId("player a", "owner/1", "*card", 7, "event", 10f, 0.5f);
var localPlayARepeat = networkSession.ReuseOrCreateLocalPlayId("player a", "owner/1", "*card", 7, "event", 10.4f, 0.5f);
Assert(localPlayA == localPlayARepeat, "same local action reuses its play id inside the duplicate window");
Assert(localPlayA.StartsWith("player_a:owner_1:*card:", StringComparison.Ordinal)
       && localPlayA.EndsWith(":fight-1", StringComparison.Ordinal),
    "local play ids sanitize token parts and retain fight identity");
var localPlayANext = networkSession.ReuseOrCreateLocalPlayId("player a", "owner/1", "*card", 7, "event", 11f, 0.5f);
Assert(localPlayANext != localPlayA && networkSession.RecentLocalActionCount == 1, "expired local actions receive a new bounded play id");
Assert(networkSession.TryClaimPlayback("player-a", localPlayANext, out _), "network session accepts first playback claim");
Assert(!networkSession.TryClaimPlayback("player-a", localPlayANext, out _), "network session rejects duplicate playback claim");
networkSession.ResetTransient();
Assert(networkSession.FightToken == "" && networkSession.RecentLocalActionCount == 0, "network session reset clears fight and local action state");
Assert(networkSession.TryClaimPlayback("player-a", localPlayANext, out _), "network session reset releases playback claims");

var adventureHistory = new AuraCgAdventurePreloadHistory(2);
Assert(adventureHistory.TryBegin("adventure-a"), "first adventure preload begins");
Assert(!adventureHistory.TryBegin("adventure-a"), "adventure preload deduplicated");
Assert(adventureHistory.TryBegin("adventure-b") && adventureHistory.TryBegin("adventure-c"), "later adventure keys accepted");
Assert(adventureHistory.Count == 2, "adventure preload history remains bounded");
Assert(adventureHistory.TryBegin("adventure-a"), "oldest adventure key is evicted");

var preloadScheduler = new AuraCgPreloadScheduler<object>(
    maximumPending: 4,
    maximumPendingPerOwner: 2,
    maximumConcurrent: 2);
Assert(preloadScheduler.TryEnqueue("", "owner-a", new object(), alreadyCached: false) == AuraCgPreloadEnqueueResult.Invalid, "invalid preload keys are rejected");
Assert(preloadScheduler.TryEnqueue("cached", "owner-a", new object(), alreadyCached: true) == AuraCgPreloadEnqueueResult.AlreadyCached, "cached media is not queued");
var preloadA1 = new object();
var preloadA2 = new object();
var preloadB1 = new object();
var preloadB2 = new object();
Assert(preloadScheduler.TryEnqueue("a1", "owner-a", preloadA1, alreadyCached: false) == AuraCgPreloadEnqueueResult.Accepted, "first owner preload is queued");
Assert(preloadScheduler.TryEnqueue("a2", "owner-a", preloadA2, alreadyCached: false) == AuraCgPreloadEnqueueResult.Accepted, "second owner preload is queued");
Assert(preloadScheduler.TryEnqueue("a1", "owner-b", new object(), alreadyCached: false) == AuraCgPreloadEnqueueResult.Duplicate, "media identity deduplicates across owners");
Assert(preloadScheduler.TryEnqueue("a3", "owner-a", new object(), alreadyCached: false) == AuraCgPreloadEnqueueResult.CapacityExceeded, "per-owner pending capacity is enforced");
Assert(preloadScheduler.TryEnqueue("b1", "owner-b", preloadB1, alreadyCached: false) == AuraCgPreloadEnqueueResult.Accepted, "second owner can use reserved global capacity");
Assert(preloadScheduler.TryEnqueue("b2", "owner-b", preloadB2, alreadyCached: false) == AuraCgPreloadEnqueueResult.Accepted, "second owner fills its pending share");
Assert(preloadScheduler.TryEnqueue("c1", "owner-c", new object(), alreadyCached: false) == AuraCgPreloadEnqueueResult.CapacityExceeded, "global pending capacity is enforced");
Assert(preloadScheduler.PendingCount == 4 && preloadScheduler.QueuedCount == 4 && preloadScheduler.CapacityRejectedCount == 2, "preload scheduler exposes bounded backlog statistics");
var preloadFirst = preloadScheduler.TakeReady(1);
Assert(preloadFirst.Count == 1 && preloadFirst[0].Key == "a1" && ReferenceEquals(preloadFirst[0].Request, preloadA1), "per-frame start budget launches one preload");
var preloadSecond = preloadScheduler.TakeReady(10);
Assert(preloadSecond.Count == 1 && preloadSecond[0].Key == "b1", "owner rotation gives the next start to another owner");
Assert(preloadScheduler.TakeReady(10).Count == 0 && preloadScheduler.ActiveCount == 2, "global preload concurrency is enforced");
Assert(preloadScheduler.Complete("a1"), "active preload completion releases its claim");
var preloadThird = preloadScheduler.TakeReady(10);
Assert(preloadThird.Count == 1 && preloadThird[0].Key == "a2", "owner rotation resumes the first owner fairly");
Assert(!preloadScheduler.Complete("unknown"), "unknown completion cannot corrupt active counts");
Assert(preloadScheduler.Complete("b1") && preloadScheduler.Complete("a2"), "remaining active preloads complete independently");
var preloadFourth = preloadScheduler.TakeReady(2);
Assert(preloadFourth.Count == 1 && preloadFourth[0].Key == "b2", "queued work starts after concurrency becomes available");
Assert(preloadScheduler.Complete("b2") && preloadScheduler.PendingCount == 0 && preloadScheduler.ActiveCount == 0, "completion clears global and owner pending state");
Assert(preloadScheduler.GetOwnerPendingCount("owner-a") == 0 && preloadScheduler.TryEnqueue("a1", "owner-a", preloadA1, alreadyCached: false) == AuraCgPreloadEnqueueResult.Accepted, "completed preload keys can be retried");

var submissionEnumerated = 0;
var boundedSubmission = AuraCgPreloadSubmission<int>.Capture(CountedPreloadSubmission(), 2);
Assert(boundedSubmission.Items.SequenceEqual(new[] { 0, 1 }), "preload submission retains only the configured prefix");
Assert(boundedSubmission.Truncated, "preload submission reports producer truncation");
Assert(submissionEnumerated == 3, "preload submission probes only one item beyond its hard limit");

var mediaCache = new AuraCgMediaCache<object, object>();
var spriteA = new object();
var spriteB = new object();
mediaCache.StoreSprite("sprite:a", spriteA);
Assert(mediaCache.ContainsSprite("SPRITE:A") && mediaCache.TryGetSprite("sprite:a", out var cachedSprite) && ReferenceEquals(spriteA, cachedSprite), "sprite cache owns case-insensitive identity");
mediaCache.StoreSequence("sequence:a", new List<object> { spriteA, spriteB });
Assert(mediaCache.TryGetSequence("SEQUENCE:A", out var cachedSequence) && cachedSequence.Count == 2, "sequence cache returns canonical list");
mediaCache.StoreSequence("sequence:empty", new List<object>());
Assert(!mediaCache.ContainsSequence("sequence:empty") && mediaCache.SequenceCount == 1, "empty sequences are not retained");
mediaCache.StoreBundle("bundle:missing", null);
Assert(mediaCache.TryGetBundle("BUNDLE:MISSING", out var missingBundle) && missingBundle == null, "bundle cache retains negative lookup sentinel");
var derived = new object();
mediaCache.StoreDerivedSprite(7, derived);
Assert(mediaCache.TryGetDerivedSprite(7, out var cachedDerived) && ReferenceEquals(derived, cachedDerived), "derived sprite cache has explicit owner");
Assert(mediaCache.GetStats().EntryCount == 4 && mediaCache.GetStats().EstimatedBytes == 0L, "unweighted cache statistics remain observable");

var lruReleases = new List<object>();
var lruCache = new AuraCgMediaCache<object, object>(
    maximumEntries: 2,
    maximumEstimatedBytes: 100L,
    onSpriteReleased: (value, _) => lruReleases.Add(value));
var lruA = new object();
var lruB = new object();
var lruC = new object();
lruCache.StoreSprite("a", lruA, 40L, AuraCgMediaOwnership.RuntimeObject);
lruCache.StoreSprite("b", lruB, 40L, AuraCgMediaOwnership.RuntimeObject);
Assert(lruCache.TryGetSprite("a", out _), "cache hit refreshes global recency");
lruCache.StoreSprite("c", lruC, 40L, AuraCgMediaOwnership.RuntimeObject);
Assert(!lruCache.ContainsSprite("b") && lruCache.ContainsSprite("a") && lruCache.ContainsSprite("c"), "entry cap evicts the least recently used media");
Assert(lruReleases.Count == 1 && ReferenceEquals(lruReleases[0], lruB), "LRU eviction releases the orphaned runtime resource");
Assert(lruCache.EstimatedBytes == 80L && lruCache.EntryCount == 2, "LRU statistics track retained bytes and entries");

var sharedReleases = new List<object>();
var sharedReferenceCache = new AuraCgMediaCache<object, object>(
    maximumEntries: 10,
    maximumEstimatedBytes: 100L,
    onSpriteReleased: (value, _) => sharedReleases.Add(value));
var sharedA = new object();
var sharedB = new object();
sharedReferenceCache.StoreSprite("sprite:a", sharedA, 60L, AuraCgMediaOwnership.RuntimeObjectAndTexture);
sharedReferenceCache.StoreSequence(
    "sequence:a",
    new List<object> { sharedA, sharedA },
    _ => 60L,
    AuraCgMediaOwnership.RuntimeObjectAndTexture);
Assert(sharedReferenceCache.EstimatedBytes == 60L, "shared sprite instances are estimated once across cache entries");
Assert(sharedReferenceCache.ContainsSpriteReference(sharedA), "shared sequence references participate in retention ownership");
sharedReferenceCache.StoreSprite("sprite:b", sharedB, 60L, AuraCgMediaOwnership.RuntimeObject);
Assert(!sharedReferenceCache.ContainsSprite("sprite:a") && !sharedReferenceCache.ContainsSequence("sequence:a"), "byte budget keeps evicting until shared references are released");
Assert(sharedReleases.Count == 1 && ReferenceEquals(sharedReleases[0], sharedA), "shared resource releases exactly once after its final cache reference");
Assert(sharedReferenceCache.EstimatedBytes == 60L && sharedReferenceCache.ContainsSprite("sprite:b"), "byte budget retains the newest fitting resource");
sharedReferenceCache.Clear();
Assert(sharedReferenceCache.EntryCount == 0 && sharedReferenceCache.EstimatedBytes == 0L, "cache clear resets the weighted retention ledger");
Assert(sharedReleases.Count == 2 && ReferenceEquals(sharedReleases[1], sharedB), "cache clear releases remaining runtime resources");

var replacementReleases = new List<object>();
var replacementCache = new AuraCgMediaCache<object, object>(
    maximumEntries: 10,
    maximumEstimatedBytes: 500L,
    onSpriteReleased: (value, _) => replacementReleases.Add(value));
var replacementShared = new object();
var replacementOld = new object();
var replacementNew = new object();
replacementCache.StoreSequence("sequence", new List<object> { replacementShared, replacementOld }, _ => 20L, AuraCgMediaOwnership.RuntimeObject);
replacementCache.StoreSequence("sequence", new List<object> { replacementShared, replacementNew }, _ => 20L, AuraCgMediaOwnership.RuntimeObject);
Assert(replacementReleases.Count == 1 && ReferenceEquals(replacementReleases[0], replacementOld), "sequence replacement releases only resources absent from the new entry");
Assert(replacementCache.ContainsSpriteReference(replacementShared) && replacementCache.EstimatedBytes == 40L, "sequence replacement preserves shared instance retention without transient release");

var oversizedReleases = new List<object>();
var oversizedCache = new AuraCgMediaCache<object, object>(
    maximumEntries: 10,
    maximumEstimatedBytes: 50L,
    onSpriteReleased: (value, _) => oversizedReleases.Add(value));
var oversized = new object();
oversizedCache.StoreDerivedSprite(9, oversized, 80L, AuraCgMediaOwnership.RuntimeObjectAndTexture);
Assert(oversizedCache.EntryCount == 0 && oversizedCache.EstimatedBytes == 0L, "single oversized resources are returned to the caller but not retained");
Assert(oversizedReleases.Count == 1 && ReferenceEquals(oversizedReleases[0], oversized), "oversized runtime resource produces a release notification");

var releasedBundles = new List<object>();
var bundleCache = new AuraCgMediaCache<object, object>(
    maximumEntries: 1,
    maximumEstimatedBytes: 100L,
    onBundleReleased: releasedBundles.Add);
var ownedBundle = new object();
bundleCache.StoreBundle("missing", null);
bundleCache.StoreBundle("owned", ownedBundle, 20L);
Assert(releasedBundles.Count == 0 && bundleCache.TryGetBundle("owned", out _), "negative bundle sentinel carries no release ownership");
bundleCache.StoreBundle("newer", new object(), 20L);
Assert(releasedBundles.Count == 1 && ReferenceEquals(releasedBundles[0], ownedBundle), "bundle handles follow the same global LRU");

var releaseQueue = new AuraCgMediaReleaseQueue<object, object>();
var queuedSprite = new object();
var queuedBundle = new object();
releaseQueue.QueueSprite(queuedSprite, AuraCgMediaOwnership.RuntimeObject);
releaseQueue.QueueSprite(queuedSprite, AuraCgMediaOwnership.RuntimeObjectAndTexture);
releaseQueue.QueueBundle(queuedBundle);
Assert(releaseQueue.SpriteCount == 1 && releaseQueue.BundleCount == 1, "deferred release queue deduplicates resource instances");
var flushedSprites = new List<(object Value, AuraCgMediaOwnership Ownership)>();
var flushedBundles = new List<object>();
Assert(!releaseQueue.Flush(false, _ => false, _ => false, (value, ownership) => flushedSprites.Add((value, ownership)), flushedBundles.Add), "active playback or preload keeps release work queued");
Assert(releaseQueue.SpriteCount == 1 && releaseQueue.BundleCount == 1, "blocked release flush preserves pending ownership");
Assert(releaseQueue.Flush(true, value => ReferenceEquals(value, queuedSprite), _ => false, (value, ownership) => flushedSprites.Add((value, ownership)), flushedBundles.Add), "idle release flush completes");
Assert(flushedSprites.Count == 0 && flushedBundles.Count == 1 && ReferenceEquals(flushedBundles[0], queuedBundle), "idle flush skips resources retained again and releases orphaned bundles");
Assert(releaseQueue.SpriteCount == 0 && releaseQueue.BundleCount == 0, "completed release flush clears stale notifications");
releaseQueue.QueueSprite(queuedSprite, AuraCgMediaOwnership.RuntimeObjectAndTexture);
releaseQueue.Flush(true, _ => false, _ => false, (value, ownership) => flushedSprites.Add((value, ownership)), flushedBundles.Add);
Assert(flushedSprites.Count == 1 && flushedSprites[0].Ownership == AuraCgMediaOwnership.RuntimeObjectAndTexture, "later eviction can release a resource whose stale notification was skipped");

var sequenceKey = AuraCgMediaCacheKeys.Sequence(request);
Assert(AuraCgMediaCacheKeys.Preload(request) == "sequence:" + sequenceKey, "sequence preload uses canonical cache key");
Assert(AuraCgMediaCacheKeys.Sprite("cg.png", "black", 0.03f, 0.08f).Contains(SkillCgAlphaModes.BlackKey, StringComparison.Ordinal), "sprite key normalizes alpha aliases");

Assert(AuraCgMediaPathResolver.NormalizeRelativeResourcePath("  \\\\cg\\sequence\\frame.png  ") == "cg/sequence/frame.png", "media resource paths normalize separators and leading roots");
Assert(AuraCgMediaPathResolver.NormalizeBundleId("  bundles\\cg.bundle  ") == "bundles/cg.bundle", "bundle identities share canonical path normalization");
Assert(AuraCgMediaPathResolver.IsSupportedSequenceFrame("frame.PNG")
       && AuraCgMediaPathResolver.IsSupportedSequenceFrame("frame.jpeg")
       && !AuraCgMediaPathResolver.IsSupportedSequenceFrame("frame.webp"),
    "sequence frame extensions preserve the supported image contract");
Assert(AuraCgMediaPathResolver.IsBundleSequenceAsset("assets/cg/sequence/002.png", "cg/sequence"), "bundle sequence prefix matches nested asset roots");
Assert(!AuraCgMediaPathResolver.IsBundleSequenceAsset("assets/cg/other/002.png", "cg/sequence"), "bundle sequence prefix rejects sibling assets");

var flashPolicyRequest = new SkillCgRequest { FlashMode = SkillCgFlashModes.MaskedInvert };
Assert(AuraCgPresentationPolicy.UsesMaskedFlash(flashPolicyRequest), "masked-invert mode enables the masked overlay");
Assert(!AuraCgPresentationPolicy.UsesScreenBwFlash(flashPolicyRequest), "masked-invert mode does not enable the screen pulse overlay");
flashPolicyRequest.FlashMode = SkillCgFlashModes.ScreenBwPulse;
Assert(!AuraCgPresentationPolicy.UsesMaskedFlash(flashPolicyRequest), "screen pulse mode keeps the masked overlay disabled");
Assert(AuraCgPresentationPolicy.UsesScreenBwFlash(flashPolicyRequest), "screen pulse mode enables the screen overlay");
flashPolicyRequest.FlashMode = SkillCgFlashModes.HybridBwPulse;
Assert(AuraCgPresentationPolicy.UsesMaskedFlash(flashPolicyRequest)
       && AuraCgPresentationPolicy.UsesScreenBwFlash(flashPolicyRequest),
    "hybrid mode enables both flash layers");
flashPolicyRequest.FlashMode = SkillCgFlashModes.Screen;
flashPolicyRequest.FlashStartFrame = 3;
Assert(AuraCgPresentationPolicy.UsesMaskedFlash(flashPolicyRequest), "legacy frame ranges continue to imply masked flash");
flashPolicyRequest.FlashStartFrame = 0;
Assert(!AuraCgPresentationPolicy.UsesMaskedFlash(flashPolicyRequest)
       && !AuraCgPresentationPolicy.UsesScreenBwFlash(flashPolicyRequest),
    "ordinary timed flash mode keeps both specialized layers disabled");

var frameDirectory = Path.Combine(Path.GetTempPath(), "aura-cg-path-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(frameDirectory);
try
{
    File.WriteAllText(Path.Combine(frameDirectory, "010.jpg"), "test");
    File.WriteAllText(Path.Combine(frameDirectory, "002.PNG"), "test");
    File.WriteAllText(Path.Combine(frameDirectory, "ignore.txt"), "test");
    var resolvedFrames = AuraCgMediaPathResolver.ResolveSequenceFramePaths(frameDirectory);
    Assert(resolvedFrames.Count == 2
           && Path.GetFileName(resolvedFrames[0]) == "002.PNG"
           && Path.GetFileName(resolvedFrames[1]) == "010.jpg",
        "file sequence discovery filters unsupported files and returns deterministic order");
}
finally
{
    Directory.Delete(frameDirectory, recursive: true);
}

var pendingNow = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc).Ticks;
var pendingStore = new AuraCgPendingPlaybackStore(maximumCount: 2);
var pendingPlayback = new SkillCgPlaybackSnapshot
{
    IssuerPlayerId = "player-1",
    SkillCgPlayId = "play-pending-1",
    Events = new List<SkillCgNetworkEvent> { registeredEvent }
};
Assert(pendingStore.Enqueue(pendingPlayback, "rpc", relayAfterApply: false, pendingNow),
    "unresolved network playback enters the registration wait store");
Assert(!pendingStore.Enqueue(pendingPlayback, "duplicate", relayAfterApply: false, pendingNow),
    "pending network playback identity is deduplicated before claim");
var pendingItem = pendingStore.Snapshot().Single();
Assert(pendingItem.ExpiresAtUtcTicks
       == pendingNow + TimeSpan.TicksPerMillisecond * AuraCgPendingPlaybackStore.DefaultWaitMilliseconds,
    "pending network playback has a bounded registration deadline");
Assert(pendingItem.Source == "rpc" && !pendingItem.RelayAfterApply,
    "pending client playback preserves its completion mode");

var serverPending = new SkillCgPlaybackSnapshot
{
    IssuerPlayerId = "player-2",
    SkillCgPlayId = "play-pending-2",
    Events = new List<SkillCgNetworkEvent> { registeredEvent }
};
Assert(pendingStore.Enqueue(serverPending, "server", relayAfterApply: true, pendingNow),
    "server-bound playback retains authorized relay intent while pending");
Assert(pendingStore.Snapshot().Single(item => item.Playback.SkillCgPlayId == "play-pending-2").RelayAfterApply,
    "recovered server playback will broadcast after local resolution");
pendingStore.Clear();
Assert(pendingStore.Count == 0, "fight lifecycle cleanup clears pending CG playback");

Console.WriteLine($"AuraCgShared tests passed: {assertions} assertions.");

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }

    assertions++;
}

IEnumerable<int> CountedPreloadSubmission()
{
    for (var i = 0; i < 100; i++)
    {
        submissionEnumerated++;
        yield return i;
    }
}

SkillCgRequest PlaybackRequest(string id, long actionSequence, int priority, float createdAt)
{
    return new SkillCgRequest
    {
        ProviderId = "provider-" + id,
        OwnerInstanceId = "owner",
        CardId = "card-" + id,
        ImagePath = "cg/" + id + ".png",
        MediaType = SkillCgMediaTypes.Image,
        ActionSequence = actionSequence,
        Priority = priority,
        CreatedAt = createdAt
    };
}

bool Near(float left, float right, float epsilon = 0.0001f)
{
    return Math.Abs(left - right) <= epsilon;
}

internal sealed class TestProviderRequest
{
    public long ActionSequence { get; init; }

    public string ImagePath { get; init; } = "";
}

internal sealed class TestCgProvider
{
    private readonly TestProviderRequest request;

    public TestCgProvider(string providerId, string ownerModId, int priority, long actionSequence, string imagePath)
    {
        ProviderId = providerId;
        OwnerModId = ownerModId;
        Priority = priority;
        request = new TestProviderRequest { ActionSequence = actionSequence, ImagePath = imagePath };
    }

    public string ProviderId { get; }

    public string OwnerModId { get; }

    public int Priority { get; }

    public IEnumerable<TestProviderRequest> BuildRequests(SkillCgTriggerContext context)
    {
        yield return request;
    }
}

internal sealed class EmptyIdCgProvider
{
    public string ProviderId => "";
}

internal sealed class ThrowingCgProvider
{
    public string ProviderId => "throwing";

    public string OwnerModId => "OwnerFailure";

    public int Priority => 99;

    public IEnumerable<TestProviderRequest> BuildRequests(SkillCgTriggerContext context)
    {
        throw new InvalidOperationException("expected provider failure");
    }
}
