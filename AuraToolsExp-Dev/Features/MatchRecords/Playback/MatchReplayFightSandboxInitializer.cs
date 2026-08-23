using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using Data.Save;
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

            // FightType.Init suppresses enemy action selection during object construction. We
            // instantiate no FightUnit and return to None before control reaches another frame.
            manager.fightType = FightType.Init;

            stage = "create-player-views";
            RoleTable.Instance.SpecialVarMap["ResurrectionCount"] = "0";
            CreatePlayerViews(manager);
            if (FightPlayer.Instance == null)
            {
                throw new InvalidOperationException("Recorded player view was not created.");
            }

            stage = "initialize-card-containers";
            ResetCardRuntimeVariables();

            stage = "create-enemy-views";
            CreateEnemyViews(manager, initialState);

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

    internal static void Activate(FightManager manager)
    {
        if (manager == null)
        {
            throw new InvalidOperationException("Fight runtime is unavailable during replay activation.");
        }

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI")
                      ?? throw new InvalidOperationException("Prepared FightUI is unavailable during replay activation.");
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
    }

    private static void CreatePlayerViews(FightManager manager)
    {
        var background = GameApp.Instance?.NowBackground
                         ?? throw new InvalidOperationException("Recorded battle background is unavailable.");
        var scene = background.transform.Find("com")?.GetComponent<SceneInfo>()
                    ?? throw new InvalidOperationException("Recorded battle scene metadata is unavailable.");
        var orderedIds = manager.roleQueue
            .Select(value => value.InstanceId)
            .Where(manager.TempRoleList.ContainsKey)
            .Concat(manager.TempRoleList.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var count = orderedIds.Count;
        if (count == 0) throw new InvalidOperationException("Recorded player view context is empty.");
        var spacing = count > 1 ? 2.5f : 3.5f;
        var origin = -3.5f;
        Singleton<TempDataManager>.Instance.RoleStatusMap.Clear();
        var localId = RoleTable.Instance.Id;
        if (!orderedIds.Contains(localId, StringComparer.Ordinal)) localId = orderedIds[0];
        for (var index = 0; index < orderedIds.Count; index++)
        {
            var id = orderedIds[index];
            var role = JsonConvert.DeserializeObject<RoleTable>(manager.TempRoleList[id])
                       ?? throw new InvalidOperationException("Recorded role view is invalid: " + id);
            var prefab = ResourceLoader.Load("Model/player");
            var instance = prefab == null ? null : Object.Instantiate(prefab) as GameObject;
            if (instance == null) throw new InvalidOperationException("Native player presentation prefab is unavailable.");
            MatchReplayNativeViewRuntime.OwnPresentationRoot(instance);
            Singleton<TempDataManager>.Instance.RoleStatusMap[id] = new List<string>();
            instance.transform.localScale = Vector3.one;
            FightObject player;
            if (string.Equals(id, localId, StringComparison.Ordinal))
            {
                RoleTable.Instance.ResetFight(role);
                player = instance.AddComponent<FightPlayer>();
                ((FightPlayer)player).Init(id);
            }
            else
            {
                player = instance.AddComponent<OtherPlayer>();
                ((OtherPlayer)player).Init(id);
            }
            var status = instance.GetComponent<StatusManager>()
                         ?? throw new InvalidOperationException(
                             "Native player status component was not created: " + id);
            // FightPlayer.Status resolves through FightManager.statuses. Seed the native
            // dictionary from the concrete component before reading that virtual property,
            // exactly as the native game role loader does.
            var registeredStatus = MatchReplayNativeStatusRegistration.Register(
                manager.statuses,
                id,
                status,
                () => player.Status as StatusManager);
            registeredStatus.animatedState = IStatusManager.AnimatedState.Idle;
            player.InitBound();
            var x = origin + (count - 1 - index - (count - 1) / 2f) * spacing;
            registeredStatus.SetPosition(new Vector3(
                x,
                scene.ground_y - instance.transform.Find("bottom").localPosition.y,
                0f));
        }
        manager.IsRet = false;
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

    private static void CreateEnemyViews(FightManager manager, MatchReplayInitialState initialState)
    {
        var enemyManager = manager.enemyManager
                           ?? throw new InvalidOperationException("Enemy view manager is unavailable.");
        var level = Singleton<GameConfigManager>.Instance.GetOne(DataType.Level, manager.level)
                    ?? throw new InvalidOperationException("Recorded level configuration is unavailable.");

        enemyManager.LevelId = manager.level;
        enemyManager.IndexCount = 0;
        enemyManager.enemyList = new List<Enemy>();
        EnemyManager.enemyCount = 0;
        EnemyManager.levelData = level;
        EnemyManager.SettlementMultiplier = SettlementMultiplier(level);

        var enemies = (initialState.BaselineState?.Statuses ?? new List<MatchReplayStatusState>())
            .Where(status => string.Equals(status.EntityKind, "Enemy", StringComparison.OrdinalIgnoreCase))
            .Where(status => !string.IsNullOrWhiteSpace(status.ContentId))
            .OrderBy(status => status.SlotIndex)
            .ToList();
        if (enemies.Count == 0)
        {
            throw new InvalidOperationException("Recorded replay has no owner-qualified enemy presentation entities.");
        }

        foreach (var recorded in enemies)
        {
            var prefab = ResourceLoader.Load("Model/AncientDragonStatue");
            var instance = prefab == null ? null : Object.Instantiate(prefab) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException("Enemy presentation prefab is unavailable.");
            }
            MatchReplayNativeViewRuntime.OwnPresentationRoot(instance);

            var enemy = instance.AddComponent<Enemy>();
            enemy.Init(
                new DataConfig(recorded.ContentId, DataType.Enemy),
                manager.SumOfEnemyPositive,
                enemyManager.IndexCount,
                manager.EnemyHp);
            var generatedId = enemy.InstanceId;
            manager.statuses.Remove(generatedId);
            enemy.InstanceId = recorded.InstanceId;
            manager.statuses[recorded.InstanceId] = enemy.Status as StatusManager;
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

                ownedStatuses.Add(recorded.InstanceId);
            }
        }

        enemyManager.UpdatePos();
        CreatePartnerViews(manager, initialState);
    }

    private static void CreatePartnerViews(FightManager manager, MatchReplayInitialState initialState)
    {
        var partners = (initialState.BaselineState?.Statuses ?? new List<MatchReplayStatusState>())
            .Where(status => string.Equals(status.EntityKind, "Summon", StringComparison.OrdinalIgnoreCase))
            .Where(status => !string.IsNullOrWhiteSpace(status.ContentId))
            .OrderBy(status => status.SlotIndex)
            .ToList();
        manager.patternManager.PatternList.Clear();
        for (var index = 0; index < partners.Count; index++)
        {
            var recorded = partners[index];
            var prefab = ResourceLoader.Load("Model/AncientDragonStatue");
            var instance = prefab == null ? null : Object.Instantiate(prefab) as GameObject;
            if (instance == null) throw new InvalidOperationException("Partner presentation prefab is unavailable.");
            MatchReplayNativeViewRuntime.OwnPresentationRoot(instance);
            instance.transform.localScale = Vector3.one;
            var partner = instance.AddComponent<Partner>();
            partner.Init(new DataConfig(recorded.ContentId, DataType.FightParent), manager.SumOfEnemyPositive, index);
            if (partner.Status == null) throw new InvalidOperationException("Recorded partner view could not be initialized.");
            var generatedId = partner.InstanceId;
            manager.statuses.Remove(generatedId);
            partner.InstanceId = recorded.InstanceId;
            manager.statuses[recorded.InstanceId] = partner.Status as StatusManager;
            manager.patternManager.PatternList.Add(partner);
        }
        manager.patternManager.UpdatePos();
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
