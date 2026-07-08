using System;
using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace SunExp.Dll.Hooks.Visual;

public static class PolymorphCardFaceRuntime
{
    private static readonly object ReapplySync = new();
    private static string pendingReapplySource = "";
    private static int pendingReapplyCount;

    public static void Initialize(ModConfig modConfig)
    {
        SunExpCardLifecycleRouter.Register("PolymorphCardFace", new SunExpCardLifecycleSubscription
        {
            AfterSetCardStyle = ApplyFromSetCardStyle,
            AfterCardItemInit = context => ApplyFromItemRoot(context, SunExpHookTargets.CardItemInit),
            AfterAttackCardItemInit = context => ApplyFromItemRoot(context, SunExpHookTargets.AttackCardItemInit),
            AfterCardItemDataUpdate = context => ApplyFromItemRoot(context, SunExpHookTargets.CardItemDataUpdate),
            AfterFightUiCreateCardItem = context => RequestActiveCombatCardsReapply(SunExpHookTargets.FightUiCreateCardItem),
            AfterFightUiCreateCardItemInternal = ApplyFromFightUiCreateCardItemInternal,
            AfterScriptExecutorGetCardFromDeck = context => RequestActiveCombatCardsReapply(SunExpHookTargets.ScriptExecutorGetCardFromDeck)
        });
        SunExpLog.Info("Polymorph card face runtime initialized");
    }

