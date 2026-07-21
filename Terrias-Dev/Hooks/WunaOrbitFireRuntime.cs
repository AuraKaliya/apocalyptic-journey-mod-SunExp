using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AuraShared.Core;
using Terrias.Dll.Hooks.Visual;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;

namespace Terrias.Dll.Hooks;

public static class WunaOrbitFireRuntime
{
    private const string LogPrefix = "[WunaOrbitFire]";
    private static readonly HashSet<int> AttachedRendererIds = new();
    private static readonly HashSet<string> LoggedSkips = new(StringComparer.Ordinal);

    public static void Initialize(ModConfig modConfig)
    {
        TerriasStatusLifecycleRouter.Register("WunaOrbitFire", new TerriasStatusLifecycleSubscription
        {
            AfterInitAnimator = AttachFromStatusContext,
            AfterSetSprite = AttachFromStatusContext,
            AfterFightUiFadeIn = AttachFromFightUiContext
        });
        TerriasCombatActionRouter.Register("WunaOrbitFire", new TerriasCombatActionSubscription
        {
            AfterFightUiActionAnimation = AttachFromActionContext
        });
        TerriasLog.Info(LogPrefix + " runtime initialized");
    }

    public static void AttachFromExecutor(IScriptExecutor? executor, string action = "", string source = "executor")
    {
        try
        {
            var status = executor?.Self as StatusManager;
            AttachToStatus(status, action, source);
            if (status == null)
            {
                return;
            }

            TerriasFrameDispatcher.RunOnceNextFrame(LogPrefix + ".attach." + source + ".next", () =>
            {
                AttachToStatus(status, action, source + ":next");
                TerriasFrameDispatcher.RunOnceNextFrame(LogPrefix + ".attach." + source + ".second", () =>
                {
                    AttachToStatus(status, action, source + ":second");
                });
            });
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " executor attach failed from " + source + ": " + ex.Message);
        }
    }

    private static void AttachFromStatusContext(ModHookContext context)
    {
        try
        {
            AttachToStatus(context.Target as StatusManager, "", "StatusManager");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " status attach failed: " + ex.Message);
        }
    }

    private static void AttachFromFightUiContext(ModHookContext context)
    {
        try
        {
            if (!IsCurrentCareerWuna())
            {
                return;
            }

            foreach (var status in StatusesFromFightUi(context.Target))
            {
                AttachToStatus(status, "", "FightUI");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " FightUI attach failed: " + ex.Message);
        }
    }

    private static void AttachFromActionContext(ModHookContext context)
    {
        try
        {
            var executor = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as IScriptExecutor
                : null;
            var action = ReadData(executor?.dataConfig, "Action");
            AttachToStatus(executor?.Self as StatusManager, action, "Action");
        }
        catch (Exception ex)
        {
            TerriasLog.Warn(LogPrefix + " action attach failed: " + ex.Message);
        }
    }

    private static void AttachToStatus(StatusManager? status, string action, string source)
    {
        if (!TerriasPerformanceSettings.WunaOrbitFireEnabled)
        {
            LogSkipOnce("disabled", "skipped from " + source + ": orbit fire is disabled by default.");
            return;
        }

        if (status == null)
        {
            LogSkipOnce(source + ":null-status", "skipped from " + source + ": status is null.");
            return;
        }

        if (!IsWunaStatus(status))
        {
            LogSkipOnce(source + ":not-wuna:" + status.InstanceId, "skipped from " + source + ": status is not recognized as Wuna; instance=" + status.InstanceId);
            return;
        }

        var renderer = FindBodyRenderer(status);
        if (renderer == null)
        {
            LogSkipOnce(source + ":no-renderer:" + status.InstanceId, "skipped from " + source + ": no body SpriteRenderer found for Wuna status; path=" + RendererPath(status.transform));
            return;
        }

        var rendererId = renderer.GetInstanceID();
        var controller = renderer.GetComponentInChildren<WunaOrbitFireController>(true);
        if (controller == null)
        {
            var root = new GameObject("Terrias_WunaOrbitFire");
            root.transform.SetParent(renderer.transform, false);
            controller = root.AddComponent<WunaOrbitFireController>();
            controller.Configure(renderer);
            AttachedRendererIds.Add(rendererId);
            TerriasLog.Info(LogPrefix + " attached to renderer from " + source + ": " + RendererPath(renderer.transform));
        }
        else if (!AttachedRendererIds.Contains(rendererId))
        {
            controller.Configure(renderer);
            AttachedRendererIds.Add(rendererId);
            TerriasLog.Info(LogPrefix + " reconfigured existing controller from " + source + ": " + RendererPath(renderer.transform));
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            controller.BoostForAction(action);
        }
    }

    private static IEnumerable<StatusManager> StatusesFromFightUi(object? target)
    {
        if (target is FightUI fightUi)
        {
            foreach (var status in StatusesFromList(ReadMember(fightUi, "StatusList")))
            {
                yield return status;
            }
        }

        foreach (var status in StatusesFromFightManager())
        {
            yield return status;
        }
    }

    private static IEnumerable<StatusManager> StatusesFromFightManager()
    {
        var statuses = FightManager.Instance?.statuses;
        if (statuses == null)
        {
            yield break;
        }

        foreach (var status in statuses.Values)
        {
            if (status != null)
            {
                yield return status;
            }
        }
    }

    private static IEnumerable<StatusManager> StatusesFromList(object? value)
    {
        if (value is not IEnumerable items)
        {
            yield break;
        }

        foreach (var item in items)
        {
            if (item is StatusManager status)
            {
                yield return status;
            }
        }
    }

    private static SpriteRenderer? FindBodyRenderer(StatusManager status)
    {
        var renderers = new List<SpriteRenderer>();
        AddRenderers(status.transform, renderers);

        if (ReadMember(status, "actionObj") is GameObject[] actionObjects)
        {
            foreach (var actionObject in actionObjects)
            {
                AddRenderers(actionObject?.transform, renderers);
            }
        }

        SpriteRenderer? best = null;
        var bestScore = float.MinValue;
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.sprite == null)
            {
                continue;
            }

            var score = RendererScore(renderer);
            if (score > bestScore)
            {
                best = renderer;
                bestScore = score;
            }
        }

        return best;
    }

    private static void AddRenderers(Transform? root, ICollection<SpriteRenderer> renderers)
    {
        if (root == null)
        {
            return;
        }

        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer != null && !renderers.Contains(renderer))
            {
                renderers.Add(renderer);
            }
        }
    }

    private static float RendererScore(SpriteRenderer renderer)
    {
        var sprite = renderer.sprite;
        var bounds = sprite.bounds;
        var area = Math.Max(0.001f, bounds.size.x * bounds.size.y);
        var path = RendererPath(renderer.transform);
        var name = (sprite.name ?? "") + "|" + path;
        var score = area;

        if (ContainsAny(name, "wuna", "wuna_", "WuNa", "Idle_", "Attack_", "Skill_", "Defend_", "Hit_"))
        {
            score += 1000f;
        }

        if (ContainsAny(path, "effect", "buff", "shadow", "damage", "grave"))
        {
            score -= 500f;
        }

        return score;
    }

    private static bool IsWunaStatus(StatusManager status)
    {
        var fatherObject = status.fatherObject;
        var fatherId = AuraSharedReflection.ReadString(fatherObject, "Id", "id");
        if (IsWunaRoleId(fatherId))
        {
            return true;
        }

        if (!IsCurrentCareerWuna())
        {
            return false;
        }

        if (IsEnemyLikeStatus(status))
        {
            return false;
        }

        var ownerName = (fatherObject?.GetType().Name ?? "") + "|" + (fatherObject?.ToString() ?? "");
        var statusPath = RendererPath(status.transform);
        return ContainsAny(ownerName, "FightPlayer", "Player")
            || ContainsAny(statusPath, "player(Clone)", "/player", "player/");
    }

    private static bool IsEnemyLikeStatus(StatusManager status)
    {
        var path = RendererPath(status.transform);
        var ownerName = (status.fatherObject?.GetType().Name ?? "") + "|" + (status.fatherObject?.ToString() ?? "");
        return ContainsAny(ownerName, "Enemy", "Monster")
            || ContainsAny(path, "enemy", "Enemy", "e0", "e1", "e2", "e3");
    }

    private static bool IsCurrentCareerWuna()
    {
        return IsWunaRoleId(ReadData(RoleTable.Instance?.Career ?? GameEntryUI.career, "Id"));
    }

    private static bool IsWunaRoleId(string value)
    {
        var normalized = TerriasContentIdCompatibility.Canonicalize(
            AuraSharedIdentity.NormalizeRoleId(value).TrimStart('*').Trim());
        return string.Equals(normalized, "wuna", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Terrias_wuna_wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("_wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(":wuna", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".wuna", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void LogSkipOnce(string key, string message)
    {
        if (LoggedSkips.Add(key))
        {
            TerriasLog.Info(LogPrefix + " " + message);
        }
    }

    private static string ReadData(IDataConfig? dataConfig, string key)
    {
        try
        {
            return dataConfig?.data != null && dataConfig.data.TryGetValue(key, out var value)
                ? value ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static object? ReadMember(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return target.GetType().GetProperty(name, flags)?.GetValue(target)
            ?? target.GetType().GetField(name, flags)?.GetValue(target);
    }

    private static string RendererPath(Transform? transform)
    {
        if (transform == null)
        {
            return "";
        }

        var names = new Stack<string>();
        var current = transform;
        while (current != null && names.Count < 8)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names.ToArray());
    }

}
