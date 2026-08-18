using System;
using System.Collections.Generic;
using System.Linq;

namespace Terrias.Dll.Mechanics;

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

public enum SpiritIntentPool
{
    Pve,
    PvpReserved,
    Fallback
}

public sealed class CompanionStats
{
    public CompanionStats(int maxHp, int maxMagic, int attack, int armor, int speed = 100)
    {
        MaxHp = Math.Max(1, maxHp);
        MaxMagic = Math.Max(1, maxMagic);
        CurrentMagic = MaxMagic;
        Attack = Math.Max(0, attack);
        Armor = Math.Max(0, armor);
        Speed = Math.Max(1, speed);
    }

    public int MaxHp { get; }

    public int MaxMagic { get; }

    public int CurrentMagic { get; private set; }

    public int Attack { get; }

    public int Armor { get; }

    public int Speed { get; }

    public bool TrySpendMagic(int amount)
    {
        var cost = Math.Max(0, amount);
        if (CurrentMagic < cost)
        {
            return false;
        }

        CurrentMagic -= cost;
        return true;
    }

    public void SpendMagic(int amount)
    {
        TrySpendMagic(amount);
    }

    public void RecoverMagic(int amount)
    {
        CurrentMagic = Math.Min(MaxMagic, CurrentMagic + Math.Max(0, amount));
    }

    public void SetCurrentMagic(int value)
    {
        CurrentMagic = Math.Max(0, Math.Min(MaxMagic, value));
    }
}

[Serializable]
public sealed class CompanionEntityIdentity
{
    public string StatusId { get; set; } = "";

    public string OwnerPlayerId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string Faction { get; set; } = "Friendly";

    public string EntityKind { get; set; } = "Companion";

    public int SlotIndex { get; set; } = -1;
}

[Serializable]
public sealed class CompanionIntentPlan
{
    public string PlanId { get; set; } = "";

    public string StatusId { get; set; } = "";

    public int TurnIndex { get; set; }

    public string IntentId { get; set; } = "";

    public string EnemyCardId { get; set; } = "";

    public List<string> OrderedTargetIds { get; set; } = new();

    public int ResolvedValue { get; set; }

    public int Cost { get; set; }

    public int ReadyOnTurn { get; set; }

    public int PreviewThreat { get; set; }

    public int Priority { get; set; } = 1;

    public int StateRevision { get; set; }

    public bool IsWait { get; set; }

    public int NumericBonusPercent { get; set; }

    public List<string> AppliedModifierKeys { get; set; } = new();

    public List<CompanionResolvedEffect> ResolvedEffects { get; set; } = new();

    public CompanionIntentPlan Snapshot()
    {
        return new CompanionIntentPlan
        {
            PlanId = PlanId,
            StatusId = StatusId,
            TurnIndex = TurnIndex,
            IntentId = IntentId,
            EnemyCardId = EnemyCardId,
            OrderedTargetIds = new List<string>(OrderedTargetIds ?? new List<string>()),
            ResolvedValue = ResolvedValue,
            Cost = Cost,
            ReadyOnTurn = ReadyOnTurn,
            PreviewThreat = PreviewThreat,
            Priority = Priority,
            StateRevision = StateRevision,
            IsWait = IsWait,
            NumericBonusPercent = NumericBonusPercent,
            AppliedModifierKeys = new List<string>(AppliedModifierKeys ?? new List<string>()),
            ResolvedEffects = (ResolvedEffects ?? new List<CompanionResolvedEffect>())
                .Select(effect => effect.Snapshot())
                .ToList()
        };
    }
}

[Serializable]
public sealed class CompanionResolvedEffect
{
    public string HandlerId { get; set; } = "";

    public List<string> TargetIds { get; set; } = new();

    public int Value { get; set; }

    public int RepeatCount { get; set; } = 1;

    public string BuffId { get; set; } = "";

    public int BuffStacks { get; set; }

    public CompanionResolvedEffect Snapshot()
    {
        return new CompanionResolvedEffect
        {
            HandlerId = HandlerId,
            TargetIds = new List<string>(TargetIds ?? new List<string>()),
            Value = Value,
            RepeatCount = RepeatCount,
            BuffId = BuffId,
            BuffStacks = BuffStacks
        };
    }
}

public sealed class CompanionBattleState
{
    private readonly Dictionary<string, int> readyOnTurn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> passiveState = new(StringComparer.Ordinal);
    private List<SpiritVisibleStatusSnapshot> visibleStatuses = new();

