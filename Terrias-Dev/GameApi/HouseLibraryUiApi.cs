using System;
using System.Reflection;
using Terrias.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;
using Object = UnityEngine.Object;

namespace Terrias.Dll.GameApi;

public sealed class HouseLibraryUiContext
{
    public HouseLibraryUiContext(
        Transform window,
        Transform parent,
        Transform? cardButton,
        Transform? rollButton,
        Transform? template,
        UnityEngine.Component? templateManager)
    {
        Window = window;
        Parent = parent;
        CardButton = cardButton;
        RollButton = rollButton;
        Template = template;
        TemplateManager = templateManager;
    }

    public Transform Window { get; }

    public Transform Parent { get; }

    public Transform? CardButton { get; }

    public Transform? RollButton { get; }

    public Transform? Template { get; }

    public UnityEngine.Component? TemplateManager { get; }
}

public static class HouseLibraryUiApi
{
    public static HouseLibraryUiContext? Resolve(object? houseManager)
    {
        var window = ResolveLibraryWindow(houseManager);
        if (window == null)
        {
            return null;
        }

        var cardButton = FindHouseItemTransform(window, "cardShop")
                         ?? FindButtonLikeTransformByText(window, "借阅图书", "借閱圖書");
        var rollButton = FindHouseItemTransform(window, "rollShop")
                         ?? FindButtonLikeTransformByText(window, "查找典籍", "查找典籍");
        var template = rollButton ?? cardButton ?? FindLibraryButtonTemplate(window);
        var parent = template?.parent ?? ResolveLibraryButtonParent(window);
        if (parent == null)
        {
            return null;
        }

        var templateManager = template == null ? null : FindButtonManagerComponent(template);
        return new HouseLibraryUiContext(window, parent, cardButton, rollButton, template, templateManager);
    }

    public static bool ContainsComponentNamed(Transform transform, string typeName)
    {
        foreach (var component in transform.GetComponents<UnityEngine.Component>())
        {
            if (component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static void StripClonedHouseItems(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().Name != "HouseItem")
            {
                continue;
            }

            try
            {
                Singleton<EventCenter>.Instance.RemoveEventListener(
                    LanguageEvent.LanguageChange.ToString(),
                    component);
            }
            catch (Exception ex)
            {
                TerriasLog.Warn("[LibrarySubMenu] failed to detach cloned HouseItem language listener: " + ex.Message);
            }

            component.enabled = false;
            Object.Destroy(component);
        }
    }

    private static Transform? ResolveLibraryWindow(object? houseManager)
    {
        var windowItemParent = Member(houseManager, "WindowItemParent") as Transform;
        if (windowItemParent == null)
        {
            return null;
        }

        var windowButtonParent = Member(houseManager, "WindowButtonParent") as Transform;
        if (windowButtonParent != null && windowButtonParent.childCount > 0)
        {
            var byFirstWindowButton = windowItemParent.Find(windowButtonParent.GetChild(0).name);
            if (byFirstWindowButton != null)
            {
                return byFirstWindowButton;
            }
        }

        foreach (var name in new[] { "图书馆", "圖書館", "Library" })
        {
            var byName = windowItemParent.Find(name);
            if (byName != null)
            {
                return byName;
            }
        }

        return windowItemParent.childCount > 0 ? windowItemParent.GetChild(0) : null;
    }

    private static Transform? FindHouseItemTransform(Transform root, string typeName)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().Name != "HouseItem")
            {
                continue;
            }

            if (string.Equals(Convert.ToString(Member(component, "houseItemType")), typeName, StringComparison.Ordinal))
            {
                return component.transform;
            }
        }

        return null;
    }

    private static Transform? FindLibraryButtonTemplate(Transform libraryWindow)
    {
        foreach (var component in libraryWindow.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.GetType().Name == "ButtonManager")
            {
                return component.transform;
            }
        }

        var button = libraryWindow.GetComponentInChildren<Button>(true);
        return button == null ? null : button.transform;
    }

    private static UnityEngine.Component? FindButtonManagerComponent(Transform root)
    {
        foreach (var component in root.GetComponents<MonoBehaviour>())
        {
            if (component != null && component.GetType().Name == "ButtonManager")
            {
                return component;
            }
        }

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.GetType().Name == "ButtonManager")
            {
                return component;
            }
        }

        return null;
    }

    private static Transform? FindButtonLikeTransformByText(Transform root, params string[] texts)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
            {
                continue;
            }

            var typeName = component.GetType().Name;
            if (typeName == "ButtonManager"
                && (MatchesAnyText(Convert.ToString(Member(component, "buttonText")), texts)
                    || MatchesAnyChildText(component.transform, texts)))
            {
                return component.transform;
            }

            if (typeName == "HouseItem"
                && MatchesAnyText(Convert.ToString(Member(component, "oriStr")), texts))
            {
                return component.transform;
            }
        }

        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            if (MatchesAnyText(text.text, texts))
            {
                return FindButtonLikeRoot(root, text.transform);
            }
        }

        foreach (var component in root.GetComponentsInChildren<UnityEngine.Component>(true))
        {
            if (component != null
                && IsTmpText(component)
                && MatchesAnyText(Convert.ToString(Member(component, "text")), texts))
            {
                return FindButtonLikeRoot(root, component.transform);
            }
        }

        return null;
    }

    private static Transform? ResolveLibraryButtonParent(Transform libraryWindow)
    {
        foreach (var path in new[] { "Content/Right/Buttons", "Content/Right", "Content/Buttons", "Content" })
        {
            var candidate = libraryWindow.Find(path);
            if (candidate != null)
            {
                return candidate;
            }
        }

        return libraryWindow;
    }

    private static bool MatchesAnyChildText(Transform root, params string[] texts)
    {
        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            if (MatchesAnyText(text.text, texts))
            {
                return true;
            }
        }

        foreach (var component in root.GetComponentsInChildren<UnityEngine.Component>(true))
        {
            if (component != null
                && IsTmpText(component)
                && MatchesAnyText(Convert.ToString(Member(component, "text")), texts))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesAnyText(string? value, params string[] texts)
    {
        var actual = value?.Trim() ?? "";
        if (actual.Length == 0)
        {
            return false;
        }

        foreach (var expected in texts)
        {
            if (string.Equals(actual, expected, StringComparison.Ordinal)
                || actual.IndexOf(expected, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Transform FindButtonLikeRoot(Transform boundary, Transform source)
    {
        var current = source;
        while (current.parent != null && current != boundary)
        {
            if (current.GetComponent<Button>() != null
                || ContainsComponentNamed(current, "ButtonManager")
                || ContainsComponentNamed(current, "HouseItem"))
            {
                return current;
            }

            current = current.parent;
        }

        return source;
    }

    private static bool IsTmpText(UnityEngine.Component component)
    {
        var typeName = component.GetType().Name;
        return typeName == "TextMeshProUGUI" || typeName == "TMP_Text";
    }

    private static object? Member(object? target, string name)
    {
        if (target == null)
        {
            return null;
        }

        var type = target.GetType();
        return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target)
               ?? type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
    }
}
