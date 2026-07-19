using AudioArbiter.Shared;

var tests = new AudioArbiterContractTests();
tests.Run();

internal sealed class AudioArbiterContractTests
{
    private int assertions;

    public void Run()
    {
        VerifyManifestDefaults();
        VerifyConstants();
        VerifyFileLoadPolicy();
        VerifyHookCatalog();
        VerifyRequestFactory();
        VerifyLowHealthCoordinator();
        VerifyPropertyReader();
        VerifyRequestProjection();
        VerifyNetworkProjection();
        VerifyNetworkPolicy();
        VerifyNetworkSessionState();
        VerifyManifestLoader();
        VerifyManifestMatchPolicy();
        VerifyVariantSelectionPolicy();
        VerifyProviderIdentityAndOrdering();
        VerifyProviderResolution();
        VerifyCooldownPolicy();
        VerifyPresentationPolicy();
        VerifySuppressionPolicy();
        VerifyReplacementCoordinator();

        Console.WriteLine($"AudioArbiterShared behavior tests passed: {assertions} assertions.");
    }

    private void VerifyManifestDefaults()
    {
        var manifest = new AudioRegistryManifest();
        Equal(1, manifest.schemaVersion, "registry schema default");
        Equal("", manifest.ownerModId, "registry owner default");
        Null(manifest.audioProtocol, "registry protocol default");
        Null(manifest.defaults, "registry defaults default");
        Null(manifest.providers, "registry providers default");

        var protocol = new AudioProtocolManifest();
        Equal(1, protocol.minVersion, "protocol minimum default");
        Equal(1, protocol.preferredVersion, "protocol preferred default");

        var defaults = new AudioRegistryDefaults();
        Equal("", defaults.bus, "bus default");
        Equal("", defaults.policy, "policy default");
        Null(defaults.hardClaim, "hard claim default");
        Null(defaults.sync, "sync default");
        Null(defaults.cooldownSeconds, "cooldown default");
        Null(defaults.gainDb, "gain default");
        Null(defaults.volumeMultiplier, "volume default");

        var provider = new AudioProviderManifest();
        Equal("", provider.providerId, "provider id default");
        Equal("", provider.ownerModId, "provider owner default");
        Equal("", provider.kind, "provider kind default");
        Equal("", provider.vocalState, "provider vocal default");
        Equal("", provider.path, "provider path default");
        Null(provider.variantPaths, "provider variant paths default");
        Equal(0, provider.priority, "provider priority default");
        Null(provider.match, "provider match default");
        Null(provider.suppressOriginal, "provider suppression default");
    }

    private void VerifyConstants()
    {
        Equal(10000, SoundPlaybackRequest.DefaultPresentationMaxAgeMilliseconds, "presentation max age");
        Equal("CardUse", SoundEventKinds.CardUse, "card-use kind");
        Equal("SkillVoice", SoundEventKinds.SkillVoice, "skill-voice kind");
        Equal("CareerSelected", SoundEventKinds.CareerSelected, "career kind");
        Equal("BuffApplied", SoundEventKinds.BuffApplied, "buff kind");
        Equal("LowHealth", SoundEventKinds.LowHealth, "low-health kind");
        Equal("BattleCompleted", SoundEventKinds.BattleCompleted, "battle-completed kind");
        Equal("VocalState", SoundEventKinds.VocalState, "vocal-state kind");
        Equal("Effect", SoundBuses.Effect, "effect bus");
        Equal("Vocal", SoundBuses.Vocal, "vocal bus");
        Equal("Ui", SoundBuses.Ui, "ui bus");
        Equal("Additive", SoundPolicies.Additive, "additive policy");
        Equal("Replace", SoundPolicies.Replace, "replace policy");
        Equal("ReplaceOriginal", SoundPolicies.ReplaceOriginal, "replace-original policy");
        Equal("SuppressOriginal", SoundPolicies.SuppressOriginal, "suppress-original policy");
    }

