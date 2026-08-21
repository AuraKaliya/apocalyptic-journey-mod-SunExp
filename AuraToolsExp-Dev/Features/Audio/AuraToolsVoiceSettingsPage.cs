using System.Linq;
using AudioArbiter.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Audio;

public static class AuraToolsVoiceSettingsPage
{
    private static readonly string[] Signals =
    {
        SoundEventKinds.CareerSelected,
        SoundEventKinds.SkillVoice,
        SoundEventKinds.CardUse,
        SoundEventKinds.BuffApplied,
        SoundEventKinds.VocalState,
        SoundEventKinds.LowHealth,
        SoundEventKinds.BattleCompleted
    };

    private static readonly string[] Stages =
    {
        AudioSignalStages.Committed,
        AudioSignalStages.PresentationCommitted,
        AudioSignalStages.Applied,
        AudioSignalStages.Observed,
        AudioSignalStages.ThresholdCrossedDown,
        AudioSignalStages.Completed
    };

    public static void Show(Transform parent)
    {
        AuraToolsAudioRuntime.RegisterProviders();
        var window = AuraToolsUi.CreateOverlay("AuraTools.VoiceSettings", parent, "角色语音管理", Save);
        var content = AuraToolsUi.CreateScroll(window.transform, "VoiceBindings");
        foreach (var pair in AuraToolsConfigService.Audio.Voice.Bindings
                     .OrderBy(value => value.Value.Signal)
                     .ThenBy(value => value.Key))
        {
            AddBinding(content, pair.Value);
        }
    }

    private static void AddBinding(Transform parent, AuraToolsVoiceBindingSettings binding)
    {
        var block = AuraToolsUi.CreateLayout("Voice-" + binding.ProviderId, parent);
        AuraToolsUi.SetFixedHeight(block, 116f);
        AuraToolsUi.AddImage(block, AuraToolsUi.Row);
        var vertical = block.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(8, 8, 5, 5);
        vertical.spacing = 5f;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandHeight = false;

        var header = Row(block.transform, "Header");
        AuraToolsUi.AddToggle(header, binding.Enabled, value => binding.Enabled = value);
        AuraToolsUi.AddText(header, binding.ProviderId, AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        AuraToolsUi.AddText(header, binding.Signal + " / " + binding.Stage,
            AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight, 0f, 250f);

        var signal = Row(block.transform, "Signal");
        AuraToolsUi.AddText(signal, "信号", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 44f);
        var signalIndex = System.Array.FindIndex(Signals, value =>
            string.Equals(value, binding.Signal, System.StringComparison.OrdinalIgnoreCase));
        AuraToolsUi.AddSelectButton(signal, Signals, System.Math.Max(0, signalIndex),
            value => binding.Signal = Signals[value], 150f);
        AuraToolsUi.AddText(signal, "阶段", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 44f);
        var stageIndex = System.Array.FindIndex(Stages, value =>
            string.Equals(value, binding.Stage, System.StringComparison.OrdinalIgnoreCase));
        AuraToolsUi.AddSelectButton(signal, Stages, System.Math.Max(0, stageIndex),
            value => binding.Stage = Stages[value], 170f);
        AuraToolsUi.AddText(signal, "动作", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 44f);
        AuraToolsUi.AddInput(signal, binding.ActionId, value => binding.ActionId = value.Trim(), 310f);

        var playback = Row(block.transform, "Playback");
        AuraToolsUi.AddText(playback, "增益", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 44f);
        AuraToolsUi.AddInput(playback, binding.GainDb?.ToString("0.##") ?? "继承", value =>
        {
            binding.GainDb = float.TryParse(value, out var parsed) ? parsed : null;
        }, 90f);
        AuraToolsUi.AddText(playback, "冷却", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 44f);
        AuraToolsUi.AddInput(playback, binding.CooldownSeconds?.ToString("0.##") ?? "继承", value =>
        {
            binding.CooldownSeconds = float.TryParse(value, out var parsed) ? parsed : null;
        }, 90f);
        AuraToolsUi.AddText(playback, "低血量阈值", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 90f);
        AuraToolsUi.AddInput(playback, binding.HpRatioThreshold?.ToString("0.##") ?? "继承", value =>
        {
            binding.HpRatioThreshold = float.TryParse(value, out var parsed) ? parsed : null;
        }, 90f);
        AuraToolsUi.AddInput(playback, binding.ResourcePath, value => binding.ResourcePath = value.Trim(), 360f);
    }

    private static Transform Row(Transform parent, string name)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, 30f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row.transform;
    }

    private static void Save()
    {
        AuraToolsConfigService.SaveVoice();
        AuraToolsAudioRuntime.RegisterProviders();
    }
}
