using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioArbiter.Shared;

internal static class AudioManifestMatchPolicy
{
    public static Func<object?, bool> BuildCondition(AudioProviderManifest provider)
    {
        var match = provider.match ?? new AudioProviderMatch();
        var kind = provider.kind?.Trim() ?? "";
        var vocalState = provider.vocalState?.Trim() ?? "";
        var careerIds = ToSet(match.careerIds);
        var roleIds = ToSet(match.roleIds);
        var cardIds = ToSet(match.cardIds);
        var buffIds = ToSet(match.buffIds);
        var effectNames = ToSet(match.effectNames);
        var actionNames = ToSet(match.actionNames);
        var battleResults = ToSet(match.battleResults);
        var localOwnerOnly = match.localOwnerOnly ?? false;
        var hpRatioCrossDown = match.hpRatioCrossDown;

        return request =>
        {
            if (!string.IsNullOrWhiteSpace(kind)
                && !string.Equals(AudioPropertyReader.ReadString(request, "Kind"), kind, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(vocalState)
                && !string.Equals(AudioPropertyReader.ReadString(request, "VocalState"), vocalState, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (careerIds.Count > 0 && !MatchesAnyId(careerIds, AudioPropertyReader.ReadString(request, "CareerId")))
            {
                return false;
            }

            if (roleIds.Count > 0 && !MatchesAnyId(roleIds, AudioPropertyReader.ReadString(request, "RoleId")))
            {
                return false;
            }

            if (cardIds.Count > 0 && !cardIds.Contains(AudioPropertyReader.ReadString(request, "CardId")))
            {
                return false;
            }

            if (buffIds.Count > 0 && !buffIds.Contains(AudioPropertyReader.ReadString(request, "BuffId")))
            {
                return false;
            }

            if (effectNames.Count > 0 && !effectNames.Contains(AudioPropertyReader.ReadString(request, "EffectName")))
            {
                return false;
            }

            if (actionNames.Count > 0 && !actionNames.Contains(AudioPropertyReader.ReadString(request, "ActionName")))
            {
                return false;
            }

            if (battleResults.Count > 0 && !battleResults.Contains(AudioPropertyReader.ReadString(request, "BattleResult")))
            {
                return false;
            }

            if (localOwnerOnly
                && !AudioPropertyReader.ReadBool(request, "IsRemote", false)
                && !AudioPropertyReader.ReadBool(request, "IsLocalOwner", false))
            {
                return false;
            }

            if (hpRatioCrossDown.HasValue)
            {
                var threshold = hpRatioCrossDown.Value;
                if (!(AudioPropertyReader.ReadFloat(request, "PreviousHpRatio", 0f) > threshold
                      && AudioPropertyReader.ReadFloat(request, "HpRatio", 0f) <= threshold))
                {
                    return false;
                }
            }

            return true;
        };
    }

    private static HashSet<string> ToSet(string[]? values)
    {
        return new HashSet<string>(
            values?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesAnyId(HashSet<string> accepted, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (accepted.Contains(value))
        {
            return true;
        }

        return accepted.Any(id =>
            value.StartsWith(id + "_", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("_" + id, StringComparison.OrdinalIgnoreCase));
    }
}
