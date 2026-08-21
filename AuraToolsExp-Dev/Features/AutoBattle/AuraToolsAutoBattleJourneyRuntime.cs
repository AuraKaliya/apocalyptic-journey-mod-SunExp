using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCombatSimulation.Shared;
using AuraMode.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.CardRefresh;
using AuraToolsExp.Dll.Infrastructure;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal static class AuraToolsAutoBattleJourneyRuntime
{
    private const string HandlerId = "AutoBattleJourneyTraining";
    private static readonly object WriteGate = new();
    private static bool initialized;
    private static bool settlementWritten;
    private static IDisposable? lifecycleSubscription;
    private static CombatJourneyTrainingEpisode? current;
    private static PendingReward? pendingReward;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        lifecycleSubscription = AuraBattleLifecycleRouter.Register(
            modConfig,
            AuraToolsIds.ModId,
            HandlerId,
            new AuraBattleLifecycleSubscription
            {
                AdventureStarting = _ => BeginAdventure(),
                BattleInitializing = _ => BeginBattle(),
                BattleRestarting = MarkBattleRestarting,
                BattleSettling = outcome => MarkBattleEnding(outcome.NativeContext),
                BattleEnded = outcome => FinishBattle(outcome.NativeContext)
            },
            AuraToolsLog.Info,
            AuraToolsLog.Warn);
        AuraToolsHookRegistry.Before(
            modConfig,
            "CardChoiceUI.Select",
            BeforeRewardSelected,
            HandlerId);
        AuraToolsHookRegistry.After(
            modConfig,
            "CardChoiceUI.Select",
            AfterRewardSelected,
            HandlerId);
        AuraToolsHookRegistry.Before(
            modConfig,
            "GameApp.GameOver",
            context => CompleteAdventure(context, settleUnknown: false),
            HandlerId);
        AuraToolsHookRegistry.After(
            modConfig,
            "GameExitUI.Start",
            context => CompleteAdventure(context, settleUnknown: true),
            HandlerId);
    }

    public static string DescribeCurrentCapture()
    {
        var run = current;
        if (run == null)
        {
            return "实战采集待开始";
        }
        return "本次旅程：战斗 "
               + run.Battles.Count
               + " · 奖励 "
               + run.Rewards.Count
               + (run.Complete ? " · 已结束" : " · 记录中");
    }

    private static void BeginAdventure()
    {
        if (!AuraToolsConfigService.MatchExperience.AutoBattle.CaptureTrainingSamples)
        {
            current = null;
            pendingReward = null;
            settlementWritten = false;
            return;
        }
        var activeMode = AuraModeRuntime.Current(AuraToolsIds.ModId, refresh: true);
        var modeId = activeMode?.ModeId ?? "Normal";
        var runId = activeMode?.Run?.RunId;
        var content = AuraToolsCombatContentRuntime.SnapshotContentSet();
        var autoBattle = AuraToolsConfigService.MatchExperience.AutoBattle;
        current = new CombatJourneyTrainingEpisode
        {
            JourneyRunId = string.IsNullOrWhiteSpace(runId)
                ? "live-world-simulation:" + Guid.NewGuid().ToString("N")
                : runId!,
            JourneyId = "witch.world-simulation.live",
            ModeId = string.IsNullOrWhiteSpace(modeId) ? "Normal" : modeId,
            Source = "live-world-simulation",
            PolicyId = AuraToolsAutoBattleRuntime.Active ? "policy" : "human",
            OwnerModSetHash = content.OwnerModSetHash,
            ContentSetHash = content.ContentSetHash,
            BaseModelId = autoBattle.SelectedModelId ?? "",
            ActiveAdapterIds = AuraToolsAutoBattleModelRuntime
                .SnapshotActiveAdapterIds(
                    autoBattle.Profile,
                    autoBattle.SelectedModelId ?? "")
                .ToList(),
            StartedUtc = DateTime.UtcNow
        };
        settlementWritten = false;
        pendingReward = null;
        CaptureRoleAndDeck(current, initial: true);
        AuraToolsLog.Info(
            "[AutoBattle][JourneyTraining] 已开始实战旅程采集：runId="
            + current.JourneyRunId
            + "，mode="
            + current.ModeId);
    }

    private static CombatJourneyTrainingEpisode? EnsureRun()
    {
        if (!AuraToolsConfigService.MatchExperience.AutoBattle.CaptureTrainingSamples)
        {
            return null;
        }
        if (current == null || current.Complete || settlementWritten)
        {
            BeginAdventure();
        }
        return current;
    }

    private static void BeginBattle()
    {
        var run = EnsureRun();
        if (run == null)
        {
            return;
        }
        CaptureRoleAndDeck(run, initial: run.InitialDeck.Count == 0);
        var sessionId = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        if (sessionId <= 0
            || run.Battles.Any(battle => battle.BattleSessionId == sessionId))
        {
            return;
        }
        run.Battles.Add(new CombatJourneyBattleTrainingRecord
        {
            BattleIndex = run.Battles.Count,
            BattleSessionId = sessionId,
            StageId = "live-battle-" + run.Battles.Count,
            Outcome = "in-progress"
        });
    }

    private static void MarkBattleEnding(ModHookContext context)
    {
        UpdateBattleOutcome(context);
    }

    private static void MarkBattleRestarting(ModHookContext context)
    {
        var run = current;
        var sessionId = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        var battle = run?.Battles.LastOrDefault(item => item.BattleSessionId == sessionId);
        if (battle != null && string.Equals(battle.Outcome, "in-progress", StringComparison.Ordinal))
        {
            battle.Outcome = "interrupted-restart";
        }
    }

    private static void FinishBattle(ModHookContext context)
    {
        UpdateBattleOutcome(context);
    }

    private static void UpdateBattleOutcome(ModHookContext context)
    {
        var run = current;
        var sessionId = AuraBattleLifecycleRouter.CurrentBattleSessionId;
        var battle = run?.Battles.LastOrDefault(item =>
            item.BattleSessionId == sessionId);
        if (battle == null)
        {
            return;
        }
        var hook = HookName(context);
        if (hook.IndexOf("Win", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            battle.Outcome = "victory";
        }
        else if (hook.IndexOf("Loss", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            battle.Outcome = "defeat";
        }
        else if (hook.IndexOf("Escape", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            battle.Outcome = "abandoned";
        }
    }

    private static void BeforeRewardSelected(ModHookContext context)
    {
        if (context.Target is not CardChoiceUI ui
            || !CardChoiceRefreshNativeApi.IsBattleRewardContext()
            || !CardChoiceRefreshNativeApi.TryGetItems(ui, out var items))
        {
            return;
        }
        var selected = SelectedCardId(context);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }
        pendingReward = new PendingReward
        {
            SelectedCardId = selected,
            OfferedCardIds = CardChoiceRefreshNativeApi.CurrentChoiceIds(items),
            DeckBefore = CaptureDeck()
        };
    }

    private static void AfterRewardSelected(ModHookContext context)
    {
        var pending = pendingReward;
        pendingReward = null;
        if (pending == null || string.IsNullOrWhiteSpace(pending.SelectedCardId))
        {
            return;
        }
        var run = EnsureRun();
        if (run == null)
        {
            return;
        }
        var deckAfter = CaptureDeck();
        if (deckAfter.Count <= pending.DeckBefore.Count
            && !HasAdditionalCopy(
                pending.DeckBefore,
                deckAfter,
                pending.SelectedCardId))
        {
            return;
        }
        run.Rewards.Add(new CombatJourneyRewardTrainingRecord
        {
            RewardIndex = run.Rewards.Count,
            AfterBattleIndex = Math.Max(0, run.Battles.Count - 1),
            StageId = run.Battles.LastOrDefault()?.StageId ?? "",
            OfferedCardIds = pending.OfferedCardIds,
            SelectedCardId = pending.SelectedCardId,
            Skipped = false,
            SelectedBy = "human",
            DeckBefore = pending.DeckBefore,
            DeckAfter = deckAfter
        });
        run.FinalDeck = deckAfter;
        AuraToolsLog.Info(
            "[AutoBattle][JourneyTraining] 已记录战斗奖励：offered="
            + string.Join(",", pending.OfferedCardIds)
            + "，selected="
            + pending.SelectedCardId);
    }

    private static void CompleteAdventure(ModHookContext context, bool settleUnknown)
    {
        if (settlementWritten || current == null)
        {
            return;
        }
        var victory = IsAdventureVictory();
        var defeat = IsAdventureDefeat();
        if (!victory && !defeat && !settleUnknown)
        {
            return;
        }
        current.Complete = victory || defeat;
        current.Outcome = victory ? "victory" : defeat ? "defeat" : "abandoned";
        current.ReachedBoss = victory;
        current.BossVictory = victory;
        current.TerminalReason = victory
            ? "final-boss-victory"
            : defeat
                ? "journey-defeat"
                : "player-exit";
        current.EndedUtc = DateTime.UtcNow;
        current.FinalDeck = CaptureDeck();
        if (!current.Complete)
        {
            WriteJourney(current);
            settlementWritten = true;
            AuraToolsLog.Info(
                "[AutoBattle][JourneyTraining] 冒险未形成终局，已保存为不完整旅程：runId="
                + current.JourneyRunId);
            return;
        }
        WriteJourney(current);
        settlementWritten = true;
        AuraToolsLog.Info(
            "[AutoBattle][JourneyTraining] 已保存完整旅程：runId="
            + current.JourneyRunId
            + "，outcome="
            + current.Outcome
            + "，battles="
            + current.Battles.Count
            + "，rewards="
            + current.Rewards.Count);
    }

    private static void CaptureRoleAndDeck(
        CombatJourneyTrainingEpisode episode,
        bool initial)
    {
        try
        {
            var role = RoleTable.Instance;
            if (role == null)
            {
                return;
            }
            episode.RoleId = ReadConfigId(role.Career);
            var deck = CaptureDeck();
            if (initial)
            {
                episode.InitialDeck = deck;
            }
            episode.FinalDeck = deck;
        }
        catch
        {
        }
    }

    private static List<string> CaptureDeck()
    {
        try
        {
            var role = RoleTable.Instance;
            if (role == null)
            {
                return new List<string>();
            }
            return role.cardList
                .Concat(role.UnCardList)
                .Select(ReadConfigId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string SelectedCardId(ModHookContext context)
    {
        var config = context.Arguments?
            .OfType<IDataConfig>()
            .FirstOrDefault();
        return ReadConfigId(config);
    }

    private static string ReadConfigId(IDataConfig? config)
    {
        if (config?.data != null
            && config.data.TryGetValue("Id", out var id)
            && !string.IsNullOrWhiteSpace(id))
        {
            return id.Trim();
        }
        return "";
    }

    private static bool HasAdditionalCopy(
        IEnumerable<string> before,
        IEnumerable<string> after,
        string cardId)
    {
        return after.Count(id => string.Equals(id, cardId, StringComparison.OrdinalIgnoreCase))
               > before.Count(id => string.Equals(id, cardId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAdventureVictory()
    {
        try
        {
            return MapManager.Instance != null && MapManager.Instance.WinTheGame();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAdventureDefeat()
    {
        try
        {
            return GameExitUI.loss;
        }
        catch
        {
            return false;
        }
    }

    private static string HookName(ModHookContext context)
    {
        try
        {
            return context.Target?.GetType().Name ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void WriteJourney(CombatJourneyTrainingEpisode episode)
    {
        var path = Path.Combine(
            AuraToolsCombatContentRuntime.LiveDatasetDirectory(
                episode.ContentSetHash),
            "journey-episodes-v1.jsonl");
        lock (WriteGate)
        {
            using var writer = new StreamWriter(path, append: true);
            writer.WriteLine(AuraSharedJson.SerializeCompact(episode));
        }
    }

    private sealed class PendingReward
    {
        public string SelectedCardId { get; set; } = "";

        public List<string> OfferedCardIds { get; set; } = new();

        public List<string> DeckBefore { get; set; } = new();
    }
}