    public CompanionBattleState(
        string statusId,
        string roleId,
        string ownerStatusId,
        int slotIndex,
        CompanionStats stats,
        string ownerPlayerId = "",
        string entityKind = "ProjectionAttachment")
    {
        Identity = new CompanionEntityIdentity
        {
            StatusId = statusId ?? "",
            OwnerPlayerId = ownerPlayerId ?? "",
            OwnerStatusId = ownerStatusId ?? "",
            RoleId = roleId ?? "",
            SlotIndex = slotIndex,
            EntityKind = string.IsNullOrWhiteSpace(entityKind) ? "Companion" : entityKind.Trim()
        };
        Stats = stats;
    }

    public CompanionEntityIdentity Identity { get; }

    public string StatusId => Identity.StatusId;

    public string RoleId => Identity.RoleId;

    public string OwnerStatusId => Identity.OwnerStatusId;

    public string OwnerPlayerId => Identity.OwnerPlayerId;

    public string EntityKind => Identity.EntityKind;

    public int SlotIndex => Identity.SlotIndex;

    public CompanionStats Stats { get; }

    public List<string> EquippedIntentIds { get; private set; } = new();

    public string EquippedPassiveId { get; private set; } = "";

    public int LoadoutRevision { get; private set; }

    public string LoadoutHash { get; private set; } = "";

    public string CurrentIntentId { get; set; } = "";

    public CompanionIntentPlan? CurrentPlan { get; set; }

    public int TurnIndex { get; private set; }

    public int Revision { get; private set; }

    public int Cooldown(string intentId)
    {
        return !string.IsNullOrWhiteSpace(intentId) && readyOnTurn.TryGetValue(intentId, out var value)
            ? Math.Max(0, value - TurnIndex)
            : 0;
    }

    public bool IsReady(string intentId)
    {
        return Cooldown(intentId) <= 0;
    }

    public void StartCooldown(string intentId, int turns)
    {
        if (string.IsNullOrWhiteSpace(intentId))
        {
            return;
        }

        readyOnTurn[intentId] = TurnIndex + Math.Max(0, turns) + 1;
    }

    public int ReadyOnTurn(string intentId)
    {
        return !string.IsNullOrWhiteSpace(intentId) && readyOnTurn.TryGetValue(intentId, out var value)
            ? Math.Max(0, value)
            : TurnIndex;
    }

    public IReadOnlyDictionary<string, int> ReadyOnTurnSnapshot()
    {
        return new Dictionary<string, int>(readyOnTurn, StringComparer.Ordinal);
    }

    public void ApplyReadyOnTurn(IReadOnlyDictionary<string, int>? values)
    {
        readyOnTurn.Clear();
        if (values == null)
        {
            return;
        }

        foreach (var entry in values)
        {
            if (!string.IsNullOrWhiteSpace(entry.Key))
            {
                readyOnTurn[entry.Key] = Math.Max(0, entry.Value);
            }
        }
    }

    public void ConfigureLoadout(
        IEnumerable<string>? intentIds,
        string passiveId,
        int revision,
        string hash)
    {
        EquippedIntentIds = (intentIds ?? Array.Empty<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(SpiritTrainingService.EquippedIntentCapacity)
            .ToList();
        EquippedPassiveId = (passiveId ?? "").Trim();
        LoadoutRevision = Math.Max(0, revision);
        LoadoutHash = (hash ?? "").Trim();
    }

    public int PassiveValue(string key)
    {
        return passiveState.TryGetValue(key ?? "", out var value) ? value : 0;
    }

    public void SetPassiveValue(string key, int value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (value == 0) passiveState.Remove(key);
        else passiveState[key] = value;
    }

    public IReadOnlyDictionary<string, int> PassiveStateSnapshot()
    {
        return new Dictionary<string, int>(passiveState, StringComparer.Ordinal);
    }

    public void ApplyPassiveState(IReadOnlyDictionary<string, int>? values)
    {
        passiveState.Clear();
        foreach (var entry in values ?? new Dictionary<string, int>())
        {
            if (!string.IsNullOrWhiteSpace(entry.Key)) passiveState[entry.Key] = entry.Value;
        }
    }

    public IReadOnlyList<SpiritVisibleStatusSnapshot> VisibleStatusSnapshot()
    {
        return visibleStatuses.Select(status => status.Clone()).ToArray();
    }

    public void ApplyVisibleStatuses(IEnumerable<SpiritVisibleStatusSnapshot>? values)
    {
        visibleStatuses = (values ?? Array.Empty<SpiritVisibleStatusSnapshot>())
            .Where(status => status != null && !string.IsNullOrWhiteSpace(status.Id))
            .Take(SpiritSystemContract.MaximumVisibleStatuses)
            .Select(status => status.Clone())
            .ToList();
    }

    public void AdvanceTurn()
    {
        TurnIndex++;
        Revision++;
    }

    public void TouchRevision()
    {
        Revision++;
    }

    public void ApplyRemoteProgress(int turnIndex, int revision)
    {
        TurnIndex = Math.Max(TurnIndex, turnIndex);
        Revision = Math.Max(Revision, revision);
    }
}

public sealed class CompanionIntentDefinition
{
    public string Id { get; set; } = "";

