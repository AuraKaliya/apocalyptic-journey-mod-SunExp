using System;
using System.IO;
using System.Linq;
using AuraSkin.Shared.Models;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.Skin;

public static class AuraToolsSkinEditor
{
    private static Transform? careerContent;
    private static Transform? skinContent;
    private static string activeCareerId = "";

    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay(
            "AuraTools.SkinEditor",
            parent,
            "角色皮肤 - 待选资源");
        var toolbar = CreateHorizontal("Toolbar", window.transform, Settings.AuraToolsUi.ToolbarHeight);
        Settings.AuraToolsUi.AddText(
            toolbar.transform,
            "ManualSelection",
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "打开共享目录", () =>
            FileResourceUtil.OpenDirectory(AuraToolsConfigService.SkinDirectory), 128f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "扫描新皮肤", () =>
        {
            AuraToolsSkinRuntime.RegisterBundledPackage();
            AuraToolsSkinRuntime.Reload();
            RefreshCareers();
        }, 112f);

        careerContent = Settings.AuraToolsUi.CreateFixedScroll(window.transform, "SkinCareers", 560f);
        RefreshCareers();
    }

    private static void RefreshCareers()
    {
        if (careerContent == null)
        {
            return;
        }

        var viewState = AuraUi.Shared.AuraUiViewState.CaptureForContent(careerContent);
        Settings.AuraToolsUi.ClearChildren(careerContent);
        var careerIds = AuraToolsSkinRuntime.CandidateCareerIds();
        if (careerIds.Count == 0)
        {
            Settings.AuraToolsUi.AddText(
                careerContent,
                "未扫描到已注册皮肤。",
                Settings.AuraToolsUi.BodyFontSize,
                TextAnchor.MiddleLeft,
                Settings.AuraToolsUi.MutedText,
                Settings.AuraToolsUi.TextMinHeight,
                1f);
            return;
        }

        foreach (var careerId in careerIds)
        {
            CreateCareerCard(careerId);
        }
        AuraUi.Shared.AuraUiViewState.RestoreAfterLayout(
            careerContent,
            viewState,
            "AuraTools.Skin.Careers");
    }

    private static void CreateCareerCard(string careerId)
    {
        var candidates = AuraToolsSkinRuntime.CandidateDefinitions(careerId);
        var enabled = candidates.Count(candidate =>
            AuraToolsConfigService.Skin.IsCandidateEnabled(candidate.QualifiedSkinId));
        var selected = AuraToolsSkinRuntime.SelectedQualifiedSkinId(careerId);
        var card = Settings.AuraToolsUi.CreateLayout("SkinCareer-" + careerId, careerContent!);
        Settings.AuraToolsUi.SetFixedHeight(card, 104f);
        Settings.AuraToolsUi.AddPanelImage(
            card,
            enabled > 0 ? Settings.AuraToolsUi.ActiveRow : Settings.AuraToolsUi.Row);
        var layout = card.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var selectedLabel = string.IsNullOrWhiteSpace(selected) ? "当前：默认皮肤" : "当前：" + selected;
        Settings.AuraToolsUi.AddText(
            card.transform,
            RoleCatalog.GetDisplayName(careerId)
            + " · 待选 " + enabled + "/" + candidates.Count
            + "\n" + careerId + " · " + selectedLabel,
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.Text,
            82f,
            1f);
        Settings.AuraToolsUi.AddButton(card.transform, "管理待选", () =>
            ShowCareerSkins(card.transform, careerId), 104f);
    }

    private static void ShowCareerSkins(Transform parent, string careerId)
    {
        activeCareerId = careerId;
        var window = Settings.AuraToolsUi.CreateOverlay(
            "AuraTools.SkinCandidateEditor",
            parent,
            "角色皮肤 - " + RoleCatalog.GetDisplayName(careerId),
            () => activeCareerId = "",
            true,
            1040f);
        var toolbar = CreateHorizontal("Toolbar", window.transform, Settings.AuraToolsUi.ToolbarHeight);
        Settings.AuraToolsUi.AddText(
            toolbar.transform,
            careerId,
            Settings.AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            Settings.AuraToolsUi.MutedText,
            Settings.AuraToolsUi.TextMinHeight,
            1f);
        skinContent = Settings.AuraToolsUi.CreateFixedScroll(window.transform, "SkinCandidates", 540f);
        RefreshSkinCards();
    }

    private static void RefreshSkinCards()
    {
        if (skinContent == null || string.IsNullOrWhiteSpace(activeCareerId))
        {
            return;
        }

        var viewState = AuraUi.Shared.AuraUiViewState.CaptureForContent(skinContent);
        Settings.AuraToolsUi.ClearChildren(skinContent);
        var candidates = AuraToolsSkinRuntime.CandidateDefinitions(activeCareerId);
        foreach (var candidate in candidates)
        {
            CreateSkinCard(candidate);
        }
        AuraUi.Shared.AuraUiViewState.RestoreAfterLayout(
            skinContent,
            viewState,
            "AuraTools.Skin.Candidates");
    }

    private static void CreateSkinCard(SkinDefinition candidate)
    {
        var enabled = AuraToolsConfigService.Skin.IsCandidateEnabled(candidate.QualifiedSkinId);
        var selected = string.Equals(
            AuraToolsSkinRuntime.SelectedQualifiedSkinId(candidate.TargetCareerId),
            candidate.QualifiedSkinId,
            StringComparison.OrdinalIgnoreCase);
        var card = Settings.AuraToolsUi.CreateLayout(
            "SkinCandidate-" + candidate.QualifiedSkinId,
            skinContent!);
        AuraUi.Shared.AuraUiStableId.Assign(
            card,
            "skin.candidate." + candidate.QualifiedSkinId);
        Settings.AuraToolsUi.SetFixedHeight(card, 92f);
        Settings.AuraToolsUi.AddPanelImage(
            card,
            enabled ? Settings.AuraToolsUi.ActiveRow : Settings.AuraToolsUi.Row);
        var layout = card.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var toggle = Settings.AuraToolsUi.AddToggle(card.transform, enabled, value =>
        {
            AuraToolsSkinRuntime.SetCandidateEnabled(candidate.QualifiedSkinId, value);
            RefreshSkinCards();
            RefreshCareers();
        });
        AuraUi.Shared.AuraUiStableId.Assign(
            toggle.gameObject,
            "skin.candidate." + candidate.QualifiedSkinId + ".toggle");
        Settings.AuraToolsUi.AddText(
            card.transform,
            candidate.Name
            + " · " + candidate.OwnerModId
            + (selected ? " · 当前已选" : "")
            + "\n" + candidate.QualifiedSkinId,
            Settings.AuraToolsUi.BodyFontSize,
            TextAnchor.MiddleLeft,
            enabled ? Settings.AuraToolsUi.Text : Settings.AuraToolsUi.MutedText,
            72f,
            1f);
        Settings.AuraToolsUi.AddButton(card.transform, "打开目录", () =>
        {
            var directory = string.IsNullOrWhiteSpace(candidate.ManifestPath)
                ? ""
                : Path.GetDirectoryName(candidate.ManifestPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                FileResourceUtil.OpenDirectory(directory);
            }
        }, 92f);
    }

    private static GameObject CreateHorizontal(string name, Transform parent, float height)
    {
        var row = Settings.AuraToolsUi.CreateLayout(name, parent);
        Settings.AuraToolsUi.SetFixedHeight(row, height);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row;
    }
}
