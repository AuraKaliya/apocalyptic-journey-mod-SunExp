using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal static class ReplayPlayableBootstrapContractV11
{
    internal static List<string> ValidateState(ReplayLogicalStateV11? state)
    {
        var errors = new List<string>();
        if (state == null)
        {
            errors.Add("initial logical state is missing");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(state.LevelId))
            errors.Add("initial logical state has no level id");

        var actors = state.Actors ?? new List<ReplayActorStateV11>();
        var players = actors.Where(value => Kind(value, ReplayEntityKindsV11.Player)).ToList();
        var enemies = actors.Where(value => Kind(value, ReplayEntityKindsV11.Enemy)).ToList();
        if (players.Count != 1)
            errors.Add("initial logical state must contain exactly one local player");
        if (enemies.Count == 0)
            errors.Add("initial logical state has no enemy presentation entities");

        var active = actors.FirstOrDefault(value =>
            string.Equals(value.InstanceId, state.ActiveActorId, StringComparison.Ordinal));
        if (active == null || !Kind(active, ReplayEntityKindsV11.Player))
            errors.Add("initial active actor is not the local player");

        foreach (var actor in actors)
        {
            if (string.IsNullOrWhiteSpace(actor.InstanceId)) continue;
            var expectedContentKind = ExpectedContentKind(actor.EntityKind);
            if (expectedContentKind.Length == 0)
            {
                errors.Add("initial actor has unsupported entity kind: " + actor.InstanceId);
                continue;
            }

            var content = actor.Content;
            if (content == null
                || string.IsNullOrWhiteSpace(content.OwnerModId)
                || string.IsNullOrWhiteSpace(content.StableContentId)
                || !string.Equals(content.ContentKind, expectedContentKind, StringComparison.Ordinal))
            {
                errors.Add("initial actor has no owner-qualified content identity: " + actor.InstanceId);
            }

            var expectedTeam = Kind(actor, ReplayEntityKindsV11.Enemy)
                ? ReplayTeamsV11.Enemy
                : ReplayTeamsV11.Friendly;
            if (!string.Equals(actor.Team, expectedTeam, StringComparison.Ordinal))
                errors.Add("initial actor has an invalid team: " + actor.InstanceId);
            if (actor.SlotIndex < 0)
                errors.Add("initial actor has a negative presentation slot: " + actor.InstanceId);
            if (actor.MaxHp <= 0)
                errors.Add("initial actor has no positive maximum health: " + actor.InstanceId);
        }

        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool Kind(ReplayActorStateV11 value, string expected)
    {
        return value != null && string.Equals(value.EntityKind, expected, StringComparison.Ordinal);
    }

    private static string ExpectedContentKind(string entityKind)
    {
        if (string.Equals(entityKind, ReplayEntityKindsV11.Player, StringComparison.Ordinal)
            || string.Equals(entityKind, ReplayEntityKindsV11.RemotePlayer, StringComparison.Ordinal))
            return "Role";
        if (string.Equals(entityKind, ReplayEntityKindsV11.Enemy, StringComparison.Ordinal)) return "Enemy";
        if (string.Equals(entityKind, ReplayEntityKindsV11.Summon, StringComparison.Ordinal)) return "Partner";
        return "";
    }
}
