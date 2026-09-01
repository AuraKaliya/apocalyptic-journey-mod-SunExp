using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritBattleDeploymentService
{
    private static readonly object SyncRoot = new();
    private static List<string> partyUids = new();
    private static SpiritDeploymentSnapshot? active;
    private static string deploymentToken = "";
    private static string battleToken = "";
    private static int battleExperience;

    public static void Begin(
        SpiritAdventureParty party,
        SpiritCollectionDocument collection,
        int epoch,
        int experienceReward)
    {
        lock (SyncRoot)
        {
            var byUid = collection.Instances.ToDictionary(item => item.SpiritUid, StringComparer.Ordinal);
            partyUids = (party.PartySlots ?? new List<string>()).Where(byUid.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
            var activeUid = party.ActiveSpiritUid ?? "";
            var selected = partyUids.Contains(activeUid, StringComparer.Ordinal)
                && byUid.TryGetValue(activeUid, out var selectedInstance)
                    ? selectedInstance
                    : null;
            deploymentToken = selected == null ? "" : epoch + ":" + selected.SpiritUid + ":" + Guid.NewGuid().ToString("N");
            active = selected == null ? null : SpiritDeploymentProjector.Project(collection, selected, deploymentToken);
            battleToken = "spirit-xp:" + epoch + ":" + Guid.NewGuid().ToString("N");
            battleExperience = Math.Max(0, experienceReward);
        }
    }

    public static SpiritDeploymentSnapshot? DeploymentCardSnapshot()
    {
        lock (SyncRoot)
        {
            if (active == null) return null;
            return active.Clone();
        }
    }

    public static SpiritCardBattleState CreateInitialBattleState(SpiritDeploymentSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return new SpiritCardBattleState();
        }

        var profile = SpiritIntentRegistry.ResolveProfileIdentity(snapshot.ProfileId, snapshot.ProfileKey).Profile;
        var stats = CompanionStatsService.SpiritStats(snapshot, profile);
        var result = new SpiritCardBattleState
        {
            MaxHp = stats.MaxHp,
            CurrentHp = stats.MaxHp,
            CurrentMagic = stats.MaxMagic
        };
        if (snapshot.ArtifactBattle?.StartExtraordinary > 0)
        {
            result.VisibleStatuses.Add(new SpiritVisibleStatusSnapshot
            {
                Kind = "Buff",
                Id = Terrias.Dll.Infrastructure.TerriasIds.Extraordinary,
                Stacks = snapshot.ArtifactBattle.StartExtraordinary
            });
        }
        return result;
    }

    public static bool CanSummon(SpiritDeploymentSnapshot snapshot, string ownerStatusId, bool acceptRemotePayload, out string reason)
    {
        lock (SyncRoot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DeploymentToken))
            {
                reason = "这张精灵卡不属于本场战斗的出战快照。";
                return false;
            }
            if (!SpiritDeploymentFeatureRegistry.Validate(snapshot, out reason))
            {
                return false;
            }
            if (!acceptRemotePayload && (!string.Equals(snapshot.DeploymentToken, deploymentToken, StringComparison.Ordinal)
                                         || active == null
                                         || !string.Equals(snapshot.SpiritUid, active.SpiritUid, StringComparison.Ordinal)))
            {
                reason = "出战精灵快照已经失效。";
                return false;
            }
            reason = "";
            return true;
        }
    }

    public static void MarkSummoned(string ownerStatusId)
    {
        // Active ownership is tracked by SpiritStateStore. A withdrawn Spirit
        // may be summoned again in the same battle.
    }

    public static void RaiseExperienceReward(int reward)
    {
        lock (SyncRoot) battleExperience = Math.Max(battleExperience, Math.Max(0, reward));
    }

    public static (IReadOnlyList<string> PartyUids, string ActiveUid, int Experience, string BattleToken) ExperienceSnapshot()
    {
        lock (SyncRoot)
        {
            return (partyUids.ToArray(), active?.SpiritUid ?? "", battleExperience, battleToken);
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            partyUids.Clear();
            active = null;
            deploymentToken = "";
            battleToken = "";
            battleExperience = 0;
        }
    }
}
