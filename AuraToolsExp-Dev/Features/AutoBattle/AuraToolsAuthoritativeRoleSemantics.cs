using System;
using System.Collections.Generic;
using System.Linq;
using AuraCombatAi.Shared;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal static class AuraToolsNanaDoomProgression
{
    public static int MaximumHpGainAfterAdd(int currentLevel, int addedLevels)
    {
        if (addedLevels <= 0)
        {
            return 0;
        }
        return ClampNonNegative((long)Math.Max(0, currentLevel) + addedLevels);
    }

    public static int MaximumHpGainForLevelChange(
        int persistedLevel,
        int currentLevel)
    {
        return currentLevel == persistedLevel
            ? 0
            : ClampNonNegative(currentLevel);
    }

    private static int ClampNonNegative(long value)
    {
        return (int)Math.Min(int.MaxValue, Math.Max(0L, value));
    }
}

public static class AuraToolsAuthoritativeRoleSemantics
{
    private static readonly object Gate = new();
    private static IDisposable? registration;
    private static IDisposable? strategyRegistration;
    private static IDisposable? skillTimingRegistration;

    public static void Initialize()
    {
        lock (Gate)
        {
            registration ??= CombatAiRegistry.RegisterSemanticProvider(
                "AuraToolsExp",
                "witch-authoritative-role-skills-v4",
                new WitchAuthoritativeRoleSkillProvider(),
                1000);
            strategyRegistration ??= CombatAiRegistry.RegisterRoleStrategyProvider(
                "AuraToolsExp",
                "witch-nana-role-strategy-v4",
                new AuraToolsNanaRoleStrategyProvider(),
                1000);
            skillTimingRegistration ??= CombatAiRegistry.RegisterSkillTimingProvider(
                "AuraToolsExp",
                "witch-base-role-skill-timing-v1",
                new AuraToolsWitchSkillTimingProvider(),
                900);
        }
    }

    public static IReadOnlyList<string> ValidateFrozenTrainingPreparation()
    {
        Initialize();
        var errors = new List<string>();
        var snapshot = CombatAiRegistry.SnapshotDecisionPreparation();
        if (snapshot.SemanticProviderCount <= 0)
        {
            errors.Add("frozen decision preparation has no semantic provider");
        }
        if (snapshot.RoleStrategyProviderCount <= 0)
        {
            errors.Add("frozen decision preparation has no role strategy provider");
        }
        if (snapshot.SkillTimingProviderCount <= 0)
        {
            errors.Add("frozen decision preparation has no skill timing provider");
        }

        var state = new CombatStateObservation
        {
            CurrentPower = 3,
            MaxPower = 3,
            Player = new CombatUnitObservation
            {
                RuntimeId = 1,
                DefinitionId = "career_2",
                Kind = CombatTargetKind.Self,
                CurrentHp = 115,
                MaxHp = 115,
                Statuses =
                {
                    new CombatStatusObservation
                    {
                        StatusId = "buff_DoomPower",
                        Level = 10,
                        Type = "Special"
                    },
                    new CombatStatusObservation
                    {
                        StatusId = "buff_burn",
                        Level = 5,
                        Rarity = 2,
                        Type = "Negative"
                    }
                }
            },
            Actions =
            {
                new CombatActionObservation
                {
                    CandidateId = "nana-canary-devour",
                    SourceId = "careercard_2",
                    Kind = CombatActionKind.UseSkill,
                    TargetRuntimeId = 1,
                    TargetKind = CombatTargetKind.Self
                },
                new CombatActionObservation
                {
                    CandidateId = "nana-canary-transform",
                    SourceId = "careercard_3",
                    Kind = CombatActionKind.UseSkill
                }
            }
        };
        var prepared = new CombatDecisionEngine(
                useRuntimeRegistries: false,
                decisionPreparation: snapshot)
            .PrepareStateForIsolatedWorker(state);
        var devour = prepared.Actions.First(action => string.Equals(
            action.SourceId,
            "careercard_2",
            StringComparison.OrdinalIgnoreCase));
        var transform = prepared.Actions.First(action => string.Equals(
            action.SourceId,
            "careercard_3",
            StringComparison.OrdinalIgnoreCase));
        devour.Features.TryGetValue(
            "nana:projected-doom-gain",
            out var projectedDoomGain);
        devour.Features.TryGetValue(
            "nana:projected-max-hp-gain",
            out var projectedMaximumHpGain);
        devour.Features.TryGetValue(
            CombatRoleStrategyFeatureNames.Active,
            out var roleStrategyActive);
        if (devour.SemanticFidelity
            != CombatKnowledgeFidelity.Authoritative
            || projectedDoomGain != 2d
            || projectedMaximumHpGain != 12d
            || roleStrategyActive <= 0.5d)
        {
            errors.Add("frozen Nana semantic/strategy preparation was not applied");
        }
        transform.Features.TryGetValue(
            CombatSkillTimingFeatureNames.Active,
            out var skillTimingActive);
        transform.Features.TryGetValue(
            CombatSkillTimingFeatureNames.TimingAdvantage,
            out var timingAdvantage);
        transform.Features.TryGetValue(
            "nana:post-transform-max-hp",
            out var postTransformMaximumHp);
        transform.Features.TryGetValue(
            CombatRoleStrategyFeatureNames.StrategicallyProhibited,
            out var strategicallyProhibited);
        if (skillTimingActive <= 0.5d
            || Math.Abs(timingAdvantage) <= 0.000001d
            || postTransformMaximumHp != 115d
            || strategicallyProhibited > 0.5d)
        {
            errors.Add("frozen Nana conditional transform strategy was not applied");
        }
        return errors;
    }

