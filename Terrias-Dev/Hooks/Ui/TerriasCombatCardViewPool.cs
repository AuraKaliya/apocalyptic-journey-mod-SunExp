using System;
using System.Collections.Generic;
using AuraShared.Core;
using DG.Tweening;
using Terrias.Dll.GameApi;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.Rendering;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks.Ui;

public static class TerriasCombatCardViewPool
{
    private const string PoolRootName = "Terrias.CombatCardViewPool";
    private static readonly AuraSharedObjectPool<string, CardItem> Pool =
        new(TerriasPerformanceSettings.CombatCardViewPoolCommonCapacity, IsAlive);
    private static readonly HashSet<CardItem> ActiveViews = new();
    private static readonly Queue<PendingMaterialization> Pending = new();
    private static readonly HashSet<string> PendingIds = new(StringComparer.Ordinal);
    private const int MaxNativeQueueWaitFrames = 360;
    private static int generation;
    private static int nativeQueueWaitFrames;
    private static int materializedSinceLayout;
    private static Transform? poolRoot;
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        CombatCardViewPoolApi.Register(TryMaterialize);
        TerriasHookRegistry.Before(modConfig, TerriasHookTargets.CardItemEffectOfBurnCard,
            context => SuppressNativeExitVisual(context, PooledCardExitKind.Burn), "CombatCardViewPool");
        TerriasHookRegistry.After(modConfig, TerriasHookTargets.CardItemEffectOfBurnCard,
            CompleteNativeExitVisual, "CombatCardViewPool");
        TerriasHookRegistry.Before(modConfig, TerriasHookTargets.CardItemEffectOfThrowCard,
            context => SuppressNativeExitVisual(context, ThrowExitKind(context)), "CombatCardViewPool");
        TerriasHookRegistry.After(modConfig, TerriasHookTargets.CardItemEffectOfThrowCard,
            CompleteNativeExitVisual, "CombatCardViewPool");

