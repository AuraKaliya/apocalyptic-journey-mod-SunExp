using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class SunExpHardTagRuntime
{
    private static readonly object EventOwner = new();
    private static string? registeredPlayerStatusId;

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
        try
        {
            config.AddMethodHookBefore(target, action);
            SunExpLog.Debug("Hook before registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig config, string target, Action<ModHookContext> action)
    {
        try
        {
            config.AddMethodHookAfter(target, action);
            SunExpLog.Debug("Hook after registered: " + target);
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("Hook after failed: " + target + " -> " + ex.Message);
        }
    }

    private static void OnFightStart(ModHookContext context)
    {
        try
        {
            registeredPlayerStatusId = null;
            EventCenter.Instance.Clear(EventOwner);

            if (!HasAnySunExpHardTag())
            {
                return;
            }

            ApplyWhiteRadianceCourtCards();
            ApplyScorchedWorld();
            ApplySunsetExpedition();
            RegisterPlayerRoundListener("Fight_Start.Init");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("SunExp hard tag fight start failed", ex);
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
                ApplyWhiteRadianceCourtCards();
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

        var executor = FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
        if (executor == null)
        {
            FightPlayer.Instance?.Status?.AddBuff(SunExpIds.ScorchingCanopy, level);
            return;
        }

        executor.SetStatus("Self");
        executor.AddBuff(SunExpIds.ScorchingCanopy, level.ToString());
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
        var executor = FightPlayer.Instance?.Status?.MirrorSc as ScriptExecutor;
        var status = FightPlayer.Instance?.Status;
        if (percent > 0 && executor != null && status != null)
        {
            var damage = Math.Max(1, status.CurHp * percent / 100);
            damage = Math.Min(damage, Math.Max(0, status.CurHp - 1));
            if (damage > 0)
            {
                executor.SetStatus("Self");
                executor.ChangeHp((-damage).ToString());
            }
        }

        PlayerApi.SetGameVar(SunExpIds.HardSunsetFightCountKey, (count + 1).ToString());
    }

    private static void ApplyWhiteRadianceCourtCards(ScriptExecutor? executor = null)
    {
        if (!SunExpHardTagState.Active(SunExpHardTagIds.WhiteRadianceCourt))
        {
            return;
        }

        SunExpCardTagService.ApplyWhiteRadianceToRunDeck();
        SunExpCardTagService.ApplyWhiteRadianceToFightZones(executor);
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
