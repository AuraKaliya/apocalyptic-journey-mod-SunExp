using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.GameApi;

/// <summary>
/// Preserves the game's native SettingUI preload contract. GameApp creates and
/// hides this cache during startup so the first user click reaches UIBase.Show.
/// Returning from the isolated replay host must establish the same contract.
/// </summary>
internal static class NativeSettingUiCacheApi
{
    private const string SettingUiName = "SettingUI";

    internal static IReadOnlyList<SettingUI> FindInstances()
    {
        return UnityEngine.Object.FindObjectsByType<SettingUI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(setting => setting != null && setting.gameObject != null)
            .Distinct()
            .ToList();
    }

    internal static SettingUI PrewarmAndHideFresh()
    {
        var manager = WitchUiManager.Instance
                      ?? throw new InvalidOperationException(
                          "UIManager is unavailable while rebuilding the SettingUI cache.");
        var registered = manager.GetUI<SettingUI>(SettingUiName);
        if (registered != null)
        {
            throw new InvalidOperationException(
                "A registered SettingUI still exists before native cache prewarm.");
        }

        // Unity's destroyed-object null semantics can leave a null value under
        // the dictionary key. Remove the key before ShowUI uses Dictionary.Add.
        manager.RemoveUI(SettingUiName);
        var setting = manager.ShowUI<SettingUI>(SettingUiName)
                      ?? throw new InvalidOperationException(
                          "The native SettingUI preload returned no instance.");
        var animationType = setting.animationType;
        try
        {
            // Establish the same cached-hidden state synchronously. Leaving a fade-out
            // tween alive while Show() is called again can execute its stale completion
            // callback against the newly opened settings instance.
            setting.animationType = UIBase.AnimationType.None;
            setting.Hide();
        }
        finally
        {
            setting.animationType = animationType;
        }

        if (setting.gameObject.activeSelf)
        {
            throw new InvalidOperationException(
                "The rebuilt SettingUI cache did not reach its hidden terminal state.");
        }
        return setting;
    }
}
