using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.Mechanics;

public static class ProjectionRoleDeckService
{
    public const string CardModelVersion = "projection-role-deck-v2";

    public static bool TryCaptureLocal(out ProjectionDeckRecipe? recipe, out string reason)
    {
        return TryCapture(RoleTable.Instance, out recipe, out reason);
    }

    public static bool TryCaptureAuthoritative(
        string ownerPlayerId,
        string ownerStatusId,
        out ProjectionDeckRecipe? recipe,
        out string reason)
    {
        recipe = null;
        reason = "projection owner role table is unavailable";
        RoleTable? role = null;

        if (TerriasNetworkRuntime.IsMultiplayerSession())
        {
            var localOwner = string.Equals(
                FightPlayer.Instance?.Status?.InstanceId,
                ownerStatusId,
                StringComparison.Ordinal);
            var local = RoleTable.Instance;
            var localMatchesOwner = local != null
                                    && (string.IsNullOrWhiteSpace(ownerPlayerId)
                                        || string.Equals(local.Id, ownerPlayerId, StringComparison.Ordinal)
                                        || TerriasNetworkRuntime.IsLocalPlayer(ownerPlayerId));
            RoleTable? serverRole = null;
            try
            {
                var roles = global::GameServer.Instance?.RoleTables;
                if (roles != null
                    && !string.IsNullOrWhiteSpace(ownerPlayerId)
                    && roles.TryGetValue(ownerPlayerId, out var authoritative))
                {
                    serverRole = authoritative;
                }
            }
            catch (Exception ex)
            {
                TerriasLog.Debug("[ProjectionDeck] server role lookup failed: " + ex.Message);
            }

            // The policy makes the core multiplayer invariant explicit: a
            // remote owner can use only that owner's server RoleTable.
            role = ProjectionRoleDeckSourcePolicy.Select(
                multiplayer: true,
                ownerIsLocalStatus: localOwner,
                localRoleMatchesOwner: localMatchesOwner,
                serverRoleAvailable: serverRole != null) switch
            {
                ProjectionRoleDeckSourceKind.LocalRole => local,
                ProjectionRoleDeckSourceKind.ServerRole => serverRole,
                _ => null
            };
        }
        else
        {
            role = RoleTable.Instance;
        }

        if (!TryCapture(role, out recipe, out reason))
        {
            return false;
        }

        TerriasLog.Info("[ProjectionDeck] captured authoritative role deck: owner="
            + ownerPlayerId
            + ", cards="
            + recipe!.Cards.Count
            + ", hash="
            + recipe.Hash.Substring(0, 12));
        return true;
    }

    private static bool TryCapture(
        RoleTable? role,
        out ProjectionDeckRecipe? recipe,
        out string reason)
    {
        recipe = null;
        if (role?.cardList == null)
        {
            reason = "projection owner role table is unavailable";
            return false;
        }

        var cards = new List<ProjectionDeckCardRecipe>();
        var known = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var card in role.cardList.Where(card => card != null))
        {
            var cardId = DictionaryUtil.Get(card.data, "Id").Trim();
            if (cardId.Length == 0)
            {
                continue;
            }
            if (!known.TryGetValue(cardId, out var registered))
            {
                registered = AuraGameDataHostApi.ResolveHandle(DataType.Card, cardId) != null;
                known[cardId] = registered;
            }
            if (!registered)
            {
                TerriasLog.Warn("[ProjectionDeck] skipped unregistered card id: " + cardId);
                continue;
            }

            var attachmentId = "";
            var attachmentType = "EnchTag";
            var instanceId = card.InstanceID ?? "";
            if (instanceId.Length > 0
                && role.enchasedDict != null
                && role.enchasedDict.TryGetValue(instanceId, out var attachment)
                && attachment != null)
            {
                var candidateId = DictionaryUtil.Get(attachment.data, "Id").Trim();
                if (candidateId.Length > 0
                    && AuraGameDataHostApi.ResolveHandle(attachment.Type, candidateId) != null)
                {
                    attachmentId = candidateId;
                    attachmentType = attachment.Type.ToString();
                }
            }

            cards.Add(new ProjectionDeckCardRecipe(
                cardId,
                card.Type.ToString(),
                attachmentId,
                attachmentType));
            if (cards.Count >= ProjectionDeckRecipe.MaximumCards)
            {
                break;
            }
        }

        if (cards.Count == 0)
        {
            reason = "projection owner role deck has no registered cards";
            return false;
        }

        recipe = new ProjectionDeckRecipe(cards);
        reason = "";
        return true;
    }
}
