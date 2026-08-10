using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritBattleDeploymentService
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> ConsumedOwners = new(StringComparer.Ordinal);
    private static List<string> partyUids = new();
    private static SpiritInstance? active;
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
            ConsumedOwners.Clear();
            var byUid = collection.Instances.ToDictionary(item => item.SpiritUid, StringComparer.Ordinal);
            partyUids = (party.PartySlots ?? new List<string>()).Where(byUid.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
            var activeUid = party.ActiveSpiritUid ?? "";
            active = partyUids.Contains(activeUid, StringComparer.Ordinal)
                && byUid.TryGetValue(activeUid, out var selected)
                    ? selected.Clone()
                    : null;
            deploymentToken = active == null ? "" : epoch + ":" + active.SpiritUid + ":" + Guid.NewGuid().ToString("N");
            battleToken = "spirit-xp:" + epoch + ":" + Guid.NewGuid().ToString("N");
            battleExperience = Math.Max(0, experienceReward);
        }
    }

    public static CapturedEnemySnapshot? DeploymentCardSnapshot()
    {
        lock (SyncRoot)
        {
            if (active == null) return null;
            var profile = SpiritGrowthRegistry.Resolve(active);
            var origins = SpiritGrowthService.OriginsAt(profile, active.Level, active.Aptitude);
            var snapshot = SpiritModelCloner.CloneSnapshot(active.Snapshot);
            snapshot.SpeciesId = active.SpeciesId;
            snapshot.ProfileId = active.ProfileId;
            snapshot.SpiritLevel = active.Level;
            snapshot.SpiritAptitude = active.Aptitude;
            snapshot.OriginMagic = origins.Magic;
            snapshot.OriginSpirit = origins.Spirit;
            snapshot.OriginLuck = origins.Luck;
            snapshot.OriginPerception = origins.Perception;
            snapshot.DeploymentToken = deploymentToken;
            return snapshot;
        }
    }

    public static bool CanSummon(CapturedEnemySnapshot snapshot, string ownerStatusId, bool acceptRemotePayload, out string reason)
    {
        lock (SyncRoot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DeploymentToken))
            {
                reason = "这张精灵卡不属于本场战斗的出战快照。";
                return false;
            }
            if (ConsumedOwners.Contains(ownerStatusId ?? ""))
            {
                reason = "本场战斗的精灵已经出战。";
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
        lock (SyncRoot) ConsumedOwners.Add(ownerStatusId ?? "");
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
            ConsumedOwners.Clear();
            partyUids.Clear();
            active = null;
            deploymentToken = "";
            battleToken = "";
            battleExperience = 0;
        }
    }
}
