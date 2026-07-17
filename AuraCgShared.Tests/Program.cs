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
