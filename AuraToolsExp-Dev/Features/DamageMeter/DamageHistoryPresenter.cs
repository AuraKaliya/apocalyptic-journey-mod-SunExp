using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.Core;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Features.DamageMeter;
internal static class DamageHistoryPresenter
{
    private const string HistoryName = "AuraToolsDamageMeterHistory";

    internal static void ShowHistory(DamageHistoryStore history, DamageMeterSettings settings)
    {
        AuraToolsDamageMeterUi.EnsureRoot();
        if (AuraToolsDamageMeterUi.Root == null || history.Records.Count == 0)
        {
            return;
        }

        AuraToolsDamageMeterUi.CloseHistory();
        var overlay = AuraToolsDamageMeterUi.CreateRect(HistoryName, AuraToolsDamageMeterUi.Root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AuraToolsDamageMeterUi.AddPanel(overlay, new Color(0f, 0f, 0f, 0.42f));
        var blocker = overlay.AddComponent<Button>();
        blocker.targetGraphic = overlay.GetComponent<Image>();
        blocker.onClick.AddListener(AuraToolsDamageMeterUi.CloseHistory);

        var window = AuraToolsDamageMeterUi.CreateRect(
            "Window",
            overlay.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1040f, 620f));
        AuraToolsDamageMeterUi.ApplyPanelImage(window, new Color(0.04f, 0.035f, 0.06f, 0.99f));
        var windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(12, 12, 10, 10);
        windowLayout.spacing = 8f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        var header = AuraToolsDamageMeterUi.CreateLayout("Header", window.transform);
        AuraToolsDamageMeterUi.SetHeight(header, 38f);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        AuraToolsDamageMeterUi.AddText(header.transform, "本轮冒险输出历史", 17, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 34f, 1f);
        AuraToolsDamageMeterUi.AddButton(header.transform, "关闭", AuraToolsDamageMeterUi.CloseHistory, 72f, 32f);

        var body = AuraToolsDamageMeterUi.CreateLayout("Body", window.transform);
        body.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 10f;
        bodyLayout.childControlWidth = true;
        bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = false;
        bodyLayout.childForceExpandHeight = true;

        var listViewport = AuraToolsDamageMeterUi.CreateLayout("FightList", body.transform);
        var listElement = listViewport.AddComponent<LayoutElement>();
        listElement.minWidth = 240f;
        listElement.preferredWidth = 240f;
        listElement.flexibleWidth = 0f;
        AuraToolsDamageMeterUi.AddPanel(listViewport, AuraToolsUi.Panel);
        var listContent = CreateScrollContent(listViewport);

        var details = AuraToolsDamageMeterUi.CreateLayout("FightDetails", body.transform);
        details.AddComponent<LayoutElement>().flexibleWidth = 1f;
        AuraToolsDamageMeterUi.AddPanel(details, new Color(0.055f, 0.05f, 0.075f, 0.82f));
        var detailsLayout = details.AddComponent<VerticalLayoutGroup>();
        detailsLayout.padding = new RectOffset(10, 10, 8, 8);
        detailsLayout.spacing = 5f;
        detailsLayout.childControlWidth = true;
        detailsLayout.childControlHeight = true;
        detailsLayout.childForceExpandWidth = true;
        detailsLayout.childForceExpandHeight = false;

        var ordered = history.Records.OrderByDescending(record => record.Sequence).ToList();
        foreach (var record in ordered)
        {
            var label = "第 " + record.Sequence + " 场  " + ResultLabel(record.Result)
                        + "  " + record.Snapshot.CompletedRoundCount + "回合";
            AuraToolsDamageMeterUi.AddButton(listContent, label, () => RenderHistoryRecord(details.transform, record, settings), 216f, 34f);
        }

        RenderHistoryRecord(details.transform, ordered[0], settings);
        overlay.transform.SetAsLastSibling();
    }

