using System;
using System.Collections.Generic;
using System.Linq;
using AuraCg.Shared;
using AuraShared.Core;
using AuraSkin.Shared.Mechanics;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Core;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.Cg;

internal static class AuraToolsCgTeamSnapshotService
{
    private static readonly List<ParticipantSnapshot> StableParticipants = new();

    public static void BeginAdventure()
    {
        StableParticipants.Clear();
        MergeCurrentTeam(replace: true);
    }

    public static void Refresh()
    {
        MergeCurrentTeam(replace: false);
    }

    public static void Reset()
    {
        StableParticipants.Clear();
    }

    public static AuraCgSceneSourceSnapshot? BuildSource(
        string sceneId,
        string eventToken)
    {
        Refresh();
        if (StableParticipants.Count == 0)
        {
            return null;
        }

        return new AuraCgSceneSourceSnapshot
        {
            SceneId = sceneId,
            EventToken = eventToken,
            Participants = StableParticipants
                .OrderBy(item => item.Order)
                .Select(item => new AuraCgSceneParticipantSource
                {
                    Order = item.Order,
                    PlayerId = item.PlayerId,
                    RoleId = item.RoleId,
                    RoleVariantId = item.RoleVariantId,
                    RoleLayerAsset = new AuraCgSceneAssetReference
                    {
                        OwnerModId = AuraToolsIds.ModId,
                        AssetId = AuraToolsCgSceneAssetResolver.RoleIdleAssetId
                    }
                })
                .ToList()
        };
    }

    public static AuraCgSceneSourceSnapshot? BuildPreviewSource(
        string sceneId,
        string eventToken,
        int participantCount)
    {
        var roles = RoleCatalog.GetRoles()
            .Where(role => role != null && !string.IsNullOrWhiteSpace(role.Id))
            .ToList();
        if (roles.Count == 0)
        {
            return BuildSource(sceneId, eventToken);
        }

        var count = Math.Max(1, Math.Min(AuraCgSceneProtocol.MaximumParticipants, participantCount));
        var result = new AuraCgSceneSourceSnapshot
        {
            SceneId = sceneId,
            EventToken = eventToken
        };
        for (var index = 0; index < count; index++)
        {
            var role = roles[index % roles.Count];
            var playerId = "preview-seat-" + index;
            result.Participants.Add(new AuraCgSceneParticipantSource
            {
                Order = index,
                PlayerId = playerId,
                RoleId = role.Id,
                RoleVariantId = SkinRuntime.GetSelectedQualifiedSkinId(role.Id, playerId),
                RoleLayerAsset = new AuraCgSceneAssetReference
                {
                    OwnerModId = AuraToolsIds.ModId,
                    AssetId = AuraToolsCgSceneAssetResolver.RoleIdleAssetId
                }
            });
        }

        return result;
    }

    private static void MergeCurrentTeam(bool replace)
    {
        var current = CollectCurrentTeam();
        if (replace)
        {
            StableParticipants.Clear();
        }

        foreach (var participant in current)
        {
            var existingIndex = StableParticipants.FindIndex(item =>
                Same(item.PlayerId, participant.PlayerId));
            if (existingIndex >= 0)
            {
                StableParticipants[existingIndex] = participant;
            }
            else
            {
                StableParticipants.Add(participant);
            }
        }

        StableParticipants.Sort((left, right) =>
        {
            var order = left.Order.CompareTo(right.Order);
            return order != 0
                ? order
                : string.Compare(left.PlayerId, right.PlayerId, StringComparison.OrdinalIgnoreCase);
        });
        if (StableParticipants.Count > AuraCgSceneProtocol.MaximumParticipants)
        {
            StableParticipants.RemoveRange(
                AuraCgSceneProtocol.MaximumParticipants,
                StableParticipants.Count - AuraCgSceneProtocol.MaximumParticipants);
        }
    }

    private static List<ParticipantSnapshot> CollectCurrentTeam()
    {
        var result = new List<ParticipantSnapshot>();
        try
        {
            var roleTables = GameServer.Instance?.RoleTables;
            var roles = roleTables?.Values
                .Where(role => role != null)
                .ToList() ?? new List<RoleTable>();
            var orderedPlayerIds = GameServer.Instance?.LobbyInfo?.AddedPlayers?
                .Where(player => player != null && !string.IsNullOrWhiteSpace(player.Id))
                .Select(player => player.Id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            var order = 0;
            foreach (var playerId in orderedPlayerIds)
            {
                var role = roles.FirstOrDefault(candidate => Same(candidate.Id, playerId));
                if (role == null) continue;
                Add(result, role, order++);
                roles.Remove(role);
            }

            foreach (var role in roles.OrderBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase))
            {
                Add(result, role, order++);
            }

            if (result.Count == 0 && RoleTable.Instance != null)
            {
                Add(result, RoleTable.Instance, 0);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[CG] team snapshot failed: " + ex.Message);
        }

        return result;
    }

    private static void Add(
        ICollection<ParticipantSnapshot> result,
        RoleTable role,
        int order)
    {
        var playerId = (role.Id ?? "").Trim();
        var roleId = RoleCatalog.NormalizeRoleId(ReadDataId(role.Career));
        if (string.IsNullOrWhiteSpace(roleId)) return;
        var variantId = SkinRuntime.GetSelectedQualifiedSkinId(roleId, playerId);
        result.Add(new ParticipantSnapshot
        {
            Order = order,
            PlayerId = string.IsNullOrWhiteSpace(playerId) ? "seat-" + order : playerId,
            RoleId = roleId,
            RoleVariantId = variantId
        });
    }

    private static string ReadDataId(IDataConfig? data)
    {
        try
        {
            return data?.data != null && data.data.TryGetValue("Id", out var value)
                ? value ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static bool Same(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
               && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ParticipantSnapshot
    {
        public int Order { get; set; }

        public string PlayerId { get; set; } = "";

        public string RoleId { get; set; } = "";

        public string RoleVariantId { get; set; } = "";
    }
}
