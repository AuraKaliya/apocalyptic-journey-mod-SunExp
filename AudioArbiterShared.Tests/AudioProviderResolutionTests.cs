using AudioArbiter.Shared;
using AuraAudio.Shared;

internal sealed partial class AudioArbiterContractTests
{
    private void VerifyManifestLoader()
    {
        var root = Path.Combine("root", "mod");
        var defaultPath = Path.Combine(root, "audio.registry.json");
        Equal(defaultPath, AudioManifestLoader.ResolveManifestFilePath(root, ""), "default manifest path");
        Equal(Path.Combine(root, "custom.json"), AudioManifestLoader.ResolveManifestFilePath(root, "custom.json"), "custom manifest path");
    
        var missing = AudioManifestLoader.Load(
            root, "OwnerA", "", 2, 6,
            _ => false,
            _ => throw new InvalidOperationException("must not read missing file"),
            _ => throw new InvalidOperationException("must not deserialize missing file"));
        Equal(false, missing.Success, "missing manifest rejected");
        True(missing.Error.Contains("file missing"), "missing manifest reason");
    
        var invalid = AudioManifestLoader.Load(
            root, "OwnerA", "", 2, 6,
            _ => true,
            _ => "invalid",
            _ => null);
        Equal(false, invalid.Success, "invalid manifest rejected");
        True(invalid.Error.Contains("JSON is empty or invalid"), "invalid manifest reason");
    
        var normalizedManifest = new AudioRegistryManifest
        {
            schemaVersion = 0,
            providers = new[] { new AudioProviderManifest { providerId = "voice" } }
        };
        var accepted = AudioManifestLoader.Load(
            root, "OwnerA", "", 2, 6,
            _ => true,
            _ => "{}",
            _ => normalizedManifest);
        Equal(true, accepted.Success, "compatible manifest accepted");
        Equal(1, normalizedManifest.schemaVersion, "legacy schema normalized");
        Equal("OwnerA", accepted.ManifestOwner, "fallback manifest owner");
        Equal(1, accepted.Providers.Length, "manifest providers exposed");
    
        var explicitOwner = AudioManifestLoader.Load(
            root, "OwnerA", "", 2, 6,
            _ => true,
            _ => "{}",
            _ => new AudioRegistryManifest { ownerModId = " OwnerB " });
        Equal("OwnerB", explicitOwner.ManifestOwner, "explicit manifest owner trimmed");
    
        var unsupportedSchema = AudioManifestLoader.Load(
            root, "OwnerA", "", 2, 6,
            _ => true,
            _ => "{}",
            _ => new AudioRegistryManifest { schemaVersion = 3 });
        Equal(false, unsupportedSchema.Success, "future schema rejected");
        True(unsupportedSchema.Error.Contains("unsupported schemaVersion=3"), "schema rejection reason");
    
        var unsupportedProtocol = AudioManifestLoader.Load(
            root, "OwnerA", "", 2, 6,
            _ => true,
            _ => "{}",
            _ => new AudioRegistryManifest
            {
                schemaVersion = 2,
                audioProtocol = new AudioProtocolManifest { minVersion = 7 }
            });
        Equal(false, unsupportedProtocol.Success, "future protocol rejected");
        True(unsupportedProtocol.Error.Contains("protocol minVersion=7"), "protocol rejection reason");
    
        var defaults = new AudioRegistryDefaults
        {
            bus = " Vocal ",
            policy = " Replace ",
            hardClaim = true,
            sync = false,
            cooldownSeconds = 2f,
            gainDb = 3f,
            volumeMultiplier = 0.75f
        };
        var provider = new AudioProviderManifest
        {
            providerId = " voice ",
            path = "Shared:Audio/voice.ogg",
            variantPaths = new[]
            {
                "Shared:Audio/voice-2.ogg",
                "Shared:Audio/voice.ogg",
                ""
            },
            priority = 9,
            policy = " Additive ",
            kind = "CardUse",
            match = new AudioProviderMatch { hpRatioCrossDown = 0.3f },
            suppressOriginal = new AudioSuppressOriginal
            {
                vocalStates = new[] { "Dying" },
                narrationIds = new[] { 12 }
            }
        };
        var plan = AudioManifestLoader.CreateProviderPlan(
            provider,
            defaults,
            "OwnerA",
            root,
            path => "shared/" + path);
        Equal("voice", plan.ProviderId, "planned provider id");
        Equal("OwnerA", plan.OwnerModId, "planned provider owner");
        Equal("shared/Audio/voice.ogg", plan.AudioPath, "shared provider path");
        Equal(1, plan.AudioVariantPaths.Length, "provider variant paths are normalized and deduplicated");
        Equal("shared/Audio/voice-2.ogg", plan.AudioVariantPaths[0], "shared provider variant path");
        Equal(9, plan.Priority, "planned priority");
        Equal("Vocal", plan.Bus, "default bus merged");
        Equal("Additive", plan.Policy, "provider policy overrides default");
        Equal(true, plan.HardClaim, "default hard claim merged");
        Equal(false, plan.Sync, "default sync merged");
        Equal(2f, plan.CooldownSeconds, "default cooldown merged");
        Equal(3f, plan.GainDb, "default gain merged");
        Equal(0.75f, plan.VolumeMultiplier, "default volume merged");
        Equal(0.3f, plan.LowHealthCrossDownThreshold, "low-health threshold planned");
        Equal("Dying", plan.SuppressVocalStates[0], "vocal suppression planned");
        Equal(12, plan.SuppressNarrationIds[0], "narration suppression planned");
    
        Equal("", AudioManifestLoader.ResolveProviderPath(root, "", path => path), "empty provider path");
        Equal(Path.Combine(root, "Audio", "voice.ogg"),
            AudioManifestLoader.ResolveProviderPath(root, "Audio/voice.ogg", path => path),
            "relative provider path");
    }
    
