using System;
using System.Reflection;
using AuraSkin.Shared.Infrastructure;
using AuraSkin.Shared.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.UI.Window;

namespace AuraSkin.Shared.Hooks;

public static class SkinUiRuntime
{
    private const string RootName = "AuraSkinRoot";

    public static void OnGameEntryRefresh(ModHookContext context)
    {
        Run("GameEntry refresh", () =>
        {
            if (context.Target is not GameEntryUI entry || !IsNormalMode())
            {
                return;
            }

            SkinRuntime.ScheduleApplyAllKnownSelections();
            var controller = EnsureController(entry);
            controller?.RefreshState();
            ApplyCareerChoiceIcons(entry);
        });
    }

    public static void OnCareerChanged(ModHookContext context)
    {
        Run("career changed", () =>
        {
            if (context.Target is GameEntryUI entry && IsNormalMode())
            {
                var controller = EnsureController(entry);
                controller?.RefreshState();
                controller?.QueueRefresh();
            }
        });
    }

    public static void OnCareerListReady(ModHookContext context)
    {
        Run("career list ready", () =>
        {
            if (context.Target is not GameEntryUI entry || !IsNormalMode())
            {
                return;
            }

            SkinRuntime.ScheduleApplyAllKnownSelections();
            var controller = EnsureController(entry);
            controller?.RefreshState();
            controller?.QueueRefresh();
            ApplyCareerChoiceIcons(entry);
        });
    }

    public static void OnCareerDetailApplied(ModHookContext context)
    {
        Run("career detail", () =>
        {
            if (context.Target is not GameEntryUI entry || context.Arguments == null || context.Arguments.Length < 2)
            {
                return;
            }

            var career = context.Arguments[0] as DataConfig;
            var parent = context.Arguments[1] as Transform;
            ApplyCareerImage(parent, career, false);
            var controller = EnsureController(entry);
            controller?.RefreshState();
            controller?.QueueRefresh();
        });
    }

    public static void OnCareerChoiceItemInitialized(ModHookContext context)
    {
        Run("career choice icon", () =>
        {
            if (context.Target is not UnityEngine.Component item || context.Arguments == null || context.Arguments.Length == 0)
            {
                return;
            }

            ApplyImage(item.transform, "Image", context.Arguments[0] as DataConfig, "ChoiceIcon", true, "", false);
        });
    }

    public static void OnTopBarAvatarChanged(ModHookContext context)
    {
        Run("top bar avatar", () =>
        {
            if (context.Target is UnityEngine.Component component)
            {
                ApplyImage(component.transform, "Content/PlayerStatus/Avatar", RoleTable.Instance?.Career, "Avatar", false, RoleTable.Instance?.Id ?? "", false);
            }
        });
    }

    public static void OnTopStatusChanged(ModHookContext context)
    {
        Run("top status avatar", () =>
        {
            if (context.Target is UnityEngine.Component component && context.Arguments is { Length: > 0 })
            {
                var role = context.Arguments[0] as RoleTable;
                ApplyImage(component.transform, "Avatar", role?.Career, "Avatar", false, role?.Id ?? "", false);
            }
        });
    }

    public static void OnTopStatusCareerChanged(ModHookContext context)
    {
        Run("top status career avatar", () =>
        {
            if (context.Target is UnityEngine.Component component
                && context.Arguments is { Length: > 0 }
                && context.Arguments[0] is StatusUIData data)
            {
                ApplyImage(component.transform, "Avatar", data.career, "Avatar", false, data.instanceId, false);
            }
        });
    }

    public static void OnStatusShown(ModHookContext context)
    {
        Run("status visuals", () =>
        {
            if (context.Target is not UnityEngine.Component component
                || context.Arguments == null
                || context.Arguments.Length == 0
                || context.Arguments[0] is not StatusUIData data)
            {
                return;
            }

            ApplyImage(component.transform, "Content/RoleMsg/Avatar", data.career, "DollIcon", true, data.instanceId, false);
            ApplyImage(component.transform, "Background", data.career, "Character", false, data.instanceId, false);
        });
    }

