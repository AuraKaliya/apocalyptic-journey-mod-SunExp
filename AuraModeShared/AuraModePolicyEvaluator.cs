using System;

namespace AuraMode.Shared;

public static class AuraModePolicyEvaluator
{
    public static AuraModePolicyDecision EvaluateStarterDeckMutation(
        AuraActiveModeSnapshot? snapshot,
        string actorId)
    {
        if (snapshot == null || !snapshot.IsActive)
        {
            return Decision(true, AuraModeStarterDeckAuthorities.InheritHost, "", "No semantic mode is active.");
        }

        var policy = snapshot.ResolvedPolicies?.StarterDeck ?? new AuraModeStarterDeckPolicy();
        var authority = Clean(policy.MutationAuthority);
        if (string.Equals(authority, AuraModeStarterDeckAuthorities.OfficialOnly, StringComparison.OrdinalIgnoreCase))
        {
            return Decision(false, authority, "Official", "The active mode permits only the official starter deck.");
        }
        if (!string.Equals(authority, AuraModeStarterDeckAuthorities.ModeOwnerExclusive, StringComparison.OrdinalIgnoreCase))
        {
            return Decision(true, AuraModeStarterDeckAuthorities.InheritHost, "", "The active mode inherits host starter-deck behavior.");
        }

        var provider = string.IsNullOrWhiteSpace(policy.ProviderId) ? snapshot.OwnerModId : policy.ProviderId;
        var allowed = string.Equals(Clean(actorId), Clean(provider), StringComparison.OrdinalIgnoreCase);
        return Decision(
            allowed,
            authority,
            Clean(provider),
            allowed
                ? "The caller owns the active mode starter-deck policy."
                : "The active mode reserves starter-deck mutation for its declared provider.");
    }

    private static AuraModePolicyDecision Decision(bool allowed, string policyId, string providerId, string reason)
    {
        return new AuraModePolicyDecision
        {
            Allowed = allowed,
            PolicyId = policyId,
            AuthorityProviderId = providerId,
            Reason = reason
        };
    }

    private static string Clean(string? value)
    {
        return (value ?? "").Trim();
    }
}