    private sealed class WitchAuthoritativeRoleSkillProvider :
        ICombatSemanticProvider
    {
        public bool TryDescribe(
            CombatStateObservation state,
            CombatActionObservation action,
            out CombatActionSemantics semantics)
        {
            if (state == null || action == null)
            {
                semantics = new CombatActionSemantics();
                return false;
            }
            if (string.Equals(
                    action.SourceId,
                    "careercard_1",
                    StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeDivineSelection(state);
                return true;
            }
            if (string.Equals(
                    action.SourceId,
                    "careercard_2",
                    StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeDoomDevour(state, action);
                return true;
            }
            if (string.Equals(
                    action.SourceId,
                    "careercard_3",
                    StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeCalamityIncarnate(state, action);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_5", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeWailingWall(state);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_6", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeLightlessAegis(state, action);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_7", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeChaosControl(state);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_8", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeImitation(state, action);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_9", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeSketchWorld(state);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_10", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeSlaughterTime(state, action);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_11", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeCrimson(state);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_12", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeMirrorShade(state);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_13", StringComparison.OrdinalIgnoreCase))
            {
                semantics = DescribeAbyssalCalling(state);
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_14", StringComparison.OrdinalIgnoreCase))
            {
                semantics = new CombatActionSemantics
                {
                    Buff = 2d,
                    EnergyGain = 1d,
                    Scaling = Math.Max(
                        1d,
                        state.Features.GetValueOrDefault(
                            "friendlyActorCount",
                            1d))
                };
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_15", StringComparison.OrdinalIgnoreCase))
            {
                semantics = new CombatActionSemantics
                {
                    Buff = 4d,
                    Heal = 2d,
                    EndOfCycleSelfHpLoss = 2d,
                    Risk = 1d
                };
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_16", StringComparison.OrdinalIgnoreCase))
            {
                semantics = new CombatActionSemantics
                {
                    Buff = 3d,
                    DeckValue = 2d,
                    OpensInteraction = true
                };
                return true;
            }
            if (string.Equals(action.SourceId, "careercard_17", StringComparison.OrdinalIgnoreCase))
            {
                semantics = new CombatActionSemantics
                {
                    Buff = 5d,
                    EnergyGain = 2d,
                    PersistentValue = 2d
                };
                return true;
            }
            if (!string.Equals(
                    action.SourceId,
                    "careercard_4",
                    StringComparison.OrdinalIgnoreCase))
            {
                semantics = new CombatActionSemantics();
                return false;
            }

            var soul = ResolveSoul(state);
            var tier = soul < 100 ? 1 : soul < 200 ? 2 : 3;
            var targetCardId = tier == 1
                ? "nocard_1"
                : tier == 2
                    ? "nocard_2"
                    : "nocard_3";
            var amount = (tier == 1 ? 4 : tier == 2 ? 6 : 8)
                         + soul / 8;
            var targetSemantics = new CombatActionSemantics
            {
                Damage = amount,
                TrueDamage = tier >= 2 ? amount : 0d,
                Defend = amount,
                Buff = tier >= 3 ? 1d : 0d,
                AffectedEnemyCount = Math.Max(
                    1,
                    state?.Enemies?.Count(enemy => enemy.Alive) ?? 1),
                RandomOutcome = tier >= 3,
                Uncertainty = tier >= 3 ? 0.25d : 0d
            };
            semantics = new CombatActionSemantics
            {
                OpensInteraction = true,
                HandTransform = new CombatHandTransformSemantic
                {
                    TargetCardId = targetCardId,
                    TargetCardSemantics = targetSemantics,
                    TransformAllHandCards = true,
                    PreserveInstances = true,
                    ClearsEnhancements = true,
                    ClearsVariables = true,
                    TargetRetained = true,
                    TargetExhaustsOnUse = true,
                    GrowthStateKey = "playerStatus:buff_Soul",
                    GrowthPerExhaust = 1d,
                    CurrentGrowthValue = soul,
                    TargetTier = tier,
                    NextTierThreshold = tier == 1 ? 100 : tier == 2 ? 200 : 0,
                    CooldownProgressRequired = 12d,
                    CooldownProgressEvent = "ICreateCardItem"
                }
            };
            return true;
        }

        private static CombatActionSemantics DescribeDivineSelection(
            CombatStateObservation state)
        {
            var handCapacity = Math.Max(0, 10 - state.HandCount);
            var drawPile = state.Features.TryGetValue("drawPileCount", out var value)
                ? Math.Max(0d, value)
                : Math.Max(0, state.DeckKnowledge?.DrawPileCount ?? 0);
            return new CombatActionSemantics
            {
                // The skill opens a deck-selection interaction. The card only
                // enters the hand after the follow-up choice is resolved, so
                // the parent action must not claim an immediate draw.
                Draw = 0d,
                DeckValue = handCapacity > 0 && drawPile > 0 ? 1.5d : 0d,
                CooldownTurns = 1d,
                OpensInteraction = true,
                Risk = handCapacity <= 0 || drawPile <= 0 ? 3d : 0d
            };
        }

        private static CombatActionSemantics DescribeWailingWall(
            CombatStateObservation state)
        {
            var capacity = Math.Max(0, 10 - state.HandCount);
            var effectiveDraw = Math.Min(5, capacity);
            return new CombatActionSemantics
            {
                Draw = effectiveDraw,
                Buff = 1d,
                CooldownTurns = 3d,
                PersistentValue = effectiveDraw * 0.25d,
                Risk = Math.Max(0, 5 - effectiveDraw) * 0.8d
            };
        }

        private static CombatActionSemantics DescribeLightlessAegis(
            CombatStateObservation state,
            CombatActionObservation action)
        {
            var targetThreat = (state.Threat?.Intents
                                ?? new List<CombatIntentObservation>())
                .Where(intent => intent.SourceRuntimeId == action.TargetRuntimeId
                                 && intent.Kind == CombatIntentKind.Attack)
                .Sum(intent =>
                    Math.Max(0d, intent.BlockableDamage + intent.UnblockableDamage)
                    * Math.Max(0d, Math.Min(1d, intent.Probability)));
            if (targetThreat <= 0d && state.Threat?.CurrentIntentKnown == true)
            {
                targetThreat = Math.Max(0d, state.ExpectedIncomingDamage);
            }
            return new CombatActionSemantics
            {
                Defend = targetThreat,
                CooldownTurns = 3d,
                PersistentValue = targetThreat * 0.2d,
                Risk = targetThreat <= 0d ? 3d : 0d
            };
        }

        private static CombatActionSemantics DescribeChaosControl(
            CombatStateObservation state)
        {
            var enemyCount = Math.Max(1, state.Enemies.Count(enemy => enemy.Alive));
            var enemyTrueDamage = state.Enemies.Where(enemy => enemy.Alive)
                .Sum(enemy => Math.Max(0, enemy.MaxHp) * 0.1d) / 6d;
            var selfHeal = Math.Max(0, state.Player.MaxHp) * 0.3d / 6d;
            var selfRisk = Math.Max(0, state.Player.MaxHp) * 0.2d / 6d;
            return new CombatActionSemantics
            {
                TrueDamage = enemyTrueDamage,
                Heal = selfHeal,
                Draw = 2d / 6d,
                EnergyGain = -1d / 6d,
                Buff = 2d / 6d,
                Debuff = 2d / 6d,
                AffectedEnemyCount = enemyCount,
                CooldownTurns = 2d,
                RandomOutcome = true,
                Uncertainty = 4d,
                Risk = selfRisk
            };
        }

        private static CombatActionSemantics DescribeImitation(
            CombatStateObservation state,
            CombatActionObservation action)
        {
            var target = FindTarget(state, action);
            var positive = StatusValue(target, "Positive");
            var negative = StatusValue(target, "Negative");
            return new CombatActionSemantics
            {
                Buff = positive,
                Debuff = negative,
                PersistentValue = Math.Max(0d, positive - negative) * 0.5d,
                CooldownTurns = 5d,
                Risk = negative
            };
        }

        private static CombatActionSemantics DescribeSketchWorld(
            CombatStateObservation state)
        {
            var remaining = CampaignValue(
                state,
                CombatCampaignContextFeatureNames.RemainingBattles);
            var available = (state.DeckKnowledge?.DrawPileCount ?? 0) > 0;
            return new CombatActionSemantics
            {
                DeckValue = available ? 1d : 0d,
                PersistentValue = available ? 2d + Math.Min(12d, remaining * 0.5d) : 0d,
                CooldownTurns = 2d,
                OpensInteraction = true,
                Risk = available ? 1d : 4d
            };
        }

        private static CombatActionSemantics DescribeSlaughterTime(
            CombatStateObservation state,
            CombatActionObservation action)
        {
            var target = FindTarget(state, action);
            var actions = target?.Features.TryGetValue("actionCount", out var count) == true
                ? Math.Max(1d, count)
                : Math.Max(1d, state.CurrentPower);
            var missingHp = target == null ? 0d : Math.Max(0, target.MaxHp - target.CurrentHp);
            var projectedHeal = Math.Min(missingHp, actions * 4d);
            var enemy = target?.Kind == CombatTargetKind.Enemy;
            return new CombatActionSemantics
            {
                Heal = enemy ? 0d : projectedHeal,
                Damage = enemy ? Math.Max(0d, 16d - projectedHeal) : 0d,
                Buff = 1d,
                EndOfCycleSelfHpLoss = enemy ? 0d : 16d,
                CooldownTurns = 2d,
                PersistentValue = Math.Max(0d, projectedHeal - 16d),
                Risk = enemy ? projectedHeal : Math.Max(0d, 16d - projectedHeal)
            };
        }

        private static CombatActionSemantics DescribeCrimson(
            CombatStateObservation state)
        {
            var totalBleeding = state.Player.Statuses
                .Concat(state.Friendlies.SelectMany(unit => unit.Statuses))
                .Concat(state.Enemies.SelectMany(unit => unit.Statuses))
                .Where(status => string.Equals(
                    status.StatusId,
                    "buff_bleeding",
                    StringComparison.OrdinalIgnoreCase))
                .Sum(status => Math.Max(0, status.Level));
            return new CombatActionSemantics
            {
                Damage = totalBleeding,
                AffectedEnemyCount = Math.Max(1, state.Enemies.Count(enemy => enemy.Alive)),
                CooldownTurns = 1d,
                Risk = totalBleeding <= 0 ? 2d : 0d
            };
        }

        private static CombatActionSemantics DescribeMirrorShade(
            CombatStateObservation state)
        {
            var best = BestHandCardValue(state);
            return new CombatActionSemantics
            {
                CardGeneration = best > 0d ? 1d : 0d,
                DeckValue = best * 0.5d,
                PersistentValue = best * 0.25d,
                CooldownTurns = 2d,
                OpensInteraction = true,
                Risk = best <= 0d ? 3d : 0d
            };
        }

        private static CombatActionSemantics DescribeAbyssalCalling(
            CombatStateObservation state)
        {
            var best = BestHandCardValue(state);
            var remaining = CampaignValue(
                state,
                CombatCampaignContextFeatureNames.RemainingBattles);
            return new CombatActionSemantics
            {
                Scaling = best,
                DeckValue = best * 0.5d,
                PersistentValue = best * (1d + Math.Min(10d, remaining) * 0.2d),
                CooldownTurns = 99d,
                OpensInteraction = true,
                Risk = best <= 0d ? 4d : 1d
            };
        }

        private static double StatusValue(
            CombatUnitObservation? target,
            string type)
        {
            return target?.Statuses
                       .Where(status => string.Equals(status.Type, type, StringComparison.OrdinalIgnoreCase))
                       .Sum(status => Math.Max(0, status.Level) * Math.Max(1, status.Rarity))
                   ?? 0d;
        }

        private static double BestHandCardValue(CombatStateObservation state)
        {
            return state.HandCards
                .Select(card => Math.Max(0d,
                    1d
                    + Math.Max(0d, 3d - card.EffectiveCost) * 0.5d
                    + card.EnhancementCount * 0.5d
                    + (card.Retained ? 0.5d : 0d)))
                .DefaultIfEmpty(0d)
                .Max();
        }

        private static double CampaignValue(
            CombatStateObservation state,
            string key)
        {
            return state.Features.TryGetValue(key, out var value)
                ? Math.Max(0d, value)
                : 0d;
        }

        private static CombatActionSemantics DescribeDoomDevour(
            CombatStateObservation state,
            CombatActionObservation action)
        {
            var player = state.Player ?? new CombatUnitObservation();
            var target = FindTarget(state, action);
            var negativeStatuses = (target?.Statuses
                                    ?? Enumerable.Empty<CombatStatusObservation>())
                .Where(IsNegativeStatus)
                .ToList();
            var doomGain = negativeStatuses.Sum(status => Math.Min(
                Math.Max(
                    1,
                    Math.Max(1, status.Rarity)
                    * Math.Max(0, status.Level)
                    / 5),
                10));
            var negativeStacks = negativeStatuses.Sum(status =>
                Math.Max(0, status.Level));
            var currentDoom = ResolveStatusLevel(
                state,
                "buff_DoomPower");
            var enemyTarget = action.TargetKind == CombatTargetKind.Enemy;
            var friendlyTarget = action.TargetKind == CombatTargetKind.Friendly;
            var selfTarget = action.TargetKind == CombatTargetKind.Self;
            var maximumHpGain = doomGain > 0
                ? AuraToolsNanaDoomProgression.MaximumHpGainAfterAdd(
                    currentDoom,
                    doomGain)
                : 0;
            action.Features["nana:negative-status-count"] =
                negativeStatuses.Count;
            action.Features["nana:negative-status-stacks"] = negativeStacks;
            action.Features["nana:projected-doom-gain"] = doomGain;
            action.Features["nana:projected-max-hp-gain"] = maximumHpGain;
            action.Features["nana:devour-event-max-hp-gain"] = maximumHpGain;
            action.Features["nana:enemy-cleanse-cost"] = enemyTarget
                ? negativeStacks + doomGain
                : 0d;
            action.Features["nana:self-cleanse"] = selfTarget ? 1d : 0d;
            action.Features["nana:friendly-cleanse"] = friendlyTarget ? 1d : 0d;

            return new CombatActionSemantics
            {
                Damage = enemyTarget ? 5d : 0d,
                Heal = maximumHpGain,
                Cleanse = enemyTarget ? 0d : negativeStacks,
                Scaling = doomGain,
                PersistentValue = maximumHpGain,
                CooldownTurns = 5d,
                Risk = enemyTarget
                    ? negativeStacks + doomGain
                    : friendlyTarget
                        ? 5d
                        : 0d,
                StateChanges = new System.Collections.Generic.Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["playerStatus:buff_DoomPower"] = doomGain,
                    ["playerMaxHp"] = maximumHpGain,
                    ["player.hp"] = maximumHpGain,
                    ["targetNegativeStatusStacks"] = -negativeStacks,
                    ["targetHp"] = selfTarget ? 0d : -5d
                },
                TargetEffects =
                {
                    new CombatTargetedSemanticEffect
                    {
                        Kind = CombatSemanticEffectKind.Heal,
                        TargetRuntimeId = player.RuntimeId,
                        RawAmount = maximumHpGain,
                        EffectiveAmount = maximumHpGain,
                        Probability = 1d
                    }
                }
            };
        }

        private static CombatActionSemantics DescribeCalamityIncarnate(
            CombatStateObservation state,
            CombatActionObservation action)
        {
            var player = state.Player ?? new CombatUnitObservation();
            var doom = ResolveStatusLevel(state, "buff_DoomPower");
            var alreadyTransformed = string.Equals(
                                         player.DefinitionId,
                                         "career_4",
                                         StringComparison.OrdinalIgnoreCase)
                                     || player.Statuses.Any(status =>
                                         string.Equals(
                                             status.StatusId,
                                             "SpecialBuff_CalamityIncarnates",
                                             StringComparison.OrdinalIgnoreCase));
            var maximumHpAfter = Math.Max(1, player.MaxHp);
            var passiveDamage = Math.Max(0, maximumHpAfter / 50);
            var enemyCount = Math.Max(
                0,
                state.Enemies?.Count(enemy => enemy.Alive) ?? 0);
            action.Features["nana:doom-at-transform"] = doom;
            action.Features["nana:first-transform"] =
                alreadyTransformed ? 0d : 1d;
            action.Features["nana:repeat-transform"] =
                alreadyTransformed ? 1d : 0d;
            action.Features["nana:transform-hp-clamp-loss"] = 0d;
            action.Features["nana:calamity-action-damage"] = passiveDamage;
            action.Features["nana:post-transform-max-hp"] = maximumHpAfter;
            action.Features["nana:post-transform-damage-per-action"] =
                passiveDamage;

            return new CombatActionSemantics
            {
                Damage = passiveDamage,
                AffectedEnemyCount = enemyCount,
                SelfHpLoss = 0d,
                Buff = alreadyTransformed ? 0d : doom * 2d,
                Scaling = alreadyTransformed ? 0d : doom,
                PersistentValue = alreadyTransformed
                    ? passiveDamage
                    : doom + passiveDamage,
                CooldownTurns = 2d,
                StateChanges = new System.Collections.Generic.Dictionary<string, double>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["playerRole:career_4"] = alreadyTransformed ? 0d : 1d,
                    ["playerMaxHp"] = 0d,
                    ["playerTempStrength"] = alreadyTransformed ? 0d : doom,
                    ["playerTempPerceive"] = alreadyTransformed ? 0d : doom
                }
            };
        }

        private static CombatUnitObservation? FindTarget(
            CombatStateObservation state,
            CombatActionObservation action)
        {
            if (state == null || action == null)
            {
                return null;
            }
            if (state.Player?.RuntimeId == action.TargetRuntimeId)
            {
                return state.Player;
            }
            return state.Friendlies
                       .Concat(state.Enemies)
                       .FirstOrDefault(unit =>
                           unit.RuntimeId == action.TargetRuntimeId);
        }

        private static bool IsNegativeStatus(CombatStatusObservation status)
        {
            return status != null
                   && string.Equals(
                       status.Type,
                       "Negative",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveStatusLevel(
            CombatStateObservation state,
            string statusId)
        {
            var statusValue = state?.Player?.Statuses?
                .Where(status => string.Equals(
                    status.StatusId,
                    statusId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(status => status.Level)
                .DefaultIfEmpty(0)
                .Max() ?? 0;
            var featureValue = 0d;
            state?.Features?.TryGetValue(
                "playerStatus:" + statusId,
                out featureValue);
            return Math.Max(
                0,
                Math.Max(
                    statusValue,
                    (int)Math.Floor(Math.Max(0d, featureValue))));
        }

        private static int ResolveSoul(CombatStateObservation state)
        {
            return ResolveStatusLevel(state, "buff_Soul");
        }
    }
}