    internal static void ShowOutOfRunHistory(OutOfRunDamageHistoryStore history)
    {
        AuraToolsDamageMeterUi.EnsureRoot();
        if (AuraToolsDamageMeterUi.Root == null || history == null)
        {
            return;
        }

        AuraToolsDamageMeterUi.CloseHistory();
        var overlay = AuraToolsDamageMeterUi.CreateRect(HistoryName, AuraToolsDamageMeterUi.Root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        AuraToolsDamageMeterUi.AddPanel(overlay, new Color(0f, 0f, 0f, 0.42f));
        var blocker = overlay.AddComponent<Button>();
        blocker.targetGraphic = overlay.GetComponent<Image>();
        blocker.onClick.AddListener(AuraToolsDamageMeterUi.CloseHistory);

        var window = AuraToolsDamageMeterUi.CreateRect(
            "Window",
            overlay.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(1180f, 640f));
        AuraToolsDamageMeterUi.ApplyPanelImage(window, new Color(0.04f, 0.035f, 0.06f, 0.99f));
        var windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(12, 12, 10, 10);
        windowLayout.spacing = 8f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        var header = AuraToolsDamageMeterUi.CreateLayout("Header", window.transform);
        AuraToolsDamageMeterUi.SetHeight(header, 38f);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        AuraToolsDamageMeterUi.AddText(header.transform, "局外历史记录", 17, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 34f, 1f);
        AuraToolsDamageMeterUi.AddButton(
            header.transform,
            "清空",
            () =>
            {
                AuraToolsDamageMeterRuntime.ClearOutOfRunHistory();
                ShowOutOfRunHistory(AuraToolsDamageMeterRuntime.OutOfRunHistory);
            },
            72f,
            32f);
        AuraToolsDamageMeterUi.AddButton(header.transform, "关闭", AuraToolsDamageMeterUi.CloseHistory, 72f, 32f);

        var viewport = AuraToolsDamageMeterUi.CreateLayout("HistoryList", window.transform);
        viewport.AddComponent<LayoutElement>().flexibleHeight = 1f;
        AuraToolsDamageMeterUi.AddPanel(viewport, new Color(0.055f, 0.05f, 0.075f, 0.82f));
        var content = CreateScrollContent(viewport);

        RenderOutOfRunHeader(content);
        var ordered = history.Records.OrderByDescending(record => record.Sequence).ToList();
        if (ordered.Count == 0)
        {
            AuraToolsDamageMeterUi.AddText(content, "暂无局外历史记录", 14, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 64f, 1f);
        }
        else
        {
            foreach (var record in ordered)
            {
                RenderOutOfRunRow(content, record);
            }
        }

        overlay.transform.SetAsLastSibling();
    }

    internal static void RenderOutOfRunHeader(Transform parent)
    {
        var row = AuraToolsDamageMeterUi.CreateLayout("Columns", parent);
        AuraToolsDamageMeterUi.SetHeight(row, 28f);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 3);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        AuraToolsDamageMeterUi.AddText(row.transform, "游玩模式", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 0f, 120f);
        AuraToolsDamageMeterUi.AddText(row.transform, "状态", 12, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 22f, 0f, 58f);
        AuraToolsDamageMeterUi.AddText(row.transform, "队伍成员", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 0f, 460f);
        AuraToolsDamageMeterUi.AddText(row.transform, "最强一击", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 0f, 178f);
        AuraToolsDamageMeterUi.AddText(row.transform, "队伍DPS", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 0f, 112f);
        AuraToolsDamageMeterUi.AddText(row.transform, "MVP", 12, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 1f);
    }

    internal static void RenderOutOfRunRow(Transform parent, OutOfRunDamageHistoryRecord record)
    {
        var row = AuraToolsDamageMeterUi.CreateLayout("OutOfRun-" + record.Sequence, parent);
        AuraToolsDamageMeterUi.SetHeight(row, 52f);
        AuraToolsDamageMeterUi.AddPanel(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 5, 5);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        AuraToolsDamageMeterUi.AddText(row.transform, string.IsNullOrWhiteSpace(record.ModeDisplayName) ? record.ModeId : record.ModeDisplayName, 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 40f, 0f, 120f);
        AuraToolsDamageMeterUi.AddText(row.transform, record.Status ?? "", 13, TextAnchor.MiddleCenter, AuraToolsUi.Text, 40f, 0f, 58f);

        var members = AuraToolsDamageMeterUi.CreateLayout("Members", row.transform);
        var memberElement = members.AddComponent<LayoutElement>();
        memberElement.minWidth = 460f;
        memberElement.preferredWidth = 460f;
        memberElement.flexibleWidth = 0f;
        var memberLayout = members.AddComponent<HorizontalLayoutGroup>();
        memberLayout.spacing = 5f;
        memberLayout.childControlWidth = true;
        memberLayout.childControlHeight = true;
        memberLayout.childForceExpandWidth = false;
        for (var index = 0; index < DamageMeterProtocol.MaxTeamMembers; index++)
        {
            var member = record.TeamMembers != null && index < record.TeamMembers.Count
                ? record.TeamMembers[index]
                : null;
            AddMemberCell(members.transform, member);
        }

        AuraToolsDamageMeterUi.AddText(row.transform, BestHitLabel(record.BestHit), 12, TextAnchor.MiddleLeft, AuraToolsUi.Accent, 40f, 0f, 178f);
        AuraToolsDamageMeterUi.AddText(row.transform, DamageMeterFormatters.FormatScientific(record.TeamDps), 12, TextAnchor.MiddleLeft, AuraToolsUi.Text, 40f, 0f, 112f);
        AuraToolsDamageMeterUi.AddText(row.transform, DamageMeterFormatters.TrimDisplayName(record.Mvp?.DisplayName ?? ""), 12, TextAnchor.MiddleLeft, AuraToolsUi.Text, 40f, 1f);
    }