    public static void ReapplyActiveCombatCards(string source)
    {
        var start = SunExpPerformanceCounters.Timestamp();
        try
        {
            var applied = 0;
            foreach (var item in FightUI.cardItemList ?? new List<CardItem>())
            {
                if (item?.dataConfig != null && Apply(item.transform, item.dataConfig, source))
                {
                    applied++;
                }
            }

            foreach (var item in FightUI.WaitCard ?? new List<CardItem>())
            {
                if (item?.dataConfig != null && Apply(item.transform, item.dataConfig, source))
                {
                    applied++;
                }
            }

            if (applied > 0)
            {
                SunExpLog.Debug("[PolymorphCardFace] reapplied from " + source + ": " + applied);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Polymorph card face reapply failed from " + source, ex);
        }
        finally
        {
            SunExpPerformanceCounters.RecordDuration("Polymorph.CardFaceReapply", start);
        }
    }

    private static void ApplyFromSetCardStyle(ModHookContext context)
    {
        try
        {
            var args = context.Arguments;
            if (args == null
                || args.Length < 2
                || args[0] is not Transform transform
                || args[1] is not IDataConfig config)
            {
                return;
            }

            ApplySafely(transform, config, "ICard.SetCardStyle");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Polymorph card face SetCardStyle hook failed", ex);
        }
    }

    private static void ApplyFromItemRoot(ModHookContext context, string source)
    {
        try
        {
            if (context.Target is Item item)
            {
                ApplySafely(item.transform, item.dataConfig, source);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Polymorph card face " + source + " hook failed", ex);
        }
    }

    private static void ApplyFromFightUiCreateCardItemInternal(ModHookContext context)
    {
        try
        {
            var config = ConfigFromArgument(context.Arguments, 0);
            if (config != null)
            {
                ApplySafely(FindCombatCardRoot(config), config, "FightUI.CreateCardItemInternal");
                RequestActiveCombatCardsReapply("FightUI.CreateCardItemInternal");
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Polymorph card face FightUI.CreateCardItemInternal hook failed", ex);
        }
    }

    private static IDataConfig? ConfigFromArgument(object[]? args, int index)
    {
        return args == null || index < 0 || args.Length <= index
            ? null
            : CardConfigApi.FromActionPayload(args[index]);
    }

    private static Transform? FindCombatCardRoot(IDataConfig config)
    {
        try
        {
            foreach (var item in FightUI.cardItemList ?? new List<CardItem>())
            {
                if (item != null && ReferenceEquals(item.dataConfig, config))
                {
                    return item.transform;
                }
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("Polymorph card face combat-card lookup failed: " + ex.Message);
        }

        return null;
    }

    private static void RequestActiveCombatCardsReapply(string source)
    {
        lock (ReapplySync)
        {
            pendingReapplySource = source;
            pendingReapplyCount++;
        }

        if (!SunExpFrameScheduler.RunOnceNextFrame("PolymorphCardFaceRuntime.ReapplyActiveCombatCards", FlushActiveCombatCardsReapply))
        {
            SunExpPerformanceCounters.Record("Polymorph.CardFaceReapplyDeduped");
        }
    }

    private static void FlushActiveCombatCardsReapply()
    {
        string source;
        int count;
        lock (ReapplySync)
        {
            source = pendingReapplySource;
            count = pendingReapplyCount;
            pendingReapplySource = "";
            pendingReapplyCount = 0;
        }

        ReapplyActiveCombatCards(count > 1 ? source + ".merged" + count : source + ".merged");
    }

    private static void ApplySafely(Transform? root, IDataConfig? config, string source, bool scheduleDeferred = true)
    {
        if (config == null || !PolymorphCardFaceCache.IsPolymorphRoleCard(config))
        {
            return;
        }

        if (Apply(root, config, source))
        {
            SunExpPerformanceCounters.Record("Polymorph.CardFaceApply");
        }

        if (scheduleDeferred && root != null)
        {
            var key = "PolymorphCardFaceRuntime.Deferred." + source + "." + root.GetInstanceID();
            SunExpFrameScheduler.RunOnceNextFrame(key, () => ApplySafely(root, config, source + ".deferred", scheduleDeferred: false));
        }
    }

    private static bool Apply(Transform? root, IDataConfig? config, string source)
    {
        var visualRoot = FindCardVisualRoot(root);
        if (visualRoot == null)
        {
            return false;
        }

        var asset = PolymorphCardFaceCache.GetOrCreate(config);
        if (asset == null)
        {
            return false;
        }

        var icon = visualRoot.Find("Front/icon");
        if (icon == null)
        {
            return false;
        }

        var changed = false;
        var image = icon.GetComponent<Image>();
        if (image != null)
        {
            changed = !ReferenceEquals(image.sprite, asset.Sprite);
            image.sprite = asset.Sprite;
            image.preserveAspect = false;
            image.color = Color.white;
        }

        var mesh = icon.GetComponent<MeshRenderer>();
        var material = mesh != null ? mesh.material : null;
        if (material != null)
        {
            changed = changed || !ReferenceEquals(material.mainTexture, asset.Texture);
            material.mainTexture = asset.Texture;
        }

        if (changed)
        {
            SunExpLog.Debug("[PolymorphCardFace] applied " + DictionaryUtil.Get(config?.Vars, SunExpIds.PolymorphRoleIdKey) + " from " + source);
        }

        return changed;
    }

    private static Transform? FindCardVisualRoot(Transform? root)
    {
        if (root == null)
        {
            return null;
        }

        if (HasCardVisualNodes(root))
        {
            return root;
        }

        foreach (var path in new[] { "CardItem", "cardItem", "Card", "card", "ShowCard", "DisplayCard", "Item", "Root" })
        {
            var child = root.Find(path);
            if (HasCardVisualNodes(child))
            {
                return child;
            }
        }

        var queue = new Queue<Transform>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited++ < 96)
        {
            var current = queue.Dequeue();
            if (!ReferenceEquals(current, root) && HasCardVisualNodes(current))
            {
                return current;
            }

            for (var i = 0; i < current.childCount; i++)
            {
                queue.Enqueue(current.GetChild(i));
            }
        }

        return root;
    }

    private static bool HasCardVisualNodes(Transform? root)
    {
        return root != null
            && (root.Find("Front/icon") != null || root.Find("Front/FrontBack") != null);
    }
}
