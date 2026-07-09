using System;
using System.Collections.Generic;
using System.Linq;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using UnityEngine.UI;
using Witch.Core;

namespace SunExp.Dll.Hooks.Ui;

public static class FamiliarGrowthPanel
{
    private const string PanelName = "SunExp_FamiliarGrowthPanel";
    private const string RenameIconPath = "Mods/SunExp/ModResource/Images/UI/\u66f4\u540d.png";
    private const float RowHeight = 58f;
    private const float ButtonHeight = 34f;
    private const float CompactBarHeight = 42f;
    private const float IconSize = 42f;
    private const float RenameIconSize = 32f;
    private const float InlineButtonWidth = 112f;

    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.72f);
    private static readonly Color PanelTint = new(0.025f, 0.028f, 0.075f, 0.98f);
    private static readonly Color HeaderTint = new(0.05f, 0.044f, 0.10f, 0.98f);
    private static readonly Color RowTint = new(0.08f, 0.075f, 0.15f, 0.96f);
    private static readonly Color DetailTint = new(0.045f, 0.043f, 0.10f, 0.98f);
    private static readonly Color Gold = new(0.88f, 0.78f, 0.48f);
    private static readonly Color Pale = new(0.92f, 0.88f, 0.72f);
    private static readonly Color Green = new(0.60f, 0.92f, 0.60f);
    private static readonly Color Red = new(0.95f, 0.50f, 0.45f);

    private static GameObject? activePanel;
    private static Transform? listContent;
    private static Transform? detailContent;
    private static Transform? actionContent;
    private static InputField? titleNameInput;
    private static Text? hintText;
    private static Sprite? renameIcon;
    private static string focusedInstanceId = "";
    private static bool editingName;

    public static bool IsOpen => activePanel != null;

    public static void Open()
    {
        try
        {
            Close();
            var roster = FamiliarGrowthApi.Roster();
            focusedInstanceId = string.IsNullOrWhiteSpace(roster.SelectedInstanceId)
                ? roster.Instances.FirstOrDefault(instance => !instance.Deleted)?.InstanceId ?? ""
                : roster.SelectedInstanceId;
            ShowPanel();
        }
        catch (Exception ex)
        {
            Close();
            SunExpLog.Error("Familiar growth panel failed", ex);
        }
    }

    public static void Close()
    {
        ClearChildren(listContent);
        ClearChildren(detailContent);
        ClearChildren(actionContent);
        SunExpModalHost.Close(ref activePanel, "FamiliarGrowthPanel.Close", "[FamiliarGrowth]");
        listContent = null;
        detailContent = null;
        actionContent = null;
        titleNameInput = null;
        hintText = null;
        editingName = false;
    }

    private static void ShowPanel()
    {
        var parent = SunExpModalHost.ModalParent();
        if (parent == null)
        {
            return;
        }

        activePanel = SunExpModalHost.CreateFullscreenRoot(PanelName, parent, Backdrop);
        var window = CreateRect("Window", activePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), ResolveWindowSize(parent));
        ApplyPanelImage(window, PanelTint);

        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 16, 14);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = CreateLayoutObject("Header", window.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 74f;
        ApplyPanelImage(header, HeaderTint);
        var headerLayout = header.AddComponent<VerticalLayoutGroup>();
        headerLayout.padding = new RectOffset(12, 12, 6, 6);
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandHeight = false;
        AddTextBlock(header.transform, "\u4f7f\u9b54\u6863\u6848", 27, TextAnchor.MiddleCenter, Gold, 34f);
        AddTextBlock(header.transform, "\u57f9\u517b\u5df2\u6ce8\u518c\u4f7f\u9b54\u79cd\u7c7b\u7684\u72ec\u7acb\u4e2a\u4f53\u3002", 14, TextAnchor.MiddleCenter, Pale, 24f);

        var body = CreateLayoutObject("Body", window.transform);
        var bodyElement = body.AddComponent<LayoutElement>();
        bodyElement.flexibleHeight = 1f;
        bodyElement.minHeight = 320f;
        var bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 14f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = true;
        bodyLayout.childForceExpandHeight = true;

        listContent = CreateScroll(body.transform, "FamiliarList", 360f);
        detailContent = CreateDetailRoot(body.transform);

        actionContent = CreateActionBar(window.transform);

        var footer = CreateLayoutObject("Footer", window.transform);
        var footerElement = footer.AddComponent<LayoutElement>();
        footerElement.minHeight = CompactBarHeight;
        footerElement.preferredHeight = CompactBarHeight;
        footerElement.flexibleHeight = 0f;
        ApplyPanelImage(footer, HeaderTint);
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.padding = new RectOffset(10, 10, 4, 4);
        footerLayout.spacing = 12f;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandHeight = false;
        footerLayout.childForceExpandWidth = false;
        hintText = AddTextBlock(footer.transform, "", 13, TextAnchor.MiddleLeft, Pale, ButtonHeight, 1f);
        CreateButton(footer.transform, "\u5173\u95ed", new Vector2(InlineButtonWidth, ButtonHeight), Close);

        RefreshAll();
    }

    private static void RefreshAll()
    {
        RefreshList();
        RefreshDetail();
    }

    private static void RefreshList()
    {
        if (listContent == null)
        {
            return;
        }

        ClearChildren(listContent);
        var roster = FamiliarGrowthApi.Roster();
        var species = FamiliarGrowthService.Species().ToDictionary(spec => spec.SpeciesId, StringComparer.Ordinal);
        if (roster.Instances.Count == 0)
        {
            AddTextBlock(listContent, "\u5c1a\u672a\u767b\u8bb0\u4f7f\u9b54\u3002", 15, TextAnchor.MiddleCenter, Gold, 46f);
            return;
        }

        foreach (var instance in roster.Instances.Where(instance => !instance.Deleted)
                     .OrderBy(instance => instance.SpeciesId, StringComparer.Ordinal)
                     .ThenBy(instance => instance.InstanceId, StringComparer.Ordinal))
        {
            species.TryGetValue(instance.SpeciesId, out var spec);
            CreateInstanceRow(listContent, instance, spec, roster.SelectedInstanceId);
        }
    }

    private static void RefreshDetail()
    {
        if (detailContent == null)
        {
            return;
        }

        ClearChildren(detailContent);
        ClearChildren(actionContent);
        titleNameInput = null;
        var roster = FamiliarGrowthApi.Roster();
        var instance = roster.Instances.FirstOrDefault(item => !item.Deleted && string.Equals(item.InstanceId, focusedInstanceId, StringComparison.Ordinal))
                       ?? roster.Instances.FirstOrDefault(item => !item.Deleted);
        if (instance == null)
        {
            AddTextBlock(detailContent, "\u6ca1\u6709\u53ef\u7528\u4e2a\u4f53\u3002", 16, TextAnchor.MiddleCenter, Gold, 48f);
            RefreshActions(null, "");
            return;
        }

        focusedInstanceId = instance.InstanceId;
        var spec = FamiliarGrowthService.Species().FirstOrDefault(item => string.Equals(item.SpeciesId, instance.SpeciesId, StringComparison.Ordinal));
        CreateDetail(instance, spec);
        RefreshActions(instance, roster.SelectedInstanceId);
    }

    private static void CreateInstanceRow(Transform parent, FamiliarInstance instance, FamiliarSpeciesSpec? species, string selectedId)
    {
        var row = CreateLayoutObject("Familiar-" + instance.InstanceId, parent);
        row.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        ApplyPanelImage(row, string.Equals(focusedInstanceId, instance.InstanceId, StringComparison.Ordinal)
            ? new Color(0.14f, 0.12f, 0.22f, 0.98f)
            : RowTint);
        row.GetComponent<Image>().raycastTarget = true;

        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 6, 6);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = false;

        CreateIconCell(row.transform, species, instance.Level.ToString());
        var selected = string.Equals(selectedId, instance.InstanceId, StringComparison.Ordinal)
            ? "  [\u968f\u884c]"
            : "";
        AddTextBlock(row.transform, instance.Name + selected + "\nLv." + instance.Level + "  " + instance.InstanceId,
            13, TextAnchor.MiddleLeft, Pale, 46f, 1f);
        var button = row.AddComponent<Button>();
        button.targetGraphic = row.GetComponent<Image>();
        button.onClick.AddListener(() =>
        {
            focusedInstanceId = instance.InstanceId;
            editingName = false;
            RefreshAll();
        });
    }

    private static void CreateDetail(FamiliarInstance instance, FamiliarSpeciesSpec? species)
    {
        if (detailContent == null)
        {
            return;
        }

        var title = CreateLayoutObject("DetailTitle", detailContent);
        title.AddComponent<LayoutElement>().preferredHeight = 64f;
        ApplyPanelImage(title, HeaderTint);
        var titleLayout = title.AddComponent<HorizontalLayoutGroup>();
        titleLayout.padding = new RectOffset(12, 12, 8, 8);
        titleLayout.spacing = 10f;
        titleLayout.childControlHeight = true;
        titleLayout.childControlWidth = true;
        titleLayout.childForceExpandHeight = true;
        titleLayout.childForceExpandWidth = false;
        CreateIconCell(title.transform, species, instance.Level.ToString());
        if (editingName)
        {
            titleNameInput = CreateInput(title.transform, instance.Name, 180f, 1f);
        }
        else
        {
            AddTextBlock(title.transform, instance.Name, 18, TextAnchor.MiddleLeft, Gold, 48f, 1f);
        }

        CreateRenameButton(title.transform, instance);

        AddInfo(detailContent, "\u7f16\u53f7", instance.InstanceId);
        AddInfo(detailContent, "\u79cd\u7c7b", SpeciesDisplayName(instance, species));
        AddInfo(detailContent, "\u7b49\u7ea7", "Lv." + instance.Level + " / " + FamiliarRosterService.MaxLevel);
        AddInfo(detailContent, "\u7ecf\u9a8c", ExperienceText(instance));
        AddInfo(detailContent, "\u8d44\u8d28", FamiliarBlessingRoller.AptitudeLabel(instance.Aptitude) + " (" + instance.Aptitude + ")");
        AddInfo(detailContent, "\u672c\u4f53/\u5316\u8eab", instance.IsBody ? "\u672c\u4f53" : "\u5316\u8eab");
        if (species != null && !string.IsNullOrWhiteSpace(species.NativeBlessingId))
        {
            AddInfo(detailContent, "\u539f\u751f\u795d\u798f", BlessingDisplayName(species.NativeBlessingId));
        }

        CreatePendingBlessingList(detailContent, instance);
        CreateBlessingList(detailContent, instance);
    }

    private static void RefreshActions(FamiliarInstance? instance, string selectedId)
    {
        if (actionContent == null)
        {
            return;
        }

        ClearChildren(actionContent);
        if (instance == null)
        {
            AddTextBlock(actionContent, "\u8bf7\u9009\u62e9\u4f7f\u9b54\u3002", 14, TextAnchor.MiddleLeft, Pale, 38f, 1f);
            return;
        }

        AddTextBlock(actionContent, "\u64cd\u4f5c", 14, TextAnchor.MiddleLeft, Gold, ButtonHeight, 0f, 80f);
        CreateButton(actionContent, "\u968f\u884c", new Vector2(92f, ButtonHeight), () =>
        {
            FamiliarGrowthApi.Select(instance.InstanceId);
            UpdateHint("\u5df2\u9009\u62e9 " + instance.Name + "\u968f\u884c\u3002");
            RefreshAll();
        }, !string.Equals(instance.InstanceId, selectedId, StringComparison.Ordinal));
        CreateButton(actionContent, "\u8bad\u7ec3+10", new Vector2(92f, ButtonHeight), () =>
        {
            var result = FamiliarGrowthApi.GrantExperience(instance.InstanceId, FamiliarRosterService.DefaultTrainingExperience);
            UpdateHint(result?.LeveledUp == true
                ? "\u8bad\u7ec3\u5b8c\u6210\uff1aLv." + result.Value.Instance.Level
                : "\u8bad\u7ec3\u5b8c\u6210\uff1a\u7ecf\u9a8c+" + FamiliarRosterService.DefaultTrainingExperience);
            RefreshAll();
        });
        CreateButton(actionContent, "\u767b\u8bb0\u540c\u7c7b", new Vector2(96f, ButtonHeight), () =>
        {
            var created = FamiliarGrowthApi.Create(instance.SpeciesId);
            if (created != null)
            {
                focusedInstanceId = created.InstanceId;
                editingName = false;
                UpdateHint("\u5df2\u767b\u8bb0 " + created.Name + "\u3002");
            }

            RefreshAll();
        });
        CreateButton(actionContent, "\u5220\u9664", new Vector2(86f, ButtonHeight), () =>
        {
            if (FamiliarGrowthApi.Delete(instance.InstanceId))
            {
                focusedInstanceId = FamiliarGrowthApi.Roster().SelectedInstanceId;
                editingName = false;
                UpdateHint("\u5df2\u5220\u9664\u4e2a\u4f53\u3002");
            }
            else
            {
                UpdateHint("\u672c\u4f53\u65e0\u6cd5\u5220\u9664\u3002");
            }

            RefreshAll();
        }, !instance.IsBody);
    }

    private static void CreatePendingBlessingList(Transform parent, FamiliarInstance instance)
    {
        var choices = instance.PendingBlessingChoices ?? new List<FamiliarBlessingChoice>();
        if (choices.Count == 0)
        {
            return;
        }

        AddTextBlock(parent, "\u5f85\u9009\u62e9\u795d\u798f", 17, TextAnchor.MiddleLeft, Gold, 34f);
        foreach (var choice in choices.OrderBy(choice => choice.Level).ThenBy(choice => choice.ChoiceId, StringComparer.Ordinal))
        {
            var choiceTitle = CreateLayoutObject("BlessingChoice-" + choice.ChoiceId, parent);
            choiceTitle.AddComponent<LayoutElement>().preferredHeight = 30f;
            AddTextFill(choiceTitle.transform, "Lv." + choice.Level + "  \u5019\u9009\uff08\u6700\u9ad8 " + choice.Tier + "\u9636\uff09",
                14, TextAnchor.MiddleLeft, Green);

            foreach (var blessingId in choice.BlessingIds)
            {
                var blessing = FamiliarBlessingRegistry.Find(blessingId);
                if (blessing == null)
                {
                    continue;
                }

                var localChoiceId = choice.ChoiceId;
                var localBlessingId = blessing.Id;
                var row = CreateLayoutObject("BlessingCandidate-" + blessing.Id, parent);
                row.AddComponent<LayoutElement>().preferredHeight = 72f;
                ApplyPanelImage(row, RowTint);
                var layout = row.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(10, 10, 6, 6);
                layout.spacing = 8f;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = true;
                layout.childForceExpandWidth = false;
                AddTextBlock(row.transform, blessing.Tier + "\u9636", 14, TextAnchor.MiddleCenter, Green, 60f, 0f, 44f);
                AddTextBlock(row.transform, BlessingDetailText(blessing), 13, TextAnchor.MiddleLeft, Pale, 60f, 1f);
                CreateButton(row.transform, "\u9009\u62e9", new Vector2(82f, ButtonHeight), () =>
                {
                    if (FamiliarGrowthApi.ChooseBlessing(instance.InstanceId, localChoiceId, localBlessingId))
                    {
                        UpdateHint("\u5df2\u83b7\u5f97\u795d\u798f\uff1a" + blessing.Name);
                    }
                    else
                    {
                        UpdateHint("\u795d\u798f\u9009\u62e9\u5931\u8d25\u3002");
                    }

                    RefreshAll();
                });
            }
        }
    }

    private static void CreateBlessingList(Transform parent, FamiliarInstance instance)
    {
        AddTextBlock(parent, "\u5df2\u83b7\u5f97\u795d\u798f", 17, TextAnchor.MiddleLeft, Gold, 34f);
        var blessings = FamiliarGrowthService.BlessingsFor(instance);
        if (blessings.Count == 0)
        {
            AddTextBlock(parent, "\u6682\u65e0\u3002\u63d0\u5347\u7b49\u7ea7\u540e\u4f1a\u751f\u6210\u5019\u9009\u795d\u798f\u3002", 14, TextAnchor.MiddleLeft, Pale, 42f);
            return;
        }

        foreach (var blessing in blessings)
        {
            var row = CreateLayoutObject("Blessing-" + blessing.Id, parent);
            row.AddComponent<LayoutElement>().preferredHeight = 66f;
            ApplyPanelImage(row, RowTint);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            AddTextBlock(row.transform, blessing.Tier + "\u9636", 16, TextAnchor.MiddleCenter, Green, 54f, 0f, 44f);
            AddTextBlock(row.transform, BlessingDetailText(blessing), 13, TextAnchor.MiddleLeft, Pale, 54f, 1f);
        }
    }

    private static Transform CreateActionBar(Transform parent)
    {
        var root = CreateLayoutObject("ActionBar", parent);
        var element = root.AddComponent<LayoutElement>();
        element.minHeight = CompactBarHeight;
        element.preferredHeight = CompactBarHeight;
        element.flexibleHeight = 0f;
        ApplyPanelImage(root, HeaderTint);
        var layout = root.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 4, 4);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        return root.transform;
    }

    private static void CreateRenameButton(Transform parent, FamiliarInstance instance)
    {
        var go = CreateLayoutObject("Button-Rename", parent);
        var element = go.AddComponent<LayoutElement>();
        element.minWidth = RenameIconSize;
        element.preferredWidth = RenameIconSize;
        element.minHeight = RenameIconSize;
        element.preferredHeight = RenameIconSize;

        var image = go.AddComponent<Image>();
        image.sprite = LoadRenameIcon();
        image.preserveAspect = true;
        image.raycastTarget = true;
        image.color = image.sprite != null ? Color.white : new Color(0.08f, 0.07f, 0.16f, 0.96f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            if (!editingName)
            {
                editingName = true;
                RefreshAll();
                return;
            }

            var name = titleNameInput?.text ?? "";
            if (FamiliarGrowthApi.Rename(instance.InstanceId, name))
            {
                UpdateHint("\u5df2\u4fdd\u5b58\u540d\u79f0\u3002");
            }
            else
            {
                UpdateHint("\u540d\u79f0\u672a\u53d8\u66f4\u3002");
            }

            editingName = false;
            RefreshAll();
        });

        if (image.sprite == null)
        {
            AddTextFill(go.transform, "\u6539", 14, TextAnchor.MiddleCenter, Gold);
        }
    }

    private static Sprite? LoadRenameIcon()
    {
        if (renameIcon != null)
        {
            return renameIcon;
        }

        try
        {
            renameIcon = SunExpResourceCache.Load<Sprite>(RenameIconPath, true, "familiar.growth.rename");
        }
        catch
        {
            renameIcon = null;
        }

        return renameIcon;
    }

    private static string SpeciesDisplayName(FamiliarInstance instance, FamiliarSpeciesSpec? species)
    {
        var name = species?.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? instance.SpeciesId : name ?? instance.SpeciesId;
    }

    private static string BlessingDisplayName(string blessId)
    {
        var id = (blessId ?? "").Trim();
        if (id.Length == 0)
        {
            return "";
        }

        var familiarBlessing = FamiliarBlessingRegistry.Find(id);
        if (familiarBlessing != null && !string.IsNullOrWhiteSpace(familiarBlessing.Name))
        {
            return familiarBlessing.Name;
        }

        try
        {
            var data = new DataConfig(id, DataType.Bless).data;
            var localizedName = data.Localize("Name");
            if (!string.IsNullOrWhiteSpace(localizedName) && localizedName != "Name")
            {
                return localizedName;
            }

            var rawName = DictionaryUtil.Get(data, "Name");
            if (!string.IsNullOrWhiteSpace(rawName))
            {
                return rawName;
            }
        }
        catch
        {
            // Fall through to id.
        }

        return id;
    }

    private static string BlessingDetailText(FamiliarBlessingDefinition blessing)
    {
        var description = ParsedBlessingDescription(blessing);
        return string.IsNullOrWhiteSpace(description)
            ? blessing.Name
            : blessing.Name + "\n" + description;
    }

    private static string ParsedBlessingDescription(FamiliarBlessingDefinition blessing)
    {
        var raw = blessing.Description ?? "";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        try
        {
            return raw.Description();
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[FamiliarGrowth] failed to parse blessing description for " + blessing.Id + ": " + ex.Message);
            return raw;
        }
    }

    private static Transform CreateDetailRoot(Transform parent)
    {
        var root = CreateLayoutObject("DetailRoot", parent);
        var element = root.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minWidth = 460f;
        ApplyPanelImage(root, DetailTint);

        var viewport = CreateRect("Viewport", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.offsetMin = new Vector2(10f, 10f);
        viewportRect.offsetMax = new Vector2(-10f, -10f);
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateRect("Content", viewport.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content.GetComponent<RectTransform>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        return content.transform;
    }

    private static Transform CreateScroll(Transform parent, string name, float width)
    {
        var root = CreateLayoutObject(name, parent);
        var rootElement = root.AddComponent<LayoutElement>();
        rootElement.minWidth = width;
        rootElement.preferredWidth = width;
        ApplyPanelImage(root, DetailTint);

        var viewport = CreateRect("Viewport", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.offsetMin = new Vector2(8f, 8f);
        viewportRect.offsetMax = new Vector2(-8f, -8f);
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var content = CreateRect("Content", viewport.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), Vector2.zero);
        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 8f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content.GetComponent<RectTransform>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        return content.transform;
    }

    private static void AddInfo(Transform parent, string label, string value)
    {
        var row = CreateLayoutObject("Info-" + label, parent);
        row.AddComponent<LayoutElement>().preferredHeight = 30f;
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = false;
        AddTextBlock(row.transform, label, 14, TextAnchor.MiddleLeft, Gold, 28f, 0f, 138f);
        AddTextBlock(row.transform, value, 14, TextAnchor.MiddleLeft, Pale, 28f, 1f);
    }

    private static InputField CreateInput(Transform parent, string value, float minWidth = 0f, float flexibleWidth = 0f)
    {
        var root = CreateLayoutObject("NameInput", parent);
        var element = root.AddComponent<LayoutElement>();
        element.preferredHeight = 38f;
        if (minWidth > 0f)
        {
            element.minWidth = minWidth;
            element.preferredWidth = minWidth;
        }

        if (flexibleWidth > 0f)
        {
            element.flexibleWidth = flexibleWidth;
        }

        ApplyPanelImage(root, new Color(0.02f, 0.02f, 0.05f, 0.92f), true);
        var input = root.AddComponent<InputField>();
        var textRect = CreateRect("Text", root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        textRect.GetComponent<RectTransform>().offsetMin = new Vector2(12f, 0f);
        textRect.GetComponent<RectTransform>().offsetMax = new Vector2(-12f, 0f);
        var text = ConfigureText(textRect.gameObject, value, 15, TextAnchor.MiddleLeft, Pale);
        input.textComponent = text;
        input.text = value;
        return input;
    }

    private static void CreateIconCell(Transform parent, FamiliarSpeciesSpec? species, string fallback)
    {
        var cell = CreateLayoutObject("Icon", parent);
        var element = cell.AddComponent<LayoutElement>();
        element.minWidth = IconSize;
        element.preferredWidth = IconSize;
        element.minHeight = IconSize;
        element.preferredHeight = IconSize;
        ApplyPanelImage(cell, PanelTint);

        var sprite = LoadSpeciesIcon(species);
        if (sprite == null)
        {
            AddTextFill(cell.transform, fallback, 16, TextAnchor.MiddleCenter, Gold);
            return;
        }

        var icon = CreateRect("Image", cell.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(IconSize - 6f, IconSize - 6f));
        var image = icon.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static Sprite? LoadSpeciesIcon(FamiliarSpeciesSpec? species)
    {
        if (species == null || string.IsNullOrWhiteSpace(species.IconPath))
        {
            return null;
        }

        try
        {
            return SunExpResourceCache.Load<Sprite>(species.IconPath, true, "familiar.growth.icon");
        }
        catch
        {
            return null;
        }
    }

    private static Button CreateButton(Transform parent, string label, Vector2 size, Action action, bool interactable = true)
    {
        var go = CreateLayoutObject("Button-" + label, parent);
        var element = go.AddComponent<LayoutElement>();
        var width = Mathf.Max(86f, size.x);
        element.minWidth = width;
        element.preferredWidth = width;
        element.minHeight = size.y;
        element.preferredHeight = size.y;
        var image = go.AddComponent<Image>();
        image.sprite = SunExpUiSprites.Button("[FamiliarGrowth]");
        image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        image.fillCenter = true;
        image.color = interactable ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.85f);
        if (image.sprite == null)
        {
            image.color = interactable ? new Color(0.08f, 0.07f, 0.16f, 0.96f) : new Color(0.07f, 0.07f, 0.08f, 0.84f);
        }

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = interactable;
        button.onClick.AddListener(() => action());
        AddTextFill(go.transform, label, 13, TextAnchor.MiddleCenter, interactable ? Pale : Red);
        return button;
    }

    private static void ApplyPanelImage(GameObject go, Color fallbackOrTint, bool raycastTarget = false)
    {
        SunExpUiBuilder.ApplyPanelImage(go, SunExpUiSprites.Panel("[FamiliarGrowth]"), fallbackOrTint, raycastTarget);
    }

    private static Text AddTextBlock(Transform parent, string value, int fontSize, TextAnchor anchor, Color color,
        float preferredHeight, float flexibleWidth = 0f, float preferredWidth = 0f)
    {
        var go = CreateLayoutObject("Text", parent);
        var element = go.AddComponent<LayoutElement>();
        element.minHeight = preferredHeight;
        element.preferredHeight = preferredHeight;
        if (flexibleWidth > 0f)
        {
            element.flexibleWidth = flexibleWidth;
        }

        if (preferredWidth > 0f)
        {
            element.minWidth = preferredWidth;
            element.preferredWidth = preferredWidth;
        }

        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text AddTextFill(Transform parent, string value, int fontSize, TextAnchor anchor, Color color)
    {
        var go = CreateRect("Text", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return ConfigureText(go, value, fontSize, anchor, color);
    }

    private static Text ConfigureText(GameObject go, string value, int fontSize, TextAnchor anchor, Color color)
    {
        return SunExpUiComponents.ConfigureText(go, value, fontSize, anchor, color, Math.Max(9, fontSize - 5));
    }

    private static GameObject CreateLayoutObject(string name, Transform parent)
    {
        return SunExpUiComponents.CreateLayoutObject(name, parent);
    }

    private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
    {
        return SunExpUiComponents.CreateRect(name, parent, anchorMin, anchorMax, pivot, sizeDelta);
    }

    private static Vector2 ResolveWindowSize(Transform parent)
    {
        var available = new Vector2(Screen.width, Screen.height);
        if (parent is RectTransform rect && rect.rect.width > 0f && rect.rect.height > 0f)
        {
            available = rect.rect.size;
        }

        return new Vector2(
            Mathf.Min(1120f, Mathf.Max(780f, available.x - 80f)),
            Mathf.Min(760f, Mathf.Max(620f, available.y - 44f)));
    }

    private static string ExperienceText(FamiliarInstance instance)
    {
        var needed = FamiliarRosterService.ExperienceForNextLevel(instance.Level);
        return needed <= 0 ? "\u5df2\u8fbe\u4e0a\u9650" : instance.Experience + " / " + needed;
    }

    private static void UpdateHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static void ClearChildren(Transform? parent)
    {
        SunExpUiPool.ReleaseOrDestroyChildren(parent, "FamiliarGrowthPanel.ClearChildren", "[FamiliarGrowth]");
    }
}