    public static void RefreshEntryVisuals(GameEntryUI entry, DataConfig career)
    {
        if (entry == null || career == null)
        {
            return;
        }

        SkinRuntime.EnsureAnimation(career);
        var careerWindow = FindCareerWindow(entry);
        var careerId = SkinRuntime.CareerId(career);
        ApplyCareerImage(careerWindow, career, true);
        ApplyCareerChoiceIcons(entry, careerId);

        foreach (var animatorRole in entry.GetComponentsInChildren<AnimatorRole>(true))
        {
            if (animatorRole == null
                || animatorRole.dataConfig == null
                || !string.Equals(SkinRuntime.CareerId(animatorRole.dataConfig), careerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isDetailRole = careerWindow != null && animatorRole.transform.IsChildOf(careerWindow);
            animatorRole.Init(animatorRole.dataConfig, animatorRole.InstanceId, true, animatorRole.NeeDYOffset);
            if (isDetailRole)
            {
                var scale = animatorRole.transform.localScale;
                animatorRole.transform.localScale = new Vector3(-scale.x, scale.y, scale.z);
            }
        }
    }

    private static SkinPanelController? EnsureController(GameEntryUI entry)
    {
        if (!SkinRuntime.EntryPanelEnabled)
        {
            return null;
        }

        foreach (var existingController in entry.GetComponentsInChildren<SkinPanelController>(true))
        {
            if (existingController != null && string.Equals(existingController.name, RootName, StringComparison.Ordinal))
            {
                var parent = FindCareerWindow(entry) ?? entry.transform;
                if (existingController.transform.parent != parent)
                {
                    existingController.transform.SetParent(parent, false);
                }

                existingController.transform.SetAsLastSibling();
                return existingController;
            }
        }

        var root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(FindCareerWindow(entry) ?? entry.transform, false);
        root.transform.SetAsLastSibling();
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        var controller = root.AddComponent<SkinPanelController>();
        controller.Initialize(entry);
        return controller;
    }

    private static Transform? FindCareerWindow(GameEntryUI entry)
    {
        if (entry == null)
        {
            return null;
        }

        return entry.careerChoiceParent ?? entry.transform.Find("Window Manager/Windows/职业选择");
    }

    private static void ApplyCareerChoiceIcons(GameEntryUI entry, string refreshCareerId = "")
    {
        foreach (var showCareer in entry.showCareers)
        {
            if (showCareer != null)
            {
                var applyDefault = !string.IsNullOrWhiteSpace(refreshCareerId)
                                   && string.Equals(
                                       SkinRuntime.CareerId(showCareer.dataConfig),
                                       refreshCareerId,
                                       StringComparison.OrdinalIgnoreCase);
                ApplyImage(showCareer.transform, "Image", showCareer.dataConfig, "ChoiceIcon", true, "", applyDefault);
            }
        }
    }

    private static void ApplyCareerImage(Transform? root, DataConfig? career, bool fallbackToDefault)
    {
        var image = root?.Find("RoleBack")?.GetComponent<Image>();
        if (image != null)
        {
            image.preserveAspect = true;
        }

        ApplyImage(root, "RoleBack", career, "CareerImage", false, "", fallbackToDefault);
    }

    private static void ApplyImage(
        Transform? root,
        string childPath,
        DataConfig? career,
        string field,
        bool nativeSize,
        string instanceId,
        bool fallbackToDefault)
    {
        if (root == null || career == null)
        {
            return;
        }

        var target = string.IsNullOrWhiteSpace(childPath) ? root : root.Find(childPath);
        var image = target?.GetComponent<Image>();
        var sprite = fallbackToDefault
            ? SkinRuntime.LoadSprite(career, field, instanceId)
            : SkinRuntime.LoadSelectedSprite(career, field, instanceId);
        if (image == null || sprite == null)
        {
            return;
        }

        image.sprite = sprite;
        if (nativeSize)
        {
            image.SetNativeSize();
        }
    }

    private static bool IsNormalMode()
    {
        try
        {
            var save = GameEntryUI.selectedSave;
            var modeType = save?.GetType()
                .GetField("modeType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(save) as string;
            if (!string.IsNullOrWhiteSpace(modeType)
                && !string.Equals(modeType, "normal", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var modeManager = MapManager.Instance?.ModeMapManager;
            return modeManager == null
                   || string.Equals(modeManager.GetType().Name, "NormalMapManager", StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    private static void Run(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            SkinLog.Error(step + " failed", ex);
        }
    }
}
