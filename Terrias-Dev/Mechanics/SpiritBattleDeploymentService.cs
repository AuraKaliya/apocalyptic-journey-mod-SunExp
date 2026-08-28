using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritBattleDeploymentService
{
    private static readonly object SyncRoot = new();
    private static List<string> partyUids = new();
    private static SpiritInstance? active;
    private static SpiritArtifactBattleSnapshot activeArtifacts = new();
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
            active = partyUids.Contains(activeUid, StringComparer.Ordinal)
                && byUid.TryGetValue(activeUid, out var selected)
                    ? selected.Clone()
                    : null;
            activeArtifacts = active == null
                ? new SpiritArtifactBattleSnapshot()
                : SpiritArtifactLoadoutResolver.Resolve(collection, active).Battle;
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
            var origins = SpiritAscensionService.EffectiveOrigins(active);
            var snapshot = SpiritModelCloner.CloneSnapshot(active.Snapshot);
            snapshot.SpeciesId = active.SpeciesId;
            snapshot.ProfileId = active.ProfileId;
            snapshot.SpiritElementId = active.ElementId;
            snapshot.SpiritLevel = active.Level;
            snapshot.SpiritAptitude = active.Aptitude;
            snapshot.SpiritGuiyuanValue = active.GuiyuanValue;
            snapshot.SpiritStarRank = SpiritAscensionService.StarRankFor(active.GuiyuanValue);
            var allocations = SpiritAscensionService.NormalizeAllocations(active.GuiyuanAllocations, active.GuiyuanValue);
            snapshot.GuiyuanAllocationMagic = allocations.Magic;
            snapshot.GuiyuanAllocationSpirit = allocations.Spirit;
            snapshot.GuiyuanAllocationLuck = allocations.Luck;
            snapshot.GuiyuanAllocationPerception = allocations.Perception;
            snapshot.OriginMagic = origins.Magic;
            snapshot.OriginSpirit = origins.Spirit;
            snapshot.OriginLuck = origins.Luck;
            snapshot.OriginPerception = origins.Perception;
            snapshot.SpiritSpeed = active.Speed;
            snapshot.EquippedIntentIds = new List<string>(active.EquippedIntentIds ?? new List<string>());
            snapshot.EquippedPassiveId = active.EquippedPassiveId;
            snapshot.LoadoutRevision = active.LoadoutRevision;
            snapshot.LoadoutHash = active.LoadoutHash;
            snapshot.TrainingRegistryHash = SpiritTrainingRegistry.RegistryHash;
            snapshot.ArtifactBattle = activeArtifacts.Clone();
            snapshot.DeploymentToken = deploymentToken;
            return snapshot;
        }
    }

    public static SpiritCardBattleState CreateInitialBattleState(CapturedEnemySnapshot snapshot)
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

    public static bool CanSummon(CapturedEnemySnapshot snapshot, string ownerStatusId, bool acceptRemotePayload, out string reason)
    {
        lock (SyncRoot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.DeploymentToken))
            {
                reason = "这张精灵卡不属于本场战斗的出战快照。";
                return false;
            }
            if (!SpiritTrainingService.ValidateDeploymentSnapshot(snapshot, out reason))
            {
                return false;
            }
            if (!SpiritAscensionService.ValidateDeploymentSnapshot(snapshot, out reason))
            {
                return false;
            }
            if (!SpiritElementService.ValidateDeploymentSnapshot(snapshot, out reason))
            {
                return false;
            }
            if (!SpiritArtifactLoadoutResolver.ValidateBattleSnapshot(snapshot.ArtifactBattle, out reason))
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
            activeArtifacts = new SpiritArtifactBattleSnapshot();
            deploymentToken = "";
            battleToken = "";
            battleExperience = 0;
        }
    }
}