        TerriasBattleLifecycleRouter.Register("CombatCardViewPool", new TerriasBattleLifecycleSubscription
        {
            FightStarted = _ => BeginFight(),
            FightEnded = _ => EndFight("FightEnded")
        });
        TerriasLog.InfoAlways("Combat card view pool initialized");
    }

    private static void BeginFight()
    {
        EndFight("BeginFight.Reset");
        generation++;
        EnsurePoolRoot();
        TerriasPerformanceCounters.Record("CombatCardViewPool.FightStarted");
    }

    private static void EndFight(string source)
    {
        generation++;
        Pending.Clear();
        PendingIds.Clear();
        nativeQueueWaitFrames = 0;
        materializedSinceLayout = 0;
        foreach (var active in new List<CardItem>(ActiveViews))
        {
            DestroyCardView(active);
        }

        ActiveViews.Clear();
        Pool.Clear(DestroyCardView);
        if (poolRoot != null)
        {
            UnityEngine.Object.Destroy(poolRoot.gameObject);
            poolRoot = null;
        }

        TerriasPerformanceCounters.Record("CombatCardViewPool.Cleared");
        TerriasLog.Debug("[CombatCardViewPool] cleared from " + source + ".");
    }

    private static bool TryMaterialize(ScriptExecutor self, DataConfig config, string source)
    {
        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi?.cardContainer == null
            || FightUI.cardItemList.Count + fightUi.createCardQueue.Count + Pending.Count >= fightUi.CardTopCount)
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.NativeFallback.Precondition");
            return false;
        }

        var instanceId = string.IsNullOrWhiteSpace(config.InstanceID)
            ? config.GetHashCode().ToString()
            : config.InstanceID;
        if (!PendingIds.Add(instanceId))
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.MaterializeDeduplicated");
            return true;
        }

        Pending.Enqueue(new PendingMaterialization(generation, self, config, source, instanceId));
        ScheduleDrain();
        TerriasPerformanceCounters.Record("CombatCardViewPool.MaterializeQueued");
        return true;
    }

    private static void ScheduleDrain()
    {
        AuraSharedFrameScheduler.RunOnceNextFrame(new AuraSharedFrameActionRequest
        {
            OwnerId = TerriasIds.ModId,
            Key = "CombatCardViewPool.Drain",
            Source = "Terrias.CombatCardViewPool.Drain",
            Phase = AuraSharedFramePhase.Presentation,
            Priority = 160,
            EstimatedCost = 6,
            Action = DrainPending
        });
    }

    private static void DrainPending()
    {
        if (Pending.Count == 0)
        {
            nativeQueueWaitFrames = 0;
            FinishMaterializationBatch(UIManager.Instance?.GetUI<FightUI>("FightUI"));
            return;
        }

        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null)
        {
            FallbackAllPending("FightUI unavailable");
            return;
        }

        if (fightUi.createCardQueue.Count > 0 && nativeQueueWaitFrames < MaxNativeQueueWaitFrames)
        {
            nativeQueueWaitFrames++;
            TerriasPerformanceCounters.Record("CombatCardViewPool.WaitedForNativeQueue");
            ScheduleDrain();
            return;
        }

        if (fightUi.createCardQueue.Count > 0)
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.NativeQueueTimeout");
            FallbackAllPending("native queue did not settle before pool deadline");
            FinishMaterializationBatch(fightUi);
            return;
        }

        nativeQueueWaitFrames = 0;
        var request = Pending.Dequeue();
        PendingIds.Remove(request.InstanceId);
        if (request.Generation == generation && MaterializeNow(request, fightUi))
        {
            materializedSinceLayout++;
        }

        if (Pending.Count > 0)
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.MaterializeSliceContinued");
            ScheduleDrain();
            return;
        }

        FinishMaterializationBatch(fightUi);
    }

    private static void FinishMaterializationBatch(FightUI? fightUi)
    {
        if (fightUi == null || materializedSinceLayout <= 0)
        {
            materializedSinceLayout = 0;
            return;
        }

        AuditPooledHandViews();
        fightUi.transform.SetAsFirstSibling();
        FightUiCardLayoutApi.RequestHandLayout(fightUi, "CombatCardViewPool.BatchMaterialize");
        TerriasPerformanceCounters.Record("CombatCardViewPool.BatchLayoutRequested");
        materializedSinceLayout = 0;
    }

    private static bool MaterializeNow(PendingMaterialization request, FightUI fightUi)
    {
        var self = request.Executor;
        var config = request.Config;
        CardItem? card = null;
        var commitStarted = false;
        try
        {
            if (FightUI.cardItemList.Count + fightUi.createCardQueue.Count >= fightUi.CardTopCount)
            {
                NativeFallback(request, "hand capacity reached");
                return false;
            }

            config.scriptExecutor.Self = FightPlayer.Instance.Status;
            config.scriptExecutor.RunScript("InitScript");
            if (!CombatCardViewPoolCatalog.TryResolveInitializedBucket(config, out var bucket))
            {
                NativeFallback(request, "unsupported BaseScript=" + DictionaryUtil.Get(config.Vars, "BaseScript"));
                return false;
            }

            if (!Pool.TryAcquire(bucket, out card) || card == null)
            {
                TerriasPerformanceCounters.Record("CombatCardViewPool.AcquireMiss");
                card = CreateCardView(bucket);
            }
            else
            {
                TerriasPerformanceCounters.Record("CombatCardViewPool.AcquireHit");
            }

            if (card == null)
            {
                NativeFallback(request, "view construction failed");
                return false;
            }

            var marker = card.GetComponent<PooledCombatCardViewMarker>();
            if (marker == null || marker.Generation != generation)
            {
                DestroyCardView(card);
                NativeFallback(request, "stale lease");
                TerriasPerformanceCounters.Record("CombatCardViewPool.RejectStale");
                return false;
            }

            FightCardManager.Instance.cardList.Remove(config);
            FightCardManager.Instance.usedCardList.Remove(config);
            AudioManager.Instance?.PlayEffect("NewSounds/卡牌与事件/抽牌");
            commitStarted = true;
            Singleton<EventCenter>.Instance.EventTrigger(
                "CreateInt" + FightPlayer.Instance.InstanceId,
                new CreateData(config, FightPlayer.Instance.InstanceId));

            ActivateForUse(card, marker, bucket, config, fightUi);
            var presentationSignature = CombatCardViewPoolCatalog.PresentationSignature(config, bucket);
            if (!TryLightweightRebind(card, marker, config, presentationSignature))
            {
                var initStart = TerriasPerformanceCounters.Timestamp();
                card.Init(config);
                TerriasPerformanceCounters.RecordDuration("CombatCardViewPool.FullInit", initStart);
                marker.HasInitializedPresentation = true;
                marker.PresentationSignature = presentationSignature;
                AuraCardPresentationDelta.Rebind(card.transform);
            }

            ReapplyPresentationAfterBind(card, config);

            var exitAnimator = card.GetComponent<PooledCardExitAnimator>()
                ?? card.gameObject.AddComponent<PooledCardExitAnimator>();
            exitAnimator.RefreshTextBindings(card.transform);
            if (!FightUI.cardItemList.Contains(card))
            {
                FightUI.cardItemList.Add(card);
            }
            Singleton<EventCenter>.Instance.EventTrigger("EndCreateCardItem" + FightPlayer.Instance.Status.InstanceId);
            TerriasPerformanceCounters.Record("CombatCardViewPool.Materialized");
            return true;
        }
        catch (Exception ex)
        {
            if (card != null)
            {
                DestroyCardView(card);
            }

            TerriasPerformanceCounters.Record(commitStarted
                ? "CombatCardViewPool.MaterializeFailedAfterStart"
                : "CombatCardViewPool.MaterializeFailedSafe");
            TerriasLog.Warn("[CombatCardViewPool] materialize failed for "
                + CardConfigApi.Id(config)
                + " from "
                + request.Source
                + ": "
                + ex.Message);
            if (!commitStarted)
            {
                NativeFallback(request, ex.Message);
            }
            else
            {
                RepairCommittedFailure(config);
            }

            return false;
        }
    }

    private static void NativeFallback(PendingMaterialization request, string reason)
    {
        try
        {
            request.Executor.GetCardFromDeck(request.Config);
            TerriasPerformanceCounters.Record("CombatCardViewPool.NativeFallback.Deferred");
            FightUiCardLayoutApi.RequestCurrentHandLayout(
                "CombatCardViewPool.DeferredNativeFallback:" + request.Source);
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CombatCardViewPool] deferred native fallback failed: card="
                + CardConfigApi.Id(request.Config)
                + ", reason="
                + reason
                + ", error="
                + ex.Message);
        }
    }

    private static void FallbackAllPending(string reason)
    {
        while (Pending.Count > 0)
        {
            var request = Pending.Dequeue();
            PendingIds.Remove(request.InstanceId);
            NativeFallback(request, reason);
        }
    }

    private static void RepairCommittedFailure(DataConfig config)
    {
        try
        {
            if (!FightCardManager.Instance.cardList.Contains(config))
            {
                FightCardManager.Instance.cardList.Add(config);
            }

            TerriasPerformanceCounters.Record("CombatCardViewPool.CommitFailureRepaired");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn("[CombatCardViewPool] committed failure repair failed: " + ex.Message);
        }
    }

    private static void SuppressNativeExitVisual(ModHookContext context, PooledCardExitKind kind)
    {
        if (context.Target is not CardItem card)
        {
            return;
        }

        var marker = card.GetComponent<PooledCombatCardViewMarker>();
        if (marker == null || marker.Generation != generation || marker.ReleasePending)
        {
            return;
        }

        if (!marker.TryTransition(PooledCardViewState.Bound, PooledCardViewState.NativeVisualSuppressed))
        {
            // Re-entrant native visual calls must never regain ownership of a pooled root.
            card.cardcontainer = null;
            TerriasPerformanceCounters.Record("CombatCardViewPool.InvalidExitTransition");
            return;
        }

        marker.SuppressedCardContainer = card.cardcontainer;
        marker.PendingExitKind = kind;
        marker.PendingExitTargetPath = ExitTargetPath(context);
        card.cardcontainer = null;
        TerriasPerformanceCounters.Record("CombatCardViewPool.NativeVisualSuppressed");
    }

    private static void CompleteNativeExitVisual(ModHookContext context)
    {
        if (context.Target is not CardItem card)
        {
            return;
        }

        var marker = card.GetComponent<PooledCombatCardViewMarker>();
        if (marker == null || marker.State != PooledCardViewState.NativeVisualSuppressed)
        {
            return;
        }

        card.cardcontainer = marker.SuppressedCardContainer;
        marker.SuppressedCardContainer = null;
        if (marker.Generation != generation
            || !marker.TryTransition(PooledCardViewState.NativeVisualSuppressed, PooledCardViewState.Exiting))
        {
            DestroyCardView(card);
            TerriasPerformanceCounters.Record("CombatCardViewPool.ExitRejectedStale");
            return;
        }

        if (marker.PendingExitKind == PooledCardExitKind.Unsupported)
        {
            DestroyCardView(card);
            TerriasPerformanceCounters.Record("CombatCardViewPool.UnsupportedExitDestroyed");
            return;
        }

        marker.ReleasePending = true;
        marker.ReleaseAttempts = 0;
        StopContainerTween(card);
        card.ignore = true;
        card.hasDone = true;
        card.enabled = false;
        FightUI.cardItemList.Remove(card);
        FightUI.WaitCard.Remove(card);
        FightUI.SelectedCard.Remove(card);
        SetInteraction(card, false);
        if (marker.PendingExitKind == PooledCardExitKind.MoveToDiscard
            || marker.PendingExitKind == PooledCardExitKind.MoveToDrawPile)
        {
            FightUiCardLayoutApi.RequestHandLayout(
                UIManager.Instance?.GetUI<FightUI>("FightUI"),
                "CombatCardViewPool.NativeMoveExit");
        }

        var animator = card.GetComponent<PooledCardExitAnimator>()
            ?? card.gameObject.AddComponent<PooledCardExitAnimator>();
        if (!animator.Play(
                card,
                marker.PendingExitKind,
                marker.PendingExitTargetPath,
                () => ScheduleRelease(card, marker, 1)))
        {
            DestroyCardView(card);
            TerriasPerformanceCounters.Record("CombatCardViewPool.ExitAnimationUnavailable");
            return;
        }

        TerriasPerformanceCounters.Record("CombatCardViewPool.ExitAnimationStarted." + marker.PendingExitKind);
    }

    private static void ScheduleRelease(CardItem card, PooledCombatCardViewMarker marker, int delayFrames)
    {
        var expectedGeneration = marker.Generation;
        var instanceId = card.GetInstanceID();
        TerriasFrameScheduler.RunOnceAfterFrames(
            "CombatCardViewPool.Release." + expectedGeneration + "." + instanceId + "." + marker.ReleaseAttempts,
            Math.Max(1, delayFrames),
            () => ReleaseNow(card, marker, expectedGeneration),
            AuraSharedFramePhase.Presentation,
            priority: 120,
            estimatedCost: 2);
    }

    private static void ReturnUnused(CardItem card, string bucket)
    {
        if (!Pool.Release(bucket, card))
        {
            DestroyCardView(card);
        }
    }

    private static void ReleaseNow(CardItem card, PooledCombatCardViewMarker marker, int expectedGeneration)
    {
        if (!IsAlive(card) || marker == null || expectedGeneration != generation || marker.Generation != generation)
        {
            if (IsAlive(card))
            {
                DestroyCardView(card);
            }

            TerriasPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedStale");
            return;
        }

        var cardComponents = card.GetComponents<CardItem>();
        if (cardComponents.Length != 1 && marker.ReleaseAttempts < 2)
        {
            marker.ReleaseAttempts++;
            ScheduleRelease(card, marker, 1);
            TerriasPerformanceCounters.Record("CombatCardViewPool.ReleaseDeferredComponentSwap");
            return;
        }

        if (cardComponents.Length != 1
            || !string.Equals(marker.ConfigInstanceId, card.dataConfig?.InstanceID ?? "", StringComparison.Ordinal)
            || marker.State != PooledCardViewState.Exiting)
        {
            DestroyCardView(card);
            TerriasPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedDirty");
            return;
        }

        var cardType = card.GetType();
        if (cardType != typeof(AttackCardItem) && cardType != typeof(CommonCardItem))
        {
            DestroyCardView(card);
            TerriasPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedSubclass");
            return;
        }

        var bucket = cardType == typeof(AttackCardItem)
            ? CombatCardViewPoolCatalog.AttackBucket
            : CombatCardViewPoolCatalog.CommonBucket;
        if (Pool.Count(bucket) >= Capacity(bucket))
        {
            DestroyCardView(card);
            TerriasPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedCapacity");
            return;
        }

        var start = TerriasPerformanceCounters.Timestamp();
        PrepareIdle(card, bucket, expectedGeneration);
        if (!Pool.Release(bucket, card))
        {
            DestroyCardView(card);
            TerriasPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedPool");
            return;
        }

        TerriasPerformanceCounters.Record("CombatCardViewPool.ReleaseAccepted");
        TerriasPerformanceCounters.RecordDuration("CombatCardViewPool.Reset", start);
    }

    private static CardItem? CreateCardView(string bucket)
    {
        var totalStart = TerriasPerformanceCounters.Timestamp();
        var segment = totalStart;
        var parent = EnsurePoolRoot();
        var rootMilliseconds = TerriasPerformanceCounters.ElapsedMilliseconds(segment);
        segment = TerriasPerformanceCounters.Timestamp();
        var prefab = TerriasResourceCache.Load<GameObject>("UI/CardItem", false, "combat-card-view-pool");
        var prefabLoadMilliseconds = TerriasPerformanceCounters.ElapsedMilliseconds(segment);
        if (parent == null || prefab == null)
        {
            return null;
        }

        segment = TerriasPerformanceCounters.Timestamp();
        var root = UnityEngine.Object.Instantiate(prefab, parent);
        var instantiateMilliseconds = TerriasPerformanceCounters.ElapsedMilliseconds(segment);
        root.name = "Terrias.PooledCardItem." + bucket;
        var type = bucket == CombatCardViewPoolCatalog.AttackBucket
            ? typeof(AttackCardItem)
            : typeof(CommonCardItem);
        segment = TerriasPerformanceCounters.Timestamp();
        var card = root.AddComponent(type) as CardItem;
        var addComponentMilliseconds = TerriasPerformanceCounters.ElapsedMilliseconds(segment);
        if (card == null)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        segment = TerriasPerformanceCounters.Timestamp();
        var marker = root.AddComponent<PooledCombatCardViewMarker>();
        marker.Bucket = bucket;
        marker.Generation = generation;
        var markerMilliseconds = TerriasPerformanceCounters.ElapsedMilliseconds(segment);
        var totalMilliseconds = TerriasPerformanceCounters.ElapsedMilliseconds(totalStart);
        CombatCardViewConstructionDiagnostics.Record(
            bucket,
            rootMilliseconds,
            prefabLoadMilliseconds,
            instantiateMilliseconds,
            addComponentMilliseconds,
            markerMilliseconds,
            totalMilliseconds);
        TerriasPerformanceCounters.RecordDuration("CombatCardViewConstruction.Total", totalStart);
        return card;
    }

    private static Transform? EnsurePoolRoot()
    {
        if (poolRoot != null)
        {
            return poolRoot;
        }

        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi == null)
        {
            return null;
        }

        var existing = fightUi.transform.Find(PoolRootName);
        if (existing != null)
        {
            poolRoot = existing;
            return poolRoot;
        }

        var root = new GameObject(PoolRootName, typeof(RectTransform));
        root.transform.SetParent(fightUi.transform, false);
        root.transform.SetAsFirstSibling();
        poolRoot = root.transform;
        return poolRoot;
    }

    private static void ActivateForUse(
        CardItem card,
        PooledCombatCardViewMarker marker,
        string bucket,
        DataConfig config,
        FightUI fightUi)
    {
        marker.Generation = generation;
        marker.Bucket = bucket;
        marker.ConfigInstanceId = config.InstanceID ?? "";
        marker.ForceState(PooledCardViewState.Bound);
        marker.ReleasePending = false;
        marker.ReleaseAttempts = 0;
        StopCardAnimation(card);
        card.StopAllCoroutines();
        card.transform.SetParent(fightUi.cardContainer.transform, false);
        card.gameObject.SetActive(true);
        card.enabled = true;
        card.hasUse = false;
        card.hasDone = false;
        card.ignore = false;
        card.draging = false;
        card.isReverse = false;
        card.cardcontainer = fightUi.cardContainer;
        card.selectContainer = fightUi.selectCardContainer;
        var rect = card.GetComponent<RectTransform>();
        rect.anchoredPosition = new Vector2(-1500f, fightUi.Card_y_position);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one * card.initScale;
        SetInteraction(card, true);
        SetCanvasAlpha(card, 1f);
        ActiveViews.Add(card);
    }

    private static void PrepareIdle(CardItem card, string bucket, int expectedGeneration)
    {
        var marker = card.GetComponent<PooledCombatCardViewMarker>();
        if (marker == null)
        {
            marker = card.gameObject.AddComponent<PooledCombatCardViewMarker>();
        }

        marker.ForceState(PooledCardViewState.Resetting);
        card.GetComponent<PooledCardExitAnimator>()?.ResetVisual();
        FightUI.cardItemList.Remove(card);
        FightUI.WaitCard.Remove(card);
        FightUI.SelectedCard.Remove(card);
        StopCardAnimation(card);
        card.StopAllCoroutines();
        card.enabled = false;
        card.hasUse = false;
        card.hasDone = false;
        card.ignore = false;
        card.draging = false;
        card.isReverse = false;
        SetInteraction(card, false);
        SetCanvasAlpha(card, 1f);
        var skinMarker = CardPresentationRootResolver.FindCardVisualRoot(card.transform)
            ?.GetComponent<CardVisualSkinMarker>();
        skinMarker?.ClearAllVisualOverrides();
        var parent = EnsurePoolRoot();
        if (parent != null)
        {
            card.transform.SetParent(parent, false);
        }

        marker.Generation = expectedGeneration;
        marker.Bucket = bucket;
        marker.ConfigInstanceId = "";
        marker.ReleasePending = false;
        marker.ReleaseAttempts = 0;
        marker.SuppressedCardContainer = null;
        marker.PendingExitKind = PooledCardExitKind.Unsupported;
        marker.PendingExitTargetPath = "";
        marker.ForceState(PooledCardViewState.Idle);
        ActiveViews.Remove(card);
        card.gameObject.SetActive(false);
    }

    private static bool TryLightweightRebind(
        CardItem card,
        PooledCombatCardViewMarker marker,
        DataConfig config,
        string presentationSignature)
    {
        if (!marker.HasInitializedPresentation
            || presentationSignature.Length == 0
            || !string.Equals(marker.PresentationSignature, presentationSignature, StringComparison.Ordinal))
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.LightRebind.SignatureMiss");
            return false;
        }

        var start = TerriasPerformanceCounters.Timestamp();
        try
        {
            card.dataConfig = config;
            var costUpdated = AuraCardPresentationDelta.TrySetCost(
                card.transform,
                CardConfigApi.NativeDisplayCost(config, FightPlayer.Instance?.Status).ToString());
            var descriptionUpdated = AuraCardPresentationDelta.TrySetDescription(
                card.transform,
                config.Description());
            if (!costUpdated || !descriptionUpdated)
            {
                TerriasPerformanceCounters.Record("CombatCardViewPool.LightRebind.DeltaMiss");
                return false;
            }

            marker.PresentationSignature = presentationSignature;
            TerriasPerformanceCounters.Record("CombatCardViewPool.LightRebind.Applied");
            return true;
        }
        catch (Exception ex)
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.LightRebind.Fallback");
            TerriasLog.Debug("[CombatCardViewPool] lightweight rebind fell back to full Init: " + ex.Message);
            return false;
        }
        finally
        {
            TerriasPerformanceCounters.RecordDuration("CombatCardViewPool.LightRebind", start);
        }
    }

    private static void ReapplyPresentationAfterBind(CardItem card, DataConfig config)
    {
        TerriasCardPresentationRouter.RequestApply(new TerriasCardPresentationContext
        {
            Root = card.transform,
            Config = config,
            Card = card,
            Source = "CombatCardViewPool.Bind",
            Surface = TerriasCardPresentationSurface.CombatCardInternal
        });
        TerriasPerformanceCounters.Record("CombatCardViewPool.PresentationReapply");
    }

    private static void AuditPooledHandViews()
    {
        for (var index = FightUI.cardItemList.Count - 1; index >= 0; index--)
        {
            var card = FightUI.cardItemList[index];
            if (card == null)
            {
                FightUI.cardItemList.RemoveAt(index);
                continue;
            }

            var marker = card.GetComponent<PooledCombatCardViewMarker>();
            if (marker == null)
            {
                continue;
            }

            if (marker.Generation == generation && marker.State == PooledCardViewState.Bound)
            {
                continue;
            }

            FightUI.cardItemList.RemoveAt(index);
            TerriasPerformanceCounters.Record("CombatCardViewPool.HandAuditRemovedInvalidLease");
            if (marker.Generation != generation)
            {
                DestroyCardView(card);
            }
        }
    }

    private static PooledCardExitKind ThrowExitKind(ModHookContext context)
    {
        return PooledCardViewExit.ClassifyThrowTarget(ExitTargetPath(context));
    }

    private static string ExitTargetPath(ModHookContext context)
    {
        return context.Arguments != null && context.Arguments.Length > 0
            ? context.Arguments[0] as string ?? ""
            : "";
    }

    private static void StopCardAnimation(CardItem card)
    {
        try
        {
            card.animationController?.StopMove();
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[CombatCardViewPool] StopMove failed: " + ex.Message);
        }
    }

    private static void StopContainerTween(CardItem card)
    {
        try
        {
            var container = card.cardcontainer;
            if (container != null && container.cardTweenDict.TryGetValue(card, out var tween))
            {
                tween?.Kill();
                container.cardTweenDict.Remove(card);
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Debug("[CombatCardViewPool] container tween cleanup failed: " + ex.Message);
        }
    }

    private static void SetInteraction(CardItem card, bool enabled)
    {
        var group = card.GetComponent<ObjectGroup>();
        if (group != null)
        {
            group.blocksRaycasts = enabled;
        }

        var trigger = card.transform.Find("Trigger");
        if (trigger != null)
        {
            trigger.gameObject.SetActive(enabled);
        }
    }

    private static void SetCanvasAlpha(CardItem card, float alpha)
    {
        var group = card.GetComponent<CanvasGroup>() ?? card.gameObject.AddComponent<CanvasGroup>();
        group.alpha = alpha;
        group.blocksRaycasts = alpha > 0f;
        group.interactable = alpha > 0f;
    }

    private static int Capacity(string bucket)
    {
        return bucket == CombatCardViewPoolCatalog.AttackBucket
            ? TerriasPerformanceSettings.CombatCardViewPoolAttackCapacity
            : TerriasPerformanceSettings.CombatCardViewPoolCommonCapacity;
    }

    private static bool IsAlive(CardItem card)
    {
        return card != null && card.gameObject != null;
    }

    private static void DestroyCardView(CardItem card)
    {
        if (IsAlive(card))
        {
            ActiveViews.Remove(card);
            UnityEngine.Object.Destroy(card.gameObject);
        }
    }

    private readonly struct PendingMaterialization
    {
        public PendingMaterialization(
            int generation,
            ScriptExecutor executor,
            DataConfig config,
            string source,
            string instanceId)
        {
            Generation = generation;
            Executor = executor;
            Config = config;
            Source = source ?? "";
            InstanceId = instanceId ?? "";
        }

        public int Generation { get; }
        public ScriptExecutor Executor { get; }
        public DataConfig Config { get; }
        public string Source { get; }
        public string InstanceId { get; }
    }
}
