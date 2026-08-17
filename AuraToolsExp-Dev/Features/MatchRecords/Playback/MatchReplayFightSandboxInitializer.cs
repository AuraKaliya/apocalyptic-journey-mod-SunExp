using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using Data.Save;
using Mirror;
using Newtonsoft.Json;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Builds only the native objects required to present recorded state. This adapter must not
/// enter FightManager.Init/FightInit.Init: those paths execute career, relic, blessing, enemy,
/// network-ready, and turn logic that belongs to a real battle rather than a replay view.
/// </summary>
internal static class MatchReplayFightSandboxInitializer
{
    internal static string Initialize(FightManager manager, MatchReplayInitialState initialState)
    {
        if (manager == null)
        {
            throw new InvalidOperationException("Fight runtime is unavailable.");
        }

        if (initialState == null)
        {
            throw new ArgumentNullException(nameof(initialState));
        }

        if (!NetworkServer.active || !NetworkClient.active || !NetworkClient.isConnected)
        {
            throw new InvalidOperationException(
                "The dedicated replay host is not connected; local view initialization is unsafe.");
        }

        var stage = "preflight";
        try
        {
            stage = "reset-passive-runtime";
            ResetPassiveRuntime(manager, initialState);

            stage = "show-fight-ui";
            var fightUi = WitchUiManager.Instance?.ShowUI<FightUI>("FightUI")
                          ?? throw new InvalidOperationException("FightUI could not be created.");
            fightUi.gameObject.SetActive(false);
            fightUi.Init();
            manager.ResetWaitCount();

            // FightType.Init suppresses enemy action selection during object construction. We
            // instantiate no FightUnit and return to None before control reaches another frame.
            manager.fightType = FightType.Init;

            stage = "create-player-views";
            RoleTable.Instance.SpecialVarMap["ResurrectionCount"] = "0";
            new FightInit().RpcLoadRoles();
            if (FightPlayer.Instance == null)
            {
                throw new InvalidOperationException("Recorded player view was not created.");
            }

            stage = "initialize-card-containers";
            ResetCardRuntimeVariables();
            FightCardManager.Instance.Init();

            stage = "create-enemy-views";
            CreateEnemyViews(manager);

            stage = "activate-fight-ui";
            if (GameApp.Instance?.NowBackground != null)
            {
                GameApp.Instance.NowBackground.transform.SetAsLastSibling();
            }

            fightUi.StatusList = manager.statuses.Values
                .Where(status => status != null)
                .ToList();
            fightUi.gameObject.SetActive(true);
            FightUI.IsReset = false;
            fightUi.ResetButtonCheck();
            MatchReplaySkillPresenter.Initialize(fightUi);

            if (manager.statuses.Count == 0)
            {
                throw new InvalidOperationException("No passive status views were created.");
            }

            return MatchReplayViewBootstrapContract.Describe();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Passive replay view initialization failed at " + stage + ": " + ex.Message,
                ex);
        }
        finally
        {
            // A replay owns presentation objects, never a running FightUnit or action queue.
            manager.fightType = FightType.None;
            manager.StopAllCoroutines();
            manager.ActionQueue?.Clear();
        }
    }

    private static void ResetPassiveRuntime(FightManager manager, MatchReplayInitialState initialState)
    {
        var roleTable = RoleTable.Instance
                        ?? throw new InvalidOperationException("Replay role table is unavailable.");
        _ = MapManager.Instance
            ?? throw new InvalidOperationException("Replay map context is unavailable.");
        var roleQueueJson = Decode(initialState.RoleQueue, "role queue");
        var roles = JsonConvert.DeserializeObject<List<FightManager.RoleData>>(roleQueueJson)
                    ?? throw new InvalidOperationException("Recorded role queue is invalid.");
        if (roles.Count == 0)
        {
            throw new InvalidOperationException("Recorded role queue is empty.");
        }

        var temporaryRolesJson = Decode(initialState.TemporaryRoles, "temporary roles");
        var temporaryRoles = string.IsNullOrWhiteSpace(temporaryRolesJson)
            ? new Dictionary<string, string>()
            : JsonConvert.DeserializeObject<Dictionary<string, string>>(temporaryRolesJson)
              ?? new Dictionary<string, string>();
        if (temporaryRoles.Count == 0
            && !string.IsNullOrWhiteSpace(roleTable.Id))
        {
            // Single-player recordings from builds that emitted an empty temporary-role blob
            // still carry the complete immutable role snapshot.
            temporaryRoles[roleTable.Id] = initialState.RoleTableJson;
        }

        if (temporaryRoles.Count == 0)
        {
            throw new InvalidOperationException("Recorded player view context is empty.");
        }

        manager.IsFake = true;
        manager.IsRet = false;
        manager.fightType = FightType.None;
        manager.StopAllCoroutines();
        manager.SumOfEnemyPositive = initialState.EnemyPositive;
        manager.EnemyHp = initialState.EnemyHp;
        manager.level = initialState.LevelId;
        manager.roleQueue = roles;
        manager.TempRoleList = temporaryRoles;
        manager.eventList.Clear();
        manager.targetList.Clear();
        manager.statusData.Clear();
        manager.ActionQueue.Clear();
        manager.statuses.Clear();
        manager.patternManager.Reset();
        manager.enemyManager.enemyList.Clear();

        // Dice.Default is a new cursor-zero value. Recorded state is authoritative, so the
        // passive view must not inherit or consume the adventure cursor.
        var dice = Dice.Default;
        manager.CheckDice = new ScriptExecutor.DiceWrapper(dice.WithType("Check"));
        manager.DefaultDice = new ScriptExecutor.DiceWrapper(dice);
        manager.TempVarsMap = new Dictionary<string, int>(roleTable.VarsMap);
    }

    private static string Decode(byte[] bytes, string label)
    {
        if (bytes == null || bytes.Length == 0)
        {
            throw new InvalidOperationException("Recorded " + label + " payload is missing.");
        }

        try
        {
            return GZip.DecompressToString(bytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Recorded " + label + " payload cannot be decoded.", ex);
        }
    }

    private static void ResetCardRuntimeVariables()
    {
        foreach (var card in RoleTable.Instance.cardList.Concat(RoleTable.Instance.UnCardList))
        {
            card.Vars["ThisCount"] = "0";
            card.Vars["ExCost"] = "0";
            card.Vars["OnceExCost"] = "0";
            card.Vars["SpecialTag"] = "";
            if (RoleTable.Instance.enchasedDict.TryGetValue(card.InstanceID, out var enchanted)
                && enchanted != null)
            {
                enchanted.Vars["ThisCount"] = "0";
            }
        }
    }

    private static void CreateEnemyViews(FightManager manager)
    {
        var enemyManager = manager.enemyManager
                           ?? throw new InvalidOperationException("Enemy view manager is unavailable.");
        var level = Singleton<GameConfigManager>.Instance.GetOne(DataType.Level, manager.level)
                    ?? throw new InvalidOperationException("Recorded level configuration is unavailable.");
        if (!level.TryGetValue("EnemyIds", out var enemyIdsText))
        {
            throw new InvalidOperationException("Recorded level has no enemy presentation list.");
        }

        enemyManager.LevelId = manager.level;
        enemyManager.IndexCount = 0;
        enemyManager.enemyList = new List<Enemy>();
        EnemyManager.enemyCount = 0;
        EnemyManager.levelData = level;
        EnemyManager.SettlementMultiplier = SettlementMultiplier(level);

        var enemyIds = enemyIdsText.Replace(" ", "")
            .Split(',')
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if (enemyIds.Count == 0)
        {
            throw new InvalidOperationException("Recorded level enemy presentation list is empty.");
        }

        foreach (var enemyId in enemyIds)
        {
            var prefab = ResourceLoader.Load("Model/AncientDragonStatue");
            var instance = prefab == null ? null : Object.Instantiate(prefab) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Enemy presentation prefab is unavailable.");
            }

            var enemy = instance.AddComponent<Enemy>();
            enemy.Init(
                new DataConfig(enemyId, DataType.Enemy),
                manager.SumOfEnemyPositive,
                enemyManager.IndexCount,
                manager.EnemyHp);
            enemyManager.enemyList.Add(enemy);
            enemyManager.IndexCount++;
            EnemyManager.enemyCount++;

            var ownerId = RoleTable.Instance.Id;
            if (!string.IsNullOrWhiteSpace(ownerId))
            {
                if (!Singleton<TempDataManager>.Instance.RoleStatusMap.TryGetValue(ownerId, out var ownedStatuses))
                {
                    ownedStatuses = new List<string>();
                    Singleton<TempDataManager>.Instance.RoleStatusMap[ownerId] = ownedStatuses;
                }

                ownedStatuses.Add(enemy.Status.InstanceId);
            }
        }

        enemyManager.UpdatePos();
    }

    private static int SettlementMultiplier(IReadOnlyDictionary<string, string> level)
    {
        level.TryGetValue("Note", out var note);
        if ((note ?? "").Contains("无名")) return 4;
        if ((note ?? "").Contains("boss")) return 3;
        if ((note ?? "").Contains("精英")) return 2;
        return 1;
    }
}
