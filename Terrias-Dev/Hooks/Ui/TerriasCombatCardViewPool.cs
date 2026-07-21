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
    private static int generation;
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
        ScheduleWarmup(CombatCardViewPoolCatalog.CommonBucket, TerriasPerformanceSettings.CombatCardViewPoolCommonCapacity);
        ScheduleWarmup(CombatCardViewPoolCatalog.AttackBucket, TerriasPerformanceSettings.CombatCardViewPoolAttackCapacity);
        TerriasPerformanceCounters.Record("CombatCardViewPool.FightStarted");
    }

    private static void EndFight(string source)
    {
        generation++;
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

    private static void ScheduleWarmup(string bucket, int capacity)
    {
        var expectedGeneration = generation;
        for (var index = 0; index < Math.Max(0, capacity); index++)
        {
            var capturedIndex = index;
            TerriasFrameScheduler.RunOnceAfterFrames(
                "CombatCardViewPool.Warm." + expectedGeneration + "." + bucket + "." + capturedIndex,
                1 + capturedIndex,
                () => WarmOne(expectedGeneration, bucket),
                AuraSharedFramePhase.Background,
                priority: 20,
                estimatedCost: 2);
        }
    }

    private static void WarmOne(int expectedGeneration, string bucket)
    {
        if (expectedGeneration != generation
            || !TerriasPerformanceSettings.CombatCardViewPoolEnabled
            || Pool.Count(bucket) >= Capacity(bucket))
        {
            return;
        }

        var start = TerriasPerformanceCounters.Timestamp();
        var card = CreateCardView(bucket);
        if (card == null)
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.WarmFailed");
            return;
        }

        PrepareIdle(card, bucket, expectedGeneration);
        if (!Pool.Release(bucket, card))
        {
            DestroyCardView(card);
            TerriasPerformanceCounters.Record("CombatCardViewPool.WarmRejected");
            return;
        }

        TerriasPerformanceCounters.Record("CombatCardViewPool.WarmCreated");
        TerriasPerformanceCounters.RecordDuration("CombatCardViewPool.Warm", start);
    }

    private static bool TryMaterialize(ScriptExecutor self, DataConfig config, string source)
    {
        if (!CombatCardViewPoolCatalog.TryResolveBucket(config, out var bucket))
        {
            return false;
        }

        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        if (fightUi?.cardContainer == null
            || FightUI.cardItemList.Count + fightUi.createCardQueue.Count >= fightUi.CardTopCount)
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.NativeFallback.Precondition");
            return false;
        }

        if (!Pool.TryAcquire(bucket, out var pooled) || pooled == null)
        {
            TerriasPerformanceCounters.Record("CombatCardViewPool.AcquireMiss");
            return false;
        }

        TerriasPerformanceCounters.Record("CombatCardViewPool.AcquireHit");
        var sideEffectsStarted = false;
        try
        {
            var marker = pooled.GetComponent<PooledCombatCardViewMarker>();
            if (marker == null || marker.Generation != generation)
            {
                DestroyCardView(pooled);
                TerriasPerformanceCounters.Record("CombatCardViewPool.RejectStale");
                return false;
            }

            config.scriptExecutor.Self = FightPlayer.Instance.Status;
            config.scriptExecutor.RunScript("InitScript");
            if (!CombatCardViewPoolCatalog.MatchesInitializedBucket(config, bucket, out var actualBaseScript))
            {
                ReturnUnused(pooled, bucket);
                TerriasPerformanceCounters.Record("CombatCardViewPool.TypeMismatch");
                TerriasLog.Warn("[CombatCardViewPool] native fallback for component mismatch: card="
                    + CardConfigApi.Id(config)
                    + ", expected="
                    + bucket
                    + ", actual="
                    + actualBaseScript);
                return false;
            }

            FightCardManager.Instance.cardList.Remove(config);
            FightCardManager.Instance.usedCardList.Remove(config);
            AudioManager.Instance?.PlayEffect("NewSounds/卡牌与事件/抽牌");
            sideEffectsStarted = true;
            Singleton<EventCenter>.Instance.EventTrigger(
                "CreateInt" + FightPlayer.Instance.InstanceId,
                new CreateData(config, FightPlayer.Instance.InstanceId));

            ActivateForUse(pooled, marker, bucket, config, fightUi);
            var presentationSignature = CombatCardViewPoolCatalog.PresentationSignature(config, bucket);
            if (!TryLightweightRebind(pooled, marker, config, presentationSignature))
            {
                var initStart = TerriasPerformanceCounters.Timestamp();
                pooled.Init(config);
                TerriasPerformanceCounters.RecordDuration("CombatCardViewPool.FullInit", initStart);
                marker.HasInitializedPresentation = true;
                marker.PresentationSignature = presentationSignature;
            }

            var exitAnimator = pooled.GetComponent<PooledCardExitAnimator>()
                ?? pooled.gameObject.AddComponent<PooledCardExitAnimator>();
            exitAnimator.RefreshTextBindings(pooled.transform);
            FightUI.cardItemList.Add(pooled);
            fightUi.transform.SetAsFirstSibling();
            FightUiCardLayoutApi.RequestHandLayout(fightUi, "CombatCardViewPool.Materialize");
            Singleton<EventCenter>.Instance.EventTrigger("EndCreateCardItem" + FightPlayer.Instance.Status.InstanceId);
            TerriasPerformanceCounters.Record("CombatCardViewPool.Materialized");
            return true;
        }
        catch (Exception ex)
        {
            DestroyCardView(pooled);
            TerriasPerformanceCounters.Record(sideEffectsStarted
                ? "CombatCardViewPool.MaterializeFailedAfterStart"
                : "CombatCardViewPool.MaterializeFailedSafe");
            TerriasLog.Warn("[CombatCardViewPool] materialize fallback for "
                + CardConfigApi.Id(config)
                + " from "
                + source
                + ": "
                + ex.Message);
            return false;
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

        var bucket = card is AttackCardItem
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
            ICard.SetCardMsg(card.transform, config, null);
            card.DataUpdate();
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
}
