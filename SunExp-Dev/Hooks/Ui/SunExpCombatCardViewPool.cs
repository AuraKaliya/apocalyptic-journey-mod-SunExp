using System;
using System.Collections.Generic;
using AuraShared.Core;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks.Visual;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.Rendering;
using Witch.Core;
using Witch.Mod;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks.Ui;

public static class SunExpCombatCardViewPool
{
    private const string PoolRootName = "SunExp.CombatCardViewPool";
    private static readonly AuraSharedObjectPool<string, CardItem> Pool =
        new(SunExpPerformanceSettings.CombatCardViewPoolCommonCapacity, IsAlive);
    private static readonly Func<CommonCardItem, (bool, bool, Action)> UseCallback = OnCardUse;
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
        if (!CommonCardItem.UseCallback.Contains(UseCallback))
        {
            CommonCardItem.UseCallback.Add(UseCallback);
        }

        SunExpBattleLifecycleRouter.Register("CombatCardViewPool", new SunExpBattleLifecycleSubscription
        {
            FightStarted = _ => BeginFight(),
            FightEnded = _ => EndFight("FightEnded")
        });
        SunExpLog.InfoAlways("Combat card view pool initialized");
    }

    private static void BeginFight()
    {
        EndFight("BeginFight.Reset");
        generation++;
        EnsurePoolRoot();
        ScheduleWarmup(CombatCardViewPoolCatalog.CommonBucket, SunExpPerformanceSettings.CombatCardViewPoolCommonCapacity);
        ScheduleWarmup(CombatCardViewPoolCatalog.AttackBucket, SunExpPerformanceSettings.CombatCardViewPoolAttackCapacity);
        SunExpPerformanceCounters.Record("CombatCardViewPool.FightStarted");
    }

    private static void EndFight(string source)
    {
        generation++;
        Pool.Clear(DestroyCardView);
        if (poolRoot != null)
        {
            UnityEngine.Object.Destroy(poolRoot.gameObject);
            poolRoot = null;
        }

        SunExpPerformanceCounters.Record("CombatCardViewPool.Cleared");
        SunExpLog.Debug("[CombatCardViewPool] cleared from " + source + ".");
    }

    private static void ScheduleWarmup(string bucket, int capacity)
    {
        var expectedGeneration = generation;
        for (var index = 0; index < Math.Max(0, capacity); index++)
        {
            var capturedIndex = index;
            SunExpFrameScheduler.RunOnceAfterFrames(
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
            || !SunExpPerformanceSettings.CombatCardViewPoolEnabled
            || Pool.Count(bucket) >= Capacity(bucket))
        {
            return;
        }

        var start = SunExpPerformanceCounters.Timestamp();
        var card = CreateCardView(bucket);
        if (card == null)
        {
            SunExpPerformanceCounters.Record("CombatCardViewPool.WarmFailed");
            return;
        }

        PrepareIdle(card, bucket, expectedGeneration);
        if (!Pool.Release(bucket, card))
        {
            DestroyCardView(card);
            SunExpPerformanceCounters.Record("CombatCardViewPool.WarmRejected");
            return;
        }

        SunExpPerformanceCounters.Record("CombatCardViewPool.WarmCreated");
        SunExpPerformanceCounters.RecordDuration("CombatCardViewPool.Warm", start);
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
            SunExpPerformanceCounters.Record("CombatCardViewPool.NativeFallback.Precondition");
            return false;
        }

        if (!Pool.TryAcquire(bucket, out var pooled) || pooled == null)
        {
            SunExpPerformanceCounters.Record("CombatCardViewPool.AcquireMiss");
            return false;
        }

        SunExpPerformanceCounters.Record("CombatCardViewPool.AcquireHit");
        var sideEffectsStarted = false;
        try
        {
            var marker = pooled.GetComponent<PooledCombatCardViewMarker>();
            if (marker == null || marker.Generation != generation)
            {
                DestroyCardView(pooled);
                SunExpPerformanceCounters.Record("CombatCardViewPool.RejectStale");
                return false;
            }

            config.scriptExecutor.Self = FightPlayer.Instance.Status;
            config.scriptExecutor.RunScript("InitScript");
            if (!CombatCardViewPoolCatalog.MatchesInitializedBucket(config, bucket, out var actualBaseScript))
            {
                ReturnUnused(pooled, bucket);
                SunExpPerformanceCounters.Record("CombatCardViewPool.TypeMismatch");
                SunExpLog.Warn("[CombatCardViewPool] native fallback for component mismatch: card="
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
            pooled.Init(config);
            FightUI.cardItemList.Add(pooled);
            fightUi.transform.SetAsFirstSibling();
            FightUiCardLayoutApi.RequestHandLayout(fightUi, "CombatCardViewPool.Materialize");
            Singleton<EventCenter>.Instance.EventTrigger("EndCreateCardItem" + FightPlayer.Instance.Status.InstanceId);
            SunExpPerformanceCounters.Record("CombatCardViewPool.Materialized");
            return true;
        }
        catch (Exception ex)
        {
            DestroyCardView(pooled);
            SunExpPerformanceCounters.Record(sideEffectsStarted
                ? "CombatCardViewPool.MaterializeFailedAfterStart"
                : "CombatCardViewPool.MaterializeFailedSafe");
            SunExpLog.Warn("[CombatCardViewPool] materialize fallback for "
                + CardConfigApi.Id(config)
                + " from "
                + source
                + ": "
                + ex.Message);
            return false;
        }
    }

    private static (bool, bool, Action) OnCardUse(CommonCardItem card)
    {
        var marker = card.GetComponent<PooledCombatCardViewMarker>();
        if (marker == null
            || !marker.InUse
            || marker.ReleasePending
            || marker.Generation != generation)
        {
            return (false, false, null!);
        }

        return (true, false, () => BeginReleaseAfterUse(card, marker));
    }

    private static void BeginReleaseAfterUse(CommonCardItem card, PooledCombatCardViewMarker marker)
    {
        if (card == null || marker == null || marker.ReleasePending)
        {
            return;
        }

        marker.ReleasePending = true;
        marker.ReleaseAttempts = 0;
        FightUI.cardItemList.Remove(card);
        FightUI.WaitCard.Remove(card);
        FightUI.SelectedCard.Remove(card);
        if (FightUI.cardItemList.Count == 0 && FightPlayer.Instance != null)
        {
            Singleton<EventCenter>.Instance.EventTrigger("NoCard" + FightPlayer.Instance.InstanceId);
        }

        try
        {
            if (DictionaryUtil.Get(card.Vars, "HasBurn", "False") != "True")
            {
                if (!FightCardManager.Instance.usedCardList.Contains(card.dataConfig))
                {
                    FightCardManager.Instance.usedCardList.Add(card.dataConfig);
                }

                if (FightManager.Instance != null && FightManager.Instance.fightType != FightType.None)
                {
                    card.RunScript("DropScript");
                }

                card.DataUpdate();
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[CombatCardViewPool] native discard bookkeeping fallback: " + ex.Message);
        }

        HidePendingRelease(card);
        FightUiCardLayoutApi.RequestHandLayout(
            UIManager.Instance?.GetUI<FightUI>("FightUI"),
            "CombatCardViewPool.ReleaseAfterUse");
        ScheduleRelease(card, marker, 1);
    }

    private static void ScheduleRelease(CardItem card, PooledCombatCardViewMarker marker, int delayFrames)
    {
        var expectedGeneration = marker.Generation;
        var instanceId = card.GetInstanceID();
        SunExpFrameScheduler.RunOnceAfterFrames(
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

            SunExpPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedStale");
            return;
        }

        var cardComponents = card.GetComponents<CardItem>();
        if (cardComponents.Length != 1 && marker.ReleaseAttempts < 2)
        {
            marker.ReleaseAttempts++;
            ScheduleRelease(card, marker, 1);
            SunExpPerformanceCounters.Record("CombatCardViewPool.ReleaseDeferredComponentSwap");
            return;
        }

        if (cardComponents.Length != 1
            || !string.Equals(marker.ConfigInstanceId, card.dataConfig?.InstanceID ?? "", StringComparison.Ordinal)
            || card.hasDone)
        {
            DestroyCardView(card);
            SunExpPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedDirty");
            return;
        }

        var bucket = card is AttackCardItem
            ? CombatCardViewPoolCatalog.AttackBucket
            : CombatCardViewPoolCatalog.CommonBucket;
        if (Pool.Count(bucket) >= Capacity(bucket))
        {
            DestroyCardView(card);
            SunExpPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedCapacity");
            return;
        }

        var start = SunExpPerformanceCounters.Timestamp();
        PrepareIdle(card, bucket, expectedGeneration);
        if (!Pool.Release(bucket, card))
        {
            DestroyCardView(card);
            SunExpPerformanceCounters.Record("CombatCardViewPool.ReleaseRejectedPool");
            return;
        }

        SunExpPerformanceCounters.Record("CombatCardViewPool.ReleaseAccepted");
        SunExpPerformanceCounters.RecordDuration("CombatCardViewPool.Reset", start);
    }

    private static CardItem? CreateCardView(string bucket)
    {
        var parent = EnsurePoolRoot();
        var prefab = SunExpResourceCache.Load<GameObject>("UI/CardItem", false, "combat-card-view-pool");
        if (parent == null || prefab == null)
        {
            return null;
        }

        var root = UnityEngine.Object.Instantiate(prefab, parent);
        root.name = "SunExp.PooledCardItem." + bucket;
        var type = bucket == CombatCardViewPoolCatalog.AttackBucket
            ? typeof(AttackCardItem)
            : typeof(CommonCardItem);
        var card = root.AddComponent(type) as CardItem;
        if (card == null)
        {
            UnityEngine.Object.Destroy(root);
            return null;
        }

        var marker = root.AddComponent<PooledCombatCardViewMarker>();
        marker.Bucket = bucket;
        marker.Generation = generation;
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
        marker.InUse = true;
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
    }

    private static void PrepareIdle(CardItem card, string bucket, int expectedGeneration)
    {
        var marker = card.GetComponent<PooledCombatCardViewMarker>();
        if (marker == null)
        {
            marker = card.gameObject.AddComponent<PooledCombatCardViewMarker>();
        }

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
        marker.InUse = false;
        marker.ReleasePending = false;
        marker.ReleaseAttempts = 0;
        card.gameObject.SetActive(false);
    }

    private static void HidePendingRelease(CardItem card)
    {
        card.enabled = false;
        SetInteraction(card, false);
        SetCanvasAlpha(card, 0f);
    }

    private static void StopCardAnimation(CardItem card)
    {
        try
        {
            card.animationController?.StopMove();
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[CombatCardViewPool] StopMove failed: " + ex.Message);
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
            ? SunExpPerformanceSettings.CombatCardViewPoolAttackCapacity
            : SunExpPerformanceSettings.CombatCardViewPoolCommonCapacity;
    }

    private static bool IsAlive(CardItem card)
    {
        return card != null && card.gameObject != null;
    }

    private static void DestroyCardView(CardItem card)
    {
        if (IsAlive(card))
        {
            UnityEngine.Object.Destroy(card.gameObject);
        }
    }
}