    private void VerifyVariantSelectionPolicy()
    {
        Equal(0, AudioVariantSelectionPolicy.SelectStartIndex("event", "Owner:voice", 0), "empty variant pool selects zero");
        Equal(0, AudioVariantSelectionPolicy.SelectStartIndex("event", "Owner:voice", 1), "single variant pool selects zero");
    
        var first = AudioVariantSelectionPolicy.SelectStartIndex("event-1", "Owner:voice", 3);
        Equal(first, AudioVariantSelectionPolicy.SelectStartIndex("event-1", "Owner:voice", 3), "variant selection is event-stable");
        True(first >= 0 && first < 3, "variant selection stays in range");
    
        var selected = new HashSet<int>();
        for (var i = 0; i < 64; i++)
        {
            selected.Add(AudioVariantSelectionPolicy.SelectStartIndex("event-" + i, "Owner:voice", 3));
        }
    
        Equal(3, selected.Count, "stable event hashing reaches every equal-weight variant");
    }
    
    private void VerifyManifestMatchPolicy()
    {
        var provider = new AudioProviderManifest
        {
            kind = "CardUse",
            vocalState = "Attack",
            match = new AudioProviderMatch
            {
                careerIds = new[] { "career" },
                roleIds = new[] { "role" },
                cardIds = new[] { "card-1" },
                buffIds = new[] { "buff-1" },
                effectNames = new[] { "effect-1" },
                actionNames = new[] { "action-1" },
                battleResults = new[] { "Victory" },
                localOwnerOnly = true,
                hpRatioCrossDown = 0.3f
            }
        };
        var condition = AudioManifestMatchPolicy.BuildCondition(provider);
        var request = CreateRequest();
        request.CareerId = "career_variant";
        request.RoleId = "prefix_role";
        request.VocalState = "Attack";
        request.PreviousHpRatio = 0.5f;
        request.HpRatio = 0.3f;
        Equal(true, condition(request), "complete manifest match");
    
        request.CardId = "other";
        Equal(false, condition(request), "card mismatch rejected");
        request.CardId = "card-1";
        request.IsLocalOwner = false;
        Equal(false, condition(request), "non-owner local request rejected");
        request.IsRemote = true;
        Equal(true, condition(request), "remote request preserves existing local-owner semantics");
        request.IsRemote = false;
        request.IsLocalOwner = true;
        request.PreviousHpRatio = 0.3f;
        Equal(false, condition(request), "non-crossing hp threshold rejected");
    }
    
