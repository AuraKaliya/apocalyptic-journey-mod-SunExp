using System;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class LoneerCardGrantService
{
    public static CardGrantResult GrantGuidanceCopyToHand(ScriptExecutor self, string cardId, string source)
    {
        var resolved = CardApi.ResolveCardId(cardId);
        return GrantGuidanceCopyToHand(self, CreatePreparedGuidance(resolved), source);
    }

    public static CardGrantResult GrantGuidanceCopyToHand(ScriptExecutor self, PreparedGuidanceCard guidance, string source)
    {
        var resolved = CardApi.ResolveCardId(guidance?.CardId ?? "");
        var prepared = guidance == null || !string.Equals(guidance.CardId, resolved, StringComparison.Ordinal)
            ? CreatePreparedGuidance(resolved)
            : guidance;

        var request = CardGrantRequest.ToHand(resolved)
            .WithSource("loneer-guidance." + source)
            .Configure(CardMutationService.SetRuntimeMarkersMutation(prepared.RuntimeMarkers))
            .Configure(CardMutationService.AddSpecialTagsMutation(prepared.SpecialTags));

        if (!prepared.IsWitchStarScore)
        {
            request
                .WithRuntimeTags(prepared.RuntimeTags)
                .Configure(CardMutationService.SetTemporaryCostMutation(1));
        }

        return CardApi.GrantCardToHand(self, request);
    }

    private static PreparedGuidanceCard CreatePreparedGuidance(string cardId)
    {
        var resolved = CardApi.ResolveCardId(cardId);
        var isWitchStarScore = string.Equals(resolved, TerriasIds.WitchStarScoreCardId, StringComparison.Ordinal)
            || string.Equals(NormalizeLocalId(resolved), "witch_star_score", StringComparison.Ordinal);
        return new PreparedGuidanceCard(
            resolved,
            "",
            isWitchStarScore,
            isWitchStarScore ? Array.Empty<string>() : new[] { "Burnout", "Nihility" },
            new[] { TerriasIds.LoneerDerivedMarker, TerriasIds.LoneerGuidanceMarker },
            new[] { TerriasIds.LoneerDerivedTag, TerriasIds.LoneerGuidanceTag });
    }

    private static string NormalizeLocalId(string id)
    {
        var value = (id ?? "").Replace("*", "").Trim();
        var last = value.LastIndexOf("_", StringComparison.Ordinal);
        return value.StartsWith("Terrias_", StringComparison.Ordinal) && last >= 0
            ? value.Substring(last + 1)
            : value;
    }
}

public static class WunaCardGrantService
{
    public static CardGrantResult GrantCoronationTokenToHand(ScriptExecutor self, string cardId)
    {
        var request = CardGrantRequest.ToHand(cardId)
            .WithSource("wuna-coronation-token")
            .WithRuntimeTags("Burnout", "Froze")
            .Configure(RuntimeCardAttachmentService.AttachMutation(
                RuntimeCardAttachmentService.WunaCoronationTokenAttachment()));

        return CardApi.GrantCardToHand(self, request);
    }
}