    internal static void AddMemberCell(Transform parent, OutOfRunTeamMemberSnapshot? member)
    {
        var cell = AuraToolsDamageMeterUi.CreateLayout("Member", parent);
        var element = cell.AddComponent<LayoutElement>();
        element.minWidth = 110f;
        element.preferredWidth = 110f;
        element.flexibleWidth = 0f;
        var layout = cell.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        var avatar = AuraToolsDamageMeterUi.CreateLayout("Avatar", cell.transform);
        var avatarElement = avatar.AddComponent<LayoutElement>();
        avatarElement.minWidth = 32f;
        avatarElement.preferredWidth = 32f;
        avatarElement.minHeight = 32f;
        avatarElement.preferredHeight = 32f;
        avatarElement.flexibleWidth = 0f;
        var avatarImage = AuraToolsDamageMeterUi.AddPanel(avatar, new Color(0.08f, 0.075f, 0.105f, 0.95f));
        var sprite = TryLoadAvatarSprite(member?.AvatarPngBase64);
        if (sprite != null)
        {
            avatarImage.sprite = sprite;
            avatarImage.type = Image.Type.Simple;
            avatarImage.preserveAspect = true;
            avatarImage.color = Color.white;
        }

        var memberName = AuraToolsDamageMeterUi.AddText(
            cell.transform,
            MemberDisplayName(member),
            12,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            32f,
            0f,
            74f);
        memberName.horizontalOverflow = HorizontalWrapMode.Overflow;
        memberName.verticalOverflow = VerticalWrapMode.Truncate;
    }

    internal static string MemberDisplayName(OutOfRunTeamMemberSnapshot? member)
    {
        if (member == null)
        {
            return "";
        }

        var displayName = string.IsNullOrWhiteSpace(member.PlayerDisplayName)
            ? member.DisplayName
            : member.PlayerDisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = member.PlayerId;
        }

