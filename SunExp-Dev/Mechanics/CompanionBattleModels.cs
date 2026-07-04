using System;
using System.Collections.Generic;

namespace SunExp.Dll.Mechanics;

public enum CompanionIntentType
{
    Attack,
    Defense,
    Support,
    Recovery,
    Interference
}

public enum CompanionIntentTendency
{
    Attack,
    Defense
}

public sealed class CompanionStats
{
    public CompanionStats(int maxHp, int maxMagic, int attack, int armor)
    {
        MaxHp = Math.Max(1, maxHp);
        MaxMagic = Math.Max(1, maxMagic);
        CurrentMagic = MaxMagic;
        Attack = Math.Max(0, attack);
        Armor = Math.Max(0, armor);
    }

    public int MaxHp { get; }

    public int MaxMagic { get; }

    public int CurrentMagic { get; private set; }

    public int Attack { get; }

    public int Armor { get; }

    public void SpendMagic(int amount)
    {
        CurrentMagic = Math.Max(0, CurrentMagic - Math.Max(0, amount));
    }

    public void RecoverMagic(int amount)
    {
        CurrentMagic = Math.Min(MaxMagic, CurrentMagic + Math.Max(0, amount));
    }
}

public sealed class CompanionBattleState
{
    private readonly Dictionary<string, int> cooldowns = new(StringComparer.Ordinal);

    public CompanionBattleState(string statusId, string roleId, string ownerStatusId, int slotIndex, CompanionStats stats)
    {
        StatusId = statusId ?? "";
        RoleId = roleId ?? "";
        OwnerStatusId = ownerStatusId ?? "";
        SlotIndex = slotIndex;
        Stats = stats;
    }

    public string StatusId { get; }

    public string RoleId { get; }

    public string OwnerStatusId { get; }

    public int SlotIndex { get; }

    public CompanionStats Stats { get; }

    public string CurrentIntentId { get; set; } = "";

    public int Cooldown(string intentId)
    {
        return !string.IsNullOrWhiteSpace(intentId) && cooldowns.TryGetValue(intentId, out var value)
            ? Math.Max(0, value)
            : 0;
    }

    public void StartCooldown(string intentId, int turns)
    {
        if (string.IsNullOrWhiteSpace(intentId))
        {
            return;
        }

        cooldowns[intentId] = Math.Max(0, turns);
    }

    public void TickCooldowns()
    {
        var keys = new List<string>(cooldowns.Keys);
        foreach (var key in keys)
        {
            cooldowns[key] = Math.Max(0, cooldowns[key] - 1);
        }
    }
}

public sealed class CompanionIntentDefinition
{
    public string Id { get; set; } = "";

    public string EnemyCardId { get; set; } = "";

    public string Type { get; set; } = "Attack";

    public int Cost { get; set; }

    public int Cooldown { get; set; }

    public int BasePriority { get; set; } = 10;

    public string Effect { get; set; } = "";

    public int FlatValue { get; set; }

    public float AttackScale { get; set; }

    public float ArmorScale { get; set; }

    public float MagicScale { get; set; }

    public string PriorityBonus { get; set; } = "";

    public CompanionIntentThreatSpec Threat { get; set; } = new();
}

public sealed class CompanionIntentThreatSpec
{
    public int Preview { get; set; }

    public int OnUse { get; set; }

    public int Decay { get; set; } = 4;

    public string TargetBias { get; set; } = "";
}

public sealed class CompanionIntentProfile
{
    public string RoleId { get; set; } = "*";

    public List<string> AttackTendency { get; set; } = new();

    public List<string> DefenseTendency { get; set; } = new();
}

public sealed class CompanionIntentRegistryDocument
{
    public List<CompanionIntentDefinition> Intents { get; set; } = new();

    public List<CompanionIntentProfile> Profiles { get; set; } = new();
}

public readonly struct CompanionIntentChoice
{
    public CompanionIntentChoice(CompanionIntentDefinition intent, IStatusManager? target, int priority)
    {
        Intent = intent;
        Target = target;
        Priority = Math.Max(1, priority);
    }

    public CompanionIntentDefinition Intent { get; }

    public IStatusManager? Target { get; }

    public int Priority { get; }
}
