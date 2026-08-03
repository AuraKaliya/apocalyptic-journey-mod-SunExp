using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AuraCombatSimulation.Shared;

namespace AuraToolsExp.Dll.Features.AutoBattle;

public sealed class AuraToolsNativeRewardExtensionFactory :
    ICombatSimulationRuntimeExtensionFactory
{
    private static readonly ConcurrentDictionary<string, bool> NativeDefinitionPresence =
        new(StringComparer.Ordinal);

    public ICombatSimulationRuntimeExtension? Create(
        CombatScenarioDefinition scenario,
        CombatRuleset ruleset)
    {
        var advanced = scenario.Player?.Variables.GetValueOrDefault(
            "Difficulty",
            1d) >= 5d;
        var cacheKey = ruleset.Version + ":" + ruleset.RulesetHash;
        var nativeDefinitions = NativeDefinitionPresence.GetOrAdd(
            cacheKey,
            _ => ruleset.SnapshotCards().Any(
                     AuraToolsNativeGameScriptAudit.UsesNativeScript)
                 || ruleset.SnapshotStatuses().Any(
                     AuraToolsNativeGameScriptAudit.UsesNativeScript));
        var nativeRole = !string.IsNullOrWhiteSpace(
            scenario.Player?.RoleFightScript);
        return scenario.RewardRules.Count == 0
               && !advanced
               && !nativeDefinitions
               && !nativeRole
            ? null
            : new AuraToolsNativeRewardExtension();
    }
}

public sealed class AuraToolsNativeProgramPackageValidation
{
    public int ReferencedProgramCount { get; set; }

    public int PrecompiledProgramCount { get; set; }

    public string ProgramSetSha256 { get; set; } = "";

    public List<string> Errors { get; set; } = new();

    public bool Success => Errors.Count == 0;
}

public static class AuraToolsNativeProgramPackageAudit
{
    public static AuraToolsNativeProgramPackageValidation Validate(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        if (campaign == null) throw new ArgumentNullException(nameof(campaign));
        if (ruleset == null) throw new ArgumentNullException(nameof(ruleset));
        var scripts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reward in campaign.Rewards)
        {
            Add(scripts, reward.FightScript);
        }
        Add(scripts, campaign.Player?.RoleFightScript ?? "");
        foreach (var card in ruleset.SnapshotCards()
                     .Where(AuraToolsNativeGameScriptAudit.UsesNativeScript))
        {
            AddMetadata(
                scripts,
                card.Metadata,
                "NativeInitScript",
                "NativeUseScript",
                "NativeDrawScript",
                "NativeDropScript");
        }
        foreach (var status in ruleset.SnapshotStatuses()
                     .Where(AuraToolsNativeGameScriptAudit.UsesNativeScript))
        {
            AddMetadata(
                scripts,
                status.Metadata,
                "NativeInitScript",
                "NativeApplyScript",
                "NativeClearScript");
        }

        var errors = AuraToolsNativeRewardScriptAudit.Validate(campaign);
        errors.AddRange(AuraToolsNativeGameScriptAudit.Validate(ruleset));
        errors.AddRange(ValidateRoleProgram(campaign.Player));
        errors.AddRange(ValidateLiteralReferences(campaign, ruleset));
        return new AuraToolsNativeProgramPackageValidation
        {
            ReferencedProgramCount = scripts.Count,
            PrecompiledProgramCount = NativeRewardProgramRegistry.ProgramCount,
            ProgramSetSha256 = HashProgramSet(scripts),
            Errors = errors
        };
    }

    private static string HashProgramSet(IEnumerable<string> keys)
    {
        using var sha256 = SHA256.Create();
        var payload = string.Join(
            "\n",
            keys.OrderBy(item => item, StringComparer.Ordinal));
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var result = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            result.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    private static void AddMetadata(
        ISet<string> target,
        IReadOnlyDictionary<string, string> metadata,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            Add(target, metadata.GetValueOrDefault(key, ""));
        }
    }

    private static void Add(ISet<string> target, string script)
    {
        if (!string.IsNullOrWhiteSpace(script))
        {
            target.Add(NativeRewardProgramRegistry.Key(script));
        }
    }

    private static IEnumerable<string> ValidateLiteralReferences(
        CombatCampaignDefinition campaign,
        CombatRuleset ruleset)
    {
        var errors = new HashSet<string>(StringComparer.Ordinal);
        var scripts = campaign.Rewards
            .Select(item => item.FightScript)
            .Concat(new[] { campaign.Player?.RoleFightScript ?? "" })
            .Concat(ruleset.SnapshotCards().SelectMany(item => new[]
            {
                item.Metadata.GetValueOrDefault("NativeInitScript", ""),
                item.Metadata.GetValueOrDefault("NativeUseScript", ""),
                item.Metadata.GetValueOrDefault("NativeDrawScript", ""),
                item.Metadata.GetValueOrDefault("NativeDropScript", "")
            }))
            .Concat(ruleset.SnapshotStatuses().SelectMany(item => new[]
            {
                item.Metadata.GetValueOrDefault("NativeInitScript", ""),
                item.Metadata.GetValueOrDefault("NativeApplyScript", ""),
                item.Metadata.GetValueOrDefault("NativeClearScript", "")
            }))
            .Where(item => !string.IsNullOrWhiteSpace(item));
        var cardPattern = new Regex(
            @"\b(?:AddCard|AddCardById|AddCardToDeckById|RandomAddCard|CreateCard|DelayAddCard)"
            + @"\s*\(\s*(?:""(?<literal>[^""]+)""|DataId\.(?<data>[A-Za-z0-9_]+))"
            + @"\s*(?:\)|,)",
            RegexOptions.CultureInvariant);
        var statusPattern = new Regex(
            @"\b(?:AddBuff|RemoveBuff|GetBuff|RunImmediately)"
            + @"\s*\(\s*(?:""(?<literal>[^""]+)""|DataId\.(?<data>[A-Za-z0-9_]+))"
            + @"\s*(?:\)|,)",
            RegexOptions.CultureInvariant);
        foreach (var script in scripts)
        {
            ValidateReferences(
                script,
                cardPattern,
                "card",
                id => ruleset.TryGetCard(id, out _),
                errors);
            ValidateReferences(
                script,
                statusPattern,
                "status",
                id => ruleset.TryGetStatus(id, out _),
                errors);
        }
        return errors.OrderBy(item => item, StringComparer.Ordinal);
    }

    private static IEnumerable<string> ValidateRoleProgram(
        CombatPlayerSetup? player)
    {
        if (player == null || string.IsNullOrWhiteSpace(player.RoleFightScript))
        {
            yield break;
        }
        var key = NativeRewardProgramRegistry.Key(player.RoleFightScript);
        if (!string.Equals(
                key,
                player.RoleNativeScriptHash,
                StringComparison.OrdinalIgnoreCase))
        {
            yield return "native-role-script-hash:"
                         + player.RoleId
                         + ": expected="
                         + player.RoleNativeScriptHash
                         + ", actual="
                         + key;
        }
        var validation = NativeRewardProgramRegistry.Validate(
            new CombatScenarioRewardRule
            {
                RewardId = player.RoleId,
                Kind = "Role",
                NativeScriptHash = player.RoleNativeScriptHash,
                FightScript = player.RoleFightScript
            });
        if (!validation.Success)
        {
            yield return "native-role-script:"
                         + player.RoleId
                         + ":"
                         + validation.Message;
        }
    }

    private static void ValidateReferences(
        string script,
        Regex pattern,
        string kind,
        Func<string, bool> exists,
        ISet<string> errors)
    {
        foreach (Match match in pattern.Matches(script))
        {
            var id = match.Groups["literal"].Success
                ? match.Groups["literal"].Value
                : match.Groups["data"].Value;
            if (!string.IsNullOrWhiteSpace(id) && !exists(id))
            {
                errors.Add(
                    "native-script-reference:"
                    + kind
                    + ":"
                    + id
                    + ": definition is missing from the authoritative ruleset");
            }
        }
    }
}

internal sealed class AuraToolsNativeRewardExtension :
    ICombatSimulationRuntimeExtension,
    ICombatSimulationDecisionRuntimeExtension
{
    private readonly List<NativeRewardScriptGlobals> programs = new();
    private readonly Dictionary<int, NativeRewardScriptGlobals> cardPrograms =
        new();
    private readonly Dictionary<string, NativeRewardScriptGlobals> statusPrograms =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, int> endTurnCostModifiers = new();
    private readonly HashSet<string> noPowerDecisionStates = new();
    private bool applyingScriptExecuteAdjustment;

    private void RegisterProgram(NativeRewardScriptGlobals globals)
    {
        if (!programs.Contains(globals))
        {
            programs.Add(globals);
        }
    }

    public void Initialize(ICombatSimulationRuntimeContext context)
    {
        InitializeRoleProgram(context);
        ApplyHardAffixes(context);
        CombatScenarioRewardRule? previousRelicRule = null;
        foreach (var rule in context.Scenario.RewardRules.ToList())
        {
            for (var stack = 0; stack < Math.Max(1, rule.Stacks); stack++)
            {
                var globals = new NativeRewardScriptGlobals(
                    context,
                    rule,
                    registerProgram: RegisterProgram);
                var result = NativeRewardProgramRegistry.TryRun(rule, globals);
                if (!result.Success)
                {
                    context.AddUnsupported(
                        "reward-script:"
                        + rule.RewardId
                        + ":"
                        + result.Message);
                    continue;
                }
                programs.Add(globals);
                if (string.Equals(
                        rule.RewardId,
                        "CrowdFundingRelic_63",
                        StringComparison.OrdinalIgnoreCase)
                    && previousRelicRule != null)
                {
                    globals.ApplyCopiedProgramDefaults(previousRelicRule);
                    var copied = NativeRewardProgramRegistry.TryRun(
                        previousRelicRule,
                        globals);
                    if (!copied.Success)
                    {
                        context.AddUnsupported(
                            "reward-script:"
                            + rule.RewardId
                            + ":copied-previous:"
                            + copied.Message);
                    }
                }
                if (string.Equals(
                        rule.Kind,
                        "Relic",
                        StringComparison.OrdinalIgnoreCase))
                {
                    previousRelicRule = rule;
                }
            }
        }
        foreach (var actor in context.State.Actors.ToList())
        {
            foreach (var status in actor.Statuses.ToList())
            {
                EnsureStatusProgram(
                    context,
                    actor.ActorId,
                    status.StatusId,
                    null);
            }
        }
        AuditCrossRoleSkillCards(context);
    }

    private void InitializeRoleProgram(
        ICombatSimulationRuntimeContext context)
    {
        var player = context.Scenario.Player;
        if (player == null || string.IsNullOrWhiteSpace(player.RoleFightScript))
        {
            return;
        }
        var rule = new CombatScenarioRewardRule
        {
            RewardId = player.RoleId,
            Kind = "Role",
            NativeScriptHash = player.RoleNativeScriptHash,
            FightScript = player.RoleFightScript
        };
        var globals = new NativeRewardScriptGlobals(
            context,
            rule,
            registerProgram: RegisterProgram);
        var result = globals.RunScript(rule, null);
        if (!result.Success)
        {
            context.AddUnsupported(
                "role-script:" + player.RoleId + ":" + result.Message);
            return;
        }
        RegisterProgram(globals);
    }

    public void OnEvent(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        if (sourceEvent.Kind == CombatSimulationEventKind.TurnStarted)
        {
            RestoreEndTurnCosts(context);
        }
        foreach (var program in programs.ToList())
        {
            program.Dispatch(sourceEvent);
        }
        if (sourceEvent.Kind == CombatSimulationEventKind.CardCreated
            && IsAdvancedSpell(sourceEvent.DefinitionId))
        {
            foreach (var program in programs.ToArray())
            {
                program.DispatchNamedEvent("AllDharmas", sourceEvent);
            }
        }
        if ((sourceEvent.Kind == CombatSimulationEventKind.CardExhausted
             || sourceEvent.Kind == CombatSimulationEventKind.ActionResolved)
            && context.State.Hand.Count == 0)
        {
            foreach (var program in programs.ToArray())
            {
                program.DispatchNamedEvent("NoCard", sourceEvent);
            }
        }
        if (!applyingScriptExecuteAdjustment
            && sourceEvent.Kind == CombatSimulationEventKind.DamageDealt
            && sourceEvent.Amount > 0
            && sourceEvent.SourceActorId == context.State.PlayerActorId)
        {
            var adjusted = sourceEvent.Amount;
            foreach (var program in programs.ToArray())
            {
                adjusted = program.DispatchScriptExecute(
                    sourceEvent,
                    adjusted);
            }
            var extra = Math.Max(0, adjusted - sourceEvent.Amount);
            if (extra > 0)
            {
                applyingScriptExecuteAdjustment = true;
                try
                {
                    context.ApplyEffects(
                        new[]
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.Damage,
                                Target = CombatSimulationTarget.SelectedEnemy,
                                DefinitionId = "native-script-adjustment",
                                Amount = extra
                            }
                        },
                        sourceEvent.SourceActorId,
                        sourceEvent.TargetActorId,
                        sourceEvent);
                }
                finally
                {
                    applyingScriptExecuteAdjustment = false;
                }
            }
        }
        switch (sourceEvent.Kind)
        {
            case CombatSimulationEventKind.CardPlayed:
                RunCardPhase(
                    context,
                    sourceEvent,
                    "NativeUseScript",
                    "use");
                RememberPreviousCard(context, sourceEvent);
                break;
            case CombatSimulationEventKind.CardDrawn:
                RunCardPhase(
                    context,
                    sourceEvent,
                    "NativeDrawScript",
                    "draw");
                break;
            case CombatSimulationEventKind.CardDiscarded:
                RunCardPhase(
                    context,
                    sourceEvent,
                    "NativeDropScript",
                    "drop");
                break;
            case CombatSimulationEventKind.StatusAdded:
                EnsureStatusProgram(
                    context,
                    sourceEvent.TargetActorId,
                    sourceEvent.DefinitionId,
                    sourceEvent);
                break;
            case CombatSimulationEventKind.StatusRemoved:
                ClearStatusProgram(context, sourceEvent);
                break;
        }
    }

    public void Complete(ICombatSimulationRuntimeContext context)
    {
        foreach (var program in programs.ToArray())
        {
            program.Complete();
        }
        AuditCrossRoleSkillCards(context);
    }

    private static bool AuditCrossRoleSkillCards(
        ICombatSimulationRuntimeContext context)
    {
        var currentRoleSkills = new HashSet<string>(
            context.Scenario.Player.SkillCardIds,
            StringComparer.OrdinalIgnoreCase);
        var foreignRoleSkills = new HashSet<string>(
            context.Scenario.RewardCatalog
                .Where(item =>
                    item.Kind.Equals(
                        "Card",
                        StringComparison.OrdinalIgnoreCase)
                    && item.CardAcquisition
                       == CombatCampaignCardAcquisition.SkillOnly
                    && !currentRoleSkills.Contains(item.RewardId))
                .Select(item => item.RewardId),
            StringComparer.OrdinalIgnoreCase);
        var leaked = context.State.Cards.FirstOrDefault(card =>
            foreignRoleSkills.Contains(card.CardId)
            && !card.CreationCrossRoleSkillAuthorized);
        if (leaked == null)
        {
            return false;
        }
        context.AddUnsupported(
            "cross-role-skill-card:"
            + leaked.CardId
            + ":source="
            + leaked.CreationSource
            + ":sourceId="
            + leaked.CreationSourceId);
        context.Terminate(
            CombatSimulationOutcome.Invalid,
            CombatTerminationReason.UnsupportedRule);
        return true;
    }

    public void BeforePolicyDecision(ICombatSimulationRuntimeContext context)
    {
        if (AuditCrossRoleSkillCards(context))
        {
            return;
        }
        var player = context.State.Player;
        if (player == null || !player.Alive || context.State.Hand.Count == 0)
        {
            return;
        }
        if (player.Variables.GetValueOrDefault(
                "NativeEndTurnRequested",
                0d) > 0d)
        {
            foreach (var instanceId in context.State.Hand)
            {
                var instance = context.State.FindCard(instanceId);
                if (instance == null)
                {
                    continue;
                }
                if (!endTurnCostModifiers.ContainsKey(instanceId))
                {
                    endTurnCostModifiers[instanceId] = instance.CostModifier;
                }
                instance.CostModifier = 10000;
            }
            return;
        }
        var cards = context.Ruleset.SnapshotCards()
            .ToDictionary(item => item.CardId, StringComparer.OrdinalIgnoreCase);
        var costs = context.State.Hand
            .Select(context.State.FindCard)
            .Where(item => item != null && cards.ContainsKey(item.CardId))
            .Select(item => Math.Max(
                0,
                cards[item!.CardId].Cost + item.CostModifier))
            .ToList();
        if (costs.Count == 0
            || costs.Any(cost => cost <= player.Energy)
            || !costs.Any(cost => cost > player.Energy))
        {
            return;
        }
        var key = string.Join(
            ":",
            context.State.Turn,
            player.Energy,
            context.State.Hand.Count);
        if (!noPowerDecisionStates.Add(key))
        {
            return;
        }
        foreach (var program in programs.ToArray())
        {
            program.DispatchNamedEvent("NoPowerWhenTry", null);
        }
    }

    private void RestoreEndTurnCosts(
        ICombatSimulationRuntimeContext context)
    {
        foreach (var pair in endTurnCostModifiers)
        {
            var instance = context.State.FindCard(pair.Key);
            if (instance != null)
            {
                instance.CostModifier = pair.Value;
            }
        }
        endTurnCostModifiers.Clear();
        var player = context.State.Player;
        if (player != null)
        {
            player.Variables["NativeEndTurnRequested"] = 0d;
        }
    }

    private void RunCardPhase(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent,
        string metadataKey,
        string phase)
    {
        var instance = context.State.FindCard(sourceEvent.CardInstanceId);
        if (instance == null
            || !context.Ruleset.TryGetCard(instance.CardId, out var definition)
            || !AuraToolsNativeGameScriptAudit.UsesNativeScript(definition))
        {
            return;
        }
        if (!cardPrograms.TryGetValue(instance.InstanceId, out var globals))
        {
            var master = NewNativeRule(
                definition.CardId,
                "Card",
                string.Join(
                    "\n",
                    definition.Metadata.GetValueOrDefault(
                        "NativeInitScript",
                        ""),
                    definition.Metadata.GetValueOrDefault(
                        "NativeUseScript",
                        ""),
                    definition.Metadata.GetValueOrDefault(
                        "NativeDrawScript",
                        ""),
                    definition.Metadata.GetValueOrDefault(
                        "NativeDropScript",
                        "")));
            var sourceActorId =
                context.State.FindActor(sourceEvent.SourceActorId)?.Alive == true
                    ? sourceEvent.SourceActorId
                    : context.State.PlayerActorId;
            globals = new NativeRewardScriptGlobals(
                context,
                master,
                sourceActorId,
                sourceEvent.TargetActorId,
                instance.InstanceId,
                registerProgram: RegisterProgram);
            cardPrograms[instance.InstanceId] = globals;
            programs.Add(globals);
            if (!RunNativePhase(
                    context,
                    globals,
                    definition.CardId,
                    "Card",
                    "init",
                    definition.Metadata.GetValueOrDefault(
                        "NativeInitScript",
                        ""),
                    sourceEvent))
            {
                return;
            }
        }
        RunNativePhase(
            context,
            globals,
            definition.CardId,
            "Card",
            phase,
            definition.Metadata.GetValueOrDefault(metadataKey, ""),
            sourceEvent);
        globals.SynchronizeCardVariables();
    }

    private void EnsureStatusProgram(
        ICombatSimulationRuntimeContext context,
        int actorId,
        string statusId,
        CombatSimulationEvent? sourceEvent)
    {
        if (actorId <= 0
            || string.IsNullOrWhiteSpace(statusId)
            || !context.Ruleset.TryGetStatus(statusId, out var definition)
            || !AuraToolsNativeGameScriptAudit.UsesNativeScript(definition))
        {
            return;
        }
        var key = actorId + "|" + statusId;
        if (statusPrograms.ContainsKey(key))
        {
            return;
        }
        var master = NewNativeRule(
            statusId,
            "Status",
            string.Join(
                "\n",
                definition.Metadata.GetValueOrDefault(
                    "NativeInitScript",
                    ""),
                definition.Metadata.GetValueOrDefault(
                    "NativeApplyScript",
                    ""),
                definition.Metadata.GetValueOrDefault(
                    "NativeClearScript",
                    "")));
        var globals = new NativeRewardScriptGlobals(
            context,
            master,
            actorId,
            sourceEvent?.SourceActorId ?? 0,
            registerProgram: RegisterProgram);
        statusPrograms[key] = globals;
        programs.Add(globals);
        if (!RunNativePhase(
                context,
                globals,
                statusId,
                "Status",
                "init",
                definition.Metadata.GetValueOrDefault(
                    "NativeInitScript",
                    ""),
                sourceEvent))
        {
            return;
        }
        RunNativePhase(
            context,
            globals,
            statusId,
            "Status",
            "apply",
            definition.Metadata.GetValueOrDefault(
                "NativeApplyScript",
                ""),
            sourceEvent);
        globals.DispatchNamedEvent(
            statusId + "OnLevelChange",
            sourceEvent);
    }

    private void ClearStatusProgram(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        var key = sourceEvent.TargetActorId + "|" + sourceEvent.DefinitionId;
        if (!statusPrograms.TryGetValue(key, out var globals))
        {
            return;
        }
        if (context.Ruleset.TryGetStatus(
                sourceEvent.DefinitionId,
                out var definition))
        {
            RunNativePhase(
                context,
                globals,
                definition.StatusId,
                "Status",
                "clear",
                definition.Metadata.GetValueOrDefault(
                    "NativeClearScript",
                    ""),
                sourceEvent);
        }
        globals.DeferredEffects(
            sourceEvent.TargetActorId,
            sourceEvent.DefinitionId).Clear();
        statusPrograms.Remove(key);
        programs.Remove(globals);
    }

    private static CombatScenarioRewardRule NewNativeRule(
        string definitionId,
        string kind,
        string script)
    {
        return new CombatScenarioRewardRule
        {
            RewardId = definitionId,
            Kind = kind,
            FightScript = script
        };
    }

    private static bool RunNativePhase(
        ICombatSimulationRuntimeContext context,
        NativeRewardScriptGlobals globals,
        string definitionId,
        string kind,
        string phase,
        string script,
        CombatSimulationEvent? sourceEvent)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return true;
        }
        var result = globals.RunScript(
            NewNativeRule(definitionId, kind, script),
            sourceEvent);
        if (result.Success)
        {
            return true;
        }
        context.AddUnsupported(
            "native-"
            + kind.ToLowerInvariant()
            + "-script:"
            + definitionId
            + ":"
            + phase
            + ":"
            + result.Message);
        return false;
    }

    private static void RememberPreviousCard(
        ICombatSimulationRuntimeContext context,
        CombatSimulationEvent sourceEvent)
    {
        var actor = context.State.FindActor(sourceEvent.SourceActorId);
        if (actor == null
            || !context.Ruleset.TryGetCard(
                sourceEvent.DefinitionId,
                out var definition))
        {
            return;
        }
        actor.Variables["NativePreviousCardCombo"] =
            definition.Tags.Contains(
                "Combo",
                StringComparer.OrdinalIgnoreCase)
                ? 1d
                : 0d;
        actor.Variables["NativeHasPreviousCard"] = 1d;
    }

    private static bool IsAdvancedSpell(string cardId)
    {
        return cardId is "SpellCard_16"
            or "SpellCard_17"
            or "SpellCard_18"
            or "SpellCard_19"
            or "SpellCard_20"
            or "SpellCard_21"
            or "SpellCard_22"
            or "SpellCard_23";
    }

    private static void ApplyHardAffixes(
        ICombatSimulationRuntimeContext context)
    {
        var player = context.State.Player;
        if (player == null
            || player.Variables.GetValueOrDefault("Difficulty", 1d) < 5d)
        {
            return;
        }
        var enemies = context.State.Actors
            .Where(item => item.Kind == CombatSimulationActorKind.Enemy)
            .OrderBy(item => item.ActorId)
            .ToList();
        foreach (var enemy in enemies)
        {
            AddOrIncreaseStatus(enemy, "buff_elements", 4);
        }
        var encounterKind = (int)Math.Round(
            player.Variables.GetValueOrDefault("EncounterKind", 0d));
        if (encounterKind == (int)CombatCampaignEncounterKind.Elite)
        {
            foreach (var enemy in enemies)
            {
                AddOrIncreaseStatus(enemy, "buff_elementalBody", 1);
            }
        }
        if ((encounterKind == (int)CombatCampaignEncounterKind.Elite
             || encounterKind == (int)CombatCampaignEncounterKind.Boss)
            && enemies.Count > 0)
        {
            var target = enemies
                .OrderByDescending(item => item.MaxHp)
                .ThenBy(item => item.ActorId)
                .First();
            var traits = new[]
            {
                "SpecialBuff_Restrain",
                "SpecialBuff_Irritable",
                "SpecialBuff_Hysteresis"
            };
            var selected = traits[context.NextRandomInt(
                "hard-affix:Hard_8",
                traits.Length)];
            AddOrIncreaseStatus(target, selected, 2);
        }
    }

    private static void AddOrIncreaseStatus(
        CombatActorState actor,
        string statusId,
        int stacks)
    {
        var current = actor.Statuses.FirstOrDefault(item => string.Equals(
            item.StatusId,
            statusId,
            StringComparison.OrdinalIgnoreCase));
        if (current == null)
        {
            actor.Statuses.Add(new CombatStatusState
            {
                StatusId = statusId,
                Stacks = stacks
            });
        }
        else
        {
            current.Stacks += stacks;
        }
    }
}

internal sealed class NativeRewardScriptCompileResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = "";
}

internal static class NativeRewardProgramRegistry
{
    public static int ProgramCount =>
        NativeRewardScriptGlobals.PrecompiledProgramCount;

    public static NativeRewardScriptCompileResult TryRun(
        CombatScenarioRewardRule rule,
        NativeRewardScriptGlobals globals)
    {
        var key = Key(rule.FightScript);
        return globals.TryRunPrecompiledProgram(key, out var message)
            ? new NativeRewardScriptCompileResult { Success = true }
            : new NativeRewardScriptCompileResult { Message = message };
    }

    public static NativeRewardScriptCompileResult Validate(
        CombatScenarioRewardRule rule)
    {
        var key = Key(rule.FightScript);
        return NativeRewardScriptGlobals.ContainsPrecompiledProgram(key)
            ? new NativeRewardScriptCompileResult { Success = true }
            : new NativeRewardScriptCompileResult
            {
                Message = "precompiled native program is missing: " + key
            };
    }

    internal static string Key(string script)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(
            Encoding.UTF8.GetBytes(Normalize(script)));
        var result = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            result.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    internal static string Normalize(string script)
    {
        var result = script ?? "";
        result = Regex.Replace(
            result,
            @"\bDataId\.([A-Za-z_][A-Za-z0-9_]*)",
            match => "\"" + match.Groups[1].Value + "\"");
        result = result.Replace(
            "EventType.ScriptExecute.ToString()",
            "\"ScriptExecute\"");
        result = Regex.Replace(result, @"\bHurtData\b", "NativeRewardHurtData");
        result = Regex.Replace(result, @"\bActionData\b", "NativeRewardActionData");
        result = Regex.Replace(result, @"\bCreateData\b", "NativeRewardCreateData");
        result = Regex.Replace(result, @"\bBurnData\b", "NativeRewardBurnData");
        result = Regex.Replace(result, @"\bAddBuffData\b", "NativeRewardAddBuffData");
        result = Regex.Replace(result, @"\bOutHealData\b", "NativeRewardOutHealData");
        result = Regex.Replace(
            result,
            @"\bScriptExecuteData\b",
            "NativeRewardScriptExecuteData");
        result = Regex.Replace(result, @"\bIDataConfig\b", "NativeRewardDataConfig");
        result = Regex.Replace(result, @"\bDataConfig\b", "NativeRewardDataConfig");
        result = Regex.Replace(result, @"\bCardItem\b", "NativeRewardCardItem");
        result = Regex.Replace(
            result,
            @"(\b[A-Za-z_][A-Za-z0-9_]*)\.data\.Localize\(([^)\r\n]+)\)",
            "$1.data.GetValueOrDefault($2, \"\")");
        result = result.Replace("FightManager.Instance.statuses", "Statuses");
        result = Regex.Replace(result, @"\bDataType\b", "NativeRewardDataType");
        result = Regex.Replace(result, @"\bIStatusManager\b", "NativeRewardActor");
        result = result.Replace(
            "NativeRewardActor.State",
            "NativeRewardActorState");
        result = result.Replace("Dice.State", "NativeRewardDiceState");
        result = Regex.Replace(
            result,
            @"\bFightCardManager\b",
            "NativeRewardFightCardManager");
        result = Regex.Replace(
            result,
            @"\bRoleTable\b",
            "NativeRewardRoleTable");
        result = result.Replace(
            "new List<NativeRewardDataConfig>(DeckCard)",
            "DeckCard.Select(x => x.dataConfig).ToList()");
        result = result.Replace(
            "new List<NativeRewardDataConfig>(UsedCard)",
            "UsedCard.Select(x => x.dataConfig).ToList()");
        result = Regex.Replace(
            result,
            @"\bnew\s+NativeRewardDataConfig\s*\(",
            "CreateDataConfig(");
        result = Regex.Replace(
            result,
            @"\b(?<config>[A-Za-z_][A-Za-z0-9_]*)\.data\s*=\s*"
            + @"(?<value>[A-Za-z_][A-Za-z0-9_]*)\s*;",
            "${config}.ReplaceData(${value});");
        result = Regex.Replace(
            result,
            @"\b(?<pile>DeckCard|HandCard|UsedCard)\s*"
            + @"\.Cast<NativeRewardDataConfig>\s*\(\s*\)",
            "${pile}.Select(x => x.dataConfig)");
        result = Regex.Replace(
            result,
            @"\b(?<actor>Self|Target)\.AddBuff\(\s*"
            + @"(?<id>""[^""]+"")\s*,\s*(?<amount>[^,)]+)\s*\)"
            + @"\.GetBuff\(\s*\k<id>\s*\)",
            "AddAndGetBuff(${actor}, ${id}, ${amount})");
        result = result.Replace(
            "effectList.Add((dataConfig, () => { RunScript(\"UseScript\"); }));",
            "effectList.Add(dataConfig, () => { RunScript(\"UseScript\"); });");
        result = result.Replace(
            "effectList.Add((tempCard.dataConfig, () => { tempCard.dataConfig.scriptExecutor.RunScript(\"UseScript\"); }));",
            "effectList.Add(tempCard.dataConfig, () => { UseCard(tempCard.dataConfig); });");
        result = Regex.Replace(
            result,
            @"(?<effects>\b[A-Za-z_][A-Za-z0-9_]*\??\.effectList)"
            + @"\.First\s*\(\s*\)\.action\s*\(\s*\)",
            "${effects}.InvokeFirst()");
        result = Regex.Replace(
            result,
            @"(?<effects>\b[A-Za-z_][A-Za-z0-9_]*\??\.effectList)"
            + @"\.Last\s*\(\s*\)\.action\s*\(\s*\)",
            "${effects}.InvokeLast()");
        result = Regex.Replace(
            result,
            @"int\s+c\s*=\s*DeckCard\.Count\s*;\s*"
            + @"for\s*\(\s*int\s+i\s*=\s*c\s*-\s*1\s*;\s*i\s*>=\s*0\s*;\s*i--\s*\)\s*"
            + @"\{\s*var\s+d\s*=\s*DeckCard\s*\[\s*i\s*\]\s*;\s*"
            + @"UseCard\s*\(\s*d\s*\)\s*;\s*"
            + @"BurnCardByData\s*\(\s*d\s*\)\s*;\s*\}\s*"
            + @"if\s*\(\s*c\s*>\s*0\s*\)\s*"
            + @"\{\s*ChangePower\s*\(\s*c\.ToString\s*\(\s*\)\s*\)\s*;\s*\}",
            "UseAndBurnDrawPileSnapshot();");
        result = Regex.Replace(result, @"\bMathf\b", "NativeRewardMathf");
        result = Regex.Replace(result, @"\bDebug\b", "NativeRewardDebug");
        return result;
    }
}

public static class AuraToolsNativeRewardScriptAudit
{
    public static List<string> Validate(CombatCampaignDefinition campaign)
    {
        var failures = new List<string>();
        foreach (var card in campaign.Rewards
                     .Where(item => item.Kind == CombatCampaignRewardKind.Card)
                     .OrderBy(item => item.RewardId, StringComparer.Ordinal))
        {
            if (CombatCampaignCardAcquisitionPolicy
                    .IsGeneratedOnlyIdentifier(card.RewardId)
                && card.CardAcquisition
                != CombatCampaignCardAcquisition.GeneratedOnly)
            {
                failures.Add(
                    card.RewardId
                    + ": generated-only card is not classified GeneratedOnly");
            }
            if (card.CardAcquisition
                    == CombatCampaignCardAcquisition.GeneratedOnly
                && CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(card))
            {
                failures.Add(
                    card.RewardId
                    + ": generated-only card leaked into reward pool");
            }
        }
        foreach (var starterId in campaign.Player.Deck)
        {
            var card = campaign.Rewards.FirstOrDefault(item =>
                item.Kind == CombatCampaignRewardKind.Card
                && string.Equals(
                    item.RewardId,
                    starterId,
                    StringComparison.OrdinalIgnoreCase));
            if (CombatCampaignCardAcquisitionPolicy
                    .IsGeneratedOnlyIdentifier(starterId)
                || (card != null
                    && !CombatCampaignCardAcquisitionPolicy
                        .CanEnterStartingDeck(card)))
            {
                failures.Add(
                    starterId
                    + ": card acquisition policy forbids starting deck");
            }
        }
        foreach (var skillId in campaign.Player.SkillCardIds)
        {
            var card = campaign.Rewards.FirstOrDefault(item =>
                item.Kind == CombatCampaignRewardKind.Card
                && string.Equals(
                    item.RewardId,
                    skillId,
                    StringComparison.OrdinalIgnoreCase));
            if (card == null
                || card.CardAcquisition
                   != CombatCampaignCardAcquisition.SkillOnly
                || CombatCampaignCardAcquisitionPolicy.CanEnterRewardPool(card))
            {
                failures.Add(
                    skillId
                    + ": role skill must be classified SkillOnly and excluded from rewards");
            }
        }
        foreach (var blessingId in campaign.Player.FamiliarBlessingIds)
        {
            var blessing = campaign.Rewards.FirstOrDefault(item =>
                item.Kind == CombatCampaignRewardKind.Blessing
                && string.Equals(
                    item.RewardId,
                    blessingId,
                    StringComparison.OrdinalIgnoreCase));
            if (blessing == null
                || blessing.BlessingAcquisition
                   != CombatCampaignBlessingAcquisition.FamiliarInnate)
            {
                failures.Add(
                    blessingId
                    + ": familiar blessing must be classified FamiliarInnate");
            }
        }
        foreach (var reward in campaign.Rewards
                     .Where(item => item.Kind != CombatCampaignRewardKind.Card)
                     .OrderBy(item => item.RewardId, StringComparer.Ordinal))
        {
            ValidateProgression(reward, failures);
            if (string.IsNullOrWhiteSpace(reward.FightScript))
            {
                continue;
            }
            var result = NativeRewardProgramRegistry.Validate(
                new CombatScenarioRewardRule
                {
                    RewardId = reward.RewardId,
                    Kind = reward.Kind.ToString(),
                    NativeScriptHash = reward.NativeScriptHash,
                    FightScript = reward.FightScript,
                    Variables = new Dictionary<string, string>(
                        reward.InitialVariables,
                        StringComparer.OrdinalIgnoreCase)
                });
            if (!result.Success)
            {
                failures.Add(reward.RewardId + ": " + result.Message);
            }
        }
        return failures;
    }

    private static void ValidateProgression(
        CombatCampaignRewardDefinition reward,
        ICollection<string> failures)
    {
        var script = reward.OwnScript ?? "";
        if (string.IsNullOrWhiteSpace(script))
        {
            return;
        }
        var supportedCalls = new HashSet<string>(
            new[]
            {
                "PlayerInfo.AddBless",
                "PlayerInfo.AddCard",
                "PlayerInfo.DelayAddBless",
                "PlayerInfo.DelayAddCard",
                "PlayerInfo.DelayAddRelic",
                "PlayerInfo.ChangeSelected",
                "PlayerInfo.RandomRemoveCard",
                "PlayerInfo.SpecialVars.ContainsKey",
                "ReplaceSelfRelicWithRandomRelic"
            },
            StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     script,
                     @"\b([A-Za-z_][A-Za-z0-9_.]*)\s*\("))
        {
            var call = match.Groups[1].Value;
            if (supportedCalls.Contains(call)
                || call is "if"
                or "for"
                or "foreach"
                or "while"
                or "switch"
                or "Math.Max"
                or "Math.Min"
                or "System.Math.Max"
                or "System.Math.Min"
                or "int.Parse"
                or "float.Parse"
                or "string.Join")
            {
                continue;
            }
            failures.Add(
                reward.RewardId + ": unsupported progression call " + call);
        }
        if (script.IndexOf(
                "RandomRemoveCard",
                StringComparison.Ordinal) >= 0
            && reward.RandomCardRemovalCount <= 0)
        {
            failures.Add(reward.RewardId + ": random card removal not modeled");
        }
        if (script.IndexOf(
                "ReplaceSelfRelicWithRandomRelic",
                StringComparison.Ordinal) >= 0
            && reward.ReplacementRelicTier <= 0)
        {
            failures.Add(reward.RewardId + ": relic replacement not modeled");
        }
        if (script.IndexOf(
                "SpecialVars.ContainsKey",
                StringComparison.Ordinal) >= 0
            && string.IsNullOrWhiteSpace(
                reward.OneTimeSpecialVariableKey))
        {
            failures.Add(reward.RewardId + ": one-time guard not modeled");
        }
    }
}

