using System;
using System.Collections.Generic;

namespace AuraShared.Core;

public static class AuraLifecycleOperationLedger
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Claims = new(StringComparer.Ordinal);

    public static bool TryClaimBattleOperation(
        string ownerModId,
        string featureId,
        string operationId,
        string targetId,
        string effectCategory,
        string effectId)
    {
        var sessionId = AuraLifecycleSessionRuntime.EnsureBattleSession();
        return TryClaim(
            "battle:" + sessionId,
            ownerModId,
            featureId,
            operationId,
            targetId,
            effectCategory,
            effectId);
    }

    public static bool TryClaim(
        string scopeId,
        string ownerModId,
        string featureId,
        string operationId,
        string targetId,
        string effectCategory,
        string effectId)
    {
        var key = BuildKey(scopeId, ownerModId, featureId, operationId, targetId, effectCategory, effectId);
        if (key.Length == 0)
        {
            return false;
        }

        lock (Gate)
        {
            return Claims.Add(key);
        }
    }

    public static void ClearScopePrefix(string scopePrefix)
    {
        var prefix = Normalize(scopePrefix);
        if (prefix.Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            Claims.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    private static string BuildKey(
        string scopeId,
        string ownerModId,
        string featureId,
        string operationId,
        string targetId,
        string effectCategory,
        string effectId)
    {
        var scope = Normalize(scopeId);
        var owner = Normalize(ownerModId);
        var feature = Normalize(featureId);
        var operation = Normalize(operationId);
        var target = Normalize(targetId);
        var category = Normalize(effectCategory);
        var effect = Normalize(effectId);
        if (scope.Length == 0 || owner.Length == 0 || feature.Length == 0 || operation.Length == 0)
        {
            return "";
        }

        return scope
               + "|owner=" + owner
               + "|feature=" + feature
               + "|operation=" + operation
               + "|target=" + target
               + "|category=" + category
               + "|effect=" + effect;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }
}
