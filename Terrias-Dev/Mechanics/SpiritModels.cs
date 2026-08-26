using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class CapturedEnemySnapshot
{
    public string SpiritUid { get; set; } = "";
    public string SourceModId { get; set; } = "";
    public string EnemyId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string InstanceId { get; set; } = "";
    // Protocol v16 treats these as compatibility fallbacks only. Identity and local
    // presentation resolution must use EnemyId/VariantId instead of trusting wire text.
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string AnimationPath { get; set; } = "";
    public string DictPath { get; set; } = "";
    public string IdlePath { get; set; } = "";
    public string CaptureOrigin { get; set; } = "";
    public string CapturedAt { get; set; } = "";
    public int BaseHp { get; set; }
    public int BaseAttack { get; set; }
    public int BaseArmor { get; set; }
    public int Rarity { get; set; }
    public List<string> SourceEnemyCardIds { get; set; } = new();

    // Populated only on the temporary battle deployment card/network payload.
    // Permanent collection snapshots keep these fields at their defaults.
    public string SpeciesId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string SpiritElementId { get; set; } = "";
    public int SpiritLevel { get; set; }
    public int SpiritAptitude { get; set; }
    public int SpiritGuiyuanValue { get; set; }
    public int SpiritStarRank { get; set; }
    public int GuiyuanAllocationMagic { get; set; }
    public int GuiyuanAllocationSpirit { get; set; }
    public int GuiyuanAllocationLuck { get; set; }
    public int GuiyuanAllocationPerception { get; set; }
    public int OriginMagic { get; set; }
    public int OriginSpirit { get; set; }
    public int OriginLuck { get; set; }
    public int OriginPerception { get; set; }
    public int SpiritSpeed { get; set; } = 100;
    public List<string> EquippedIntentIds { get; set; } = new();
    public string EquippedPassiveId { get; set; } = "";
    public int LoadoutRevision { get; set; }
    public string LoadoutHash { get; set; } = "";
    public string TrainingRegistryHash { get; set; } = "";
    public string DeploymentToken { get; set; } = "";

    public string ProfileKey => SpiritProfileKey.Create(EnemyId, VariantId);

    public string IntentProfileKey => string.IsNullOrWhiteSpace(ProfileId) ? ProfileKey : ProfileId;
}

[Serializable]
public sealed class SpiritCardBattleState
{
    public int TurnIndex { get; set; }

    public Dictionary<string, int> ReadyOnTurn { get; set; } = new(StringComparer.Ordinal);

    public int MaxHp { get; set; }

    public int CurrentHp { get; set; }

    public int CurrentDefend { get; set; }

    public int CurrentMagic { get; set; }

    public Dictionary<string, int> PassiveState { get; set; } = new(StringComparer.Ordinal);

    public List<SpiritVisibleStatusSnapshot> VisibleStatuses { get; set; } = new();

    public static SpiritCardBattleState From(CompanionBattleState? state)
    {
        return new SpiritCardBattleState
        {
            TurnIndex = Math.Max(0, state?.TurnIndex ?? 0),
            ReadyOnTurn = state == null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : state.ReadyOnTurnSnapshot()
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            CurrentMagic = Math.Max(0, state?.Stats.CurrentMagic ?? 0),
            PassiveState = state == null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : state.PassiveStateSnapshot()
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            VisibleStatuses = state == null
                ? new List<SpiritVisibleStatusSnapshot>()
                : state.VisibleStatusSnapshot().Select(status => status.Clone()).ToList()
        };
    }
}

public sealed class SpiritEligibilityResult
{
    private SpiritEligibilityResult(bool eligible, string reason, CapturedEnemySnapshot? snapshot)
    {
        Eligible = eligible;
        Reason = reason ?? "";
        Snapshot = snapshot;
    }

    public bool Eligible { get; }

    public string Reason { get; }

    public CapturedEnemySnapshot? Snapshot { get; }

    public static SpiritEligibilityResult Allow(CapturedEnemySnapshot snapshot) => new(true, "", snapshot);

    public static SpiritEligibilityResult Reject(string reason) => new(false, reason, null);
}

public static class SpiritProfileKey
{
    public static string Create(string enemyId, string variantId)
    {
        var enemy = (enemyId ?? "").Trim();
        var variant = string.IsNullOrWhiteSpace(variantId) ? enemy : variantId.Trim();
        return SpiritProfileIdentityResolver.CreateProfileKey(enemy, variant);
    }
}

[Serializable]
public sealed class SpiritIntentProfile
{
    public string ProfileId { get; set; } = "";

    public string EnemyId { get; set; } = "*";
    public string VariantId { get; set; } = "*";
    public List<string> SourceEnemyCardIds { get; set; } = new();
    public List<string> PveAttackTendency { get; set; } = new();
    public List<string> PveDefenseTendency { get; set; } = new();
    public List<string> PvpAttackTendency { get; set; } = new();
    public List<string> PvpDefenseTendency { get; set; } = new();
    public List<string> FallbackAttackTendency { get; set; } = new();
    public List<string> FallbackDefenseTendency { get; set; } = new();
    public List<string> PvpSourceEnemyCardIds { get; set; } = new();
    public List<string> FallbackSourceEnemyCardIds { get; set; } = new();
    public int AttackWeight { get; set; } = 60;
    public int DefenseWeight { get; set; } = 40;
    public float HpMultiplier { get; set; } = 1f;
    public float MagicMultiplier { get; set; } = 1f;
    public float AttackMultiplier { get; set; } = 1f;
    public float ArmorMultiplier { get; set; } = 1f;
}

[Serializable]
public sealed class SpiritIntentRegistryDocument
{
    public int SchemaVersion { get; set; } = SpiritSystemContract.IntentRegistrySchemaVersion;
    public List<CompanionIntentDefinition> Intents { get; set; } = new();
    public List<SpiritIntentProfile> Profiles { get; set; } = new();
}

[Serializable]
public sealed class SpiritCaptureProfile
{
    public string EnemyId { get; set; } = "*";
    public string VariantId { get; set; } = "*";
    public string ResolutionMode { get; set; } = "GuardedTerminal";
    public List<string> SuppressedSuccessorIds { get; set; } = new();
    public bool RunNativeDeath { get; set; } = true;
    public bool AllowRewards { get; set; } = true;
}

[Serializable]
public sealed class SpiritCaptureRegistryDocument
{
    public int SchemaVersion { get; set; } = SpiritSystemContract.CaptureRegistrySchemaVersion;
    public List<SpiritCaptureProfile> Profiles { get; set; } = new();
}
