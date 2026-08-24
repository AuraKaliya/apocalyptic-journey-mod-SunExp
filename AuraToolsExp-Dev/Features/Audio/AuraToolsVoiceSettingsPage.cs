using System;
using System.Collections.Generic;
using System.Linq;
using AudioArbiter.Shared;
using AuraAudio.Shared;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.Audio;

public static class AuraToolsVoiceSettingsPage
{
    public static void Show(Transform parent)
    {
        AuraToolsAudioRuntime.RegisterProviders();
        var window = AuraToolsUi.CreateOverlay(
            "AuraTools.VoiceSettings",
            parent,
            "角色语音管理",
            Save);
        var content = AuraToolsUi.CreateScroll(window.transform, "VoiceBindings");
        foreach (var entry in Entries())
        {
            AddBinding(content, entry);
        }
    }

    private static IReadOnlyList<VoiceEntry> Entries()
    {
        var result = new List<VoiceEntry>();
        foreach (var contribution in AuraAudioRegistryRuntime.GetSnapshot().Contributions)
        {
            var defaults = contribution.Manifest.defaults ?? new AudioRegistryDefaults();
            foreach (var provider in contribution.Manifest.providers ?? Array.Empty<AudioProviderManifest>())
            {
                if (provider == null || string.IsNullOrWhiteSpace(provider.providerId)) continue;
                var owner = string.IsNullOrWhiteSpace(provider.ownerModId)
                    ? contribution.OwnerModId
                    : provider.ownerModId.Trim();
                var qualifiedId = owner + ":" + provider.providerId.Trim();
                if (!AuraToolsConfigService.Audio.Voice.Bindings.TryGetValue(
                        qualifiedId,
                        out var settings)
                    || settings == null)
                {
                    settings = new AuraToolsVoiceBindingSettings
                    {
                        ProviderId = qualifiedId,
                        Signal = provider.kind,
                        Stage = provider.match?.stages?.FirstOrDefault() ?? "",
                        ActionId = FirstActionId(provider),
                        SkillSlot = provider.match?.skillSlot,
                        HpRatioThreshold = provider.match?.hpRatioCrossDown
                    };
                    settings.Normalize(qualifiedId);
                    AuraToolsConfigService.Audio.Voice.Bindings[qualifiedId] = settings;
                }
                result.Add(new VoiceEntry(owner, provider, defaults, settings));
            }
        }
        return result
            .OrderBy(entry => entry.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddBinding(Transform parent, VoiceEntry entry)
    {
        var binding = entry.Settings;
        var block = AuraToolsUi.CreateLayout("Voice-" + binding.ProviderId, parent);
        AuraToolsUi.SetFixedHeight(block, 104f);
        AuraToolsUi.AddListRowImage(block, AuraToolsUi.Row);
        var vertical = block.AddComponent<VerticalLayoutGroup>();
        vertical.padding = new RectOffset(10, 10, 7, 7);
        vertical.spacing = 6f;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        var header = Row(block.transform, "Header", 40f);
        AuraToolsUi.AddToggle(header, binding.Enabled, value => binding.Enabled = value);
        AuraToolsUi.AddText(
            header,
            entry.DisplayName,
            AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddText(
            header,
            AuraToolsPlayerDisplay.AudioTrigger(
                string.IsNullOrWhiteSpace(binding.Signal) ? entry.Provider.kind : binding.Signal,
                string.IsNullOrWhiteSpace(binding.Stage)
                    ? entry.Provider.match?.stages?.FirstOrDefault() ?? ""
                    : binding.Stage),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleRight,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            220f);

        var controls = Row(block.transform, "Controls", 40f);
        var target = TargetName(entry);
        if (!string.IsNullOrWhiteSpace(target))
        {
            AuraToolsUi.AddText(
                controls,
                target,
                AuraToolsUi.HintFontSize,
                TextAnchor.MiddleLeft,
                AuraToolsUi.MutedText,
                AuraToolsUi.TextMinHeight,
                1f);
        }
        AuraToolsUi.AddText(
            controls,
            AuraToolsPlayerDisplay.ResourceName(
                string.IsNullOrWhiteSpace(binding.ResourcePath)
                    ? entry.Provider.path
                    : binding.ResourcePath),
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            AuraToolsUi.TextMinHeight,
            target.Length == 0 ? 1f : 0f,
            target.Length == 0 ? 0f : 180f);
        NumberField(controls, "音量", binding.GainDb ?? entry.Provider.gainDb ?? entry.Defaults.gainDb,
            value => binding.GainDb = value);
        NumberField(controls, "间隔", binding.CooldownSeconds ?? entry.Provider.cooldownSeconds ?? entry.Defaults.cooldownSeconds,
            value => binding.CooldownSeconds = value);
        if (string.Equals(entry.Provider.kind, SoundEventKinds.SkillVoice, StringComparison.OrdinalIgnoreCase))
        {
            NumberField(
                controls,
                "技能序号",
                binding.SkillSlot ?? entry.Provider.match?.skillSlot,
                value => binding.SkillSlot = NormalizeSkillSlot(entry.Provider, value));
        }
        if (string.Equals(entry.Provider.kind, SoundEventKinds.LowHealth, StringComparison.OrdinalIgnoreCase))
        {
            NumberField(
                controls,
                "生命阈值",
                binding.HpRatioThreshold ?? entry.Provider.match?.hpRatioCrossDown,
                value => binding.HpRatioThreshold = value);
        }
        if (!string.IsNullOrWhiteSpace(binding.ResourcePath))
        {
            AuraToolsUi.AddButton(controls, "恢复默认", () => binding.ResourcePath = "", 88f, 38f);
        }
    }

    private static void NumberField(
        Transform parent,
        string label,
        float? value,
        Action<float?> changed)
    {
        AuraToolsUi.AddText(
            parent,
            label,
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleCenter,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            0f,
            62f);
        AuraToolsUi.AddInput(
            parent,
            value?.ToString("0.##") ?? "",
            text => changed(float.TryParse(text, out var parsed) ? parsed : null),
            88f,
            38f);
    }

    private static Transform Row(Transform parent, string name, float height)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row.transform;
    }

    private static string TargetName(VoiceEntry entry)
    {
        var provider = entry.Provider;
        if (string.Equals(provider.kind, SoundEventKinds.SkillVoice, StringComparison.OrdinalIgnoreCase))
        {
            var slot = entry.Settings.SkillSlot ?? provider.match?.skillSlot;
            var skill = ResolveProviderSkills(provider).FirstOrDefault(value => value.Slot == slot);
            return slot.HasValue
                ? "技能" + slot.Value + "·" + (skill?.DisplayName ?? skill?.Id ?? "未配置")
                : "技能序号未配置";
        }

        var card = (provider.match?.cardIds ?? Array.Empty<string>())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("*"));
        if (!string.IsNullOrWhiteSpace(card)) return AuraToolsPlayerDisplay.CardName(card);
        var buff = (provider.match?.buffIds ?? Array.Empty<string>())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("*"));
        if (!string.IsNullOrWhiteSpace(buff)) return AuraToolsPlayerDisplay.BuffName(buff);
        var result = provider.match?.battleResults?.FirstOrDefault();
        if (string.Equals(result, "Win", StringComparison.OrdinalIgnoreCase)) return "胜利时";
        return "";
    }

