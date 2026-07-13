using System;
using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

[Serializable]
public sealed class CapturedEnemySnapshot
{
    public string SpiritUid { get; set; } = "";
    public string SourceModId { get; set; } = "";
    public string EnemyId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public string InstanceId { get; set; } = "";
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

    public string ProfileKey => SpiritProfileKey.Create(EnemyId, VariantId);
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
        return "spirit:" + enemy + "#" + variant;
    }
}

[Serializable]
public sealed class SpiritIntentProfile
{
    public string EnemyId { get; set; } = "*";
    public string VariantId { get; set; } = "*";
    public List<string> SourceEnemyCardIds { get; set; } = new();
    public List<string> AttackTendency { get; set; } = new();
    public List<string> DefenseTendency { get; set; } = new();
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
    public int SchemaVersion { get; set; } = 1;
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
    public int SchemaVersion { get; set; } = 1;
    public List<SpiritCaptureProfile> Profiles { get; set; } = new();
}
