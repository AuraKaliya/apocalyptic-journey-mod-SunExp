using System;
using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.Dll.Hooks.Ui;

public sealed class TerriasLocalizationScope : MonoBehaviour
{
    private readonly List<TextBinding> textBindings = new();
    private readonly List<Action> refreshers = new();
    private bool subscribed;

    public static TerriasLocalizationScope Attach(GameObject root)
    {
        return root.GetComponent<TerriasLocalizationScope>()
               ?? root.AddComponent<TerriasLocalizationScope>();
    }

    public static TerriasLocalizationScope? Find(Transform? child)
    {
        return child == null ? null : child.GetComponentInParent<TerriasLocalizationScope>(true);
    }

    public static bool BindLegacyIfAvailable(Text? target, string source)
    {
        var scope = target == null ? null : Find(target.transform);
        if (scope == null)
        {
            return false;
        }

        if (!TerriasTextCatalog.TryResolveLegacy(source, out _))
        {
            return false;
        }

        scope.Bind(target!, () => TerriasTextCatalog.ResolveLegacy(source));
        return true;
    }

    public void Bind(Text target, Func<string> resolve)
    {
        if (target == null || resolve == null)
        {
            return;
        }

        for (var index = 0; index < textBindings.Count; index++)
        {
            if (textBindings[index].Target == target)
            {
                textBindings[index] = new TextBinding(target, resolve);
                Refresh(target, resolve);
                return;
            }
        }

        textBindings.Add(new TextBinding(target, resolve));
        Refresh(target, resolve);
    }

    public void RegisterRefresh(Action refresh)
    {
        if (refresh != null && !refreshers.Contains(refresh))
        {
            refreshers.Add(refresh);
        }
    }

    public void RefreshNow()
    {
        for (var index = textBindings.Count - 1; index >= 0; index--)
        {
            var binding = textBindings[index];
            if (binding.Target == null)
            {
                textBindings.RemoveAt(index);
                continue;
            }

            Refresh(binding.Target, binding.Resolve);
        }

        foreach (var refresh in refreshers.ToArray())
        {
            try
            {
                refresh();
            }
            catch (Exception ex)
            {
                Infrastructure.TerriasLog.Warn("[Localization] scoped refresh failed: " + ex.Message);
            }
        }

        if (transform is RectTransform rect)
        {
            LayoutRebuilder.MarkLayoutForRebuild(rect);
        }
    }

    private void OnEnable()
    {
        if (!subscribed)
        {
            TerriasLanguageApi.Subscribe(this, RefreshNow);
            subscribed = true;
        }

        RefreshNow();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        textBindings.Clear();
        refreshers.Clear();
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        TerriasLanguageApi.Unsubscribe(this);
        subscribed = false;
    }

    private static void Refresh(Text target, Func<string> resolve)
    {
        try
        {
            target.text = resolve() ?? "";
        }
        catch (Exception ex)
        {
            Infrastructure.TerriasLog.WarnOnce(
                "Localization.TextBinding." + target.GetInstanceID(),
                "[Localization] text binding failed: " + ex.Message);
        }
    }

    private readonly struct TextBinding
    {
        public TextBinding(Text target, Func<string> resolve)
        {
            Target = target;
            Resolve = resolve;
        }

        public Text Target { get; }

        public Func<string> Resolve { get; }
    }
}