public static class AuraToolsNativeGameScriptAudit
{
    public static List<string> Validate(CombatRuleset ruleset)
    {
        var failures = new List<string>();
        foreach (var card in ruleset.SnapshotCards()
                     .Where(UsesNativeScript)
                     .OrderBy(item => item.CardId, StringComparer.Ordinal))
        {
            ValidateScript(
                "card",
                card.CardId,
                "init",
                card.Metadata.GetValueOrDefault("NativeInitScript", ""),
                failures);
            ValidateScript(
                "card",
                card.CardId,
                "use",
                card.Metadata.GetValueOrDefault("NativeUseScript", ""),
                failures);
            ValidateScript(
                "card",
                card.CardId,
                "draw",
                card.Metadata.GetValueOrDefault("NativeDrawScript", ""),
                failures);
            ValidateScript(
                "card",
                card.CardId,
                "drop",
                card.Metadata.GetValueOrDefault("NativeDropScript", ""),
                failures);
        }
        foreach (var status in ruleset.SnapshotStatuses()
                     .Where(UsesNativeScript)
                     .OrderBy(item => item.StatusId, StringComparer.Ordinal))
        {
            ValidateScript(
                "status",
                status.StatusId,
                "init",
                status.Metadata.GetValueOrDefault("NativeInitScript", ""),
                failures);
            ValidateScript(
                "status",
                status.StatusId,
                "apply",
                status.Metadata.GetValueOrDefault("NativeApplyScript", ""),
                failures);
            ValidateScript(
                "status",
                status.StatusId,
                "clear",
                status.Metadata.GetValueOrDefault("NativeClearScript", ""),
                failures);
        }
        return failures;
    }

    internal static bool UsesNativeScript(CombatCardDefinition definition)
    {
        return definition.Metadata.GetValueOrDefault(
            "NativeExecution",
            "").Equals("Script", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool UsesNativeScript(CombatStatusDefinition definition)
    {
        return definition.Metadata.GetValueOrDefault(
            "NativeExecution",
            "").Equals("Script", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateScript(
        string kind,
        string definitionId,
        string phase,
        string script,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return;
        }
        var result = NativeRewardProgramRegistry.Validate(
            new CombatScenarioRewardRule
            {
                RewardId = definitionId,
                Kind = kind,
                NativeScriptHash = "",
                FightScript = script
            });
        if (!result.Success)
        {
            failures.Add(
                kind
                + ":"
                + definitionId
                + ":"
                + phase
                + ": "
                + result.Message);
        }
    }
}

public sealed partial class NativeRewardScriptGlobals
{
    private static readonly HashSet<string> CosmeticNoOpApis =
        new(StringComparer.Ordinal)
        {
            "AddDescription",
            "ClearAllDharmasSpellList",
            "UpdateAllDharmasSpellList",
            "UpdateRelicShow",
            "UpdateAch"
        };

    private static readonly ConditionalWeakTable<
        ICombatSimulationRuntimeContext,
        Dictionary<int, NativeRewardDataConfig>> SharedCardConfigurations =
        new();
    private static readonly ConditionalWeakTable<
        ICombatSimulationRuntimeContext,
        Dictionary<
            string,
            List<(NativeRewardDataConfig dataConfig, Action action)>>>
        SharedDeferredEffects = new();
    private static readonly ConditionalWeakTable<
        ICombatSimulationRuntimeContext,
        Dictionary<string, NativeRewardDataConfig>>
        SharedStatusConfigurations = new();
    private readonly ICombatSimulationRuntimeContext context;
    private readonly CombatScenarioRewardRule rule;
    private readonly Dictionary<string, List<NativeRewardEventHandler>> handlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> activeHandlerChains =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> activeHandlerIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<
        string,
        List<(NativeRewardDataConfig dataConfig, Action action)>> deferredEffects;
    private readonly Dictionary<string, NativeRewardDataConfig>
        statusConfigurations;
    private readonly Dictionary<int, NativeRewardDataConfig> cardConfigurations;
    private readonly Action<NativeRewardScriptGlobals>? registerProgram;
    private CombatSimulationEvent? currentEvent;
    private List<int> selectedActorIds = new();
    private int executionSourceActorId;
    private int executionTargetActorId;
    private int nextHandlerOrdinal;

    public NativeRewardScriptGlobals(
        ICombatSimulationRuntimeContext context,
        CombatScenarioRewardRule rule,
        int sourceActorId = 0,
        int targetActorId = 0,
        int cardInstanceId = 0,
        NativeRewardDataConfig? dataConfigOverride = null,
        Action<NativeRewardScriptGlobals>? registerProgram = null)
    {
        this.context = context;
        this.rule = rule;
        this.registerProgram = registerProgram;
        cardConfigurations = SharedCardConfigurations.GetOrCreateValue(context);
        deferredEffects = SharedDeferredEffects.GetOrCreateValue(context);
        statusConfigurations =
            SharedStatusConfigurations.GetOrCreateValue(context);
        executionSourceActorId = sourceActorId > 0
            ? sourceActorId
            : context.State.PlayerActorId;
        executionTargetActorId = targetActorId;
        dataConfig = dataConfigOverride
                     ?? (cardInstanceId > 0
            ? CardConfig(cardInstanceId)
            : new NativeRewardDataConfig(rule.RewardId));
        Vars = dataConfigOverride != null
            ? dataConfig.Vars
            : cardInstanceId > 0
            ? dataConfig.Vars
            : new NativeRewardStringDictionary(rule.Variables);
        foreach (var pair in rule.Variables)
        {
            Vars[pair.Key] = pair.Value;
        }
        foreach (Match match in Regex.Matches(
                     rule.FightScript ?? "",
                     @"\bVars\s*\[\s*""([^""]+)""\s*\]"))
        {
            if (!Vars.ContainsKey(match.Groups[1].Value))
            {
                Vars[match.Groups[1].Value] = "0";
            }
        }
        dataConfig.Vars = Vars;
        if (string.Equals(
                rule.Kind,
                "Status",
                StringComparison.OrdinalIgnoreCase))
        {
            statusConfigurations[
                StatusConfigurationKey(
                    executionSourceActorId,
                    rule.RewardId)] = dataConfig;
        }
        foreach (Match match in Regex.Matches(
                     rule.FightScript ?? "",
                     @"\bSpecialVars\s*\[\s*""([^""]+)""\s*\]"))
        {
            if (!context.Scenario.CampaignVariables.ContainsKey(
                    match.Groups[1].Value))
            {
                context.Scenario.CampaignVariables[
                    match.Groups[1].Value] = "0";
            }
        }
        PlayerInfo = new NativeRewardPlayerInfo(this);
        CheckDice = new NativeRewardDice(this, "check");
        DefaultDice = new NativeRewardDice(this, "default");
        NativeRewardFightCardManager.Instance.Globals = this;
        dataConfig.Vars = Vars;
        SetStatus("Self");
    }

    internal void ApplyCopiedProgramDefaults(
        CombatScenarioRewardRule sourceRule)
    {
        foreach (var pair in sourceRule.Variables)
        {
            if (!Vars.TryGetValue(pair.Key, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                Vars[pair.Key] = pair.Value;
            }
        }
        foreach (Match match in Regex.Matches(
                     sourceRule.FightScript ?? "",
                     @"\bVars\s*\[\s*""([^""]+)""\s*\]"))
        {
            var key = match.Groups[1].Value;
            if (!Vars.ContainsKey(key))
            {
                Vars[key] = "0";
            }
        }
    }

    public NativeRewardStringDictionary Vars { get; }

    public NativeRewardPlayerInfo PlayerInfo { get; }

    public NativeRewardActor? Self => Actor(executionSourceActorId);

    public NativeRewardActor? Target => Actor(executionTargetActorId);

    public NativeRewardDice CheckDice { get; }

    public NativeRewardDice DefaultDice { get; }

    public NativeRewardDataConfig dataConfig { get; }

    public Dictionary<NativeRewardActor, Dictionary<string, int>> GetStatus { get; } =
        new();

    public Dictionary<string, Delegate> ScriptDict { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<NativeRewardActor> Object => selectedActorIds
        .Select(Actor)
        .Where(item => item != null)
        .Cast<NativeRewardActor>()
        .ToList();

    public Dictionary<int, NativeRewardActor> Statuses =>
        context.State.Actors.ToDictionary(
            actor => actor.ActorId,
            actor => new NativeRewardActor(this, actor.ActorId));

    public List<NativeRewardCardItem> HandCard =>
        Cards(context.State.Hand);

    public List<NativeRewardCardItem> DeckCard =>
        Cards(context.State.DrawPile);

    public List<NativeRewardCardItem> UsedCard =>
        Cards(context.State.DiscardPile);

    public void AddEvent(string eventName, Action action)
    {
        Register(eventName, _ => action());
    }

    public void AddEvent<T>(string eventName, Action<T> action)
    {
        Register(eventName, action);
    }

    public void AddBaseEvent(string eventName, Action action)
    {
        AddEvent(eventName, action);
    }

    public void AddDescription(params object[] _)
    {
        IgnoreCosmeticApi("AddDescription");
    }

    public NativeRewardDataConfig CreateDataConfig(
        object id,
        NativeRewardDataType type)
    {
        var definitionId = Text(id);
        NativeRewardDataConfig? result = null;
        result = new NativeRewardDataConfig(
            definitionId,
            phase => RunDataConfigScript(result!, type, phase));
        result.data["Type"] = DataKind(type);

        if (context.Ruleset.TryGetCard(definitionId, out var card))
        {
            foreach (var pair in card.Metadata)
            {
                result.data[pair.Key] = pair.Value;
                result.Vars[pair.Key] = pair.Value;
            }
            result.data["Name"] = card.DisplayName;
            result.data["Tag"] = string.Join(",", card.Tags);
            result.data["Rarity"] =
                card.Rarity.ToString(CultureInfo.InvariantCulture);
            result.data["Expend"] =
                card.Cost.ToString(CultureInfo.InvariantCulture);
            return result;
        }

        var reward = context.Scenario.RewardCatalog.FirstOrDefault(item =>
            string.Equals(
                item.RewardId,
                definitionId,
                StringComparison.OrdinalIgnoreCase));
        if (reward == null)
        {
            return result;
        }
        result.data["Type"] = reward.Kind;
        result.data["Rarity"] =
            Math.Max(1, reward.Tier).ToString(CultureInfo.InvariantCulture);
        foreach (var pair in reward.Variables)
        {
            result.Vars[pair.Key] = pair.Value;
        }
        return result;
    }

    private void RunDataConfigScript(
        NativeRewardDataConfig config,
        NativeRewardDataType type,
        string phase)
    {
        var definitionId = config.data.GetValueOrDefault(
            "Id",
            config.InstanceID);
        var script = "";
        var kind = DataKind(type);
        var nativeScriptHash = "";
        if (context.Ruleset.TryGetCard(definitionId, out var card))
        {
            kind = "Card";
            var key = phase.Equals(
                "UseScript",
                StringComparison.OrdinalIgnoreCase)
                ? "NativeUseScript"
                : phase.Equals(
                    "DrawScript",
                    StringComparison.OrdinalIgnoreCase)
                    ? "NativeDrawScript"
                    : phase.Equals(
                        "DropScript",
                        StringComparison.OrdinalIgnoreCase)
                        ? "NativeDropScript"
                        : "NativeInitScript";
            script = card.Metadata.GetValueOrDefault(key, "");
        }
        else
        {
            var reward = context.Scenario.RewardCatalog.FirstOrDefault(item =>
                string.Equals(
                    item.RewardId,
                    definitionId,
                    StringComparison.OrdinalIgnoreCase));
            if (reward != null
                && phase.Equals(
                    "FightScript",
                    StringComparison.OrdinalIgnoreCase))
            {
                kind = reward.Kind;
                nativeScriptHash = reward.NativeScriptHash;
                script = reward.FightScript;
            }
        }
        if (string.IsNullOrWhiteSpace(script))
        {
            return;
        }

        var executionRule = new CombatScenarioRewardRule
        {
            RewardId = definitionId,
            Kind = kind,
            NativeScriptHash = nativeScriptHash,
            FightScript = script,
            Variables = new Dictionary<string, string>(
                config.Vars,
                StringComparer.OrdinalIgnoreCase)
        };
        var nested = new NativeRewardScriptGlobals(
            context,
            executionRule,
            executionSourceActorId,
            executionTargetActorId,
            dataConfigOverride: config,
            registerProgram: registerProgram);
        var execution = nested.RunScript(executionRule, currentEvent);
        if (!execution.Success)
        {
            context.AddUnsupported(
                "native-created-data-script:"
                + definitionId
                + ":"
                + phase
                + ":"
                + execution.Message);
            return;
        }
        registerProgram?.Invoke(nested);
    }

    private static string DataKind(NativeRewardDataType type)
    {
        return type switch
        {
            NativeRewardDataType.Bless => "Blessing",
            NativeRewardDataType.Relic => "Relic",
            NativeRewardDataType.Buff => "Status",
            NativeRewardDataType.EnchTag => "Enchantment",
            NativeRewardDataType.EnemyCard => "EnemyCard",
            _ => "Card"
        };
    }

    public void RunScript(string phase)
    {
        string script = "";
        if (context.Ruleset.TryGetCard(rule.RewardId, out var card))
        {
            var key = phase.Equals(
                "UseScript",
                StringComparison.OrdinalIgnoreCase)
                ? "NativeUseScript"
                : phase.Equals(
                    "DrawScript",
                    StringComparison.OrdinalIgnoreCase)
                    ? "NativeDrawScript"
                    : phase.Equals(
                        "DropScript",
                        StringComparison.OrdinalIgnoreCase)
                        ? "NativeDropScript"
                        : "NativeInitScript";
            script = card.Metadata.GetValueOrDefault(key, "");
        }
        else if (context.Ruleset.TryGetStatus(
                     rule.RewardId,
                     out var status))
        {
            var key = phase.Equals(
                "ClearScript",
                StringComparison.OrdinalIgnoreCase)
                ? "NativeClearScript"
                : phase.Equals(
                    "ApplyScript",
                    StringComparison.OrdinalIgnoreCase)
                    ? "NativeApplyScript"
                    : "NativeInitScript";
            script = status.Metadata.GetValueOrDefault(key, "");
        }
        if (!string.IsNullOrWhiteSpace(script))
        {
            RunScript(
                new CombatScenarioRewardRule
                {
                    RewardId = rule.RewardId,
                    Kind = rule.Kind,
                    FightScript = script
                },
                currentEvent);
            SynchronizeCardVariables();
        }
    }

    internal NativeRewardScriptCompileResult RunScript(
        CombatScenarioRewardRule executionRule,
        CombatSimulationEvent? sourceEvent)
    {
        var previousEvent = currentEvent;
        var previousSource = executionSourceActorId;
        var previousTarget = executionTargetActorId;
        try
        {
            SynchronizeNativeSkillTimesFromState();
            currentEvent = sourceEvent;
            if (sourceEvent?.SourceActorId > 0)
            {
                executionSourceActorId = sourceEvent.SourceActorId;
            }
            if (sourceEvent?.TargetActorId > 0)
            {
                executionTargetActorId = sourceEvent.TargetActorId;
            }
            return NativeRewardProgramRegistry.TryRun(executionRule, this);
        }
        finally
        {
            SynchronizeStateSkillCooldownsFromNative();
            currentEvent = previousEvent;
            executionSourceActorId = previousSource;
            executionTargetActorId = previousTarget;
        }
    }

    public void SetStatus(string selector)
    {
        var normalized = (selector ?? "").Trim();
        var excludeSelf = normalized.IndexOf(
            "ExSelf",
            StringComparison.OrdinalIgnoreCase) >= 0;
        normalized = Regex.Replace(
            normalized,
            "ExSelf",
            "",
            RegexOptions.IgnoreCase);
        var random = normalized.StartsWith(
            "AllRandom",
            StringComparison.OrdinalIgnoreCase);
        if (random)
        {
            normalized = normalized.Substring("AllRandom".Length);
        }
        var countMatch = Regex.Match(normalized, @"\d+");
        var count = countMatch.Success
            ? Math.Max(1, Number(countMatch.Value))
            : 1;
        if (countMatch.Success)
        {
            normalized = normalized.Replace(countMatch.Value, "");
        }
        var self = context.State.FindActor(executionSourceActorId);
        if (self == null)
        {
            selectedActorIds = new List<int>();
            return;
        }
        var allLiving = context.State.Actors
            .Where(actor => actor.Alive)
            .OrderBy(actor => actor.ActorId)
            .ToList();
        if (!random
            && normalized.Equals("Self", StringComparison.OrdinalIgnoreCase))
        {
            selectedActorIds = new List<int> { executionSourceActorId };
            return;
        }
        if (!random
            && normalized.Equals("Target", StringComparison.OrdinalIgnoreCase))
        {
            var opponents = allLiving.Where(actor =>
                    (actor.Kind == CombatSimulationActorKind.Enemy)
                    != (self.Kind == CombatSimulationActorKind.Enemy))
                .ToList();
            selectedActorIds = opponents.Any(actor =>
                    actor.ActorId == executionTargetActorId)
                ? new List<int> { executionTargetActorId }
                : opponents.Take(1).Select(actor => actor.ActorId).ToList();
            return;
        }

        var allFriendsOverride = Vars.GetValueOrDefault(
            "IsAllFriend",
            "False").Equals(
            "True",
            StringComparison.OrdinalIgnoreCase);
        IEnumerable<CombatActorState> candidates;
        if (allFriendsOverride
            || normalized.IndexOf(
                "Friends",
                StringComparison.OrdinalIgnoreCase) >= 0)
        {
            candidates = allLiving.Where(actor =>
                (actor.Kind == CombatSimulationActorKind.Enemy)
                == (self.Kind == CombatSimulationActorKind.Enemy));
        }
        else if (normalized.IndexOf(
                     "Target",
                     StringComparison.OrdinalIgnoreCase) >= 0)
        {
            candidates = allLiving.Where(actor =>
                (actor.Kind == CombatSimulationActorKind.Enemy)
                != (self.Kind == CombatSimulationActorKind.Enemy));
        }
        else
        {
            candidates = allLiving;
        }
        if (excludeSelf)
        {
            candidates = candidates.Where(actor =>
                actor.ActorId != executionSourceActorId);
        }
        var pool = candidates.ToList();
        if (random)
        {
            var selected = new List<int>();
            while (pool.Count > 0 && selected.Count < count)
            {
                var index = context.NextRandomInt(
                    rule.RewardId
                    + ":target:"
                    + selected.Count,
                    pool.Count);
                selected.Add(pool[index].ActorId);
                pool.RemoveAt(index);
            }
            selectedActorIds = selected;
        }
        else
        {
            selectedActorIds = pool.Select(actor => actor.ActorId).ToList();
        }
    }

    public void SetStatusById(string instanceId)
    {
        var actor = context.State.Actors.FirstOrDefault(item =>
            string.Equals(
                item.InstanceKey,
                instanceId,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                item.ActorId.ToString(CultureInfo.InvariantCulture),
                instanceId,
                StringComparison.OrdinalIgnoreCase));
        selectedActorIds = actor == null
            ? new List<int>()
            : new List<int> { actor.ActorId };
    }

    public void SetStatus(IEnumerable<NativeRewardActor> actors)
    {
        selectedActorIds = actors
            .Where(item => item != null)
            .Select(item => item.ActorId)
            .Distinct()
            .ToList();
    }

    public void AddBuff(object id, object amount)
    {
        Apply(
            CombatSimulationEffectKind.AddStatus,
            Text(id),
            Number(amount));
    }

    public NativeRewardBuff AddAndGetBuff(
        NativeRewardActor? actor,
        object id,
        object amount)
    {
        var statusId = Text(id);
        var actorId = actor?.ActorId ?? context.State.PlayerActorId;
        actor?.AddBuff(statusId, amount);
        return actor?.GetBuff(statusId)
               ?? new NativeRewardBuff(this, actorId, statusId);
    }

    public void RemoveBuff(object id)
    {
        Apply(CombatSimulationEffectKind.RemoveStatus, Text(id), 1);
    }

    public void ChangeHp(object amount)
    {
        var value = Number(amount);
        Apply(
            value >= 0
                ? CombatSimulationEffectKind.Heal
                : CombatSimulationEffectKind.DirectHpLoss,
            rule.RewardId,
            Math.Abs(value));
    }

    public void SetHp(object amount)
    {
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor != null)
            {
                ApplyTo(
                    actorId,
                    CombatSimulationEffectKind.SetHp,
                    "Hp",
                    Math.Max(0, Math.Min(actor.MaxHp, Number(amount))));
            }
        }
    }

    public void ChangeDefence(object amount)
    {
        var value = Number(amount);
        foreach (var actorId in Targets())
        {
            if (value >= 0)
            {
                ApplyTo(
                    actorId,
                    CombatSimulationEffectKind.GainBlock,
                    rule.RewardId,
                    value);
            }
            else
            {
                var actor = context.State.FindActor(actorId);
                ApplyTo(
                    actorId,
                    CombatSimulationEffectKind.SetBlock,
                    rule.RewardId,
                    Math.Max(0, (actor?.Block ?? 0) + value));
            }
        }
    }

    public void Damage(object amount)
    {
        Damage(amount, "");
    }

    public void Damage(object amount, object damageType)
    {
        Apply(
            Text(damageType).Equals("True", StringComparison.OrdinalIgnoreCase)
                ? CombatSimulationEffectKind.TrueDamage
                : CombatSimulationEffectKind.Damage,
            rule.RewardId,
            Math.Max(0, Number(amount)));
    }

    public void ChangePower(object amount)
    {
        Apply(
            CombatSimulationEffectKind.GainEnergy,
            rule.RewardId,
            Number(amount));
    }

    public void SetPower(object amount)
    {
        Apply(
            CombatSimulationEffectKind.SetEnergy,
            rule.RewardId,
            Math.Max(0, Number(amount)));
    }

    public void ChangeRound()
    {
        var actor = context.State.FindActor(executionSourceActorId);
        if (actor != null)
        {
            if (actor.Kind == CombatSimulationActorKind.Enemy)
            {
                actor.CurrentIntentIds.Clear();
                actor.CurrentIntentId = "";
            }
            else
            {
                actor.Variables["NativeEndTurnRequested"] = 1d;
            }
        }
    }

    public void ShuffleDeck()
    {
        Shuffle(context.State.DrawPile, "deck");
    }

    public void ShuffleHand()
    {
        foreach (var instanceId in context.State.Hand.ToList())
        {
            context.State.Hand.Remove(instanceId);
            context.State.DrawPile.Add(instanceId);
        }
        Shuffle(context.State.DrawPile, "hand");
    }

    public void ChangeMaxPower(object amount)
    {
        var value = Number(amount);
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor == null) continue;
            actor.BaseEnergy = Math.Max(0, actor.BaseEnergy + value);
            actor.Energy = Math.Max(0, actor.Energy + value);
            actor.Variables["BaseEnergy"] = actor.BaseEnergy;
        }
    }

    public void ChangeMaxHp(object amount)
    {
        var requestedValue = Number(amount);
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor == null) continue;
            var value = requestedValue;
            if (actor.Kind == CombatSimulationActorKind.Player
                && string.Equals(
                    rule.RewardId,
                    "buff_DoomPower",
                    StringComparison.OrdinalIgnoreCase))
            {
                var previousLevel = 0;
                if (context.Scenario.CampaignVariables.TryGetValue(
                        "DoomPower",
                        out var persistedLevel))
                {
                    _ = int.TryParse(persistedLevel, out previousLevel);
                }
                var currentLevel = actor.Statuses
                    .Where(status => string.Equals(
                        status.StatusId,
                        "buff_DoomPower",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(status => Math.Max(0, status.Stacks))
                    .DefaultIfEmpty(0)
                    .Max();
                value = AuraToolsNanaDoomProgression.MaximumHpGainForLevelChange(
                    previousLevel,
                    currentLevel);
            }
            var before = actor.MaxHp;
            actor.MaxHp = Math.Max(1, actor.MaxHp + value);
            var actualDelta = actor.MaxHp - before;
            actor.Hp = actualDelta > 0
                ? Math.Min(actor.MaxHp, Math.Max(0, actor.Hp + actualDelta))
                : Math.Max(0, Math.Min(actor.Hp, actor.MaxHp));
            if (actualDelta != 0
                && actor.Kind == CombatSimulationActorKind.Player
                && context is ICombatPersistentProgressionContext progression)
            {
                progression.RecordPersistentVariableDelta(
                    "MaxHp",
                    actualDelta);
            }
        }
    }

    public void DrawCount(object amount)
    {
        Apply(
            CombatSimulationEffectKind.Draw,
            rule.RewardId,
            Math.Max(0, Number(amount)));
    }

    public void ChangeDynamicVar(object key, object amount)
    {
        var name = Text(key);
        Apply(
            CombatSimulationEffectKind.ModifyVariable,
            name.Equals("RoundCard", StringComparison.OrdinalIgnoreCase)
                ? "DrawPerTurnModifier"
                : name,
            Number(amount));
    }

    public void ChangeDynamicVarPercent(object key, object amount)
    {
        Apply(
            CombatSimulationEffectKind.ModifyVariablePercent,
            Text(key),
            Number(amount));
    }

    public void CreateCard(object id)
    {
        CreateCard(id, "0");
    }

    public void CreateCard(object id, object cost)
    {
        Apply(
            CombatSimulationEffectKind.CreateCard,
            Text(id),
            1,
            effect =>
            {
                effect.DestinationZone = CombatCardZone.Hand;
                effect.Amount = 1;
            });
    }

    public void CreateCard(NativeRewardDataConfig config)
    {
        var before = new HashSet<int>(
            context.State.Cards.Select(item => item.InstanceId));
        CreateCard(config.data.GetValueOrDefault("Id", config.InstanceID));
        var created = context.State.Cards
            .Where(item => !before.Contains(item.InstanceId))
            .OrderByDescending(item => item.InstanceId)
            .FirstOrDefault();
        if (created == null)
        {
            return;
        }
        var target = CardConfig(created.InstanceId);
        target.data = new NativeRewardStringDictionary(config.data);
        foreach (var pair in config.Vars)
        {
            target.Vars[pair.Key] = pair.Value;
        }
        target.InstanceID =
            created.InstanceId.ToString(CultureInfo.InvariantCulture);
        RecordCardProvenance(
            created,
            config,
            "native-script-create",
            0);
        new NativeRewardCardItem(this, created.InstanceId).DataUpdate();
    }

    public void AddCard(object id)
    {
        CreateCard(id);
    }

    public void AddCardById(object id)
    {
        CreateCard(id);
    }

    public void RandomAddCard(object id)
    {
        AddCardToDeckById(id);
        Shuffle(context.State.DrawPile, "random-add-card");
    }

    public void ThrowCard(object count)
    {
        ThrowCard(count, "1");
    }

    public void ThrowCard(object count, object _)
    {
        Apply(
            CombatSimulationEffectKind.DiscardRandom,
            rule.RewardId,
            Math.Max(0, Number(count)));
    }

    public void BurnCard(object count)
    {
        BurnCard(count, "1");
    }

    public void BurnCard(object count, object _)
    {
        Apply(
            CombatSimulationEffectKind.ExhaustRandom,
            rule.RewardId,
            Math.Max(0, Number(count)));
    }

    public void UseCard(NativeRewardDataConfig config)
    {
        var cardId = config.data.GetValueOrDefault(
            "Id",
            config.InstanceID);
        var definition = context.Ruleset.SnapshotCards().FirstOrDefault(item =>
            string.Equals(
                item.CardId,
                cardId,
                StringComparison.OrdinalIgnoreCase));
        if (definition == null)
        {
            return;
        }
        if (AuraToolsNativeGameScriptAudit.UsesNativeScript(definition))
        {
            var nestedInstance = context.State.Cards.FirstOrDefault(item =>
                string.Equals(
                    item.InstanceId.ToString(CultureInfo.InvariantCulture),
                    config.InstanceID,
                    StringComparison.OrdinalIgnoreCase));
            var nested = new NativeRewardScriptGlobals(
                context,
                new CombatScenarioRewardRule
                {
                    RewardId = definition.CardId,
                    Kind = "Card",
                    FightScript = string.Join(
                        "\n",
                        definition.Metadata.GetValueOrDefault(
                            "NativeInitScript",
                            ""),
                        definition.Metadata.GetValueOrDefault(
                            "NativeUseScript",
                            ""))
                },
                executionSourceActorId,
                executionTargetActorId,
                nestedInstance?.InstanceId ?? 0);
            var nestedEvent = new CombatSimulationEvent
            {
                Kind = CombatSimulationEventKind.CardPlayed,
                SourceActorId = executionSourceActorId,
                TargetActorId = executionTargetActorId,
                DefinitionId = definition.CardId
            };
            nested.RunScript(
                new CombatScenarioRewardRule
                {
                    RewardId = definition.CardId,
                    Kind = "Card",
                    FightScript = definition.Metadata.GetValueOrDefault(
                        "NativeInitScript",
                        "")
                },
                nestedEvent);
            nested.RunScript(
                new CombatScenarioRewardRule
                {
                    RewardId = definition.CardId,
                    Kind = "Card",
                    FightScript = definition.Metadata.GetValueOrDefault(
                        "NativeUseScript",
                        "")
                },
                nestedEvent);
            return;
        }
        context.ApplyEffects(
            definition.Effects,
            executionSourceActorId,
            executionTargetActorId,
            currentEvent);
    }

    public void UseCard(NativeRewardCardItem card)
    {
        UseCard(card.dataConfig);
    }

    public void UseAndBurnDrawPileSnapshot()
    {
        var snapshot = DeckCard.ToList();
        foreach (var card in snapshot.AsEnumerable().Reverse())
        {
            var instanceId = NativeRewardExtensions.ToInt(
                card.dataConfig.InstanceID);
            if (!context.State.DrawPile.Contains(instanceId))
            {
                continue;
            }

            // Taking the card out before executing its native script prevents
            // a copied Supernova from recursively seeing itself in DeckCard.
            context.State.DrawPile.RemoveAll(item => item == instanceId);
            UseCard(card);
            if (context.State.FindCard(instanceId) != null)
            {
                BurnCardByData(card);
            }
        }

        if (snapshot.Count > 0)
        {
            ChangePower(snapshot.Count.ToString(CultureInfo.InvariantCulture));
        }
    }

    public bool ComboCheck()
    {
        var actor = context.State.FindActor(executionSourceActorId);
        if (actor == null || !actor.Alive)
        {
            return false;
        }
        if (actor.Statuses.All(item => !string.Equals(
                item.StatusId,
                "buff_revelation",
                StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus("Self");
            AddBuff("buff_revelation", "1");
            context.ApplyEffects(
                new[]
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.RetrieveCards,
                        Target = CombatSimulationTarget.Self,
                        Amount = 1,
                        RequiredCardTag = "Combo",
                        SourceZone = CombatCardZone.DrawPile,
                        DestinationZone = CombatCardZone.Hand
                    }
                },
                executionSourceActorId,
                executionTargetActorId,
                currentEvent);
        }
        actor = context.State.FindActor(executionSourceActorId);
        if (actor == null
            || !actor.Alive
            || actor.Statuses.All(item => !string.Equals(
                item.StatusId,
                "buff_revelation",
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return actor.Variables.GetValueOrDefault(
                   "NativeHasPreviousCard",
                   0d) <= 0d
               || actor.Variables.GetValueOrDefault(
                   "NativePreviousCardCombo",
                   0d) > 0d;
    }

    public void DesEnemyAllAction()
    {
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor?.Kind != CombatSimulationActorKind.Enemy)
            {
                continue;
            }
            actor.CurrentIntentIds.Clear();
            actor.CurrentIntentId = "";
        }
    }

    public void DesEnemyAction()
    {
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor?.Kind != CombatSimulationActorKind.Enemy)
            {
                continue;
            }
            actor.CurrentIntentIds.Clear();
            actor.CurrentIntentId = "";
        }
    }

