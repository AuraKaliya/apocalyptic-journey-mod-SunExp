using AudioArbiter.Shared;
using AuraAudio.Shared;

internal sealed partial class AudioArbiterContractTests
{
    private void VerifyRequestFactory()
    {
        var career = AudioRequestFactory.CreateCareerSelected(new AudioCareerObservation
        {
            CareerId = "career",
            SourceName = "career-source"
        });
        Equal(SoundEventKinds.CareerSelected, career.Kind, "career request kind");
        Equal(AudioSignalStages.Committed, career.Stage, "career request stage");
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
        Equal(AudioSignalStages.PresentationCommitted, combat.CardUse.Stage, "card-use request stage");
        Equal("card", combat.CardUse.CardId, "card-use request card");
        Equal("career", combat.CardUse.CareerId, "card-use request career");
        Equal("role", combat.CardUse.RoleId, "card-use request role");
        Equal("status", combat.CardUse.StatusInstanceId, "card-use request status");
        Equal("effect", combat.CardUse.EffectName, "card-use request effect");
        Equal("action", combat.CardUse.ActionName, "card-use request action");
        Equal("combat-source", combat.CardUse.SourceName, "card-use request source");
        Equal("", combat.SkillVoice.EventId, "skill voice does not reuse authoritative card event id");
        Equal(SoundEventKinds.SkillVoice, combat.SkillVoice.Kind, "skill voice request kind");
        Equal(AudioSignalStages.PresentationCommitted, combat.SkillVoice.Stage, "skill voice request stage");
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
        Equal(AudioSignalStages.Applied, buff.Stage, "buff request stage");
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
        Equal(AudioSignalStages.Observed, vocal.Stage, "vocal request stage");
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
        Equal(AudioSignalStages.ThresholdCrossedDown, lowHealth.Stage, "low-health request stage");
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
        Equal(AudioSignalStages.Completed, battle.Stage, "battle request stage");
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
        Equal("PresentationCommitted", request.Stage, scope + " stage");
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
        request.Stage = AudioSignalStages.Applied;
        Equal("invalid event stage", AudioNetworkPolicy.ValidateServerCardUsePresentation(request, sender, (_, _) => true), "invalid server presentation stage");
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
}
