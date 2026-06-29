using System;
using System.Collections.Generic;
using System.Linq;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks;

public static class SunExpHardTagRuntime
{
    private static readonly object EventOwner = new();
    private static string? registeredPlayerStatusId;
    private static bool whiteRadianceRunDeckApplied;
    private static int whiteRadianceLastFightZoneSignature = -1;

    public static void Initialize(ModConfig modConfig)
    {
        RegisterAfter(modConfig, "Fight_Start.Init", OnFightStart);
        RegisterAfter(modConfig, "Fight_PlayerTurn.Init", OnPlayerTurn);
        RegisterAfter(modConfig, "Enemy.Init", OnEnemyInit);
        RegisterBefore(modConfig, "OtherObj.DoOneAction", OnEnemyDoOneAction);
        RegisterBefore(modConfig, "CommonCardItem.TrueUse", OnCardUse);
        RegisterBefore(modConfig, "AttackCardItem.TrueUse", OnCardUse);
        SunExpLog.Info("SunExp hard tag runtime initialized");
    }

    public static List<DataConfig> SelectedRuntimeHardTags()
    {
        var result = new List<DataConfig>();
        try
        {
            var entries = Singleton<GameRuntimeData>.Instance?.HardTags;
            if (entries == null)
            {
                return result;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var id = DictionaryUtil.Get(entry.Data, "Id");
                if (string.IsNullOrWhiteSpace(id) || entry.DynamicValue <= 0)
                {
                    continue;
                }

                for (var i = 0; i < entry.DynamicValue; i++)
                {
                    result.Add(new DataConfig(id, DataType.Hard));
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Failed to capture selected hard tags: " + ex.Message);
        }

        return result;
    }

    private static void RegisterBefore(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterBefore(config, target, action, SunExpLog.Debug, SunExpLog.Warn);
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        AuraSharedHooks.RegisterAfter(config, target, action, SunExpLog.Debug, SunExpLog.Warn);
    }

    private static void OnFightStart(ModHookContext context)
    {
        try
        {
            registeredPlayerStatusId = null;
            ResetWhiteRadianceScanState();
            EventCenter.Instance.Clear(EventOwner);

            if (!HasAnySunExpHardTag())
            {
                return;
            }

            RunFightStartStep("WhiteRadianceCourt", () => ApplyWhiteRadianceCourtCards(force: true));
            RunFightStartStep("ScorchedWorld", ApplyScorchedWorld);
            RunFightStartStep("SunsetExpedition", ApplySunsetExpedition);
            RunFightStartStep("BlackSunListener", () => RegisterPlayerRoundListener("Fight_Start.Init"));
        }
        catch (Exception ex)
        {
            SunExpLog.Error("SunExp hard tag fight start failed", ex);
        }
    }

    private static void RunFightStartStep(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SunExpLog.Error("SunExp hard tag fight-start step failed: " + name, ex);
        }
    }

    private static void OnPlayerTurn(ModHookContext context)
    {
        try
        {
            if (!HasAnySunExpHardTag())
            {
                return;
            }

            ApplyWhiteRadianceCourtCards();
            RegisterPlayerRoundListener("Fight_PlayerTurn.Init");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("SunExp hard tag player turn failed", ex);
        }
    }

    private static void OnEnemyInit(ModHookContext context)
    {
        try
        {
            if (!HasAnySunExpHardTag() || !IsServerAuthority())
            {
                return;
            }

            var enemy = context.Target as Enemy;
            var status = enemy?.Status;
            if (status == null)
            {
                return;
            }

            if (SunExpHardTagState.Active(SunExpHardTagIds.Rebirth))
            {
                status.AddBuff(SunExpHardTagIds.RebirthBuff, 50);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("SunExp hard tag enemy init failed", ex);
        }
    }

    private static void OnEnemyDoOneAction(ModHookContext context)
    {
        try
        {
            if (!SunExpHardTagState.Active(SunExpHardTagIds.WhiteRadianceCourt)
                || !IsServerAuthority()
                || context.Target is not Enemy enemy
                || enemy.Status == null
                || enemy.Status.state != IStatusManager.State.Default
                || enemy.Status.CurHp <= 0)
            {
                return;
            }

            enemy.Status.AddBuff(SunExpIds.SolarRadiance, 1);
        }
        catch (Exception ex)
        {
            SunExpLog.Error("SunExp white court enemy action failed", ex);
        }
    }

    private static void OnCardUse(ModHookContext context)
    {
        try
        {
            if (SunExpHardTagState.Active(SunExpHardTagIds.WhiteRadianceCourt))
            {
                ApplyWhiteRadianceCourtCards(includeRunDeck: false);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("SunExp white court card use scan failed", ex);
        }
    }

    private static void RegisterPlayerRoundListener(string source)
    {
        if (!SunExpHardTagState.Active(SunExpHardTagIds.BlackSunCalamity))
        {
            return;
        }

        var player = FightPlayer.Instance;
        var status = player?.Status;
        var executor = status?.MirrorSc as ScriptExecutor;
        var statusId = status?.InstanceId;
        if (string.IsNullOrWhiteSpace(statusId) || executor == null || registeredPlayerStatusId == statusId)
        {
            return;
        }

        EventCenter.Instance.AddEventListener("StartRound" + statusId, new Action(() => OnLocalPlayerStartRound(executor)), EventOwner, EventDispose.OnFightEnd);
        registeredPlayerStatusId = statusId;
        SunExpLog.Info("Registered black sun player StartRound listener from " + source + ": statusId=" + statusId);
    }

    private static void OnLocalPlayerStartRound(ScriptExecutor executor)
    {
        if (!SunExpHardTagState.Active(SunExpHardTagIds.BlackSunCalamity))
        {
            return;
        }

        ApplyWhiteRadianceCourtCards(executor);
        var statusId = PlayerApi.LocalPlayerStatusId();
        var key = string.IsNullOrWhiteSpace(statusId)
            ? "SunExpHard_BlackSun_LocalTurnCount"
            : "SunExpHard_BlackSun_TurnCount_" + statusId;
        var count = ExecutorApi.CombatIntAdd(key, 1);
        if (count % 5 == 0)
        {
            AnnihilateRandomLocalCard(executor);
        }
    }

    private static void ApplyScorchedWorld()
    {
        var level = Math.Max(0, Math.Min(4, SunExpHardTagState.Level(SunExpHardTagIds.ScorchedWorld)));
        if (level <= 0)
        {
            return;
        }

        var status = FightPlayer.Instance?.Status;
        if (status == null)
        {
            return;
        }

        status.AddBuff(SunExpIds.ScorchingCanopy, level);
        var executor = status.MirrorSc as ScriptExecutor;
        ExecutorApi.SyncFieldStacks(executor, SunExpFieldId.ScorchingCanopy);
    }

    private static void ApplySunsetExpedition()
    {
        if (!SunExpHardTagState.Active(SunExpHardTagIds.SunsetExpedition))
        {
            return;
        }

        var count = Math.Max(0, DictionaryUtil.ParseInt(PlayerApi.GetGameVar(SunExpIds.HardSunsetFightCountKey, "0")));
        var percent = Math.Min(50, count);
        var status = FightPlayer.Instance?.Status;
        if (percent > 0 && status != null && status.CurHp > 1)
        {
            var oldHp = status.CurHp;
            var damage = Math.Max(1, oldHp * percent / 100);
            var nextHp = Math.Max(1, oldHp - damage);
            if (nextHp < oldHp)
            {
                status.CurHp = nextHp;
                SunExpLog.Info("[SunsetExpedition] count="
                    + count
                    + "; percent="
                    + percent
                    + "; hp="
                    + oldHp
                    + "->"
                    + nextHp
                    + "; statusId="
                    + status.InstanceId);
            }
        }

        if (IsServerAuthority())
        {
            PlayerApi.SetGameVar(SunExpIds.HardSunsetFightCountKey, (count + 1).ToString());
        }
    }

    private static void ApplyWhiteRadianceCourtCards(ScriptExecutor? executor = null, bool force = false, bool includeRunDeck = true)
    {
        if (!SunExpHardTagState.Active(SunExpHardTagIds.WhiteRadianceCourt))
        {
            return;
        }

        if (includeRunDeck && (force || !whiteRadianceRunDeckApplied))
        {
            SunExpCardTagService.ApplyWhiteRadianceToRunDeck();
            whiteRadianceRunDeckApplied = true;
        }

        var signature = WhiteRadianceFightZoneSignature(executor);
        if (!force && signature == whiteRadianceLastFightZoneSignature)
        {
            return;
        }

        SunExpCardTagService.ApplyWhiteRadianceToFightZones(executor);
        whiteRadianceLastFightZoneSignature = signature;
    }

    private static void ResetWhiteRadianceScanState()
    {
        whiteRadianceRunDeckApplied = false;
        whiteRadianceLastFightZoneSignature = -1;
    }

    private static int WhiteRadianceFightZoneSignature(ScriptExecutor? executor)
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (FightUI.cardItemList?.Count ?? 0);
            hash = hash * 31 + (FightCardManager.Instance?.cardList?.Count ?? 0);
            hash = hash * 31 + (FightCardManager.Instance?.usedCardList?.Count ?? 0);
            hash = hash * 31 + (executor?.HandCard?.Count ?? 0);
            hash = hash * 31 + (executor?.DeckCard?.Count ?? 0);
            hash = hash * 31 + (executor?.UsedCard?.Count ?? 0);
            return hash;
        }
    }

    private static void AnnihilateRandomLocalCard(ScriptExecutor executor)
    {
        var pool = BuildAnnihilationPool(executor);
        if (pool.Count <= 0)
        {
            return;
        }

        var index = UnityEngine.Random.Range(0, pool.Count);
        executor.BurnCardByData(pool[index]);
    }

    private static List<IDataConfig> BuildAnnihilationPool(ScriptExecutor executor)
    {
        var cards = new List<IDataConfig>();
        foreach (var card in executor.HandCard ?? Enumerable.Empty<CardItem>())
        {
            if (card?.dataConfig != null)
            {
                cards.Add(card.dataConfig);
            }
        }

        cards.AddRange((executor.DeckCard ?? new List<DataConfig>()).Where(card => card != null));
        cards.AddRange((executor.UsedCard ?? new List<DataConfig>()).Where(card => card != null));
        return cards;
    }

    private static bool IsServerAuthority()
    {
        return PlayerManager.Instance == null || PlayerManager.Instance.isServer;
    }

    private static bool HasAnySunExpHardTag()
    {
        return SunExpHardTagState.Active(SunExpHardTagIds.ScorchedWorld)
            || SunExpHardTagState.Active(SunExpHardTagIds.BlackSunCalamity)
            || SunExpHardTagState.Active(SunExpHardTagIds.WhiteRadianceCourt)
            || SunExpHardTagState.Active(SunExpHardTagIds.SunsetExpedition)
            || SunExpHardTagState.Active(SunExpHardTagIds.Rebirth);
    }
}