    public void DoAction(object index)
    {
        var actor = context.State.FindActor(executionSourceActorId);
        if (actor?.Kind != CombatSimulationActorKind.Enemy
            || !context.Ruleset.TryGetEnemy(
                actor.DefinitionId,
                out var definition))
        {
            return;
        }
        var intents = actor.CurrentIntentIds.Count > 0
            ? actor.CurrentIntentIds
            : definition.Intents.Select(item => item.IntentId).ToList();
        var selectedIndex = Math.Max(
            0,
            Math.Min(intents.Count - 1, Number(index)));
        if (intents.Count == 0)
        {
            return;
        }
        var intent = definition.Intents.FirstOrDefault(item => string.Equals(
            item.IntentId,
            intents[selectedIndex],
            StringComparison.OrdinalIgnoreCase));
        if (intent != null)
        {
            context.ApplyEffects(
                intent.Effects,
                executionSourceActorId,
                context.State.PlayerActorId,
                currentEvent);
        }
    }

    public void DiceCheck(int percent, Action<bool> action)
    {
        action(CheckDice.Roll().Value >= percent);
    }

    public void RemoveBadBuff(object count, object good)
    {
        var removePositive = Text(good).Equals(
            "true",
            StringComparison.OrdinalIgnoreCase);
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor == null)
            {
                continue;
            }
            var candidates = actor.Statuses
                .Where(status =>
                    context.Ruleset.TryGetStatus(
                        status.StatusId,
                        out var definition)
                    && definition.Tags.Contains(
                        removePositive ? "Positive" : "Negative",
                        StringComparer.OrdinalIgnoreCase))
                .OrderBy(status => status.StatusId, StringComparer.Ordinal)
                .Take(Math.Max(0, Number(count)))
                .Select(status => status.StatusId)
                .ToList();
            foreach (var statusId in candidates)
            {
                ApplyTo(
                    actorId,
                    CombatSimulationEffectKind.RemoveStatus,
                    statusId,
                    1);
            }
        }
    }

    public void RemoveBadBuff(object count)
    {
        RemoveBadBuff(count, false);
    }

    public void RemoveAllBadBuff()
    {
        RemoveBadBuff(int.MaxValue, false);
    }

    public void RemoveAllBadBuff(object _)
    {
        RemoveAllBadBuff();
    }

    public void SetDamageFilter(object _, object percent)
    {
        var filterId = Text(_);
        var reduction = Math.Max(0d, Math.Min(100d, Number(percent)));
        foreach (var actorId in Targets())
        {
            var actor = Actor(actorId);
            if (actor != null)
            {
                actor.DamageFilter[filterId] = Math.Max(
                    actor.DamageFilter.GetValueOrDefault(filterId),
                    reduction);
            }
        }
    }

    public void ChangeVars(object key, object amount)
    {
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor != null)
            {
                var name = Text(key);
                actor.Variables[name] =
                    actor.Variables.GetValueOrDefault(name, 0d)
                    + Number(amount);
            }
        }
    }

    public NativeRewardActor? GetEnemy(NativeRewardActor? actor)
    {
        return actor != null
               && context.State.FindActor(actor.ActorId)?.Kind
               == CombatSimulationActorKind.Enemy
            ? actor
            : null;
    }

    public void ChangeCareer(object definitionId)
    {
        var roleId = Text(definitionId);
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor != null)
            {
                if (actor.ActorId == context.State.PlayerActorId)
                {
                    ApplyRoleRuntimeForm(
                        actor,
                        roleId);
                }
                actor.DefinitionId = roleId;
            }
        }
    }

    private void ApplyRoleRuntimeForm(
        CombatActorState actor,
        string roleId)
    {
        var player = context.Scenario.Player;
        var form = player.RoleRuntimeForms.FirstOrDefault(item => string.Equals(
            item.RoleId,
            roleId,
            StringComparison.OrdinalIgnoreCase));
        if (form == null)
        {
            return;
        }
        player.RoleId = form.RoleId;
        player.SkillCardIds = form.SkillCardIds.ToList();
        player.SkillCooldownTurns = new Dictionary<string, int>(
            form.SkillCooldownTurns,
            StringComparer.OrdinalIgnoreCase);
    }

    public void ChangeSummon(bool summon)
    {
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor != null)
            {
                actor.Variables["NativeIsSummon"] = summon ? 1d : 0d;
            }
        }
    }

    public void AddEnemyAction(NativeRewardDataConfig action)
    {
        foreach (var actorId in Targets())
        {
            var actor = context.State.FindActor(actorId);
            if (actor?.Kind != CombatSimulationActorKind.Enemy)
            {
                continue;
            }
            var id = action.data.GetValueOrDefault("Id", action.InstanceID);
            actor.CurrentIntentIds.Add(id);
            actor.CurrentIntentId = actor.CurrentIntentIds[0];
        }
    }

    public void AddFakeCard(bool toUsed)
    {
        const string fakeCardId = "cursecard_15";
        if (!context.Ruleset.TryGetCard(fakeCardId, out _))
        {
            context.AddUnsupported("card:" + fakeCardId);
            return;
        }
        var candidates = (context.Scenario.Player?.Deck ?? new List<string>())
            .Select(cardId => context.Ruleset.TryGetCard(cardId, out var card)
                ? card
                : null)
            .Where(card => card != null)
            .Select(card => card!)
            .ToList();
        if (candidates.Count == 0)
        {
            context.AddUnsupported(
                "fake-card-disguise-pool:" + rule.RewardId);
            return;
        }
        var apparent = candidates[context.NextRandomInt(
            rule.RewardId + ":fake-card-disguise",
            candidates.Count)];
        var before = context.State.NextCardInstanceId;
        Apply(
            CombatSimulationEffectKind.CreateCard,
            fakeCardId,
            1,
            effect =>
            {
                effect.DestinationZone = toUsed
                    ? CombatCardZone.DiscardPile
                    : CombatCardZone.DrawPile;
                effect.RandomizeDestination = !toUsed;
            });
        var created = context.State.Cards
            .Where(card => card.InstanceId >= before)
            .OrderByDescending(card => card.InstanceId)
            .FirstOrDefault();
        if (created == null)
        {
            return;
        }
        created.ApparentCardId = apparent.CardId;
        created.Tags = apparent.Tags
            .Concat(new[] { "VisibleFake" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        created.EnchantmentIds = new List<string> { "enchtag_16" };
        created.Variables["IsFake"] = "True";
        created.Variables["DisguiseSourceCardId"] = apparent.CardId;
    }

    public NativeRewardDataConfig? CardGetEnch(
        NativeRewardDataConfig card)
    {
        if (card == null
            || !int.TryParse(
                card.InstanceID,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var instanceId))
        {
            return null;
        }
        var enchantmentId = context.State.FindCard(instanceId)
            ?.EnchantmentIds
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(enchantmentId)
            ? null
            : new NativeRewardDataConfig(
                enchantmentId!,
                NativeRewardDataType.EnchTag);
    }

    public void AddTempEvent(string eventName, Action action)
    {
        AddEvent(eventName, action);
    }

    public void RepeatByBuffLevel(object statusId, Action action)
    {
        var stacks = Self?.GetBuff(statusId)?.buffConfig.Level ?? 0;
        for (var index = 0; index < Math.Max(0, stacks); index++)
        {
            action();
        }
    }

    public void ClearAllDharmasSpellList()
    {
        IgnoreCosmeticApi("ClearAllDharmasSpellList");
    }

    public void UpdateAllDharmasSpellList()
    {
        IgnoreCosmeticApi("UpdateAllDharmasSpellList");
    }

    public void CopyCardWare(
        object count,
        List<NativeRewardDataConfig> cards,
        Action<List<NativeRewardDataConfig>>? action,
        object? _ = null)
    {
        action?.Invoke(SelectSkillDataConfigs(cards, Math.Max(0, Number(count)))
            .Select(item => item.Clone())
            .ToList());
    }

    public void PackToDeckAction(
        object count,
        List<NativeRewardDataConfig> cards,
        Action<List<NativeRewardDataConfig>>? action)
    {
        action?.Invoke(SelectSkillDataConfigs(
            cards,
            Math.Max(0, Number(count))));
    }

    public void PackToDeckAction(
        object count,
        List<NativeRewardDataConfig> cards,
        Action<List<NativeRewardDataConfig>>? action,
        object? _)
    {
        PackToDeckAction(count, cards, action);
    }

    public void GetCardFromDeck(NativeRewardDataConfig card)
    {
        if (!int.TryParse(
                card.InstanceID,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var instanceId)
            || !context.State.DrawPile.Contains(instanceId)
            || context.State.Hand.Count >= context.Scenario.HandLimit)
        {
            return;
        }
        context.State.DrawPile.Remove(instanceId);
        context.State.Hand.Add(instanceId);
    }

    public void UpdateSkillTime()
    {
    }

    public string GetDesValue(object index)
    {
        return Vars.GetValueOrDefault("DesVal" + Text(index), "0");
    }

    public void Log(object value)
    {
        NativeRewardDebug.Log(value);
    }

    public int atk()
    {
        return (int)Math.Round(
            context.State.FindActor(executionSourceActorId)?.Variables
                .GetValueOrDefault("BaseAttack", 0d) ?? 0d);
    }

    public int def()
    {
        return (int)Math.Round(
            context.State.FindActor(executionSourceActorId)?.Variables
                .GetValueOrDefault("BaseDefend", 0d) ?? 0d);
    }

    public void AddCardToDeckById(object id)
    {
        AddCardToDeckById(id, true);
    }

    public void AddCardToDeckById(object id, bool _)
    {
        Apply(
            CombatSimulationEffectKind.CreateCard,
            Text(id),
            1,
            effect => effect.DestinationZone = CombatCardZone.DiscardPile);
    }

    public void AddCardToFightManager(object id)
    {
        AddCardToDeckById(id);
    }

    public void RandomAddGoodBuff(object count)
    {
        RandomAddGoodBuff(count, "1");
    }

    public void RandomAddGoodBuff(object count, object type)
    {
        AddRandomStatus(
            count,
            Text(type) == "1"
                ? NativeRandomStatusPool.Positive
                : NativeRandomStatusPool.Negative);
    }

    public void RandomAddBuff(object count)
    {
        AddRandomStatus(count, NativeRandomStatusPool.Ordinary);
    }

    public void RandomAddBuffAndAbility(object count)
    {
        AddRandomStatus(count, NativeRandomStatusPool.OrdinaryAndAbility);
    }

    public void RandomAddBless(object count)
    {
        PlayerInfo.RandomAddBless(Number(count));
    }

    public void RandomAddRelic(object count)
    {
        PlayerInfo.RandomAddRelic(Number(count));
    }

    public void Resentment(object amount)
    {
        AddBuff("buff_resentment", amount);
    }

    public void Resurrection(object amount)
    {
        SetHp(Math.Max(1, Number(amount)));
        DispatchNamed("Resurrection", currentEvent);
        DispatchNamed("ResurrectionEnd", currentEvent);
    }

    public void EscapeFight()
    {
        context.Terminate(
            CombatSimulationOutcome.Victory,
            CombatTerminationReason.Victory);
    }

    public void UpdateRelicShow()
    {
        IgnoreCosmeticApi("UpdateRelicShow");
    }

    public void RunImmediately(object definitionId, object eventName)
    {
        DispatchNamed(Text(eventName), currentEvent);
    }

    public void ChooseCardToAction(object count, Action<List<NativeRewardCardItem>> action)
    {
        ChooseCardToAction(count, action, "");
    }

    public void ChooseCardToAction(
        object count,
        Action<List<NativeRewardCardItem>> action,
        object _)
    {
        var selected = SelectSkillDataConfigs(
                HandCard.Select(item => item.dataConfig).ToList(),
                Math.Max(0, Number(count)))
            .Select(item => HandCard.First(card =>
                string.Equals(
                    card.dataConfig.InstanceID,
                    item.InstanceID,
                    StringComparison.OrdinalIgnoreCase)))
            .ToList();
        action(selected);
    }

    private List<NativeRewardDataConfig> SelectSkillDataConfigs(
        IEnumerable<NativeRewardDataConfig> source,
        int count)
    {
        var cards = (source ?? Enumerable.Empty<NativeRewardDataConfig>())
            .Where(item => item != null)
            .ToList();
        var skillId = dataConfig.data.GetValueOrDefault(
            "Id",
            dataConfig.InstanceID);
        if (count <= 0 || cards.Count == 0)
        {
            return new List<NativeRewardDataConfig>();
        }
        if (!string.Equals(skillId, "careercard_1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skillId, "careercard_9", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skillId, "careercard_12", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skillId, "careercard_13", StringComparison.OrdinalIgnoreCase))
        {
            return cards.Take(count).ToList();
        }

        return cards
            .OrderByDescending(card => NativeSkillChoiceScore(skillId, card))
            .ThenBy(card => card.data.GetValueOrDefault("Id", card.InstanceID), StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }

    private double NativeSkillChoiceScore(
        string skillId,
        NativeRewardDataConfig card)
    {
        var cardId = card.data.GetValueOrDefault("Id", card.InstanceID);
        context.Ruleset.TryGetCard(cardId, out var definition);
        var cost = Number(card.Vars.GetValueOrDefault(
            "Expend",
            card.data.GetValueOrDefault("Expend", definition?.Cost.ToString() ?? "0")));
        var rarity = Math.Max(1, Number(card.data.GetValueOrDefault(
            "Rarity",
            definition?.Rarity.ToString() ?? "1")));
        var extraCost = Number(card.Vars.GetValueOrDefault("TotalExCost", "0"));
        var extraUses = Number(card.Vars.GetValueOrDefault("ExUseCount", "0"));
        var baseValue = Math.Max(0d,
            rarity * 1.5d
            + Math.Max(0d, 3d - cost) * 0.5d
            + (definition?.Effects.Sum(effect =>
                Math.Max(0, effect.Amount)
                * Math.Max(0d, Math.Min(1d, effect.Probability))) ?? 0d) * 0.2d);
        if (string.Equals(skillId, "careercard_9", StringComparison.OrdinalIgnoreCase))
        {
            var blessingValue = (rarity >= 3 ? 4d : 2d) + Math.Max(0d, cost) * 0.75d;
            return blessingValue - baseValue;
        }
        if (string.Equals(skillId, "careercard_13", StringComparison.OrdinalIgnoreCase))
        {
            var remainingBattles = context.Scenario.Player.Variables.TryGetValue(
                CombatCampaignPublicContextKeys.RemainingBattles,
                out var remaining)
                ? Math.Max(0d, remaining)
                : 0d;
            return baseValue
                   * (1d + Math.Min(10d, remainingBattles) * 0.18d)
                   / (1d + Math.Max(0d, cost + extraCost) * 0.2d)
                   / (1d + Math.Max(0d, extraUses) * 0.35d);
        }
        return baseValue;
    }

    public void OutFightSelectCardToAction(
        object count,
        Action<List<NativeRewardDataConfig>> action)
    {
        action(PlayerInfo.CardList
            .Take(Math.Max(0, Number(count)))
            .ToList());
    }

    public void OutFightSelectCardToAction(
        object count,
        List<NativeRewardDataConfig> source,
        Action<List<NativeRewardDataConfig>> action)
    {
        action(source.Take(Math.Max(0, Number(count))).ToList());
    }

    public List<Dictionary<string, string>> GetcardsByRarity(
        object minimum,
        object maximum)
    {
        var min = Number(minimum);
        var max = Number(maximum);
        return context.Ruleset.SnapshotCards()
            .Where(card => card.Rarity >= min
                           && card.Rarity <= max
                           && CanEnterDynamicCardPool(card.CardId))
            .Select(card =>
            {
                var data = new Dictionary<string, string>(
                    card.Metadata,
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = card.CardId,
                    ["Name"] = card.DisplayName,
                    ["Tag"] = string.Join(",", card.Tags),
                    ["Expend"] = card.Cost.ToString(
                        CultureInfo.InvariantCulture),
                    ["Rarity"] = card.Rarity.ToString(
                        CultureInfo.InvariantCulture),
                    ["CreationSource"] = "dynamic-card-pool",
                    ["CreationSourceId"] = rule.RewardId,
                    ["CrossRoleSkillAuthorized"] =
                        AllowsCrossRoleSkill()
                            ? "true"
                            : "false"
                };
                return data;
            })
            .ToList();
    }

    public List<NativeRewardDataConfig> GetcardsOutLock()
    {
        return context.Ruleset.SnapshotCards()
            .Where(item => CanEnterDynamicCardPool(item.CardId))
            .OrderBy(item => item.CardId, StringComparer.Ordinal)
            .Select(item =>
            {
                var config = new NativeRewardDataConfig(item.CardId);
                foreach (var pair in item.Metadata)
                {
                    config.data[pair.Key] = pair.Value;
                    config.Vars[pair.Key] = pair.Value;
                }
                config.data["Id"] = item.CardId;
                config.data["Name"] = item.DisplayName;
                config.data["Tag"] = string.Join(",", item.Tags);
                config.data["Rarity"] = item.Rarity.ToString(
                    CultureInfo.InvariantCulture);
                config.data["Expend"] = item.Cost.ToString(
                    CultureInfo.InvariantCulture);
                config.Vars["CreationSource"] =
                    "dynamic-card-pool";
                config.Vars["CreationSourceId"] = rule.RewardId;
                config.Vars["CrossRoleSkillAuthorized"] =
                    AllowsCrossRoleSkill()
                        ? "true"
                        : "false";
                return config;
            })
            .ToList();
    }

    private bool CanEnterDynamicCardPool(string cardId)
    {
        var entry = context.Scenario.RewardCatalog.FirstOrDefault(item =>
            item.Kind.Equals(
                "Card",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                item.RewardId,
                cardId,
                StringComparison.OrdinalIgnoreCase));
        return CombatCampaignCardAcquisitionPolicy
            .CanEnterDynamicGenerationPool(
                entry,
                context.Scenario.Player.SkillCardIds,
                context.Scenario.EnabledRewardCardPackIds,
                AllowsCrossRoleSkill());
    }

    private bool AllowsCrossRoleSkill()
    {
        var value = Vars.GetValueOrDefault(
            "AllowCrossRoleSkill",
            "false");
        return string.Equals(
                   value,
                   "true",
                   StringComparison.OrdinalIgnoreCase)
               || string.Equals(
                   value,
                   "1",
                   StringComparison.Ordinal);
    }

    public List<NativeRewardDataConfig> UniqueDeck()
    {
        return PlayerInfo.CardList
            .GroupBy(item => item.data.GetValueOrDefault("Id", ""))
            .Select(group => group.First())
            .ToList();
    }

    public bool CardTopCheck()
    {
        return context.State.Hand.Count >= context.Scenario.HandLimit;
    }

    public void ChangeCardTop(object amount)
    {
        context.Scenario.HandLimit = Math.Max(
            1,
            context.Scenario.HandLimit + Number(amount));
    }

    public int returnFightType()
    {
        var player = context.State.Player;
        return player?.Variables.TryGetValue("EncounterKind", out var value) == true
            ? (int)value
            : 0;
    }

    public void RemoveRelic(object id)
    {
        context.Scenario.RewardRules.RemoveAll(item =>
            string.Equals(item.RewardId, Text(id), StringComparison.OrdinalIgnoreCase));
    }

    public void RemoveCard(object instanceId)
    {
        var raw = Convert.ToString(
            instanceId,
            CultureInfo.InvariantCulture) ?? "";
        var cardId = int.TryParse(
                         raw,
                         NumberStyles.Integer,
                         CultureInfo.InvariantCulture,
                         out var runtimeId)
            ? context.State.FindCard(runtimeId)?.CardId ?? raw
            : raw;
        context.RecordRewardMutation("Remove", "Card", cardId);
    }

    public void ShowItemShowUI(params object[] _)
    {
    }

    public void RemoveBless(object id)
    {
        RemoveRelic(id);
    }

    public void ChangeMoney(object amount)
    {
        PlayerInfo.Money += Number(amount);
    }

    public void ChangeMoney(object amount, object _)
    {
        ChangeMoney(amount);
    }

    public bool TagCheck(NativeRewardDataConfig config, object tag)
    {
        return config.data.GetValueOrDefault("Tag", "")
            .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Contains(Text(tag), StringComparer.OrdinalIgnoreCase);
    }

    public void BurnCardByData(NativeRewardCardItem card)
    {
        card.Burning(1f);
    }

    public void BurnCardByData(NativeRewardDataConfig card)
    {
        var instance = context.State.Cards.FirstOrDefault(item =>
            string.Equals(
                item.InstanceId.ToString(CultureInfo.InvariantCulture),
                card.InstanceID,
                StringComparison.OrdinalIgnoreCase));
        if (instance != null)
        {
            new NativeRewardCardItem(this, instance.InstanceId).Burning(1f);
        }
    }

    public void FightRelicCheck(
        Action<List<NativeRewardDataConfig>, string> action)
    {
        foreach (var actor in context.State.Actors
                     .Where(item => item.Alive)
                     .OrderBy(item => item.ActorId)
                     .ToList())
        {
            action(PlayerInfo.RelicList, actor.InstanceKey);
        }
    }

    public void AddBless(object id)
    {
        PlayerInfo.AddBless(Text(id));
    }

    public int DamageCalculate(int amount)
    {
        var actor = context.State.Player;
        var multiplier = actor?.Variables.GetValueOrDefault("PercentDamage", 1d) ?? 1d;
        return Math.Max(0, (int)Math.Round(amount * multiplier));
    }

    public void Dispatch(CombatSimulationEvent sourceEvent)
    {
        var previousEvent = currentEvent;
        try
        {
            SynchronizeNativeSkillTimesFromState();
            currentEvent = sourceEvent;
            foreach (var name in EventNames(sourceEvent))
            {
                DispatchNamed(name, sourceEvent);
            }
        }
        finally
        {
            SynchronizeStateSkillCooldownsFromNative();
            currentEvent = previousEvent;
        }
    }

    internal void DispatchNamedEvent(
        string eventName,
        CombatSimulationEvent? sourceEvent)
    {
        try
        {
            SynchronizeNativeSkillTimesFromState();
            DispatchNamed(eventName, sourceEvent);
        }
        finally
        {
            SynchronizeStateSkillCooldownsFromNative();
        }
    }

    private void SynchronizeNativeSkillTimesFromState()
    {
        foreach (var instanceId in context.State.SkillCards)
        {
            var card = context.State.FindCard(instanceId);
            if (card == null)
            {
                continue;
            }
            context.State.SkillUseCounts[card.CardId] =
                context.State.SkillCooldowns.TryGetValue(
                    instanceId,
                    out var cooldown)
                    ? cooldown
                    : 0;
        }
    }

    private void SynchronizeStateSkillCooldownsFromNative()
    {
        foreach (var instanceId in context.State.SkillCards)
        {
            var card = context.State.FindCard(instanceId);
            if (card != null
                && context.State.SkillUseCounts.TryGetValue(
                    card.CardId,
                    out var cooldown))
            {
                context.State.SkillCooldowns[instanceId] = Math.Max(0, cooldown);
            }
        }
    }

    internal void PrepareDiceCheck()
    {
        DispatchNamed("OnDiceCheck", currentEvent);
    }

    internal int DispatchScriptExecute(
        CombatSimulationEvent sourceEvent,
        int amount)
    {
        var executor = new NativeRewardScriptExecutor
        {
            Self = Actor(sourceEvent.SourceActorId),
            dataConfig = new NativeRewardDataConfig(
                sourceEvent.DefinitionId)
        };
        var target = Actor(sourceEvent.TargetActorId);
        if (target != null)
        {
            executor.Object.Add(target);
        }
        var payload = new NativeRewardScriptExecuteData
        {
            data = executor.dataConfig,
            Id = Actor(sourceEvent.SourceActorId)?.InstanceId ?? "",
            Executor = executor,
            Arguments = new object?[]
            {
                amount.ToString(CultureInfo.InvariantCulture)
            },
            MethodName = "Damage"
        };
        DispatchNamedPayload("ScriptExecute", sourceEvent, payload);
        return payload.Arguments.Length > 0
            ? NativeRewardExtensions.ToInt(Convert.ToString(
                payload.Arguments[0],
                CultureInfo.InvariantCulture))
            : amount;
    }

    public void Complete()
    {
        rule.Variables = new Dictionary<string, string>(
            Vars,
            StringComparer.OrdinalIgnoreCase);
    }

    internal NativeRewardActor? Actor(int actorId)
    {
        return context.State.FindActor(actorId) == null
            ? null
            : new NativeRewardActor(this, actorId);
    }

    internal void AddBuffToActor(int actorId, object id, object amount)
    {
        ApplyTo(
            actorId,
            CombatSimulationEffectKind.AddStatus,
            Text(id),
            Number(amount));
    }

    internal void RemoveBuffFromActor(int actorId, string statusId)
    {
        ApplyTo(
            actorId,
            CombatSimulationEffectKind.RemoveStatus,
            statusId,
            1);
    }

    internal NativeRewardDeferredEffectCollection
        DeferredEffects(int actorId, string statusId)
    {
        var key = actorId + "|" + statusId;
        if (!deferredEffects.TryGetValue(key, out var effects))
        {
            effects = new List<(
                NativeRewardDataConfig dataConfig,
                Action action)>();
            deferredEffects[key] = effects;
        }
        return new NativeRewardDeferredEffectCollection(
            effects,
            context.State.DeferredEffects,
            actorId,
            statusId);
    }

    internal NativeRewardDataConfig CardConfig(int instanceId)
    {
        if (cardConfigurations.TryGetValue(instanceId, out var existing))
        {
            return existing;
        }
        var instance = context.State.FindCard(instanceId);
        var definition = instance == null
            ? null
            : context.Ruleset.SnapshotCards().FirstOrDefault(card =>
                string.Equals(
                    card.CardId,
                    string.IsNullOrWhiteSpace(instance.ApparentCardId)
                        ? instance.CardId
                        : instance.ApparentCardId,
                    StringComparison.OrdinalIgnoreCase));
        var result = new NativeRewardDataConfig(instance?.CardId ?? "")
        {
            InstanceID = instanceId.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var pair in definition?.Metadata
                             ?? new Dictionary<string, string>())
        {
            result.data[pair.Key] = pair.Value;
            result.Vars[pair.Key] = pair.Value;
        }
        result.data["Id"] = instance?.CardId ?? "";
        result.data["Name"] = definition?.DisplayName ?? "";
        result.data["Tag"] = string.Join(
            ",",
            (definition?.Tags ?? new List<string>())
            .Concat(instance?.Tags ?? new List<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase));
        result.data["Rarity"] = (definition?.Rarity ?? 1)
            .ToString(CultureInfo.InvariantCulture);
        result.data["Expend"] = (definition?.Cost ?? 0)
            .ToString(CultureInfo.InvariantCulture);
        result.Vars["ExCost"] = (instance?.CostModifier ?? 0)
            .ToString(CultureInfo.InvariantCulture);
        foreach (var pair in instance?.Variables
                             ?? new Dictionary<string, string>())
        {
            result.Vars[pair.Key] = pair.Value;
        }
        cardConfigurations[instanceId] = result;
        return result;
    }

    internal void RecordCardProvenance(
        CombatCardInstanceState instance,
        NativeRewardDataConfig config,
        string fallbackSource,
        int parentInstanceId)
    {
        var source = config.Vars.GetValueOrDefault(
            "CreationSource",
            config.data.GetValueOrDefault(
                "CreationSource",
                fallbackSource));
        var sourceId = config.Vars.GetValueOrDefault(
            "CreationSourceId",
            config.data.GetValueOrDefault(
                "CreationSourceId",
                rule.RewardId));
        var randomStream = config.Vars.GetValueOrDefault(
            "CreationRandomStreamId",
            config.data.GetValueOrDefault(
                "CreationRandomStreamId",
                ""));
        instance.CreationSource = string.IsNullOrWhiteSpace(source)
            ? fallbackSource
            : source;
        instance.CreationSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? rule.RewardId
            : sourceId;
        instance.CreationParentInstanceId = Math.Max(
            0,
            parentInstanceId);
        instance.CreationRandomStreamId = randomStream;
        instance.CreationCrossRoleSkillAuthorized =
            string.Equals(
                config.Vars.GetValueOrDefault(
                    "CrossRoleSkillAuthorized",
                    config.data.GetValueOrDefault(
                        "CrossRoleSkillAuthorized",
                        "false")),
                "true",
                StringComparison.OrdinalIgnoreCase);
        instance.Variables["CreationSource"] =
            instance.CreationSource;
        instance.Variables["CreationSourceId"] =
            instance.CreationSourceId;
        instance.Variables["CreationParentInstanceId"] =
            instance.CreationParentInstanceId.ToString(
                CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(randomStream))
        {
            instance.Variables["CreationRandomStreamId"] =
                randomStream;
        }
        instance.Variables["CrossRoleSkillAuthorized"] =
            instance.CreationCrossRoleSkillAuthorized
                ? "true"
                : "false";
    }

    internal void SynchronizeCardVariables()
    {
        if (!int.TryParse(
                dataConfig.InstanceID,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var instanceId))
        {
            return;
        }
        var instance = context.State.FindCard(instanceId);
        if (instance == null)
        {
            return;
        }
        foreach (var pair in Vars)
        {
            instance.Variables[pair.Key] = pair.Value;
        }
    }

    internal NativeRewardDataConfig StatusConfig(
        int actorId,
        string statusId)
    {
        var key = StatusConfigurationKey(actorId, statusId);
        if (statusConfigurations.TryGetValue(key, out var existing))
        {
            return existing;
        }
        var created = new NativeRewardDataConfig(statusId);
        created.Vars["ThisCount"] = "0";
        statusConfigurations[key] = created;
        return created;
    }

    private static string StatusConfigurationKey(
        int actorId,
        string statusId)
    {
        return actorId + "|" + (statusId ?? "").ToLowerInvariant();
    }

    internal ICombatSimulationRuntimeContext Context => context;

    private void Register(string eventName, Action<CombatSimulationEvent?> callback)
    {
        if (!handlers.TryGetValue(eventName, out var list))
        {
            list = new List<NativeRewardEventHandler>();
            handlers[eventName] = list;
        }
        list.Add(new NativeRewardEventHandler
        {
            HandlerId = NextHandlerId(eventName),
            SourceRewardId = rule.RewardId,
            ActorIds = new HashSet<int>(Targets()),
            Callback = callback
        });
    }

    private void Register<T>(string eventName, Action<T> callback)
    {
        if (!handlers.TryGetValue(eventName, out var list))
        {
            list = new List<NativeRewardEventHandler>();
            handlers[eventName] = list;
        }
        list.Add(new NativeRewardEventHandler
        {
            HandlerId = NextHandlerId(eventName),
            SourceRewardId = rule.RewardId,
            ActorIds = new HashSet<int>(Targets()),
            PayloadType = typeof(T),
            PayloadCallback = payload => callback((T)payload!)
        });
    }

    private void DispatchNamed(
        string eventName,
        CombatSimulationEvent? sourceEvent)
    {
        if (!handlers.TryGetValue(eventName, out var list))
        {
            return;
        }
        foreach (var handler in list.ToList())
        {
            if (!MatchesSubscription(
                    eventName,
                    handler.ActorIds,
                    sourceEvent))
            {
                continue;
            }
            if (!TryEnterHandler(
                    handler,
                    sourceEvent,
                    out var handlerEvent,
                    out var chainKey))
            {
                continue;
            }
            try
            {
                var previousEvent = currentEvent;
                currentEvent = handlerEvent;
                try
                {
                    if (handler.PayloadType != null
                        && handler.PayloadCallback != null)
                    {
                        handler.PayloadCallback(CreatePayload(
                            handler.PayloadType,
                            handlerEvent,
                            sourceEvent?.SourceRewardId));
                    }
                    else
                    {
                        handler.Callback(handlerEvent);
                    }
                }
                finally
                {
                    currentEvent = previousEvent;
                }
            }
            finally
            {
                ExitHandler(chainKey);
            }
        }
    }

    private void DispatchNamedPayload(
        string eventName,
        CombatSimulationEvent? sourceEvent,
        object payload)
    {
        if (!handlers.TryGetValue(eventName, out var list))
        {
            return;
        }
        foreach (var handler in list.ToList())
        {
            if (!MatchesSubscription(
                    eventName,
                    handler.ActorIds,
                    sourceEvent))
            {
                continue;
            }
            if (!TryEnterHandler(
                    handler,
                    sourceEvent,
                    out var handlerEvent,
                    out var chainKey))
            {
                continue;
            }
            try
            {
                var previousEvent = currentEvent;
                currentEvent = handlerEvent;
                try
                {
                    if (handler.PayloadType?.IsInstanceOfType(payload) == true
                        && handler.PayloadCallback != null)
                    {
                        handler.PayloadCallback(payload);
                    }
                    else if (handler.PayloadType == null)
                    {
                        handler.Callback(handlerEvent);
                    }
                }
                finally
                {
                    currentEvent = previousEvent;
                }
            }
            finally
            {
                ExitHandler(chainKey);
            }
        }
    }

    private string NextHandlerId(string eventName)
    {
        nextHandlerOrdinal++;
        return rule.RewardId
               + ":"
               + eventName
               + ":"
               + nextHandlerOrdinal.ToString(CultureInfo.InvariantCulture);
    }

    private bool TryEnterHandler(
        NativeRewardEventHandler handler,
        CombatSimulationEvent? sourceEvent,
        out CombatSimulationEvent? handlerEvent,
        out string chainKey)
    {
        handlerEvent = null;
        var chainId = sourceEvent?.CausalChainId > 0
            ? sourceEvent.CausalChainId
            : sourceEvent?.Sequence > 0
                ? sourceEvent.Sequence
                : context.State.EventSequence + 1;
        chainKey = chainId.ToString(CultureInfo.InvariantCulture)
                   + "|"
                   + handler.HandlerId;
        if (!activeHandlerIds.Add(handler.HandlerId))
        {
            return false;
        }
        if (!activeHandlerChains.Add(chainKey))
        {
            activeHandlerIds.Remove(handler.HandlerId);
            return false;
        }
        if (sourceEvent != null)
        {
            handlerEvent = sourceEvent.Clone();
            handlerEvent.CausalChainId = chainId;
            handlerEvent.HandlerId = handler.HandlerId;
            handlerEvent.SourceRewardId = handler.SourceRewardId;
            handlerEvent.SourceActionId = sourceEvent.SourceActionId > 0
                ? sourceEvent.SourceActionId
                : context.State.ActionSequence;
        }
        return true;
    }

    private void ExitHandler(string chainKey)
    {
        activeHandlerChains.Remove(chainKey);
        var separator = chainKey.IndexOf('|');
        if (separator >= 0 && separator + 1 < chainKey.Length)
        {
            activeHandlerIds.Remove(chainKey.Substring(separator + 1));
        }
    }

    private static bool MatchesSubscription(
        string eventName,
        HashSet<int> actorIds,
        CombatSimulationEvent? sourceEvent)
    {
        if (actorIds.Count == 0 || sourceEvent == null)
        {
            return true;
        }
        if (sourceEvent.Kind is CombatSimulationEventKind.BattleStarted
            or CombatSimulationEventKind.TurnStarted
            or CombatSimulationEventKind.TurnEnded
            or CombatSimulationEventKind.BattleEnded)
        {
            return true;
        }
        if (string.Equals(
                eventName,
                "Hurt",
                StringComparison.OrdinalIgnoreCase))
        {
            return actorIds.Contains(sourceEvent.TargetActorId);
        }
        if (string.Equals(
                eventName,
                "Damage",
                StringComparison.OrdinalIgnoreCase))
        {
            return actorIds.Contains(sourceEvent.SourceActorId);
        }
        return actorIds.Contains(sourceEvent.SourceActorId)
               || actorIds.Contains(sourceEvent.TargetActorId);
    }

    private static IEnumerable<string> EventNames(CombatSimulationEvent item)
    {
        switch (item.Kind)
        {
            case CombatSimulationEventKind.BattleStarted:
                yield return "FightStart";
                break;
            case CombatSimulationEventKind.TurnStarted:
                yield return "StartRound";
                yield return "StartRoundEnd";
                break;
            case CombatSimulationEventKind.TurnEnded:
                yield return "EndRound";
                break;
            case CombatSimulationEventKind.ActionResolved:
                yield return "Action";
                yield return "AttackDone";
                break;
            case CombatSimulationEventKind.CardExhausted:
                yield return "BurnCard";
                break;
            case CombatSimulationEventKind.DeckShuffled:
                yield return "Shuffle";
                break;
            case CombatSimulationEventKind.CardDrawn:
                yield return "ICreateCardItem";
                yield return "EndCreateCardItem";
                break;
            case CombatSimulationEventKind.CardCreated:
                yield return "CreateInt";
                break;
            case CombatSimulationEventKind.DamageDealt:
                yield return "Damage";
                yield return "Hurt";
                break;
            case CombatSimulationEventKind.Healed:
                yield return "Heal";
                yield return "HealOut";
                break;
            case CombatSimulationEventKind.EnergyChanged:
                yield return item.Amount < 0 ? "CostPower" : "AddPower";
                break;
            case CombatSimulationEventKind.StatusAdded:
                yield return "AddBuff";
                yield return item.DefinitionId + "OnLevelChange";
                break;
            case CombatSimulationEventKind.DeferredEffectTriggered:
                yield return item.DefinitionId + "OnTriggerEffect";
                break;
            case CombatSimulationEventKind.ActorDefeated:
                yield return "BeforeDead";
                yield return "Dead";
                break;
            case CombatSimulationEventKind.BattleEnded:
                yield return item.Amount == (int)CombatSimulationOutcome.Victory
                    ? "Win"
                    : "Escape";
                break;
        }
    }

    private object CreatePayload(
        Type type,
        CombatSimulationEvent? item,
        string? triggerSourceRewardId = null)
    {
        var card = item?.CardInstanceId > 0
            ? CardConfig(item.CardInstanceId)
            : new NativeRewardDataConfig(item?.DefinitionId ?? "");
        if (type == typeof(NativeRewardHurtData))
        {
            return new NativeRewardHurtData
            {
                val = (item?.Amount ?? 0).ToString(CultureInfo.InvariantCulture),
                sourceId = Actor(item?.SourceActorId ?? 0)?.InstanceId ?? "",
                toId = Actor(item?.TargetActorId ?? 0)?.InstanceId ?? "",
                fromDataId = item?.DefinitionId ?? "",
                damageType = item?.Message == "TrueDamage"
                    ? "True"
                    : item?.Message == "DirectHpLoss"
                      && (item.DefinitionId ?? "").StartsWith(
                          "buff_",
                          StringComparison.OrdinalIgnoreCase)
                        ? "Dot"
                        : "Normal"
            };
        }
        if (type == typeof(NativeRewardActionData))
        {
            return new NativeRewardActionData
            {
                data = card,
                dataId = item?.DefinitionId ?? "",
                Id = Actor(item?.SourceActorId ?? 0)?.InstanceId ?? ""
            };
        }
        if (type == typeof(NativeRewardCreateData))
        {
            return new NativeRewardCreateData
            {
                data = card,
                dataId = item?.DefinitionId ?? "",
                Id = Actor(item?.SourceActorId ?? 0)?.InstanceId ?? ""
            };
        }
        if (type == typeof(NativeRewardAddBuffData))
        {
            var status = context.Ruleset.SnapshotStatuses()
                .FirstOrDefault(definition => string.Equals(
                    definition.StatusId,
                    item?.DefinitionId,
                    StringComparison.OrdinalIgnoreCase));
            var statusData = new NativeRewardDataConfig(
                item?.DefinitionId ?? "");
            foreach (var pair in status?.Metadata
                                 ?? new Dictionary<string, string>())
            {
                statusData.data[pair.Key] = pair.Value;
            }
            statusData.data["Name"] = status?.DisplayName ?? "";
            statusData.data["Tag"] = string.Join(
                ",",
                status?.Tags ?? new List<string>());
            statusData.data["Type"] = status?.Tags.Contains(
                "Negative",
                StringComparer.OrdinalIgnoreCase) == true
                ? "负面"
                : "正面";
            return new NativeRewardAddBuffData
            {
                data = statusData,
                dataId = item?.DefinitionId ?? "",
                fromId = Actor(item?.SourceActorId ?? 0)?.InstanceId ?? "",
                dataFromid = triggerSourceRewardId ?? item?.SourceRewardId ?? "",
                toId = Actor(item?.TargetActorId ?? 0)?.InstanceId ?? ""
            };
        }
        if (type == typeof(NativeRewardBurnData))
        {
            return new NativeRewardBurnData
            {
                data = card,
                dataId = item?.DefinitionId ?? "",
                Id = Actor(item?.SourceActorId ?? 0)?.InstanceId ?? ""
            };
        }
        if (type == typeof(NativeRewardOutHealData))
        {
            return new NativeRewardOutHealData
            {
                val = item?.Amount ?? 0,
                Id = Actor(item?.TargetActorId ?? 0)?.InstanceId ?? ""
            };
        }
        if (string.Equals(
                type.Name,
                "PowerData",
                StringComparison.Ordinal))
        {
            var actorId =
                Actor(item?.TargetActorId ?? 0)?.InstanceId
                ?? Actor(item?.SourceActorId ?? 0)?.InstanceId
                ?? "";
            var stringConstructor = type.GetConstructor(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);
            if (stringConstructor != null)
            {
                return stringConstructor.Invoke(new object[] { actorId });
            }
            var payload = Activator.CreateInstance(type)!;
            var idProperty = type.GetProperty("Id");
            if (idProperty?.CanWrite == true)
            {
                idProperty.SetValue(payload, actorId);
            }
            else
            {
                var idField = type.GetField("Id");
                if (idField != null)
                {
                    idField.SetValue(payload, actorId);
                }
            }
            return payload;
        }
        return Activator.CreateInstance(type)!;
    }

    private List<NativeRewardCardItem> Cards(IEnumerable<int> ids)
    {
        return ids.Select(id => new NativeRewardCardItem(this, id)).ToList();
    }

    private IEnumerable<int> Targets()
    {
        return selectedActorIds.Count == 0
            ? new[] { executionSourceActorId }
            : selectedActorIds.ToArray();
    }

    private void Apply(
        CombatSimulationEffectKind kind,
        string definitionId,
        int amount,
        Action<CombatSimulationEffectDefinition>? configure = null)
    {
        foreach (var actorId in Targets())
        {
            ApplyTo(actorId, kind, definitionId, amount, configure);
        }
    }

    private void ApplyTo(
        int actorId,
        CombatSimulationEffectKind kind,
        string definitionId,
        int amount,
        Action<CombatSimulationEffectDefinition>? configure = null)
    {
        var actor = context.State.FindActor(actorId);
        if ((kind == CombatSimulationEffectKind.AddStatus
             || kind == CombatSimulationEffectKind.RemoveStatus)
            && (actor == null || !actor.Alive))
        {
            // Native game scripts commonly retain an event target after the
            // target dies. Buff mutations against that stale target are a
            // no-op in the game and must not invalidate an authoritative run.
            return;
        }
        var effect = new CombatSimulationEffectDefinition
        {
            Kind = kind,
            Target = CombatSimulationTarget.EventTarget,
            DefinitionId = definitionId,
            Amount = amount
        };
        configure?.Invoke(effect);
        context.ApplyEffects(
            new[] { effect },
            executionSourceActorId,
            actorId,
            new CombatSimulationEvent
            {
                SourceActorId = executionSourceActorId,
                TargetActorId = actorId,
                CardInstanceId = currentEvent?.CardInstanceId ?? 0,
                DefinitionId = definitionId,
                CausalChainId = currentEvent?.CausalChainId ?? 0,
                HandlerId = currentEvent?.HandlerId ?? "",
                SourceRewardId = string.IsNullOrWhiteSpace(
                    currentEvent?.SourceRewardId)
                    ? rule.RewardId
                    : currentEvent!.SourceRewardId,
                SourceActionId = currentEvent?.SourceActionId > 0
                    ? currentEvent.SourceActionId
                    : context.State.ActionSequence
            });
    }

    private void AddRandomStatus(
        object count,
        NativeRandomStatusPool pool)
    {
        var candidates = context.Ruleset.SnapshotStatuses()
            .Where(status => MatchesRandomStatusPool(status, pool))
            .Where(status => pool == NativeRandomStatusPool.Positive
                             || pool == NativeRandomStatusPool.Negative
                             || PlayerInfo.TempLucky < 20
                             || !IsStatusType(status, "Negative", "负面"))
            .OrderBy(status => status.StatusId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            context.AddUnsupported(
                "reward-random-status:" + rule.RewardId + ":" + pool);
            return;
        }
        var selectedCounts = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Math.Max(0, Number(count)); index++)
        {
            var selected = candidates[context.NextRandomInt(
                rule.RewardId + ":status:" + pool,
                candidates.Count)];
            selectedCounts[selected.StatusId] =
                selectedCounts.GetValueOrDefault(selected.StatusId, 0) + 1;
        }
        foreach (var selected in selectedCounts)
        {
            AddBuff(
                selected.Key,
                selected.Value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool MatchesRandomStatusPool(
        CombatStatusDefinition status,
        NativeRandomStatusPool pool)
    {
        var positive = IsStatusType(status, "Positive", "正面");
        var negative = IsStatusType(status, "Negative", "负面");
        var ability = IsStatusType(status, "Ability", "能力");
        return pool switch
        {
            NativeRandomStatusPool.Positive => positive,
            NativeRandomStatusPool.Negative => negative,
            NativeRandomStatusPool.OrdinaryAndAbility =>
                positive || negative || ability,
            _ => positive || negative
        };
    }

    private static bool IsStatusType(
        CombatStatusDefinition status,
        string tag,
        string metadataValue)
    {
        return status.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)
               || status.Metadata.TryGetValue("Type", out var type)
               && (string.Equals(
                       type?.Trim(),
                       metadataValue,
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(
                       type?.Trim(),
                       tag,
                       StringComparison.OrdinalIgnoreCase));
    }

    private void Shuffle(List<int> cards, string stream)
    {
        for (var index = cards.Count - 1; index > 0; index--)
        {
            var selected = context.NextRandomInt(
                rule.RewardId + ":shuffle:" + stream + ":" + index,
                index + 1);
            var temporary = cards[index];
            cards[index] = cards[selected];
            cards[selected] = temporary;
        }
    }

    private void IgnoreCosmeticApi(string api)
    {
        if (!CosmeticNoOpApis.Contains(api))
        {
            context.AddUnsupported(
                "native-no-op:" + rule.RewardId + ":" + api);
        }
    }

    internal void IgnoreCosmetic(string api)
    {
        IgnoreCosmeticApi(api);
    }

    private static int Number(object? value)
    {
        if (value == null) return 0;
        if (value is int integer) return integer;
        if (value is long longValue) return (int)longValue;
        if (value is double doubleValue) return (int)doubleValue;
        return int.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
    }

    private static string Text(object? value)
    {
        if (value is NativeRewardDataConfig dataConfig)
        {
            return dataConfig.data.GetValueOrDefault(
                "Id",
                dataConfig.InstanceID);
        }
        if (value is NativeRewardCardItem card)
        {
            return card.dataConfig.data.GetValueOrDefault(
                "Id",
                card.dataConfig.InstanceID);
        }
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    private enum NativeRandomStatusPool
    {
        Ordinary,
        OrdinaryAndAbility,
        Positive,
        Negative
    }
}

public sealed class NativeRewardEventHandler
{
    public string HandlerId { get; set; } = "";

    public string SourceRewardId { get; set; } = "";

    public HashSet<int> ActorIds { get; set; } = new();

    public Action<CombatSimulationEvent?> Callback { get; set; } = _ => { };

    public Type? PayloadType { get; set; }

    public Action<object?>? PayloadCallback { get; set; }
}

public sealed class NativeRewardActor
{
    private readonly NativeRewardScriptGlobals globals;
    private readonly int actorId;

    public NativeRewardActor(NativeRewardScriptGlobals globals, int actorId)
    {
        this.globals = globals;
        this.actorId = actorId;
        dynamicVariables = new NativeRewardDoubleDictionary(() => State.Variables);
        DamageFilter = new NativeRewardDoubleDictionary(
            () => State.Variables,
            "DamageFilter.");
    }

    private CombatActorState State =>
        globals.Context.State.FindActor(actorId) ?? new CombatActorState();

    public string InstanceId => State.InstanceKey;

    public int ActorId => actorId;

    public NativeRewardDataConfig dataConfig =>
        new(State.DefinitionId)
        {
            InstanceID = State.InstanceKey
        };

    public int CurHp
    {
        get => State.Hp;
        set => State.Hp = Math.Max(0, Math.Min(State.MaxHp, value));
    }

    public int MaxHp
    {
        get => State.MaxHp;
        set
        {
            State.MaxHp = Math.Max(1, value);
            State.Hp = Math.Min(State.Hp, State.MaxHp);
        }
    }

    public int Defend
    {
        get => State.Block;
        set => State.Block = Math.Max(0, value);
    }

    public NativeRewardDoubleDictionary dynamicVariables { get; }

    public NativeRewardDoubleDictionary DamageFilter { get; }

    public NativeRewardActorState state =>
        State.Alive ? NativeRewardActorState.Alive : NativeRewardActorState.Dead;

    public bool IsNull()
    {
        return globals.Context.State.FindActor(actorId) == null;
    }

    public NativeRewardBuff? GetBuff(object id)
    {
        var status = State.Statuses.FirstOrDefault(item =>
            string.Equals(
                item.StatusId,
                Convert.ToString(id, CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase));
        return status == null
            ? null
            : new NativeRewardBuff(globals, actorId, status.StatusId);
    }

    public NativeRewardBuff[] GetBuffs()
    {
        return State.Statuses
            .Select(item => new NativeRewardBuff(
                globals,
                actorId,
                item.StatusId))
            .ToArray();
    }

    public NativeRewardActor AddBuff(object id, object amount)
    {
        globals.AddBuffToActor(actorId, id, amount);
        return this;
    }

    public NativeRewardActor? FindSummon(object _)
    {
        return globals.Context.State.Actors
            .Where(item => item.Alive
                           && item.Kind == CombatSimulationActorKind.Friendly)
            .OrderBy(item => item.ActorId)
            .Select(item => globals.Actor(item.ActorId))
            .FirstOrDefault();
    }

    public int DamageCalculate(int amount)
    {
        return globals.DamageCalculate(amount);
    }

    public int UnDamageCalucate(int amount)
    {
        var multiplier = State.Variables.GetValueOrDefault(
            "AttackedPercentDamage",
            1d);
        return Math.Max(0, (int)Math.Round(amount * multiplier));
    }

    public void RemoveBuff(object id)
    {
        globals.RemoveBuffFromActor(
            actorId,
            Convert.ToString(id, CultureInfo.InvariantCulture) ?? "");
    }

    public override bool Equals(object? obj)
    {
        return obj is NativeRewardActor other && other.actorId == actorId;
    }

    public override int GetHashCode()
    {
        return actorId;
    }

    public enum AnimatedState
    {
        None,
        Idle,
        Hit,
        Dead
    }
}

public enum NativeRewardActorState
{
    Alive,
    Dead
}

public sealed class NativeRewardBuff
{
    public NativeRewardBuff(
        NativeRewardScriptGlobals globals,
        int actorId,
        string statusId)
    {
        buffConfig = new NativeRewardBuffConfig(globals, actorId, statusId);
        effectList = globals.DeferredEffects(actorId, statusId);
    }

    public NativeRewardBuffConfig buffConfig { get; }

    public NativeRewardDeferredEffectCollection effectList {
        get;
    }

    public void ClearBuff()
    {
        buffConfig.Clear();
    }
}

public sealed class NativeRewardDeferredEffectCollection :
    IEnumerable<(NativeRewardDataConfig dataConfig, Action action)>
{
    private readonly List<(
        NativeRewardDataConfig dataConfig,
        Action action)> items;
    private readonly List<CombatDeferredEffectState> stateItems;
    private readonly int actorId;
    private readonly string statusId;

    internal NativeRewardDeferredEffectCollection(
        List<(NativeRewardDataConfig dataConfig, Action action)> items,
        List<CombatDeferredEffectState> stateItems,
        int actorId,
        string statusId)
    {
        this.items = items;
        this.stateItems = stateItems;
        this.actorId = actorId;
        this.statusId = statusId ?? "";
    }

    public int Count => items.Count;

    public (NativeRewardDataConfig dataConfig, Action action) this[int index] =>
        items[index];

    public void Add(
        NativeRewardDataConfig dataConfig,
        Action action)
    {
        items.Add((dataConfig, action));
        AddStateItem(dataConfig);
    }

    public void Add(
        (NativeRewardDataConfig dataConfig, Action action) item)
    {
        items.Add(item);
        AddStateItem(item.dataConfig);
    }

    public void RemoveAt(int index)
    {
        RemoveStateItem(index);
        items.RemoveAt(index);
    }

    public void Clear()
    {
        items.Clear();
        stateItems.RemoveAll(item =>
            item.ActorId == actorId
            && string.Equals(
                item.StatusId,
                statusId,
                StringComparison.OrdinalIgnoreCase));
    }

    public bool InvokeFirst()
    {
        if (items.Count == 0)
        {
            return false;
        }
        items[0].action();
        return true;
    }

    public bool InvokeLast()
    {
        if (items.Count == 0)
        {
            return false;
        }
        items[items.Count - 1].action();
        return true;
    }

    public IEnumerator<(
        NativeRewardDataConfig dataConfig,
        Action action)> GetEnumerator()
    {
        return items.GetEnumerator();
    }

    private void AddStateItem(NativeRewardDataConfig dataConfig)
    {
        var nextSequence = stateItems
            .Where(item => item.ActorId == actorId
                           && string.Equals(
                               item.StatusId,
                               statusId,
                               StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Sequence)
            .DefaultIfEmpty(-1)
            .Max() + 1;
        _ = int.TryParse(
            dataConfig?.InstanceID ?? "",
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var instanceId);
        stateItems.Add(new CombatDeferredEffectState
        {
            Sequence = nextSequence,
            ActorId = actorId,
            StatusId = statusId,
            SourceCardId = dataConfig?.data.GetValueOrDefault(
                "Id",
                dataConfig.InstanceID) ?? "",
            SourceCardInstanceId = instanceId
        });
    }

    private void RemoveStateItem(int index)
    {
        var matches = stateItems
            .Where(item => item.ActorId == actorId
                           && string.Equals(
                               item.StatusId,
                               statusId,
                               StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Sequence)
            .ToList();
        if (index >= 0 && index < matches.Count)
        {
            stateItems.Remove(matches[index]);
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public sealed class NativeRewardBuffConfig
{
    private readonly NativeRewardScriptGlobals globals;
    private readonly int actorId;
    private readonly string statusId;

    public NativeRewardBuffConfig(
        NativeRewardScriptGlobals globals,
        int actorId,
        string statusId)
    {
        this.globals = globals;
        this.actorId = actorId;
        this.statusId = statusId;
        dataConfig = globals.StatusConfig(actorId, statusId);
        var definition = globals.Context.Ruleset.SnapshotStatuses()
            .FirstOrDefault(item => string.Equals(
                item.StatusId,
                statusId,
                StringComparison.OrdinalIgnoreCase));
        foreach (var pair in definition?.Metadata
                             ?? new Dictionary<string, string>())
        {
            dataConfig.data[pair.Key] = pair.Value;
        }
        dataConfig.data["Type"] = definition?.Tags.Contains(
            "Negative",
            StringComparer.OrdinalIgnoreCase) == true
            ? "负面"
            : "正面";
    }

    private CombatStatusState? State => globals.Context.State
        .FindActor(actorId)?.Statuses.FirstOrDefault(item =>
            string.Equals(
                item.StatusId,
                statusId,
                StringComparison.OrdinalIgnoreCase));

    public string BuffId => statusId;

    public string Type => dataConfig.data.GetValueOrDefault("Type", "");

    public int Level
    {
        get => State?.Stacks ?? 0;
        set
        {
            if (State != null)
            {
                State.Stacks = Math.Max(0, value);
            }
        }
    }

    public NativeRewardDataConfig dataConfig { get; }

    internal void Clear()
    {
        globals.RemoveBuffFromActor(actorId, statusId);
    }
}

public sealed class NativeRewardPlayerInfo
{
    private readonly NativeRewardScriptGlobals globals;

    public NativeRewardPlayerInfo(NativeRewardScriptGlobals globals)
    {
        this.globals = globals;
    }

    private CombatActorState Player =>
        globals.Context.State.Player ?? new CombatActorState();

    public Dictionary<string, string> SpecialVars =>
        globals.Context.Scenario.CampaignVariables;

    public Dictionary<string, int> SkillTime =>
        globals.Context.State.SkillUseCounts;

    public void ShowCaption(object _)
    {
    }

    public int Strength
    {
        get => Variable("Strength");
        set => SetVariable("Strength", value);
    }

    public int Wisdom
    {
        get => Variable("Wisdom");
        set => SetVariable("Wisdom", value);
    }

    public int Perceive
    {
        get => Variable("Perceive");
        set => SetVariable("Perceive", value);
    }

    public int Lucky
    {
        get => Variable("Lucky");
        set => SetVariable("Lucky", value);
    }

    public int TempStrength
    {
        get => Variable("TempStrength");
        set => SetVariable("TempStrength", value);
    }

    public int TempWisdom
    {
        get => Variable("TempWisdom");
        set => SetVariable("TempWisdom", value);
    }

    public int TempPerceive
    {
        get => Variable("TempPerceive");
        set => SetVariable("TempPerceive", value);
    }

    public int TempLucky
    {
        get => Variable("TempLucky");
        set => SetVariable("TempLucky", value);
    }

    public int Money
    {
        get => Variable("Money");
        set => SetVariable("Money", Math.Max(0, value));
    }

    public int MoneyMultiplier
    {
        get => Variable("MoneyMultiplier");
        set => SetVariable("MoneyMultiplier", value);
    }

    public int Hp
    {
        get => Player.Hp;
        set => Player.Hp = Math.Max(0, Math.Min(Player.MaxHp, value));
    }

    public int MaxHp
    {
        get => Player.MaxHp;
        set
        {
            Player.MaxHp = Math.Max(1, value);
            Player.Hp = Math.Min(Player.Hp, Player.MaxHp);
        }
    }

    public int Power
    {
        get => Player.Energy;
        set => Player.Energy = Math.Max(0, value);
    }

    public int MaxPower
    {
        get => Player.BaseEnergy;
        set => Player.BaseEnergy = Math.Max(0, value);
    }

    public int Level => globals.Context.State.Turn;

    public int enemylevel => globals.Context.State.Player?.Variables
        .GetValueOrDefault("EncounterKind", 0d) switch
    {
        0d => 1,
        1d => 2,
        2d => 3,
        _ => 4
    };

    public int enemyCount => globals.Context.State.LivingEnemies.Count();

    public int CardTotalCount => globals.Context.State.Cards.Count;

    public int PlayerCount => 1;

    public int Reward
    {
        get => Variable("Reward");
        set => SetVariable("Reward", value);
    }

    public List<NativeRewardDataConfig> CardList => globals.Context.State.Cards
        .Select(item => globals.CardConfig(item.InstanceId))
        .ToList();

    public List<NativeRewardDataConfig> UnCardList => new();

    public List<NativeRewardDataConfig> RelicList => globals.Context.Scenario.RewardRules
        .Where(item => item.Kind.Equals("Relic", StringComparison.OrdinalIgnoreCase))
        .Select(item => new NativeRewardDataConfig(item.RewardId))
        .ToList();

    public List<NativeRewardDataConfig> BlessingList => globals.Context.Scenario.RewardRules
        .Where(item => item.Kind.Equals("Blessing", StringComparison.OrdinalIgnoreCase))
        .Select(item => new NativeRewardDataConfig(item.RewardId))
        .ToList();

    public NativeRewardDataConfig GetCareer()
    {
        return new NativeRewardDataConfig(globals.Context.Scenario.Player.RoleId);
    }

    public NativeRewardDataConfig GetCareerData()
    {
        return GetCareer();
    }

    public string GetTagDiff()
    {
        return Variable("TagDiff").ToString(CultureInfo.InvariantCulture);
    }

    public void EventTrigger(object eventName)
    {
        globals.DispatchNamedEvent(
            Convert.ToString(eventName, CultureInfo.InvariantCulture) ?? "",
            null);
    }

    public void AddEvent(string eventName, Action action, object? _ = null)
    {
        globals.AddEvent(eventName, action);
    }

    public void UpdateAch(object _, object __)
    {
        globals.IgnoreCosmetic("UpdateAch");
    }

    public void WinTheFight()
    {
        globals.Context.Terminate(
            CombatSimulationOutcome.Victory,
            CombatTerminationReason.Victory);
    }

    public void ChangeSelected(object amount)
    {
        var value = NativeRewardExtensions.ToInt(
            Convert.ToString(amount, CultureInfo.InvariantCulture));
        Strength += value;
        Wisdom += value;
    }

    public void ChangeAllVars(object amount)
    {
        var value = NativeRewardExtensions.ToInt(
            Convert.ToString(amount, CultureInfo.InvariantCulture));
        Strength += value;
        Wisdom += value;
        Perceive += value;
        Lucky += value;
    }

    public void AddBless(object id)
    {
        var text = Convert.ToString(id, CultureInfo.InvariantCulture) ?? "";
        globals.Context.RecordRewardMutation("Add", "Blessing", text);
    }

    public void RemoveBless(object id)
    {
        var text = Convert.ToString(id, CultureInfo.InvariantCulture) ?? "";
        globals.Context.Scenario.RewardRules.RemoveAll(item =>
            item.Kind.Equals("Blessing", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                item.RewardId,
                text,
                StringComparison.OrdinalIgnoreCase));
        globals.Context.RecordRewardMutation("Remove", "Blessing", text);
    }

    public void RemoveRelic(object id)
    {
        var text = Convert.ToString(id, CultureInfo.InvariantCulture) ?? "";
        globals.Context.Scenario.RewardRules.RemoveAll(item =>
            item.Kind.Equals("Relic", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                item.RewardId,
                text,
                StringComparison.OrdinalIgnoreCase));
        globals.Context.RecordRewardMutation("Remove", "Relic", text);
    }

    public void RemoveCard(object instanceId)
    {
        var raw = Convert.ToString(
            instanceId,
            CultureInfo.InvariantCulture) ?? "";
        var cardId = int.TryParse(
                         raw,
                         NumberStyles.Integer,
                         CultureInfo.InvariantCulture,
                         out var runtimeId)
            ? globals.Context.State.FindCard(runtimeId)?.CardId ?? raw
            : raw;
        globals.Context.RecordRewardMutation("Remove", "Card", cardId);
    }

    public void ShowItemShowUI(params object[] _)
    {
    }

    public void AddCard(object id)
    {
        globals.CreateCard(id);
    }

    public void DelayAddCard(object id)
    {
        AddCard(id);
    }

    public void DelayAddBless(object id)
    {
        AddBless(id);
    }

    public void DelayAddRelic(object id)
    {
        var text = Convert.ToString(id, CultureInfo.InvariantCulture) ?? "";
        globals.Context.RecordRewardMutation("Add", "Relic", text);
    }

    public void RandomAddBless(int count)
    {
        var candidates = globals.Context.Scenario.RewardCatalog
            .Where(item => item.Kind.Equals(
                               "Blessing",
                               StringComparison.OrdinalIgnoreCase)
                           && !item.Negative)
            .OrderBy(item => item.RewardId, StringComparer.Ordinal)
            .ToList();
        for (var index = 0;
             index < Math.Max(0, count) && candidates.Count > 0;
             index++)
        {
            var selected = candidates[globals.Context.NextRandomInt(
                "reward:random-blessing",
                candidates.Count)];
            globals.Context.RecordRewardMutation(
                "Add",
                "Blessing",
                selected.RewardId);
        }
    }

    public void RandomAddBless(object count)
    {
        RandomAddBless(
            NativeRewardExtensions.ToInt(
                Convert.ToString(count, CultureInfo.InvariantCulture)));
    }

    public void RandomAddRelic(int count)
    {
        var candidates = globals.Context.Scenario.RewardCatalog
            .Where(item => item.Kind.Equals(
                "Relic",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.RewardId, StringComparer.Ordinal)
            .ToList();
        for (var index = 0;
             index < Math.Max(0, count) && candidates.Count > 0;
             index++)
        {
            var selected = candidates[globals.Context.NextRandomInt(
                "reward:random-relic",
                candidates.Count)];
            globals.Context.RecordRewardMutation(
                "Add",
                "Relic",
                selected.RewardId);
        }
    }

    public void RandomAddRelic(object count)
    {
        RandomAddRelic(
            NativeRewardExtensions.ToInt(
                Convert.ToString(count, CultureInfo.InvariantCulture)));
    }

    public void RandomRemoveCard(object count)
    {
        var amount = Math.Max(
            0,
            NativeRewardExtensions.ToInt(
                Convert.ToString(count, CultureInfo.InvariantCulture)));
        foreach (var instanceId in globals.Context.State.DrawPile
                     .Concat(globals.Context.State.Hand)
                     .Concat(globals.Context.State.DiscardPile)
                     .OrderBy(item => item)
                     .Take(amount)
                     .ToList())
        {
            globals.Context.State.DrawPile.Remove(instanceId);
            globals.Context.State.Hand.Remove(instanceId);
            globals.Context.State.DiscardPile.Remove(instanceId);
            globals.Context.State.ExhaustPile.Remove(instanceId);
            globals.Context.State.Cards.RemoveAll(item =>
                item.InstanceId == instanceId);
        }
    }

    public List<string> ChooseVars => new() { "Strength", "Wisdom" };

    public int Enemy => 1;

    private int Variable(string key)
    {
        return (int)Math.Round(Player.Variables.GetValueOrDefault(key, 0d));
    }

    private void SetVariable(string key, int value)
    {
        Player.Variables[key] = value;
    }
}

public sealed class NativeRewardDice
{
    private readonly NativeRewardScriptGlobals globals;
    private readonly string stream;
    private int minimum;
    private int maximum = 100;
    private bool rolling;

    public NativeRewardDice(NativeRewardScriptGlobals globals, string stream)
    {
        this.globals = globals;
        this.stream = stream;
    }

    public NativeRewardDice WithRange(int min, int max)
    {
        minimum = min;
        maximum = Math.Max(min, max);
        return this;
    }

    public NativeRewardDice Roll()
    {
        if (stream == "check" && !rolling)
        {
            globals.PrepareDiceCheck();
        }
        rolling = true;
        Value = minimum + globals.Context.NextRandomInt(
            stream,
            Math.Max(1, maximum - minimum + 1));
        var state = new NativeRewardDiceState(Value, 0);
        try
        {
            OnRoll?.Invoke(state);
            Value = state.Value;
            return this;
        }
        finally
        {
            OnRoll = null;
            minimum = 0;
            maximum = 100;
            rolling = false;
        }
    }

    public int Value { get; private set; }

    public event Action<NativeRewardDiceState>? OnRoll;

    public NativeRewardDiceState InternalRoll()
    {
        var value = minimum + globals.Context.NextRandomInt(
            stream + ":internal",
            Math.Max(1, maximum - minimum + 1));
        return new NativeRewardDiceState(value, 0);
    }
}

public sealed class NativeRewardDiceState
{
    public NativeRewardDiceState(int value, int bonus)
    {
        Value = value;
        Bonus = bonus;
    }

    public int Value { get; set; }

    public int Bonus { get; set; }

    public void CopyTo(NativeRewardDiceState target)
    {
        target.Value = Value;
        target.Bonus = Bonus;
    }
}

public enum NativeRewardDataType
{
    Card,
    EnemyCard,
    Bless,
    Relic,
    Buff,
    EnchTag
}

public sealed class NativeRewardStringDictionary :
    Dictionary<string, string>
{
    public NativeRewardStringDictionary()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }

    public NativeRewardStringDictionary(
        IDictionary<string, string> source)
        : base(source, StringComparer.OrdinalIgnoreCase)
    {
    }

    public new string this[string key]
    {
        get => TryGetValue(key, out var value) ? value : "";
        set => base[key] = value;
    }

    public bool TryAdd(string key, string value)
    {
        if (ContainsKey(key))
        {
            return false;
        }
        Add(key, value);
        return true;
    }
}

public sealed class NativeRewardDataConfig
{
    public NativeRewardDataConfig()
        : this("")
    {
    }

    public NativeRewardDataConfig(string id)
    {
        InstanceID = id;
        InitializeData(id);
        Vars = new NativeRewardStringDictionary();
        scriptExecutor = new NativeRewardScriptExecutor(this);
    }

    public NativeRewardDataConfig(string id, NativeRewardDataType _)
        : this(id)
    {
    }

    internal NativeRewardDataConfig(
        string id,
        Action<string> runScript)
    {
        InstanceID = id;
        InitializeData(id);
        Vars = new NativeRewardStringDictionary();
        scriptExecutor = new NativeRewardScriptExecutor(this, runScript);
    }

    internal NativeRewardDataConfig(
        string id,
        NativeRewardScriptExecutor scriptExecutor)
    {
        InstanceID = id;
        InitializeData(id);
        Vars = new NativeRewardStringDictionary();
        this.scriptExecutor = scriptExecutor;
    }

    private void InitializeData(string id)
    {
        data["Id"] = id;
        data["Name"] = "";
        data["Type"] = "";
        data["Tag"] = "";
        data["Expend"] = "0";
        data["Rarity"] = "1";
        data["InitScript"] = "";
    }

    public string InstanceID { get; set; }

    public NativeRewardStringDictionary data { get; set; } = new();

    public NativeRewardStringDictionary Vars { get; set; }

    public string this[string key]
    {
        get => data.GetValueOrDefault(key, "");
        set => data[key] = value;
    }

    public NativeRewardScriptExecutor scriptExecutor { get; }

    public void ReplaceData(IDictionary<string, string> source)
    {
        data = new NativeRewardStringDictionary(source);
    }

    public NativeRewardDataConfig Clone()
    {
        var clone = new NativeRewardDataConfig(
            data.GetValueOrDefault("Id", InstanceID))
        {
            InstanceID = InstanceID
        };
        foreach (var pair in data) clone.data[pair.Key] = pair.Value;
        foreach (var pair in Vars) clone.Vars[pair.Key] = pair.Value;
        return clone;
    }

    public static implicit operator NativeRewardDataConfig(
        NativeRewardCardItem card)
    {
        return card.dataConfig;
    }
}

public sealed class NativeRewardCardItem
{
    private readonly NativeRewardScriptGlobals globals;
    private readonly int instanceId;

    public NativeRewardCardItem(
        NativeRewardScriptGlobals globals,
        int instanceId)
    {
        this.globals = globals;
        this.instanceId = instanceId;
    }

    public NativeRewardDataConfig dataConfig => globals.CardConfig(instanceId);

    public Dictionary<string, string> data => dataConfig.data;

    public List<string> Tags => data.GetValueOrDefault("Tag", "")
        .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(item => item.Trim())
        .ToList();

    public NativeRewardStringDictionary Vars => dataConfig.Vars;

    public NativeRewardScriptExecutor scriptExecutor =>
        new(dataConfig, phase =>
        {
            if (string.Equals(
                    phase,
                    "UseScript",
                    StringComparison.OrdinalIgnoreCase))
            {
                globals.UseCard(dataConfig);
            }
        });

    public bool isReverse { get; set; }

    public void Burning(float _)
    {
        RemoveFromAllZones();
        globals.Context.State.ExhaustPile.Add(instanceId);
    }

    public void InternalBurning()
    {
        Burning(1f);
    }

    public void InternalThrow()
    {
        ThrowCard();
    }

    public void ThrowCard()
    {
        RemoveFromAllZones();
        globals.Context.State.DiscardPile.Add(instanceId);
    }

    public void Reverse()
    {
        isReverse = !isReverse;
    }

    public void TransformToConfiguredType(NativeRewardDataConfig config)
    {
        var instance = globals.Context.State.FindCard(instanceId);
        if (instance != null)
        {
            instance.CardId = config.data.GetValueOrDefault(
                "Id",
                config.InstanceID);
            instance.ApparentCardId = "";
            instance.EnchantmentIds.Clear();
            instance.Variables.Clear();
            dataConfig.data =
                new NativeRewardStringDictionary(config.data);
            foreach (var pair in config.Vars)
            {
                dataConfig.Vars[pair.Key] = pair.Value;
            }
            dataConfig.InstanceID =
                instanceId.ToString(CultureInfo.InvariantCulture);
            foreach (var pair in dataConfig.Vars)
            {
                instance.Variables[pair.Key] = pair.Value;
            }
            globals.RecordCardProvenance(
                instance,
                config,
                "native-transform",
                instanceId);
            RefreshTag();
        }
    }

    public void RefreshTag()
    {
        var instance = globals.Context.State.FindCard(instanceId);
        if (instance == null)
        {
            return;
        }
        instance.Tags = dataConfig.data.GetValueOrDefault("Tag", "")
            .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Concat(dataConfig.Vars.GetValueOrDefault("Tag", "")
                .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Concat(dataConfig.Vars.GetValueOrDefault("SpecialTag", "")
                .Split(new[] { ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void DataUpdate()
    {
        var instance = globals.Context.State.FindCard(instanceId);
        if (instance == null) return;
        instance.CostModifier = dataConfig.Vars
            .GetValueOrDefault("ExCost", "0")
            .ToInt();
        instance.Variables = dataConfig.Vars.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        RefreshTag();
    }

    private void RemoveFromAllZones()
    {
        globals.Context.State.Hand.RemoveAll(item => item == instanceId);
        globals.Context.State.DrawPile.RemoveAll(item => item == instanceId);
        globals.Context.State.DiscardPile.RemoveAll(item => item == instanceId);
        globals.Context.State.ExhaustPile.RemoveAll(item => item == instanceId);
    }
}

public sealed class NativeRewardDoubleDictionary
{
    private readonly Func<Dictionary<string, double>> source;
    private readonly string keyPrefix;

    public NativeRewardDoubleDictionary(
        Func<Dictionary<string, double>> source,
        string keyPrefix = "")
    {
        this.source = source;
        this.keyPrefix = keyPrefix ?? "";
    }

    public double this[string key]
    {
        get => source().GetValueOrDefault(StoredKey(key), 0d);
        set => source()[StoredKey(key)] = value;
    }

    public bool ContainsKey(string key)
    {
        return source().ContainsKey(StoredKey(key));
    }

    public double GetValueOrDefault(string key, double fallback = 0d)
    {
        return source().TryGetValue(StoredKey(key), out var value)
            ? value
            : fallback;
    }

    public void Add(string key, double value)
    {
        source().Add(StoredKey(key), value);
    }

    public void Clear()
    {
        if (keyPrefix.Length == 0)
        {
            source().Clear();
            return;
        }
        foreach (var key in source().Keys
                     .Where(key => key.StartsWith(
                         keyPrefix,
                         StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            source().Remove(key);
        }
    }

    private string StoredKey(string key)
    {
        return keyPrefix + (key ?? "");
    }
}

public sealed class NativeRewardHurtData
{
    public string damageType = "";
    public string val = "0";
    public string sourceId = "";
    public string toId = "";
    public string fromDataId = "";
}

public sealed class NativeRewardActionData
{
    public NativeRewardDataConfig data = new();
    public string dataId = "";
    public string Id = "";
}

public sealed class NativeRewardCreateData
{
    public NativeRewardDataConfig data = new();
    public string dataId = "";
    public string Id = "";
}

public sealed class NativeRewardBurnData
{
    public NativeRewardDataConfig data = new();
    public string dataId = "";
    public string Id = "";
}

public sealed class NativeRewardAddBuffData
{
    public NativeRewardDataConfig data = new();
    public string dataId = "";
    public string fromId = "";
    public string dataFromid = "";
    public string toId = "";
}

public sealed class NativeRewardOutHealData
{
    public int val;
    public string Id = "";
}

public sealed class NativeRewardScriptExecuteData
{
    public NativeRewardDataConfig data = new();
    public string Id = "";
    public NativeRewardScriptExecutor Executor = new();
    public object?[] Arguments = Array.Empty<object?>();
    public string MethodName = "";
}

public sealed class NativeRewardScriptExecutor
{
    private readonly Action<string>? runScript;

    public NativeRewardScriptExecutor()
    {
        dataConfig = new NativeRewardDataConfig("", this);
    }

    internal NativeRewardScriptExecutor(NativeRewardDataConfig dataConfig)
    {
        this.dataConfig = dataConfig;
    }

    internal NativeRewardScriptExecutor(
        NativeRewardDataConfig dataConfig,
        Action<string> runScript)
    {
        this.dataConfig = dataConfig;
        this.runScript = runScript;
    }

    public NativeRewardActor? Self { get; set; }

    public NativeRewardDataConfig dataConfig { get; set; }

    public List<NativeRewardActor> Object { get; } = new();

    public void RunScript(string phase)
    {
        runScript?.Invoke(phase);
    }
}

public sealed class NativeRewardFightCardManager
{
    public static NativeRewardFightCardManager Instance { get; } = new();

    [ThreadStatic]
    private static NativeRewardScriptGlobals? threadGlobals;

    public Dictionary<NativeRewardDataConfig, List<string>> CardTags { get; } =
        new();

    internal NativeRewardScriptGlobals? Globals
    {
        get => threadGlobals;
        set => threadGlobals = value;
    }

    public List<NativeRewardDataConfig> cardList =>
        Globals?.DeckCard.Select(item => item.dataConfig).ToList()
        ?? new List<NativeRewardDataConfig>();
}

public sealed class NativeRewardRoleTable
{
    public static NativeRewardRoleTable Instance { get; } = new();

    public Dictionary<string, NativeRewardDataConfig> enchasedDict { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<NativeRewardDataConfig> blessingConfigs { get; } = new();
}

public static class NativeRewardMathf
{
    public static int Max(int left, int right)
    {
        return Math.Max(left, right);
    }

    public static int Min(int left, int right)
    {
        return Math.Min(left, right);
    }

    public static float Pow(float value, float power)
    {
        return (float)Math.Pow(value, power);
    }

    public static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    public static float DeltaAngle(float current, float target)
    {
        var delta = (target - current) % 360f;
        if (delta > 180f) delta -= 360f;
        if (delta < -180f) delta += 360f;
        return delta;
    }
}

public static class NativeRewardDebug
{
    public static void Log(object? _)
    {
    }
}

public static class NativeRewardExtensions
{
    public static int ToInt(this string? value)
    {
        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
    }

    public static double GetValueOrDefault(
        this Dictionary<string, double> dictionary,
        string key,
        double fallback)
    {
        return dictionary.TryGetValue(key, out var value) ? value : fallback;
    }

    public static TValue GetValueOrDefault<TKey, TValue>(
        this Dictionary<TKey, TValue> dictionary,
        TKey key,
        TValue fallback)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var value) ? value : fallback;
    }

    public static TValue GetValueOrDefault<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> dictionary,
        TKey key,
        TValue fallback)
        where TKey : notnull
    {
        return dictionary.TryGetValue(key, out var value) ? value : fallback;
    }

}
