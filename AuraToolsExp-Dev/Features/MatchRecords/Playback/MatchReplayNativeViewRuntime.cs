using System.Collections.Generic;
using System.Linq;
using AuraSkin.Shared.Mechanics;
using AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;
using UnityEngine;
using Witch.Core;
using Witch.UI.Window;
using Object = UnityEngine.Object;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

internal static class MatchReplayNativeViewRuntime
{
    private static FightManager? previousFightManager;
    private static FightManager? replayFightManager;
    private static GameObject? managerRoot;
    private static List<DataConfig> previousDraw = new();
    private static List<DataConfig> previousDiscard = new();
    private static List<DataConfig> previousNascent = new();
    private static List<DataConfig> previousFightCards = new();
    private static readonly List<System.IDisposable> skinScopes = new();
    private static readonly List<GameObject> ownedPresentationRoots = new();
    private static readonly List<GameObject> retiringRoots = new();
    private static readonly HashSet<int> retiringRootIds = new();

    internal static FightManager Create()
    {
        if (replayFightManager != null) return replayFightManager;
        if (retiringRoots.Any(root => root != null))
        {
            throw new System.InvalidOperationException(
                "The previous replay still owns native presentation objects.");
        }
        previousFightManager = FightManager.Instance;
        CaptureCardState();
        managerRoot = new GameObject("AuraToolsReplayNativeFightManager");
        Object.DontDestroyOnLoad(managerRoot);
        replayFightManager = managerRoot.AddComponent<FightManager>();
        replayFightManager.IsFake = true;
        replayFightManager.fightType = FightType.None;
        return replayFightManager;
    }

    internal static void OwnPresentationRoot(GameObject root)
    {
        if (root == null || ownedPresentationRoots.Contains(root))
        {
            return;
        }

        ownedPresentationRoots.Add(root);
    }

    internal static void ApplySkinSelections(IEnumerable<ReplayScopedSkinSelectionV11>? selections)
    {
        foreach (var scope in skinScopes) scope.Dispose();
        skinScopes.Clear();
        foreach (var selection in selections ?? Enumerable.Empty<ReplayScopedSkinSelectionV11>())
        {
            var handle = SkinRuntime.PushScopedSelection(
                "AuraToolsExp.ReplayV11",
                selection.CareerId,
                selection.InstanceId,
                selection.QualifiedSkinId);
            skinScopes.Add(handle);
        }
    }

    internal static void Dispose()
    {
        MatchReplayOutcomePresenter.Clear();
        MatchReplayCardStateCapture.Reset();
        if (replayFightManager != null)
        {
            foreach (var status in replayFightManager.statuses?.Values.ToArray() ?? System.Array.Empty<StatusManager>())
            {
                var owner = status?.fatherObject;
                if (owner != null && owner.gameObject != null) OwnPresentationRoot(owner.gameObject);
            }
            replayFightManager.statuses?.Clear();
            replayFightManager.enemyManager?.enemyList?.Clear();
            replayFightManager.ActionQueue?.Clear();
            replayFightManager.eventList?.Clear();
            replayFightManager.targetList?.Clear();
        }

        var fightUi = WitchUiManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi != null && fightUi.gameObject != null)
        {
            MatchReplayUiLifecycle.ForceDestroyRoot(
                fightUi.gameObject,
                "Match replay native view dispose");
        }

        foreach (var root in ownedPresentationRoots.Where(root => root != null).Distinct().ToArray())
        {
            retiringRootIds.Add(root.GetInstanceID());
            retiringRoots.Add(root);
            MatchReplayTweenCleanup.KillTree(root);
            root.SetActive(false);
            Object.Destroy(root);
        }
        ownedPresentationRoots.Clear();

        if (managerRoot != null)
        {
            retiringRootIds.Add(managerRoot.GetInstanceID());
            retiringRoots.Add(managerRoot);
            managerRoot.SetActive(false);
            Object.Destroy(managerRoot);
        }
        FightManager.Instance = previousFightManager;
        foreach (var scope in skinScopes) scope.Dispose();
        skinScopes.Clear();
        replayFightManager = null;
        previousFightManager = null;
        managerRoot = null;
        RestoreCardState();
    }

    internal static void CompleteDispose()
    {
        var alive = retiringRoots
            .Where(root => root != null)
            .Select(root => root.name + "#" + root.GetInstanceID())
            .ToArray();
        var replayPlayer = FightPlayer.Instance;
        var replayPlayerAlive = replayPlayer != null
                                && replayPlayer.gameObject != null
                                && retiringRootIds.Contains(replayPlayer.gameObject.GetInstanceID());
        retiringRoots.Clear();
        retiringRootIds.Clear();
        if (alive.Length > 0 || replayPlayerAlive)
        {
            throw new System.InvalidOperationException(
                "Replay native object teardown did not reach its terminal state: roots="
                + (alive.Length == 0 ? "none" : string.Join("|", alive))
                + ", replayPlayerAlive=" + replayPlayerAlive + ".");
        }
    }

    private static void CaptureCardState()
    {
        var cards = FightCardManager.Instance;
        previousDraw = cards?.cardList?.ToList() ?? new List<DataConfig>();
        previousDiscard = cards?.usedCardList?.ToList() ?? new List<DataConfig>();
        previousNascent = cards?.nascentList?.ToList() ?? new List<DataConfig>();
        previousFightCards = cards?.FightcardList?.ToList() ?? new List<DataConfig>();
    }

    private static void RestoreCardState()
    {
        var cards = FightCardManager.Instance;
        if (cards == null) return;
        cards.cardList.Clear();
        foreach (var card in previousDraw) cards.cardList.Add(card);
        cards.usedCardList.Clear();
        foreach (var card in previousDiscard) cards.usedCardList.Add(card);
        cards.nascentList.Clear();
        cards.nascentList.AddRange(previousNascent);
        cards.FightcardList.Clear();
        cards.FightcardList.AddRange(previousFightCards);
        previousDraw.Clear();
        previousDiscard.Clear();
        previousNascent.Clear();
        previousFightCards.Clear();
        FightUI.cardItemList?.Clear();
        FightUI.WaitCard?.Clear();
        FightUI.SelectedCard?.Clear();
    }
}