    private void VerifyFileLoadPolicy()
    {
        Equal(AudioFileEncoding.Wav, AudioFileLoadPolicy.Classify("voice.WAV"), "wav file classification");
        Equal(AudioFileEncoding.OggVorbis, AudioFileLoadPolicy.Classify("voice.ogg"), "ogg file classification");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify("voice.mp3"), "mp3 file classification");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify("voice.m4a"), "m4a compatibility classification");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify("voice.aac"), "aac compatibility classification");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify("voice.bin"), "unknown extension preserves MPEG fallback");
        Equal(AudioFileEncoding.Mpeg, AudioFileLoadPolicy.Classify(null), "missing extension preserves MPEG fallback");
        Equal(AudioFileEncoding.UnsupportedVideoContainer, AudioFileLoadPolicy.Classify("voice.mp4"), "mp4 video container rejected");
        Equal(AudioFileEncoding.UnsupportedVideoContainer, AudioFileLoadPolicy.Classify("voice.m4v"), "m4v video container rejected");
        Equal(AudioFileEncoding.UnsupportedVideoContainer, AudioFileLoadPolicy.Classify("voice.mov"), "mov video container rejected");
    }

    private void VerifyHookCatalog()
    {
        var hooks = AudioHookCatalog.All;
        Equal(19, hooks.Count, "audio hook catalog count");
        Equal(19, hooks.Select(item => item.HandlerId).Distinct(StringComparer.Ordinal).Count(), "audio hook handler ids are unique");
        Equal(2, hooks.Count(item => item.RegistrationKind == AudioHookRegistrationKind.Before), "audio before hook count");
        Equal(16, hooks.Count(item => item.RegistrationKind == AudioHookRegistrationKind.After), "audio after hook count");
        Equal(1, hooks.Count(item => item.RegistrationKind == AudioHookRegistrationKind.CombatActionBefore), "audio combat router count");
        Equal(2, hooks.Count(item => item.Target == "Fight_Start.Init"), "fight start keeps before and after hooks");
        Equal(6, hooks.Count(item => item.Target.StartsWith("ScriptExecutor.", StringComparison.Ordinal)), "script HP hook count");
        Equal(2, hooks.Count(item => item.Target.StartsWith("StatusManager.set_", StringComparison.Ordinal)), "status HP setter hook count");
        Equal(6, hooks.Count(item => item.CallbackKind == AudioHookCallbackKind.PotentialHpChanged), "script HP hooks share one callback kind");
        Equal(2, hooks.Count(item => item.CallbackKind == AudioHookCallbackKind.StatusHpChanged), "status HP hooks share one callback kind");
        Equal(13, hooks.Select(item => item.CallbackKind).Distinct().Count(), "audio callback kind count");

        var combat = hooks.Single(item => item.HandlerId == "combat-action");
        Equal("FightUI.CallActionAnimation", combat.Target, "combat action hook target");
        Equal(AudioHookRegistrationKind.CombatActionBefore, combat.RegistrationKind, "combat action uses routed before hook");
        Equal(AudioHookCallbackKind.CombatActionBefore, combat.CallbackKind, "combat action callback kind");
        var effect = hooks.Single(item => item.HandlerId == "native-effect");
        Equal("EffectSound.Start", effect.Target, "native effect hook target");
        Equal(AudioHookRegistrationKind.Before, effect.RegistrationKind, "native effect runs before original playback");
        Equal(AudioHookCallbackKind.NativeEffectBefore, effect.CallbackKind, "native effect callback kind");
        var vocal = hooks.Single(item => item.HandlerId == "vocal-state");
        Equal("StatusManager.PlayVocal", vocal.Target, "vocal state hook target");
        Equal(AudioHookRegistrationKind.After, vocal.RegistrationKind, "vocal state is observed after native call");
        Equal(AudioHookCallbackKind.VocalState, vocal.CallbackKind, "vocal state callback kind");
        Equal(true, hooks.Any(item => item.HandlerId == "fight-win" && item.Target == "Fight_Win.ResetStates"), "fight win hook retained");
        Equal(true, hooks.Any(item => item.HandlerId == "fight-escape" && item.Target == "Fight_Escape.ResetStates"), "fight escape hook retained");
        Equal(AudioHookCallbackKind.FightWin, hooks.Single(item => item.HandlerId == "fight-win").CallbackKind, "fight win callback kind");
        Equal(AudioHookCallbackKind.FightEscape, hooks.Single(item => item.HandlerId == "fight-escape").CallbackKind, "fight escape callback kind");
    }

    private void VerifyRequestFactory()
    {
        var career = AudioRequestFactory.CreateCareerSelected(new AudioCareerObservation
        {
            CareerId = "career",
            SourceName = "career-source"
        });
        Equal(SoundEventKinds.CareerSelected, career.Kind, "career request kind");
        Equal("career", career.CareerId, "career request career");
        Equal("career", career.RoleId, "career request role follows career");
        Equal("career-source", career.SourceName, "career request source");
        Equal("", career.EventId, "career request does not invent event id");

        var combat = AudioRequestFactory.CreateCombatActionBatch(new AudioCombatActionObservation
        {
            CardId = "card",
            CareerId = "career",
            RoleId = "role",
            StatusInstanceId = "status",
            EffectName = "effect",
            ActionName = "action",
            SourceName = "combat-source"
        }, "play-id");
        Equal("play-id", combat.CardUse.EventId, "card-use request keeps network play id");
        Equal(SoundEventKinds.CardUse, combat.CardUse.Kind, "card-use request kind");
        Equal("card", combat.CardUse.CardId, "card-use request card");
        Equal("career", combat.CardUse.CareerId, "card-use request career");
        Equal("role", combat.CardUse.RoleId, "card-use request role");
        Equal("status", combat.CardUse.StatusInstanceId, "card-use request status");
        Equal("effect", combat.CardUse.EffectName, "card-use request effect");
        Equal("action", combat.CardUse.ActionName, "card-use request action");
        Equal("combat-source", combat.CardUse.SourceName, "card-use request source");
        Equal("", combat.SkillVoice.EventId, "skill voice does not reuse authoritative card event id");
        Equal(SoundEventKinds.SkillVoice, combat.SkillVoice.Kind, "skill voice request kind");
        Equal("card", combat.SkillVoice.CardId, "skill voice request card");
        Equal("career", combat.SkillVoice.CareerId, "skill voice request career");
        Equal("role", combat.SkillVoice.RoleId, "skill voice request role");
        Equal("status", combat.SkillVoice.StatusInstanceId, "skill voice request status");
        Equal("effect", combat.SkillVoice.EffectName, "skill voice request effect");
        Equal("action", combat.SkillVoice.ActionName, "skill voice request action");
        Equal("combat-source", combat.SkillVoice.SourceName, "skill voice request source");

        var buff = AudioRequestFactory.CreateBuffApplied(new AudioBuffObservation
        {
            BuffId = "buff",
            CareerId = "career",
            StatusInstanceId = "status",
            SourceName = "buff-source"
        });
        Equal(SoundEventKinds.BuffApplied, buff.Kind, "buff request kind");
        Equal("buff", buff.BuffId, "buff request id");
        Equal("career", buff.CareerId, "buff request career");
        Equal("career", buff.RoleId, "buff request role preserves current-career behavior");
        Equal("status", buff.StatusInstanceId, "buff request status");
        Equal("buff-source", buff.SourceName, "buff request source");

        var vocal = AudioRequestFactory.CreateVocalState(new AudioVocalObservation
        {
            VocalState = "Dying",
            CareerId = "career",
            RoleId = "role",
            StatusInstanceId = "status",
            SourceName = "vocal-source"
        });
        Equal(SoundEventKinds.VocalState, vocal.Kind, "vocal request kind");
        Equal("Dying", vocal.VocalState, "vocal request state");
        Equal("career", vocal.CareerId, "vocal request career");
        Equal("role", vocal.RoleId, "vocal request role");
        Equal("status", vocal.StatusInstanceId, "vocal request status");
        Equal("vocal-source", vocal.SourceName, "vocal request source");

        var lowHealth = AudioRequestFactory.CreateLowHealth(new AudioStatusSnapshot
        {
            StatusInstanceId = "status",
            RoleId = "role",
            CareerId = "career",
            Hp = 25,
            MaxHp = 100,
            HpRatio = 0.25f,
            IsLocalOwner = true,
            SourceName = "hp-source"
        }, 0.5f);
        Equal(SoundEventKinds.LowHealth, lowHealth.Kind, "low-health request kind");
        Equal("status", lowHealth.StatusInstanceId, "low-health request status");
        Equal("role", lowHealth.RoleId, "low-health request role");
        Equal("career", lowHealth.CareerId, "low-health request career");
        Equal(25, lowHealth.Hp, "low-health request hp");
        Equal(100, lowHealth.MaxHp, "low-health request max hp");
        Equal(0.5f, lowHealth.PreviousHpRatio, "low-health request previous ratio");
        Equal(0.25f, lowHealth.HpRatio, "low-health request ratio");
        Equal(true, lowHealth.IsLocalOwner, "low-health request local owner");
        Equal("hp-source", lowHealth.SourceName, "low-health request source");
        var lowHealthRoleFallback = AudioRequestFactory.CreateLowHealth(new AudioStatusSnapshot
        {
            CareerId = "career",
            RoleId = ""
        }, 0.5f);
        Equal("career", lowHealthRoleFallback.RoleId, "low-health role falls back to career");

        var battle = AudioRequestFactory.CreateBattleCompleted(new AudioBattleObservation
        {
            Result = "Win",
            CareerId = "career",
            SourceName = "Fight_Win.ResetStates"
        });
        Equal(SoundEventKinds.BattleCompleted, battle.Kind, "battle request kind");
        Equal("Win", battle.BattleResult, "battle request result");
        Equal("career", battle.CareerId, "battle request career");
        Equal("career", battle.RoleId, "battle request role follows career");
        Equal("Fight_Win.ResetStates", battle.SourceName, "battle request source");

        Equal(0, new AudioNarrationObservation().NarrationIds.Length, "narration observation defaults to empty ids");
    }

    private void VerifyLowHealthCoordinator()
    {
        Equal(0.75f, AudioLowHealthCoordinator.DefaultNoProviderCooldownSeconds, "low-health no-provider cooldown default");
        Equal(0.05f, AudioLowHealthCoordinator.DefaultRecoveryMargin, "low-health recovery margin default");
        Equal(0.35f, AudioLowHealthCoordinator.DefaultLegacyFallbackThreshold, "low-health legacy threshold default");

        var empty = new AudioLowHealthCoordinator();
        var seeded = empty.Observe(CreateStatusSnapshot("status", 0.6f));
        Equal(AudioLowHealthObservationOutcome.Seeded, seeded.Outcome, "first low-health observation seeds ratio");
        Equal(0.6f, seeded.PreviousHpRatio, "seed decision reports current ratio");
        Equal(false, seeded.ShouldRequest, "seed does not request playback");
        var unchanged = empty.Observe(CreateStatusSnapshot("status", 0.6f));
        Equal(AudioLowHealthObservationOutcome.Unchanged, unchanged.Outcome, "equal ratio is ignored");
        var noProviderCandidate = empty.Observe(CreateStatusSnapshot("status", 0.4f));
        Equal(AudioLowHealthObservationOutcome.Candidate, noProviderCandidate.Outcome, "decrease becomes a candidate before provider policy");
        var noProviderRequest = AudioRequestFactory.CreateLowHealth(
            CreateStatusSnapshot("status", 0.4f),
            noProviderCandidate.PreviousHpRatio);
        Equal(false, empty.ShouldAttempt(noProviderRequest), "no providers reject low-health attempt");

        var legacy = new AudioLowHealthCoordinator();
        legacy.ConfigureProviders(new[] { new AudioLowHealthProviderDescriptor("", -1f) });
        legacy.Seed(CreateStatusSnapshot("legacy", 0.6f));
        var legacyDecision = legacy.Observe(CreateStatusSnapshot("legacy", 0.35f));
        Equal(true, legacyDecision.ShouldRequest, "unknown provider produces legacy candidate");
        Equal(true, legacy.ShouldAttempt(AudioRequestFactory.CreateLowHealth(
            CreateStatusSnapshot("legacy", 0.35f), legacyDecision.PreviousHpRatio)), "unknown provider uses legacy crossing");
        Equal(false, legacy.ShouldAttempt(new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.LowHealth,
            PreviousHpRatio = 0.34f,
            HpRatio = 0.3f
        }), "legacy policy rejects a decrease already below threshold");

        var thresholded = new AudioLowHealthCoordinator();
        thresholded.ConfigureProviders(new[]
        {
            new AudioLowHealthProviderDescriptor(SoundEventKinds.LowHealth, 0.3f),
            new AudioLowHealthProviderDescriptor(SoundEventKinds.CardUse, 0.9f)
        });
        thresholded.Seed(CreateStatusSnapshot("threshold", 0.6f));
        var crossed = thresholded.Observe(CreateStatusSnapshot("threshold", 0.29f));
        Equal(true, thresholded.ShouldAttempt(AudioRequestFactory.CreateLowHealth(
            CreateStatusSnapshot("threshold", 0.29f), crossed.PreviousHpRatio)), "explicit threshold crossing is accepted");
        thresholded.MarkAnnounced("threshold");
        Equal(AudioLowHealthObservationOutcome.AlreadyAnnounced,
            thresholded.Observe(CreateStatusSnapshot("threshold", 0.2f)).Outcome,
            "announced status suppresses another decrease");
        var belowRecovery = thresholded.Observe(CreateStatusSnapshot("threshold", 0.34f));
        Equal(AudioLowHealthObservationOutcome.Increased, belowRecovery.Outcome, "recovery increase is observed");
        Equal(false, belowRecovery.AnnouncementReset, "recovery below threshold plus margin stays announced");
        Equal(AudioLowHealthObservationOutcome.AlreadyAnnounced,
            thresholded.Observe(CreateStatusSnapshot("threshold", 0.25f)).Outcome,
            "decrease after partial recovery stays suppressed");
        var recovered = thresholded.Observe(CreateStatusSnapshot("threshold", 0.36f));
        Equal(true, recovered.AnnouncementReset, "threshold plus margin resets announcement");
        Equal(AudioLowHealthObservationOutcome.Candidate,
            thresholded.Observe(CreateStatusSnapshot("threshold", 0.29f)).Outcome,
            "decrease after full recovery becomes candidate again");

        var mixed = new AudioLowHealthCoordinator();
        mixed.ConfigureProviders(new[]
        {
            new AudioLowHealthProviderDescriptor(SoundEventKinds.LowHealth, 0.3f),
            new AudioLowHealthProviderDescriptor(SoundEventKinds.LowHealth, -1f)
        });
        Equal(true, mixed.ShouldAttempt(new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.LowHealth,
            PreviousHpRatio = 0.2f,
            HpRatio = 0.19f
        }), "unthresholded explicit provider accepts any decrease");
        var unrelated = new AudioLowHealthCoordinator();
        unrelated.ConfigureProviders(new[] { new AudioLowHealthProviderDescriptor(SoundEventKinds.CardUse, -1f) });
        Equal(false, unrelated.ShouldAttempt(new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.LowHealth,
            PreviousHpRatio = 0.6f,
            HpRatio = 0.1f
        }), "known unrelated providers do not trigger low-health fallback");

        var missingIdentity = new AudioLowHealthCoordinator();
        missingIdentity.ConfigureProviders(new[] { new AudioLowHealthProviderDescriptor("", -1f) });
        missingIdentity.Seed(CreateStatusSnapshot("missing", 0.6f, "", ""));
        Equal(AudioLowHealthObservationOutcome.MissingRoleIdentity,
            missingIdentity.Observe(CreateStatusSnapshot("missing", 0.3f, "", "")).Outcome,
            "missing role and career are rejected after ratio tracking");

        var cooldown = new AudioLowHealthCoordinator();
        var cooldownRequest = AudioRequestFactory.CreateLowHealth(CreateStatusSnapshot("cooldown", 0.25f), 0.5f);
        cooldown.RememberNoProvider(cooldownRequest, 10f);
        Equal(true, cooldown.IsNoProviderSuppressed(cooldownRequest, 10f), "no-provider cooldown starts immediately");
        Equal(true, cooldown.IsNoProviderSuppressed(cooldownRequest, 10.749f), "no-provider cooldown remains before expiry");
        Equal(false, cooldown.IsNoProviderSuppressed(cooldownRequest, 10.75f), "no-provider cooldown expires at boundary");
        cooldown.RememberNoProvider(cooldownRequest, 20f);
        var otherBucket = AudioRequestFactory.CreateLowHealth(CreateStatusSnapshot("cooldown", 0.35f), 0.5f);
        Equal(false, cooldown.IsNoProviderSuppressed(otherBucket, 20.1f), "no-provider cooldown is isolated by ratio bucket");
        cooldown.ConfigureProviders(new[] { new AudioLowHealthProviderDescriptor(SoundEventKinds.LowHealth, 0.3f) });
        Equal(false, cooldown.IsNoProviderSuppressed(cooldownRequest, 20.1f), "provider refresh clears no-provider cooldowns");
        var nonLowHealth = new SoundPlaybackRequest { Kind = SoundEventKinds.CardUse };
        cooldown.RememberNoProvider(nonLowHealth, 30f);
        Equal(false, cooldown.IsNoProviderSuppressed(nonLowHealth, 30f), "non-low-health request ignores no-provider state");
        Equal(false, cooldown.ShouldAttempt(nonLowHealth), "non-low-health request ignores low-health provider policy");

        var reset = new AudioLowHealthCoordinator();
        reset.ConfigureProviders(new[] { new AudioLowHealthProviderDescriptor(SoundEventKinds.LowHealth, 0.3f) });
        reset.Seed(CreateStatusSnapshot("reset", 0.6f));
        var resetCandidate = reset.Observe(CreateStatusSnapshot("reset", 0.2f));
        var resetRequest = AudioRequestFactory.CreateLowHealth(CreateStatusSnapshot("reset", 0.2f), resetCandidate.PreviousHpRatio);
        reset.MarkAnnounced("RESET");
        Equal(AudioLowHealthObservationOutcome.AlreadyAnnounced,
            reset.Observe(CreateStatusSnapshot("reset", 0.19f)).Outcome,
            "announcement ids are case insensitive");
        reset.RememberNoProvider(resetRequest, 40f);
        reset.ResetFight();
        Equal(AudioLowHealthObservationOutcome.Seeded,
            reset.Observe(CreateStatusSnapshot("reset", 0.1f)).Outcome,
            "fight reset clears HP history and announcement state");
        Equal(false, reset.IsNoProviderSuppressed(resetRequest, 40.1f), "fight reset clears no-provider cooldown");
        Equal(true, reset.ShouldAttempt(new SoundPlaybackRequest
        {
            Kind = SoundEventKinds.LowHealth,
            PreviousHpRatio = 0.4f,
            HpRatio = 0.2f
        }), "fight reset preserves provider configuration");
    }

    private static AudioStatusSnapshot CreateStatusSnapshot(
        string statusInstanceId,
        float ratio,
        string roleId = "role",
        string careerId = "career")
    {
        return new AudioStatusSnapshot
        {
            StatusInstanceId = statusInstanceId,
            RoleId = roleId,
            CareerId = careerId,
            Hp = (int)(ratio * 100f),
            MaxHp = 100,
            HpRatio = ratio,
            IsLocalOwner = true,
            SourceName = "test"
        };
    }

    private void VerifyPropertyReader()
    {
        var source = new PropertySource();
        Equal("alpha", AudioPropertyReader.ReadString(source, nameof(PropertySource.Text)), "string property");
        Equal("17", AudioPropertyReader.ReadString(source, nameof(PropertySource.Number)), "string conversion");
        Equal("fallback", AudioPropertyReader.ReadString(source, "Missing", "fallback"), "missing string fallback");
        Equal(17, AudioPropertyReader.ReadInt(source, nameof(PropertySource.Number), -1), "typed integer");
        Equal(29, AudioPropertyReader.ReadInt(source, nameof(PropertySource.IntegerText), -1), "parsed integer");
        Equal(-1, AudioPropertyReader.ReadInt(source, nameof(PropertySource.Invalid), -1), "invalid integer fallback");
        Equal(1234567890123L, AudioPropertyReader.ReadLong(source, nameof(PropertySource.LongNumber), -1), "typed long");
        Equal(17L, AudioPropertyReader.ReadLong(source, nameof(PropertySource.Number), -1), "converted long");
        Equal(-1L, AudioPropertyReader.ReadLong(source, nameof(PropertySource.Invalid), -1), "invalid long fallback");
        Equal(true, AudioPropertyReader.ReadBool(source, nameof(PropertySource.Flag), false), "typed boolean");
        Equal(false, AudioPropertyReader.ReadBool(source, nameof(PropertySource.BooleanText), true), "parsed boolean");
        Equal(true, AudioPropertyReader.ReadBool(source, nameof(PropertySource.Invalid), true), "invalid boolean fallback");
        Equal(0.25f, AudioPropertyReader.ReadFloat(source, nameof(PropertySource.Ratio), -1f), "typed float");
        Equal(1.5f, AudioPropertyReader.ReadFloat(source, nameof(PropertySource.FloatText), -1f), "parsed float");
        Equal(-1f, AudioPropertyReader.ReadFloat(source, nameof(PropertySource.Invalid), -1f), "invalid float fallback");
        Equal("safe", AudioPropertyReader.ReadString(source, nameof(PropertySource.Throwing), "safe"), "throwing getter fallback");
        Equal(3, AudioPropertyReader.ReadInt(null, "Any", 3), "null source fallback");
    }

    private void VerifyRequestProjection()
    {
        var typed = CreateRequest();
        Same(typed, SoundPlaybackRequest.FromObject(typed), "typed request identity");

        var projected = SoundPlaybackRequest.FromObject(new RequestLike());
        AssertRequestValues(projected, "object projection");
        Equal(false, projected.IsRemote, "projection does not infer remote state");
        Equal(false, projected.DisableSync, "projection does not disable sync");
        Null(projected.ModConfig, "projection does not copy mod config");

        var empty = SoundPlaybackRequest.FromObject(new object());
        Equal("", empty.EventId, "missing projected event id");
        Equal(0, empty.MaxAgeMilliseconds, "missing projected max age");
        Equal(false, empty.IsLocalOwner, "missing projected local owner");
    }

    private void VerifyNetworkProjection()
    {
        var source = CreateRequest();
        source.IsRemote = true;
        source.DisableSync = false;
        source.ModConfig = new Witch.Mod.ModConfig();

        var projected = AudioNetworkEventMapper.CreateRemoteCopy(source);
        NotSame(source, projected, "network projection creates a copy");
        AssertRequestValues(projected, "network projection");
        Equal(false, projected.IsRemote, "network projection leaves receive state local");
        Equal(true, projected.DisableSync, "network projection disables relay sync");
        Null(projected.ModConfig, "network projection omits local mod config");

        source.EventId = "mutated";
        Equal("event-1", projected.EventId, "network projection is independent");
    }

    private void AssertRequestValues(SoundPlaybackRequest request, string scope)
    {
        Equal("event-1", request.EventId, scope + " event id");
        Equal("fight-1", request.FightToken, scope + " fight token");
        Equal("player-1", request.IssuerPlayerId, scope + " issuer");
        Equal("provider-1", request.ProviderId, scope + " provider");
        Equal("owner-1", request.OwnerModId, scope + " owner");
        Equal("CardUse", request.Kind, scope + " kind");
        Equal("career-1", request.CareerId, scope + " career");
        Equal("role-1", request.RoleId, scope + " role");
        Equal("status-1", request.StatusInstanceId, scope + " status");
        Equal("card-1", request.CardId, scope + " card");
        Equal("buff-1", request.BuffId, scope + " buff");
        Equal("effect-1", request.EffectName, scope + " effect");
        Equal("action-1", request.ActionName, scope + " action");
        Equal("vocal-1", request.VocalState, scope + " vocal");
        Equal("Victory", request.BattleResult, scope + " battle result");
        Equal(25, request.Hp, scope + " hp");
        Equal(100, request.MaxHp, scope + " max hp");
        Equal(0.5f, request.PreviousHpRatio, scope + " previous hp ratio");
        Equal(0.25f, request.HpRatio, scope + " hp ratio");
        Equal("source-1", request.SourceName, scope + " source");
        Equal(638000000000000000L, request.CreatedAtUtcTicks, scope + " created ticks");
        Equal(4321, request.MaxAgeMilliseconds, scope + " max age");
        Equal(true, request.IsLocalOwner, scope + " local owner");
    }

    private void VerifyNetworkPolicy()
    {
        var now = 638000000100000000L;
        var request = CreateRequest();
        request.CreatedAtUtcTicks = now - TimeSpan.TicksPerMillisecond * request.MaxAgeMilliseconds;
        Equal(false, AudioNetworkPolicy.IsExpiredPresentation(request, now), "presentation remains valid at max-age boundary");
        Equal(true, AudioNetworkPolicy.IsExpiredPresentation(request, now + 1), "presentation expires after max-age boundary");
        request.MaxAgeMilliseconds = 0;
        Equal(false, AudioNetworkPolicy.IsExpiredPresentation(request, now + TimeSpan.TicksPerDay), "unbounded presentation does not expire");
        request.Kind = SoundEventKinds.SkillVoice;
        request.MaxAgeMilliseconds = 1;
        Equal(false, AudioNetworkPolicy.IsExpiredPresentation(request, now + TimeSpan.TicksPerDay), "non-card event bypasses presentation expiry");

        request = CreateRequest();
        Equal("fight-1|player-1|event-1", AudioNetworkPolicy.PresentationDedupeKey(request), "presentation dedupe key");
        Equal(true, AudioNetworkPolicy.IsCardUsePresentation(request), "card-use presentation classification");
        request.Kind = "carduse";
        Equal(true, AudioNetworkPolicy.IsCardUsePresentation(request), "card-use classification is case-insensitive");
        request.IssuerPlayerId = "";
        Equal("missing local issuer", AudioNetworkPolicy.ValidateLocalPresentationIdentity(request, true), "multiplayer local issuer required");
        request.IssuerPlayerId = "player-1";
        request.StatusInstanceId = "";
        Equal("missing local owner status", AudioNetworkPolicy.ValidateLocalPresentationIdentity(request, true), "multiplayer local owner required");
        Equal("", AudioNetworkPolicy.ValidateLocalPresentationIdentity(request, false), "solo local identity may be absent");

        var sender = new AudioNetworkSenderSnapshot(true, true, false, "player-1");
        request = CreateRequest();
        Equal("", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, status) => status == "status-1"), "valid server presentation");
        Equal("invalid event kind", AudioNetworkPolicy.ValidateServerCardUsePresentation(null, sender, (_, _) => true), "missing server presentation");
        request.Kind = SoundEventKinds.SkillVoice;
        Equal("invalid event kind", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "invalid server presentation kind");
        request = CreateRequest();
        Equal("missing sender", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, new AudioNetworkSenderSnapshot(false, false, false, ""), (_, _) => true), "missing bound sender");
        Equal("sender outside lobby: player-1", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, new AudioNetworkSenderSnapshot(true, false, false, "player-1"), (_, _) => true), "sender lobby membership");
        request.EventId = "";
        Equal("missing event id", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "missing presentation event id");
        request.EventId = new string('e', 161);
        Equal("event id too long", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "bounded presentation event id");
        request = CreateRequest();
        request.FightToken = "";
        Equal("missing fight token", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "missing fight token");
        request.FightToken = new string('f', 97);
        Equal("fight token too long", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "bounded fight token");
        request = CreateRequest();
        request.CardId = "";
        Equal("missing card id", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "missing card id");
        request = CreateRequest();
        request.StatusInstanceId = "";
        Equal("missing owner status", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "missing owner status");
        request = CreateRequest();
        request.IssuerPlayerId = "other";
        Equal("issuer mismatch", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "payload issuer cannot replace bound sender");
        request = CreateRequest();
        Equal("owner mismatch", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => false), "sender must own status");
        request.MaxAgeMilliseconds = -1;
        Equal("invalid max age", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "negative max age rejected");
        request.MaxAgeMilliseconds = SoundPlaybackRequest.DefaultPresentationMaxAgeMilliseconds + 1;
        Equal("invalid max age", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "oversized max age rejected");
        request = CreateRequest();
        request.CreatedAtUtcTicks = now - TimeSpan.TicksPerMillisecond * request.MaxAgeMilliseconds - 1;
        Equal("expired presentation", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true, now), "server rejects expired client presentation");
    }

    private void VerifyNetworkSessionState()
    {
        var session = new AudioNetworkSessionState(2);
        var request = CreateRequest();
        request.Kind = SoundEventKinds.SkillVoice;
        Equal(AudioPresentationClaimResult.NotPresentation, session.TryClaimPresentation(request, true), "non-presentation claim bypass");

        request = CreateRequest();
        Equal(AudioPresentationClaimResult.FightSessionNotReady, session.TryClaimPresentation(request, true), "multiplayer claim waits for fight session");
        session.SetFightToken("fight-1");
        request.FightToken = "stale";
        Equal(AudioPresentationClaimResult.StaleFightSession, session.TryClaimPresentation(request, true), "stale fight claim rejected");
        request.FightToken = "fight-1";
        Equal(AudioPresentationClaimResult.Claimed, session.TryClaimPresentation(request, true), "first presentation claimed");
        Equal(AudioPresentationClaimResult.Duplicate, session.TryClaimPresentation(request, true), "duplicate presentation rejected");

        var second = CreateRequest();
        second.EventId = "event-2";
        Equal(AudioPresentationClaimResult.Claimed, session.TryClaimPresentation(second, true), "second presentation claimed");
        var third = CreateRequest();
        third.EventId = "event-3";
        Equal(AudioPresentationClaimResult.Claimed, session.TryClaimPresentation(third, true), "third presentation evicts oldest claim");
        Equal(2, session.PlaybackClaimCount, "presentation claims stay bounded");
        Equal(AudioPresentationClaimResult.Claimed, session.TryClaimPresentation(request, true), "evicted presentation can be claimed again");

        var solo = new AudioNetworkSessionState(2);
        Equal(AudioPresentationClaimResult.Claimed, solo.TryClaimPresentation(CreateRequest(), false), "solo presentation does not require fight token state");

        session.ApplyFightToken("fight-2");
        Equal("fight-2", session.FightToken, "fight token applied");
        Equal(0, session.PlaybackClaimCount, "fight token replacement clears claims");
        var firstId = session.ReuseOrCreateLocalPlayId("action-a", "player", 10f, 0.15f);
        Equal("player:audio:1:fight-2", firstId, "local play id identity");
        Equal(firstId, session.ReuseOrCreateLocalPlayId("action-a", "player", 10.15f, 0.15f), "local play id reused at window boundary");
        var secondId = session.ReuseOrCreateLocalPlayId("action-a", "player", 10.301f, 0.15f);
        Equal("player:audio:2:fight-2", secondId, "local play id advances after reuse window");
        session.ReuseOrCreateLocalPlayId("action-b", "", 12f, 0.15f);
        Equal(1, session.RecentLocalPlayCount, "stale local play identities are pruned");

        session.ResetTransient();
        Equal("", session.FightToken, "transient reset clears fight token");
        Equal(0, session.PlaybackClaimCount, "transient reset clears claims");
        Equal(0, session.RecentLocalPlayCount, "transient reset clears local play reuse");
        session.SetFightToken("fight-3");
        Equal("solo:audio:1:fight-3", session.ReuseOrCreateLocalPlayId("action", "", 20f, 0.15f), "transient reset clears play counter");
    }

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

    private void VerifyCooldownPolicy()
    {
        var ledger = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        Equal(true, AudioProviderCooldownPolicy.TryAcquire(ledger, "Owner:voice", "CardUse", "role", "status", 2f, 10f), "first cooldown acquisition");
        Equal(false, AudioProviderCooldownPolicy.TryAcquire(ledger, "Owner:voice", "CardUse", "role", "status", 2f, 11.9f), "active cooldown rejected");
        Equal(true, AudioProviderCooldownPolicy.TryAcquire(ledger, "Owner:voice", "CardUse", "role", "status", 2f, 12f), "cooldown boundary accepted");
        Equal(true, AudioProviderCooldownPolicy.TryAcquire(ledger, "Owner:voice", "CardUse", "other", "status", 2f, 12.1f), "role scope has independent cooldown");

        var noCooldown = new Dictionary<string, float>();
        Equal(true, AudioProviderCooldownPolicy.TryAcquire(noCooldown, "Owner:voice", "CardUse", "role", "status", 0f, 1f), "zero cooldown accepted");
        Equal(0, noCooldown.Count, "zero cooldown does not retain state");
        Equal("Owner:voice|CardUse|role|status", AudioProviderCooldownPolicy.BuildKey("Owner:voice", "CardUse", "role", "status"), "cooldown key shape");
    }

    private void VerifyPresentationPolicy()
    {
        var local = AudioPresentationPolicy.CreatePlan(
            SoundBuses.Effect,
            SoundPolicies.Replace,
            SoundEventKinds.CardUse,
            false,
            0.15f,
            1f);
        Equal(true, local.QueueNativeEffectReplacement, "local replacement queued");
        Equal(false, local.StartRemoteFallback, "local replacement has no remote fallback");
        Equal(1f, local.PairingSeconds, "local pairing duration");
        Equal("local-pair-pending", local.PendingOutcome, "local pending outcome");

        var remote = AudioPresentationPolicy.CreatePlan(
            "effect",
            "replaceoriginal",
            "carduse",
            true,
            0.15f,
            1f);
        Equal(true, remote.QueueNativeEffectReplacement, "remote replacement queued case-insensitively");
        Equal(true, remote.StartRemoteFallback, "remote replacement starts fallback");
        Equal(0.15f, remote.PairingSeconds, "remote pairing duration");
        Equal("remote-pair-pending", remote.PendingOutcome, "remote pending outcome");

        Equal(false, AudioPresentationPolicy.CreatePlan(SoundBuses.Vocal, SoundPolicies.Replace, SoundEventKinds.CardUse, false, 0.15f, 1f).QueueNativeEffectReplacement, "vocal bus bypasses native effect replacement");
        Equal(false, AudioPresentationPolicy.CreatePlan(SoundBuses.Effect, SoundPolicies.Additive, SoundEventKinds.CardUse, false, 0.15f, 1f).QueueNativeEffectReplacement, "additive policy bypasses replacement");
        Equal(false, AudioPresentationPolicy.CreatePlan(SoundBuses.Effect, SoundPolicies.Replace, SoundEventKinds.SkillVoice, false, 0.15f, 1f).QueueNativeEffectReplacement, "non-card event bypasses replacement");
        Equal(true, AudioPresentationPolicy.IsReplacementPolicy(SoundPolicies.SuppressOriginal), "suppress-original is replacement policy");
        Equal(false, AudioPresentationPolicy.IsReplacementPolicy(SoundPolicies.Additive), "additive is not replacement policy");
        Equal(true, AudioPresentationPolicy.IsVocalBus("vocal"), "vocal bus match");

        Equal("status", AudioPresentationPolicy.ResolveVocalRoleId("status", "role", "career", "owner", "provider"), "status id has vocal priority");
        Equal("role", AudioPresentationPolicy.ResolveVocalRoleId("", "role", "career", "owner", "provider"), "role id is vocal fallback");
        Equal("career", AudioPresentationPolicy.ResolveVocalRoleId("", "", "career", "owner", "provider"), "career id is vocal fallback");
        Equal("owner.provider", AudioPresentationPolicy.ResolveVocalRoleId("", "", "", "owner", "provider"), "provider identity is final vocal fallback");

        Equal(AudioNativeEffectAction.None, AudioPresentationPolicy.ResolveNativeEffectAction(SoundPolicies.Replace, false, 1f), "missing replacement leaves native effect unchanged");
        Equal(AudioNativeEffectAction.SuppressOriginal, AudioPresentationPolicy.ResolveNativeEffectAction(SoundPolicies.SuppressOriginal, true, 1f), "suppress policy clears original");
        Equal(AudioNativeEffectAction.ReplaceOriginalClip, AudioPresentationPolicy.ResolveNativeEffectAction(SoundPolicies.Replace, true, 1f), "identity volume replaces native clip");
        Equal(AudioNativeEffectAction.ReplaceOriginalClip, AudioPresentationPolicy.ResolveNativeEffectAction(SoundPolicies.Replace, true, 1.0005f), "volume inside tolerance replaces native clip");
        Equal(AudioNativeEffectAction.PlayReplacementAfterDelay, AudioPresentationPolicy.ResolveNativeEffectAction(SoundPolicies.Replace, true, 1.01f), "custom volume uses delayed playback adapter");
        Equal(false, AudioPresentationPolicy.UsesCustomVolume(0.9995f), "volume tolerance lower bound");
        Equal(true, AudioPresentationPolicy.UsesCustomVolume(0.99f), "custom volume detected");
    }

    private void VerifySuppressionPolicy()
    {
        var ledger = new Dictionary<int, float>();
        AudioSuppressionPolicy.ArmNarrationSuppressions(ledger, new[] { 7, 9 }, 10f, 1.5f);
        Equal(11.5f, ledger[7], "narration suppression deadline");
        Equal(11.5f, ledger[9], "all narration ids armed");
        Equal(true, AudioSuppressionPolicy.ShouldSuppressNarration(ledger, new[] { 7 }, 11.5f), "narration suppression includes deadline");
        Equal(false, AudioSuppressionPolicy.ShouldSuppressNarration(ledger, new[] { 8 }, 11.6f), "unmatched narration is not suppressed");
        Equal(0, ledger.Count, "expired narration suppressions cleaned");
        Equal(false, AudioSuppressionPolicy.ShouldSuppressNarration(ledger, Array.Empty<int>(), 20f), "empty narration ids bypass suppression");
    }

    private void VerifyReplacementCoordinator()
    {
        var coordinator = new AudioReplacementCoordinator<FakeResource>();
        var clip = new FakeResource("clip");
        coordinator.Arm(clip, SoundPolicies.Replace, 1f, 10f, "event", "card", "role", "owner:provider", false, false);
        Equal(true, coordinator.HasActivePending(10f), "pending replacement includes deadline");
        var local = coordinator.ConsumeNativeEffect(10f);
        Equal(true, local.Handled, "local native effect handled");
        Equal(AudioNativeEffectAction.ReplaceOriginalClip, local.Action, "local native clip replaced");
        Same(clip, local.Pending!.Resource!, "replacement resource retained");
        Null(coordinator.Pending, "single-use replacement consumed");

        coordinator.Arm(clip, SoundPolicies.Replace, 0.5f, 20f, "custom", "card", "role", "owner:provider", false, false);
        Equal(AudioNativeEffectAction.PlayReplacementAfterDelay, coordinator.ConsumeNativeEffect(19f).Action, "custom-volume replacement decision");

        coordinator.Arm(clip, SoundPolicies.SuppressOriginal, 1f, 30f, "suppress", "card", "role", "owner:provider", false, false);
        Equal(AudioNativeEffectAction.SuppressOriginal, coordinator.ConsumeNativeEffect(29f).Action, "suppress-only replacement decision");

        coordinator.Arm(clip, SoundPolicies.Replace, 1f, 40f, "remote", "card", "role", "owner:provider", true, false);
        var remote = coordinator.ConsumeNativeEffect(39f);
        Equal("paired-native", remote.RemoteOutcome, "remote native pairing outcome");
        Equal(1, coordinator.PairedRemoteCount, "remote pairing claim retained until fallback");
        Equal(true, coordinator.TryClaimPairedFallback("remote"), "paired fallback is consumed");
        Equal(false, coordinator.TryClaimPairedFallback("remote"), "paired fallback claim is single-use");

        coordinator.Arm(clip, SoundPolicies.SuppressOriginal, 1f, 50f, "late", "card", "role", "owner:provider", true, true);
        var late = coordinator.ConsumeNativeEffect(49f);
        Equal("fallback-original-suppressed", late.RemoteOutcome, "late original suppression outcome");
        Equal(0, coordinator.PairedRemoteCount, "fallback tail does not create pairing claim");

        coordinator.Arm(clip, SoundPolicies.Replace, 1f, 60f, "expired", "card", "role", "owner:provider", false, false);
        Equal(false, coordinator.HasActivePending(60.1f), "expired replacement cleared");
        Null(coordinator.Pending, "expired pending removed");

        coordinator.Arm(clip, SoundPolicies.Replace, 1f, 70f, "twice", "card", "role", "owner:provider", false, false, remaining: 2);
        coordinator.ConsumeNativeEffect(69f);
        Equal(1, coordinator.Pending!.Remaining, "multi-use replacement decremented");
        coordinator.ConsumeNativeEffect(69f);
        Null(coordinator.Pending, "multi-use replacement exhausted");

        coordinator.Arm(clip, SoundPolicies.Replace, 1f, 80f, "keep", "card", "role", "owner:provider", true, false);
        coordinator.ClearPendingForEvent("other");
        True(coordinator.Pending != null, "different event does not clear pending");
        coordinator.ClearPairingClaims();
        True(coordinator.Pending != null, "pairing cleanup preserves pending state");
        coordinator.ClearPendingForEvent("keep");
        Null(coordinator.Pending, "matching event clears pending");

        coordinator.Arm(clip, SoundPolicies.Replace, 1f, 90f, "clear", "card", "role", "owner:provider", true, false);
        coordinator.ConsumeNativeEffect(89f);
        coordinator.Clear();
        Null(coordinator.Pending, "full coordinator cleanup clears pending");
        Equal(0, coordinator.PairedRemoteCount, "full coordinator cleanup clears pairing claims");
    }

    private static SoundPlaybackRequest CreateRequest()
    {
        return new SoundPlaybackRequest
        {
            EventId = "event-1",
            FightToken = "fight-1",
            IssuerPlayerId = "player-1",
            ProviderId = "provider-1",
            OwnerModId = "owner-1",
            Kind = "CardUse",
            CareerId = "career-1",
            RoleId = "role-1",
            StatusInstanceId = "status-1",
            CardId = "card-1",
            BuffId = "buff-1",
            EffectName = "effect-1",
            ActionName = "action-1",
            VocalState = "vocal-1",
            BattleResult = "Victory",
            Hp = 25,
            MaxHp = 100,
            PreviousHpRatio = 0.5f,
            HpRatio = 0.25f,
            SourceName = "source-1",
            CreatedAtUtcTicks = 638000000000000000L,
            MaxAgeMilliseconds = 4321,
            IsLocalOwner = true
        };
    }

    private void Equal<T>(T expected, T actual, string name)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected={expected}, actual={actual}");
        }
    }

    private void Null(object? actual, string name)
    {
        assertions++;
        if (actual != null)
        {
            throw new InvalidOperationException($"{name}: expected null, actual={actual}");
        }
    }

    private void Same(object expected, object actual, string name)
    {
        assertions++;
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException(name + ": expected same reference");
        }
    }

    private void NotSame(object expected, object actual, string name)
    {
        assertions++;
        if (ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException(name + ": expected independent reference");
        }
    }

    private void True(bool actual, string name)
    {
        assertions++;
        if (!actual)
        {
            throw new InvalidOperationException(name + ": expected true");
        }
    }

    private sealed class FakeResource
    {
        public FakeResource(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }

    private sealed class FakeProvider : IAudioProviderCandidate<FakeResource>
    {
        public FakeProvider(
            string providerId,
            string ownerModId,
            int priority,
            bool hardClaim,
            string loadState,
            FakeResource? resource)
        {
            ProviderId = providerId;
            OwnerModId = ownerModId;
            QualifiedProviderId = AudioProviderResolver.QualifyProviderId(ownerModId, providerId);
            Priority = priority;
            HardClaim = hardClaim;
            LoadState = loadState;
            Resource = resource;
        }

        public string ProviderId { get; }

        public string OwnerModId { get; }

        public string QualifiedProviderId { get; }

        public int Priority { get; }

        public bool HardClaim { get; }

        public bool Matches { get; set; } = true;

        private string LoadState { get; }

        private FakeResource? Resource { get; }

        public bool Evaluate(object request)
        {
            return Matches;
        }

        public string GetLoadState()
        {
            return LoadState;
        }

        public FakeResource? GetResource(object request)
        {
            return Resource;
        }
    }

    private sealed class PropertySource
    {
        public string Text => "alpha";
        public int Number => 17;
        public string IntegerText => "29";
        public long LongNumber => 1234567890123L;
        public bool Flag => true;
        public string BooleanText => "false";
        public float Ratio => 0.25f;
        public string FloatText => "1.5";
        public string Invalid => "not-a-value";
        public string Throwing => throw new InvalidOperationException("getter failure");
    }

    private sealed class RequestLike
    {
        public string EventId => "event-1";
        public string FightToken => "fight-1";
        public string IssuerPlayerId => "player-1";
        public string ProviderId => "provider-1";
        public string OwnerModId => "owner-1";
        public string Kind => "CardUse";
        public string CareerId => "career-1";
        public string RoleId => "role-1";
        public string StatusInstanceId => "status-1";
        public string CardId => "card-1";
        public string BuffId => "buff-1";
        public string EffectName => "effect-1";
        public string ActionName => "action-1";
        public string VocalState => "vocal-1";
        public string BattleResult => "Victory";
        public string Hp => "25";
        public int MaxHp => 100;
        public string PreviousHpRatio => "0.5";
        public float HpRatio => 0.25f;
        public string SourceName => "source-1";
        public long CreatedAtUtcTicks => 638000000000000000L;
        public string MaxAgeMilliseconds => "4321";
        public bool IsLocalOwner => true;
    }
}