    private static int? NormalizeSkillSlot(AudioProviderManifest provider, float? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var slot = (int)Math.Round(value.Value);
        return ResolveProviderSkills(provider).Any(skill => skill.Slot == slot) ? slot : null;
    }

    private static IReadOnlyList<RoleSkillInfo> ResolveProviderSkills(AudioProviderManifest provider)
    {
        foreach (var roleId in (provider.match?.roleIds ?? Array.Empty<string>())
                     .Concat(provider.match?.careerIds ?? Array.Empty<string>())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var skills = RoleCatalog.GetRoleSkills(roleId);
            if (skills.Count > 0) return skills;
        }

        return Array.Empty<RoleSkillInfo>();
    }

    private static string FirstActionId(AudioProviderManifest provider)
    {
        return provider.match?.cardIds?.FirstOrDefault()
               ?? provider.match?.buffIds?.FirstOrDefault()
               ?? provider.match?.battleResults?.FirstOrDefault()
               ?? provider.vocalState
               ?? "";
    }

    private static void Save()
    {
        AuraToolsConfigService.SaveVoice();
        AuraToolsAudioRuntime.RegisterProviders();
    }

    private sealed class VoiceEntry
    {
        internal VoiceEntry(
            string ownerModId,
            AudioProviderManifest provider,
            AudioRegistryDefaults defaults,
            AuraToolsVoiceBindingSettings settings)
        {
            OwnerModId = ownerModId;
            Provider = provider;
            Defaults = defaults;
            Settings = settings;
        }

        internal string OwnerModId { get; }
        internal AudioProviderManifest Provider { get; }
        internal AudioRegistryDefaults Defaults { get; }
        internal AuraToolsVoiceBindingSettings Settings { get; }

        internal string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Provider.displayName)) return Provider.displayName.Trim();
                var role = Provider.match?.roleIds?.FirstOrDefault()
                           ?? Provider.match?.careerIds?.FirstOrDefault()
                           ?? "";
                var roleName = AuraToolsPlayerDisplay.RoleName(role);
                return roleName + "·" + AuraToolsPlayerDisplay.AudioTrigger(Provider.kind, "");
            }
        }
    }
}