        return DamageMeterFormatters.TrimDisplayName(displayName ?? "");
    }

    internal static string BestHitLabel(DamageBestHitRecord? bestHit)
    {
        if (bestHit == null || bestHit.Damage <= 0)
        {
            return "0.000 E+00 (-)";
        }

        return DamageMeterFormatters.FormatScientific(bestHit.Damage)
               + " ("
               + DamageMeterFormatters.TrimDisplayName(bestHit.SourceDisplayName)
               + ")";
    }

    internal static string BestHitValueForStat(DamageBestHitRecord? bestHit, string instanceId)
    {
        if (bestHit == null
            || bestHit.Damage <= 0
            || !string.Equals(bestHit.SourceInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        return DamageMeterFormatters.FormatScientific(bestHit.Damage);
    }

    internal static Sprite? TryLoadAvatarSprite(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!LoadImageIntoTexture(texture, bytes))
            {
                Object.Destroy(texture);
                return null;
            }

            texture.filterMode = FilterMode.Bilinear;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));
        }
        catch
        {
            return null;
        }
    }

    internal static bool LoadImageIntoTexture(Texture2D texture, byte[] bytes)
    {
        try
        {
            var imageConversion = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEngine.ImageConversion"))
                .FirstOrDefault(type => type != null);
            var method = imageConversion?.GetMethod(
                "LoadImage",
                new[] { typeof(Texture2D), typeof(byte[]) });
            return method?.Invoke(null, new object[] { texture, bytes }) is true;
        }
        catch
        {
            return false;
        }
    }

    internal static Transform CreateScrollContent(GameObject viewport)
    {
        var viewportRect = viewport.GetComponent<RectTransform>();
        var mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var content = AuraToolsDamageMeterUi.CreateRect(
            "Content",
            viewport.transform,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            Vector2.zero);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.offsetMin = new Vector2(6f, 0f);
        contentRect.offsetMax = new Vector2(-6f, 0f);
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 6, 6);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        return content.transform;
    }

    internal static void RenderHistoryRecord(
        Transform parent,
        DamageFightRecord record,
        DamageMeterSettings settings)
    {
        ClearChildren(parent);
        var ledger = new DamageLedger();
        if (!ledger.ApplySnapshot(record.Snapshot))
        {
            AuraToolsDamageMeterUi.AddText(parent, "历史记录无法读取。", 14, TextAnchor.MiddleCenter, AuraToolsUi.WarningText, 80f, 1f);
            return;
        }

        var grandTotal = ledger.DisplayGrandTotal(
            settings.CountShieldLoss,
            settings.FriendlyOnly,
            settings.IncludeUnknownTeam);
        AuraToolsDamageMeterUi.AddText(
            parent,
            "第 " + record.Sequence + " 场  " + ResultLabel(record.Result)
            + "  /  " + ledger.CompletedRoundCount + " 回合"
            + "  /  合计 " + grandTotal,
            15,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Accent,
            34f,
            1f);

        AuraToolsDamageMeterUi.AddText(
            parent,
            "最强一击 " + BestHitLabel(record.Snapshot.BestHit),
            13,
            TextAnchor.MiddleLeft,
            AuraToolsUi.Text,
            28f,
            1f);

        var columns = AuraToolsDamageMeterUi.CreateLayout("Columns", parent);
        AuraToolsDamageMeterUi.SetHeight(columns, 24f);
        var columnsLayout = columns.AddComponent<HorizontalLayoutGroup>();
        columnsLayout.spacing = 6f;
        columnsLayout.childControlWidth = true;
        columnsLayout.childControlHeight = true;
        columnsLayout.childForceExpandWidth = false;
        AuraToolsDamageMeterUi.AddText(columns.transform, "队", 11, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 22f, 0f, 28f);
        AuraToolsDamageMeterUi.AddText(columns.transform, "角色", 11, TextAnchor.MiddleLeft, AuraToolsUi.MutedText, 22f, 1f);
        AuraToolsDamageMeterUi.AddText(columns.transform, "总计", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 22f, 0f, 78f);
        AuraToolsDamageMeterUi.AddText(columns.transform, "最强一击", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 22f, 0f, 112f);
        AuraToolsDamageMeterUi.AddText(columns.transform, "平均", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 22f, 0f, 72f);
        AuraToolsDamageMeterUi.AddText(columns.transform, "占比", 11, TextAnchor.MiddleRight, AuraToolsUi.MutedText, 22f, 0f, 58f);
        AuraToolsDamageMeterUi.AddText(columns.transform, "", 11, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 22f, 0f, 58f);

        var visibleRows = ledger.VisibleRows(
            settings.FriendlyOnly,
            settings.IncludeUnknownTeam,
            settings.CountShieldLoss,
            Math.Max(settings.MaxRows, 12));
        foreach (var stat in visibleRows)
        {
            var row = AuraToolsDamageMeterUi.CreateLayout("History-" + stat.InstanceId, parent);
            AuraToolsDamageMeterUi.SetHeight(row, 40f);
            AuraToolsDamageMeterUi.AddPanel(row, AuraToolsUi.Row);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 3, 3);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            AuraToolsDamageMeterUi.AddText(row.transform, AuraToolsDamageMeterUi.TeamLabel(stat.Team), 12, TextAnchor.MiddleCenter, AuraToolsUi.Text, 30f, 0f, 28f);
            AuraToolsDamageMeterUi.AddText(row.transform, AuraToolsDamageMeterUi.TrimName(stat.DisplayName), 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 30f, 1f);
            AuraToolsDamageMeterUi.AddText(row.transform, stat.DisplayTotal(settings.CountShieldLoss).ToString(), 13, TextAnchor.MiddleRight, AuraToolsUi.Accent, 30f, 0f, 78f);
            AuraToolsDamageMeterUi.AddText(
                row.transform,
                BestHitValueForStat(record.Snapshot.BestHit, stat.InstanceId),
                12,
                TextAnchor.MiddleRight,
                AuraToolsUi.Accent,
                30f,
                0f,
                112f);
            AuraToolsDamageMeterUi.AddText(
                row.transform,
                stat.AveragePerCompletedRound(
                    settings.CountShieldLoss,
                    Math.Max(1, ledger.CompletedRoundCount)).ToString("0.0"),
                12,
                TextAnchor.MiddleRight,
                AuraToolsUi.Text,
                30f,
                0f,
                72f);
            AuraToolsDamageMeterUi.AddText(
                row.transform,
                grandTotal <= 0
                    ? "0%"
                    : ((double)stat.DisplayTotal(settings.CountShieldLoss) / grandTotal).ToString("P0"),
                12,
                TextAnchor.MiddleRight,
                AuraToolsUi.Text,
                30f,
                0f,
                58f);
            AuraToolsDamageMeterUi.AddButton(
                row.transform,
                "明细",
                () => AuraToolsDamageMeterUi.ShowDetails(stat.InstanceId, ledger, settings),
                58f,
                30f);
        }
    }

    internal static string ResultLabel(string result)
    {
        return result switch
        {
            "Win" => "胜利",
            "Escape" => "撤退",
            "Loss" => "失败",
            _ => "已结束"
        };
    }

    internal static void ClearChildren(Transform parent)
    {
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            Object.Destroy(parent.GetChild(index).gameObject);
        }
    }

}
