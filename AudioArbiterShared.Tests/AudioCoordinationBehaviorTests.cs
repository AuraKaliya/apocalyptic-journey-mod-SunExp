using AudioArbiter.Shared;
using AuraAudio.Shared;

internal sealed partial class AudioArbiterContractTests
{
    private void VerifyPendingPresentationQueue()
    {
        True(AudioProviderLoadStatePolicy.IsTransient("Loading"), "loading provider state is transient");
        True(AudioProviderLoadStatePolicy.IsTransient("Initializing"), "initializing provider state is transient");
        Equal(false, AudioProviderLoadStatePolicy.IsTransient("Failed"), "failed provider state is permanent");
        Equal(false, AudioProviderLoadStatePolicy.IsTransient("Missing"), "missing provider state is permanent");
        Equal(false, AudioProviderLoadStatePolicy.IsTransient("Disposed"), "disposed provider state is permanent");
    
        var now = new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var queue = new AudioPendingPresentationQueue(maximumCount: 2);
        var first = CreateRequest();
        first.EventId = "pending-1";
        first.CreatedAtUtcTicks = now;
        first.MaxAgeMilliseconds = 5000;
        True(queue.Enqueue(first, now, syncRemote: true), "first unresolved local presentation is queued");
        Equal(false, queue.Enqueue(first, now), "pending presentation identity is deduplicated");
        Equal(true, queue.Snapshot().Single().SyncRemote,
            "pending local presentation retains its one-time network relay decision");
        Equal(now + TimeSpan.TicksPerMillisecond * AudioPendingPresentationQueue.DefaultWaitMilliseconds,
            queue.Snapshot().Single().ExpiresAtUtcTicks,
            "pending audio uses its shorter local readiness deadline");
    
        var shortLived = CreateRequest();
        shortLived.EventId = "pending-2";
        shortLived.CreatedAtUtcTicks = now;
        shortLived.MaxAgeMilliseconds = 500;
        True(queue.Enqueue(shortLived, now), "second pending presentation is queued");
        Equal(now + TimeSpan.TicksPerMillisecond * 500,
            queue.Snapshot().Single(item => item.Request.EventId == "pending-2").ExpiresAtUtcTicks,
            "transport TTL bounds the pending audio deadline");
    
        var third = CreateRequest();
        third.EventId = "pending-3";
        True(queue.Enqueue(third, now), "bounded pending queue accepts newest event");
        Equal(2, queue.Count, "pending audio queue enforces its capacity");
        Equal(false, queue.Snapshot().Any(item => item.Request.EventId == "pending-1"),
            "pending audio queue evicts the oldest event at capacity");
    
        queue.Clear();
        Equal(0, queue.Count, "fight lifecycle cleanup clears pending audio");
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
}
