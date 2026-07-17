using AuraCg.Shared;

var assertions = 0;

var entry = new AuraCgRegistryEntry
{
    OwnerModId = "SunExp",
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
Assert(AuraCgRegistryQueryService.MatchesRole(entry, "WUNA"), "role match ignores case");
Assert(!AuraCgRegistryQueryService.MatchesRole(entry, "loneer"), "other role rejected");
entry.TargetRoleIds = new List<string> { "*" };
Assert(AuraCgRegistryQueryService.MatchesRole(entry, "loneer"), "role wildcard accepted");
entry.TargetRoleIds = new List<string> { "wuna" };

Assert(AuraCgRegistryQueryService.MatchesCard(entry, "sun_card"), "leading-star card identity matches");
Assert(AuraCgRegistryQueryService.MatchesCard(entry, "*sun_card"), "exact decorated card identity matches");
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
Assert(request.ProviderId == "SunExp.SkillCG.solar-prayer" && request.OwnerModId == "SunExp", "request provider identity");
Assert(request.CardId == "sun_card" && request.OwnerInstanceId == "status-1", "request trigger identity");
Assert(request.MediaType == SkillCgMediaTypes.Sequence && request.BundlePath == "visual.bundle", "request media contract");
Assert(request.Priority == 42 && request.FitMode == "cover", "request priority and presentation");
Assert(request.CreatedAt == 12.5f && request.DisableSync, "request clock injection and sync policy");

const int maxIdentifier = 16;
var networkEvent = new SkillCgNetworkEvent
{
    OwnerModId = "SunExp",
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

var preloadCoordinator = new AuraCgPreloadCoordinator(2);
Assert(preloadCoordinator.TryBeginPreload("image:a", alreadyCached: false), "preload request begins once");
Assert(!preloadCoordinator.TryBeginPreload("image:a", alreadyCached: false), "pending preload deduplicated");
Assert(preloadCoordinator.PendingCount == 1, "pending preload count owned by coordinator");
preloadCoordinator.CompletePreload("image:a");
Assert(preloadCoordinator.PendingCount == 0 && preloadCoordinator.TryBeginPreload("image:a", alreadyCached: false), "completed preload can retry");
preloadCoordinator.CompletePreload("image:a");
Assert(!preloadCoordinator.TryBeginPreload("image:cached", alreadyCached: true), "cached media is not queued");
Assert(preloadCoordinator.TryBeginAdventure("adventure-a"), "first adventure preload begins");
Assert(!preloadCoordinator.TryBeginAdventure("adventure-a"), "adventure preload deduplicated");
Assert(preloadCoordinator.TryBeginAdventure("adventure-b") && preloadCoordinator.TryBeginAdventure("adventure-c"), "later adventure keys accepted");
Assert(preloadCoordinator.AdventureCount == 2, "adventure preload history remains bounded");
Assert(preloadCoordinator.TryBeginAdventure("adventure-a"), "oldest adventure key is evicted");

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

Console.WriteLine($"AuraCgShared tests passed: {assertions} assertions.");

void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + name);
    }

    assertions++;
}