    private void VerifyProviderIdentityAndOrdering()
    {
        Equal("OwnerA:voice", AudioProviderResolver.QualifyProviderId(" OwnerA ", " voice "), "qualified provider identity");
        Equal("OwnerB:voice", AudioProviderResolver.QualifyProviderId("OwnerA", "OwnerB:voice"), "prequalified provider identity");
        Equal("unknown", AudioProviderResolver.QualifyProviderId("", ""), "unknown provider identity");
    
        Equal(true, AudioProviderResolver.MatchesProviderRequest("voice", "OwnerA", "OwnerA:voice", "voice", "OwnerA", true), "strict owner match");
        Equal(false, AudioProviderResolver.MatchesProviderRequest("voice", "OwnerA", "OwnerA:voice", "voice", "OwnerB", true), "strict owner mismatch");
        Equal(true, AudioProviderResolver.MatchesProviderRequest("voice", "OwnerA", "OwnerA:voice", "voice", "OwnerB", false), "legacy bare-id match");
        Equal(true, AudioProviderResolver.MatchesProviderRequest("voice", "OwnerA", "OwnerA:voice", "OwnerA:voice", "OwnerB", true), "qualified identity match");
    
        True(AudioProviderResolver.CompareProviderOrder(10, "OwnerB:z", 5, "OwnerA:a") < 0, "higher priority sorts first");
        True(AudioProviderResolver.CompareProviderOrder(5, "OwnerA:a", 5, "OwnerB:z") < 0, "qualified id tie-break is stable");
    }
    
    private void VerifyProviderResolution()
    {
        var request = new object();
        var lower = new FakeProvider("voice", "OwnerB", priority: 1, hardClaim: false, loadState: "Ready", resource: new FakeResource("lower"));
        var higher = new FakeProvider("voice", "OwnerA", priority: 10, hardClaim: false, loadState: "Ready", resource: new FakeResource("higher"));
        var providers = new List<FakeProvider> { higher, lower };
    
        var unscoped = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(providers, request, "", "", false);
        Equal(AudioProviderResolutionStatus.Selected, unscoped.Status, "unscoped provider selected");
        Same(higher, unscoped.Provider!, "unscoped selection follows sorted order");
    
        var strict = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(providers, request, "voice", "OwnerB", false);
        Equal(AudioProviderResolutionStatus.Selected, strict.Status, "strict owner provider selected");
        Same(lower, strict.Provider!, "strict owner isolates provider");
        Equal(false, strict.UsedLegacyFallback, "strict owner does not fall back");
    
        var localFallback = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(providers, request, "voice", "Missing", false);
        Equal(true, localFallback.HasSelection, "local owner mismatch keeps legacy fallback");
        Equal(true, localFallback.UsedLegacyFallback, "local owner mismatch reports fallback");
        Same(higher, localFallback.Provider!, "legacy fallback chooses first bare-id provider");
    
        var remoteMismatch = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(providers, request, "voice", "Missing", true);
        Equal(AudioProviderResolutionStatus.IdentityMismatch, remoteMismatch.Status, "remote owner mismatch fails closed");
        Equal(false, remoteMismatch.HasSelection, "remote mismatch has no selection");
        Equal(true, remoteMismatch.ShouldWarnRemoteMismatch, "remote mismatch requests warning");
    
        var qualifiedMismatch = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(providers, request, "Missing:voice", "", false);
        Equal(AudioProviderResolutionStatus.IdentityMismatch, qualifiedMismatch.Status, "qualified mismatch fails closed");
        Equal(false, qualifiedMismatch.UsedLegacyFallback, "qualified mismatch never falls back");
    
        var softNotReady = new FakeProvider("voice", "OwnerA", 10, false, "Loading", null);
        var softResult = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(new[] { softNotReady, lower }, request, "", "", false);
        Same(lower, softResult.Provider!, "soft not-ready provider allows fallback");
    
        var hardNotReady = new FakeProvider("voice", "OwnerA", 10, true, "Loading", null);
        var hardResult = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(new[] { hardNotReady, lower }, request, "", "", false);
        Equal(AudioProviderResolutionStatus.HardClaimBlocked, hardResult.Status, "hard claim blocks not-ready fallback");
        Equal(false, hardResult.HasSelection, "hard claim block has no selection");
    
        var hardNoResource = new FakeProvider("voice", "OwnerA", 10, true, "Ready", null);
        var noResourceResult = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(new[] { hardNoResource, lower }, request, "", "", false);
        Equal(AudioProviderResolutionStatus.HardClaimBlocked, noResourceResult.Status, "hard claim blocks missing-resource fallback");
    
        var filtered = new FakeProvider("voice", "OwnerA", 10, false, "Ready", new FakeResource("filtered")) { Matches = false };
        var filteredResult = AudioProviderResolver.Resolve<FakeProvider, FakeResource>(new[] { filtered, lower }, request, "voice", "OwnerA", false);
        Equal(AudioProviderResolutionStatus.None, filteredResult.Status, "strict matched provider condition can reject");
        Equal(false, filteredResult.UsedLegacyFallback, "condition rejection does not escape strict identity");
    }
}