    public string EnemyCardId { get; set; } = "";

    public string Pool { get; set; } = "Common";

    public string AdaptationNote { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Description { get; set; } = "";

    public string Type { get; set; } = "Attack";

    public int Cost { get; set; }

    public int Cooldown { get; set; }

    public int BasePriority { get; set; } = 10;

    public string HandlerId { get; set; } = "";

    public CompanionIntentTargetSpec Target { get; set; } = new();

    public int HitCount { get; set; } = 1;

    public string BuffId { get; set; } = "";

    public int BuffStacks { get; set; }

    public int FlatValue { get; set; }

    public float AttackScale { get; set; }

    public float ArmorScale { get; set; }

    public float MagicScale { get; set; }

    public float SpeedScale { get; set; }

    public string EligibilityPolicy { get; set; } = "";

    public string PriorityBonus { get; set; } = "";

    public CompanionIntentThreatSpec Threat { get; set; } = new();

    // Schema 3 spirit intents may preserve several effects from one native
    // enemy card. Empty keeps the projection registry backward compatible
    // with its original single-effect shape.
    public List<CompanionIntentEffectSpec> Effects { get; set; } = new();
}

public sealed class CompanionIntentEffectSpec
{
    public string HandlerId { get; set; } = "";

    public CompanionIntentTargetSpec Target { get; set; } = new();

    public int HitCount { get; set; } = 1;

    public string BuffId { get; set; } = "";

    public int BuffStacks { get; set; }

    public int FlatValue { get; set; }

    public float AttackScale { get; set; }

    public float ArmorScale { get; set; }

    public float MagicScale { get; set; }

    public float SpeedScale { get; set; }

    public int DisplayIndex { get; set; } = 1;
}

public static class CompanionIntentEffects
{
    public static IReadOnlyList<CompanionIntentEffectSpec> Expand(CompanionIntentDefinition intent)
    {
        if (intent?.Effects != null && intent.Effects.Count > 0)
        {
            return intent.Effects;
        }

        return intent == null
            ? Array.Empty<CompanionIntentEffectSpec>()
            : new[] { FromLegacy(intent) };
    }

    public static CompanionIntentDefinition AsDefinition(
        CompanionIntentDefinition parent,
        CompanionIntentEffectSpec effect)
    {
        return new CompanionIntentDefinition
        {
            Id = parent.Id,
            EnemyCardId = parent.EnemyCardId,
            Pool = parent.Pool,
            AdaptationNote = parent.AdaptationNote,
            DisplayName = parent.DisplayName,
            Description = parent.Description,
            Type = parent.Type,
            Cost = parent.Cost,
            Cooldown = parent.Cooldown,
            BasePriority = parent.BasePriority,
            HandlerId = effect.HandlerId,
            Target = effect.Target,
            HitCount = effect.HitCount,
            BuffId = effect.BuffId,
            BuffStacks = effect.BuffStacks,
            FlatValue = effect.FlatValue,
            AttackScale = effect.AttackScale,
            ArmorScale = effect.ArmorScale,
            MagicScale = effect.MagicScale,
            SpeedScale = effect.SpeedScale,
            EligibilityPolicy = parent.EligibilityPolicy,
            PriorityBonus = parent.PriorityBonus,
            Threat = parent.Threat
        };
    }

    public static CompanionIntentEffectSpec FromLegacy(CompanionIntentDefinition intent)
    {
        return new CompanionIntentEffectSpec
        {
            HandlerId = intent.HandlerId,
            Target = intent.Target,
            HitCount = intent.HitCount,
            BuffId = intent.BuffId,
            BuffStacks = intent.BuffStacks,
            FlatValue = intent.FlatValue,
            AttackScale = intent.AttackScale,
            ArmorScale = intent.ArmorScale,
            MagicScale = intent.MagicScale,
            SpeedScale = intent.SpeedScale,
            DisplayIndex = 1
        };
    }
}

public sealed class CompanionIntentThreatSpec
{
    public int Preview { get; set; }

    public int OnUse { get; set; }

    public int Decay { get; set; } = 4;

}

public sealed class CompanionIntentTargetSpec
{
    public string Scope { get; set; } = "";

    public string Mode { get; set; } = "Single";

    public string Policy { get; set; } = "";
}

public sealed class CompanionIntentProfile
{
    public string RoleId { get; set; } = "*";

    public List<string> AttackTendency { get; set; } = new();

    public List<string> DefenseTendency { get; set; } = new();

    public int AttackWeight { get; set; } = 60;

    public int DefenseWeight { get; set; } = 40;
}

public sealed class CompanionIntentRegistryDocument
{
    public int SchemaVersion { get; set; } = 3;

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
