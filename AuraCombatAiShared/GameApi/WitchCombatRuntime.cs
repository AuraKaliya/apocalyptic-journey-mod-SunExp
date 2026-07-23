using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraDecision.Shared;
using Michsky.MUIP;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.Core;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraCombatAi.Shared.GameApi;

public sealed class WitchCombatRuntime : ICombatObservationProvider, ICombatActionExecutor
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? SkillItemsField = typeof(FightUI).GetField("skillItems", InstanceFlags);
    private static readonly FieldInfo? AttackHitEnemyField = typeof(AttackCardItem).GetField("hitEnemy", InstanceFlags);
    private static readonly FieldInfo? AttackIsLineField = typeof(AttackCardItem).GetField("<isLine>k__BackingField", InstanceFlags);
    private static readonly FieldInfo? SkillHitEnemyField = typeof(SkillItem).GetField("hitEnemy", InstanceFlags);
    private static long sequence;

    public bool TryCapture(out CombatStateObservation observation, out string reason)
    {
        observation = new CombatStateObservation();
        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null || !fightUi.gameObject.activeInHierarchy)
        {
            reason = "FightUI is unavailable";
            return false;
        }

        var player = FightPlayer.Instance;
        if (player?.Status == null)
        {
            reason = "local fight player is unavailable";
            return false;
        }

        observation.BattleSessionId = AuraShared.Core.AuraBattleLifecycleRouter.CurrentBattleSessionId;
        observation.Sequence = ++sequence;
        if (player.Status is not StatusManager playerStatus)
        {
            reason = "local player status type is unsupported";
            return false;
        }

        observation.Player = ObserveUnit(playerStatus, CombatTargetKind.Self);
        observation.CurrentPower = player.CurPowerCount;
        observation.MaxPower = player.MaxPowerCount;
        observation.HandCount = FightUI.cardItemList?.Count ?? 0;
        observation.UiBusy = IsUiBusy(fightUi);
        observation.IsPlayerActionWindow = IsPlayerActionWindow(fightUi);
        AddEnemies(observation);
        AddCards(observation, fightUi);
        AddSkills(observation, fightUi);
        observation.Actions.Add(new CombatActionObservation
        {
            CandidateId = "end-turn",
            SourceId = "end-turn",
            DisplayName = "结束回合",
            Kind = CombatActionKind.EndTurn,
            RuntimeId = fightUi.turnButton == null ? 0 : fightUi.turnButton.GetInstanceID(),
            Legal = observation.IsPlayerActionWindow,
            RuntimeHandle = fightUi.turnButton
        });
        observation.Fingerprint = BuildFingerprint(observation);
        reason = "";
        return true;
    }

    public CombatExecutionResult Execute(CombatActionObservation action)
    {
        if (action == null)
        {
            return CombatExecutionResult.Rejected("action is null");
        }

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null || !IsPlayerActionWindow(fightUi))
        {
            return CombatExecutionResult.Rejected("player action window is not stable");
        }

        try
        {
            switch (action.Kind)
            {
                case CombatActionKind.EndTurn:
                    if (fightUi.turnButton == null || !fightUi.turnButton.isInteractable)
                    {
                        return CombatExecutionResult.Rejected("end-turn button is unavailable");
                    }

                    fightUi.turnButton.onClick.Invoke();
                    return CombatExecutionResult.Success("end turn");

                case CombatActionKind.PlayCard:
                    return ExecuteCard(action, fightUi);

                case CombatActionKind.UseSkill:
                    return ExecuteSkill(action);

                default:
                    return CombatExecutionResult.Rejected("unsupported action kind");
            }
        }
        catch (Exception ex)
        {
            return CombatExecutionResult.Rejected("execution failed: " + ex.Message);
        }
    }

    public static bool IsPlayerActionWindow(FightUI fightUi)
    {
        return fightUi != null
               && fightUi.gameObject.activeInHierarchy
               && FightManager.Instance != null
               && FightManager.Instance.fightType == FightType.Player
               && fightUi.turnButton != null
               && fightUi.turnButton.gameObject.activeInHierarchy
               && fightUi.turnButton.isInteractable
               && CardItem.canUse
               && !FightUI.InIEn
               && !fightUi.NowAnimation
               && fightUi.createCardQueue.Count == 0;
    }

    public static bool IsUiBusy(FightUI fightUi)
    {
        return fightUi == null
               || FightUI.InIEn
               || fightUi.NowAnimation
               || fightUi.createCardQueue.Count > 0
               || WitchCombatInteractionRuntime.HasActivePrompt;
    }

    private static CombatExecutionResult ExecuteCard(CombatActionObservation action, FightUI fightUi)
    {
        if (action.RuntimeHandle is not CommonCardItem card
            || card == null
            || card.GetInstanceID() != action.RuntimeId)
        {
            return CombatExecutionResult.Rejected("card handle is stale");
        }

        if (!TryPreflightCard(card, fightUi, out var reason))
        {
            return CombatExecutionResult.Rejected(reason);
        }

        var target = action.TargetHandle as StatusManager;
        if (card is AttackCardItem attack)
        {
            if (target == null || target.CurHp <= 0)
            {
                return CombatExecutionResult.Rejected("targeted card has no living target");
            }

            attack.scriptExecutor.Target = target;
            attack.scriptExecutor.Object.Clear();
            attack.scriptExecutor.Object.Add(target);
            AttackHitEnemyField?.SetValue(attack, target);
            AttackIsLineField?.SetValue(attack, false);
            WitchUiManager.Instance?.GetUI<LineUI>("LineUI")?.Hide();
            Cursor.visible = true;
            attack.TrueUse();
            return CombatExecutionResult.Success("played targeted card");
        }

        if (target != null && card.dataConfig?.scriptExecutor != null)
        {
            card.dataConfig.scriptExecutor.Target = target;
        }

        card.TrueUse();
        return CombatExecutionResult.Success("played card");
    }

    private static CombatExecutionResult ExecuteSkill(CombatActionObservation action)
    {
        if (action.RuntimeHandle is not SkillItem skill
            || skill == null
            || skill.GetInstanceID() != action.RuntimeId)
        {
            return CombatExecutionResult.Rejected("skill handle is stale");
        }

        if (!TryPreflightSkill(skill, out var reason))
        {
            return CombatExecutionResult.Rejected(reason);
        }

        var target = action.TargetHandle as StatusManager;
        if (!IsUntargetedSkill(skill) && target == null)
        {
            return CombatExecutionResult.Rejected("targeted skill has no target");
        }

        SkillHitEnemyField?.SetValue(skill, target);
        skill.TrueUse();
        return CombatExecutionResult.Success("used skill");
    }

    private static void AddCards(CombatStateObservation state, FightUI fightUi)
    {
        var cards = FightUI.cardItemList;
        if (cards == null)
        {
            return;
        }

        var count = Math.Min(cards.Count, 64);
        for (var i = 0; i < count; i++)
        {
            if (cards[i] is not CommonCardItem card)
            {
                continue;
            }

            var legal = TryPreflightCard(card, fightUi, out var reason);
            if (card is AttackCardItem)
            {
                if (state.Enemies.Count == 0)
                {
                    AddCardCandidate(state, card, i, null, false, "no living enemy target");
                    continue;
                }

                for (var targetIndex = 0; targetIndex < state.Enemies.Count; targetIndex++)
                {
                    var target = ResolveStatus(state.Enemies[targetIndex].RuntimeId);
                    AddCardCandidate(state, card, i, target, legal, reason);
                }
            }
            else
            {
                AddCardCandidate(state, card, i, null, legal, reason);
            }
        }
    }

    private static void AddCardCandidate(
        CombatStateObservation state,
        CommonCardItem card,
        int index,
        StatusManager? target,
        bool legal,
        string reason)
    {
        var sourceId = WitchCombatValueEstimator.IdOf(card.dataConfig);
        var targetId = target == null ? 0 : target.GetInstanceID();
        state.Actions.Add(new CombatActionObservation
        {
            CandidateId = "card:" + card.GetInstanceID() + ":" + targetId,
            SourceId = sourceId,
            DisplayName = WitchCombatValueEstimator.NameOf(card.dataConfig),
            Kind = CombatActionKind.PlayCard,
            RuntimeId = card.GetInstanceID(),
            TargetRuntimeId = targetId,
            TargetKind = target == null ? CombatTargetKind.None : CombatTargetKind.Enemy,
            Cost = ComputeCardCost(card),
            Legal = legal,
            RejectionReason = reason,
            Semantics = WitchCombatValueEstimator.Estimate(card.dataConfig, card is AttackCardItem, target == null ? CombatTargetKind.None : CombatTargetKind.Enemy),
            Features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["handIndex"] = index,
                ["isCard"] = 1d
            },
            RuntimeHandle = card,
            TargetHandle = target
        });
    }

    private static void AddSkills(CombatStateObservation state, FightUI fightUi)
    {
        if (SkillItemsField?.GetValue(fightUi) is not IEnumerable skills)
        {
            return;
        }

        foreach (var value in skills)
        {
            if (value is not SkillItem skill)
            {
                continue;
            }

            var legal = TryPreflightSkill(skill, out var reason);
            if (IsUntargetedSkill(skill))
            {
                AddSkillCandidate(state, skill, null, CombatTargetKind.None, legal, reason);
                continue;
            }

            for (var i = 0; i < state.Enemies.Count; i++)
            {
                var target = ResolveStatus(state.Enemies[i].RuntimeId);
                if (target != null)
                {
                    AddSkillCandidate(state, skill, target, CombatTargetKind.Enemy, legal, reason);
                }
            }

            if (FightPlayer.Instance?.Status is StatusManager self)
            {
                AddSkillCandidate(state, skill, self, CombatTargetKind.Self, legal, reason);
            }
        }
    }

    private static void AddSkillCandidate(
        CombatStateObservation state,
        SkillItem skill,
        StatusManager? target,
        CombatTargetKind targetKind,
        bool legal,
        string reason)
    {
        var targetId = target == null ? 0 : target.GetInstanceID();
        state.Actions.Add(new CombatActionObservation
        {
            CandidateId = "skill:" + skill.GetInstanceID() + ":" + targetId,
            SourceId = WitchCombatValueEstimator.IdOf(skill.dataConfig),
            DisplayName = WitchCombatValueEstimator.NameOf(skill.dataConfig),
            Kind = CombatActionKind.UseSkill,
            RuntimeId = skill.GetInstanceID(),
            TargetRuntimeId = targetId,
            TargetKind = targetKind,
            Legal = legal,
            RejectionReason = reason,
            Semantics = WitchCombatValueEstimator.Estimate(skill.dataConfig, false, targetKind),
            Features = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["isSkill"] = 1d
            },
            RuntimeHandle = skill,
            TargetHandle = target
        });
    }

    private static bool TryPreflightCard(CommonCardItem card, FightUI fightUi, out string reason)
    {
        if (!IsPlayerActionWindow(fightUi))
        {
            reason = "not a stable player action window";
            return false;
        }

        if (card == null
            || card.gameObject == null
            || !card.gameObject.activeInHierarchy
            || card.hasUse
            || !card.enabled
            || card.dataConfig == null
            || card.data == null
            || card.status == null)
        {
            reason = "card runtime state is unavailable";
            return false;
        }

        if (card.Tags.Contains("Unusable")
            || (card.Vars.TryGetValue("Usable", out var usable) && usable == "0"))
        {
            reason = "card is marked unusable";
            return false;
        }

        if (!card.data.TryGetValue("Expend", out var rawCost) || !int.TryParse(rawCost, out _))
        {
            reason = "card cost is invalid";
            return false;
        }

        if (ComputeCardCost(card) > FightPlayer.Instance.CurPowerCount)
        {
            reason = "insufficient power";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool TryPreflightSkill(SkillItem skill, out string reason)
    {
        if (skill == null
            || skill.dataConfig == null
            || skill.data == null
            || skill.Vars == null
            || skill.status == null
            || !CardItem.canUse
            || FightManager.Instance == null
            || FightManager.Instance.fightType != FightType.Player)
        {
            reason = "skill runtime state is unavailable";
            return false;
        }

        var id = WitchCombatValueEstimator.IdOf(skill.dataConfig);
        if (string.IsNullOrWhiteSpace(id)
            || RoleTable.Instance?.SkillTime == null
            || !RoleTable.Instance.SkillTime.TryGetValue(id, out var cooldown)
            || cooldown > 0)
        {
            reason = "skill is cooling down";
            return false;
        }

        reason = "";
        return true;
    }

    private static int ComputeCardCost(CommonCardItem card)
    {
        if (card.data == null
            || !card.data.TryGetValue("Expend", out var raw)
            || !int.TryParse(raw, out var baseCost))
        {
            return int.MaxValue;
        }

        var multiplier = 1f;
        var dynamicVariables = FightPlayer.Instance?.Status?.dynamicVariables;
        if (dynamicVariables != null && dynamicVariables.TryGetValue("CardCost", out var configuredMultiplier))
        {
            multiplier = configuredMultiplier;
        }
        var cost = Math.Min((int)(baseCost * multiplier), 4);
        cost += ParseInt(card.Vars, "TotalExCost");
        cost += ParseInt(card.Vars, "ExCost");
        cost += ParseInt(card.Vars, "OnceExCost");
        return Math.Max(0, cost);
    }

    private static int ParseInt(IDictionary<string, string> values, string key)
    {
        return values != null
               && values.TryGetValue(key, out var raw)
               && int.TryParse(raw, out var value)
            ? value
            : 0;
    }

    private static bool IsUntargetedSkill(SkillItem skill)
    {
        return skill.Vars != null
               && skill.Vars.TryGetValue("BaseScript", out var baseScript)
               && string.Equals(baseScript, "CommonCardItem", StringComparison.Ordinal);
    }

    private static void AddEnemies(CombatStateObservation state)
    {
        var enemies = EnemyManager.Instance?.enemyList;
        if (enemies == null)
        {
            return;
        }

        for (var i = 0; i < enemies.Count; i++)
        {
            if (enemies[i]?.Status is StatusManager status && status.CurHp > 0)
            {
                state.Enemies.Add(ObserveUnit(status, CombatTargetKind.Enemy));
            }
        }
    }

    private static CombatUnitObservation ObserveUnit(StatusManager status, CombatTargetKind kind)
    {
        return new CombatUnitObservation
        {
            RuntimeId = status.GetInstanceID(),
            Name = status.gameObject == null ? "" : status.gameObject.name,
            Kind = kind,
            CurrentHp = status.CurHp,
            MaxHp = status.MaxHp,
            Defend = status.Defend
        };
    }

    private static StatusManager? ResolveStatus(int runtimeId)
    {
        if (FightPlayer.Instance?.Status is StatusManager self && self.GetInstanceID() == runtimeId)
        {
            return self;
        }

        var enemies = EnemyManager.Instance?.enemyList;
        if (enemies == null)
        {
            return null;
        }

        for (var i = 0; i < enemies.Count; i++)
        {
            if (enemies[i]?.Status is StatusManager status && status.GetInstanceID() == runtimeId)
            {
                return status;
            }
        }

        return null;
    }

    private static string BuildFingerprint(CombatStateObservation state)
    {
        var enemyState = string.Join(",", state.Enemies.Select(enemy => enemy.RuntimeId + ":" + enemy.CurrentHp + ":" + enemy.Defend));
        var actionState = string.Join(",", state.Actions
            .Where(action => action.Kind != CombatActionKind.EndTurn)
            .Select(action => action.RuntimeId + ":" + action.SourceId)
            .Distinct());
        return state.BattleSessionId
               + "|" + state.Player.CurrentHp
               + "|" + state.Player.Defend
               + "|" + state.CurrentPower
               + "|" + state.HandCount
               + "|" + enemyState
               + "|" + actionState;
    }
}

public static class WitchCombatValueEstimator
{
    private static readonly string[] DamageTokens = { "damage", "hurt", "attack" };
    private static readonly string[] DefendTokens = { "defend", "shield", "armor" };
    private static readonly string[] HealTokens = { "heal", "cure", "recoverhp" };
    private static readonly string[] DrawTokens = { "draw", "getcard" };
    private static readonly string[] EnergyTokens = { "power", "energy" };
    private static readonly string[] ScalingTokens = { "strength", "dexterity", "addbuff" };

    public static CombatActionSemantics Estimate(
        IDataConfig? config,
        bool forceAttack,
        CombatTargetKind targetKind)
    {
        var result = new CombatActionSemantics();
        if (config == null)
        {
            result.Uncertainty = 3d;
            return result;
        }

        ReadDictionary(config.data, result);
        ReadDictionary(config.Vars, result);
        var script = CombinedScript(config);
        var descriptiveValue = LargestDescriptiveValue(config);
        if (result.Damage <= 0d && ContainsAny(script, "Damage", "Hit("))
        {
            result.Damage = descriptiveValue;
        }

        if (result.Defend <= 0d && ContainsAny(script, "Defend", "Shield", "Armor"))
        {
            result.Defend = descriptiveValue;
        }

        if (result.Heal <= 0d && ContainsAny(script, "Heal", "Cure", "RecoverHp"))
        {
            result.Heal = descriptiveValue;
        }

        if (result.Draw <= 0d && ContainsAny(script, "Draw", "GetCard"))
        {
            result.Draw = Math.Max(1d, descriptiveValue);
        }

        result.OpensInteraction = ContainsAny(script, "SelectCard", "PackToDeck", "OutFightSelect", "DeckUI");
        result.RandomOutcome = ContainsAny(script, "Random", "Dice", "RandomRange");
        if (result.RandomOutcome)
        {
            result.Uncertainty += 0.7d;
        }

        if (forceAttack && result.Damage <= 0d)
        {
            result.Damage = 3d;
            result.Uncertainty += 0.8d;
        }

        if (targetKind == CombatTargetKind.Self && result.Damage > 0d)
        {
            result.Risk += result.Damage;
            result.Damage = 0d;
        }

        if (result.Damage == 0d
            && result.Defend == 0d
            && result.Heal == 0d
            && result.Draw == 0d
            && result.EnergyGain == 0d
            && result.Scaling == 0d
            && result.DeckValue == 0d)
        {
            result.Uncertainty += 1.5d;
        }

        return result;
    }

    public static string IdOf(IDataConfig? config)
    {
        return Value(config?.data, "Id");
    }

    public static string NameOf(IDataConfig? config)
    {
        var name = Value(config?.data, "Name");
        return string.IsNullOrWhiteSpace(name) ? IdOf(config) : name;
    }

    private static void ReadDictionary(
        IDictionary<string, string>? values,
        CombatActionSemantics result)
    {
        if (values == null)
        {
            return;
        }

        foreach (var pair in values)
        {
            if (!double.TryParse(pair.Value, out var number))
            {
                continue;
            }

            var key = pair.Key ?? "";
            if (ContainsToken(key, DamageTokens))
            {
                result.Damage = Math.Max(result.Damage, number);
            }
            else if (ContainsToken(key, DefendTokens))
            {
                result.Defend = Math.Max(result.Defend, number);
            }
            else if (ContainsToken(key, HealTokens))
            {
                result.Heal = Math.Max(result.Heal, number);
            }
            else if (ContainsToken(key, DrawTokens))
            {
                result.Draw = Math.Max(result.Draw, number);
            }
            else if (ContainsToken(key, EnergyTokens) && !key.Contains("Expend"))
            {
                result.EnergyGain = Math.Max(result.EnergyGain, number);
            }
            else if (ContainsToken(key, ScalingTokens))
            {
                result.Scaling = Math.Max(result.Scaling, number);
            }
        }
    }

    private static string CombinedScript(IDataConfig config)
    {
        var parts = new List<string>();
        AddScript(parts, config.data);
        AddScript(parts, config.Vars);
        return string.Join(";", parts);
    }

    private static double LargestDescriptiveValue(IDataConfig config)
    {
        var maximum = 0d;
        ReadDescriptiveValues(config.data, ref maximum);
        ReadDescriptiveValues(config.Vars, ref maximum);
        return maximum <= 0d ? 1d : maximum;
    }

    private static void ReadDescriptiveValues(
        IDictionary<string, string>? values,
        ref double maximum)
    {
        if (values == null)
        {
            return;
        }

        foreach (var pair in values)
        {
            var key = pair.Key ?? "";
            if (key.IndexOf("DesVal", StringComparison.OrdinalIgnoreCase) < 0
                && key.IndexOf("Value", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            if (double.TryParse(pair.Value, out var value))
            {
                maximum = Math.Max(maximum, Math.Abs(value));
            }
        }
    }

    private static void AddScript(List<string> parts, IDictionary<string, string>? values)
    {
        if (values == null)
        {
            return;
        }

        foreach (var pair in values)
        {
            if ((pair.Key ?? "").IndexOf("Script", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                parts.Add(pair.Value ?? "");
            }
        }
    }

    private static bool ContainsToken(string value, IEnumerable<string> tokens)
    {
        return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string Value(IDictionary<string, string>? values, string key)
    {
        return values != null && values.TryGetValue(key, out var value) ? value ?? "" : "";
    }
}
