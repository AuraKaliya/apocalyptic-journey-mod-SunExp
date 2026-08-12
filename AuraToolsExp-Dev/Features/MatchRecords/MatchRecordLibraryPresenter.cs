using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Portability;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using WitchUiManager = Witch.UI.UIManager;

namespace AuraToolsExp.Dll.Features.MatchRecords;

internal static class MatchRecordLibraryPresenter
{
    private const string OverlayName = "AuraToolsMatchRecordLibrary";
    private static Transform? host;
    private static Transform? body;
    private static string collection = MatchRecordCollections.Auto;
    private static readonly List<long> Cursors = new() { 0 };
    private static int pageIndex;
    private static string message = "";
    private static string armedDeleteId = "";
    private static bool clearArmed;

    internal static void Show(Transform parent)
    {
        host = parent;
        collection = MatchRecordCollections.Auto;
        Cursors.Clear();
        Cursors.Add(0);
        pageIndex = 0;
        message = "";
        armedDeleteId = "";
        clearArmed = false;
        var window = AuraToolsUi.CreateOverlay(
            OverlayName,
            parent,
            "对局记录",
            () => ResetState(),
            maxWidth: 1320f);
        body = AuraToolsUi.CreateLayout("MatchRecordBody", window.transform).transform;
        var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        AuraToolsUi.EnsureLayoutElement(body.gameObject).flexibleHeight = 1f;
        Build();
    }

    private static void Build()
    {
        if (body == null)
        {
            return;
        }

        AuraToolsUi.ClearChildren(body);
        MatchRecordPage page;
        try
        {
            page = MatchRecordStorage.Database.LoadPage(collection, Cursors[pageIndex]);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[MatchRecords] library query failed: " + ex.Message);
            AuraToolsUi.AddText(body, "读取对局记录失败：" + ex.Message, AuraToolsUi.BodyFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 52f, 1f);
            return;
        }

        var tabs = AuraToolsUi.CreateLayout("CollectionTabs", body);
        AuraToolsUi.SetFixedHeight(tabs, AuraToolsUi.ToolbarHeight);
        var tabsLayout = tabs.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 8f;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = false;
        tabsLayout.childForceExpandHeight = false;
        AuraToolsUi.AddButton(tabs.transform, "自动记录 " + AuraToolsMatchRecordsRuntime.AutoRecordCount, () => SwitchCollection(MatchRecordCollections.Auto), 132f);
        AuraToolsUi.AddButton(tabs.transform, "收藏对局 " + AuraToolsMatchRecordsRuntime.FavoriteRecordCount, () => SwitchCollection(MatchRecordCollections.Favorite), 132f);
        AuraToolsUi.AddText(
            tabs.transform,
            collection == MatchRecordCollections.Auto
                ? "自动回放上限 " + AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit + " 场"
                : "收藏不受自动清理影响",
            AuraToolsUi.HintFontSize,
            TextAnchor.MiddleLeft,
            AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight,
            1f);
        AuraToolsUi.AddText(tabs.transform, DatabaseSizeLabel(), AuraToolsUi.HintFontSize, TextAnchor.MiddleRight, AuraToolsUi.MutedText, AuraToolsUi.TextMinHeight, 0f, 150f);
        AuraToolsUi.AddButton(tabs.transform, "导入目录", () => FileResourceUtil.OpenDirectory(MatchRecordStorage.ImportsDirectory), 92f);
        AuraToolsUi.AddButton(tabs.transform, "导入回放包", PickPackage, 104f);
        AuraToolsUi.AddButton(tabs.transform, "扫描目录", ImportPackages, 92f);
        AuraToolsUi.AddButton(tabs.transform, clearArmed ? "确认清空" : "清空当前", ClearCurrent, 104f);

        if (!string.IsNullOrWhiteSpace(message))
        {
            AuraToolsUi.AddText(body, message, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 44f, 1f);
        }

        var scroll = AuraToolsUi.CreateScroll(body, "MatchRecordRows");
        if (page.Items.Count == 0)
        {
            AuraToolsUi.AddText(scroll, "这里还没有可回放的对局。", AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 72f, 1f);
        }
        else
        {
            foreach (var item in page.Items)
            {
                AddRecordRow(scroll, item);
            }
        }

        var footer = AuraToolsUi.CreateLayout("Paging", body);
        AuraToolsUi.SetFixedHeight(footer, AuraToolsUi.FooterHeight);
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 8f;
        footerLayout.childControlWidth = true;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;
        var previous = AuraToolsUi.AddButton(footer.transform, "上一页", PreviousPage, 88f);
        previous.interactable = pageIndex > 0;
        AuraToolsUi.AddText(footer.transform, "第 " + (pageIndex + 1) + " 页，共 " + page.TotalCount + " 条", AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, AuraToolsUi.Text, AuraToolsUi.TextMinHeight, 1f);
        var next = AuraToolsUi.AddButton(footer.transform, "下一页", () => NextPage(page.NextCursor), 88f);
        next.interactable = page.HasMore;
    }

    private static void AddRecordRow(Transform parent, MatchRecord item)
    {
        var row = AuraToolsUi.CreateLayout("MatchRecord-" + item.RecordId, parent);
        AuraToolsUi.SetFixedHeight(row, 76f);
        AuraToolsUi.AddImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var title = (string.IsNullOrWhiteSpace(item.LevelId) ? "未知战斗" : item.LevelId)
                    + "   " + ResultLabel(item.Result)
                    + "   " + LocalTime(item.EndedUtc);
        var detail = "回合 " + item.TurnCount
                     + "   事件 " + item.EventCount
                     + "   DPT伤害 " + TotalDamage(item.StatisticsJson)
                     + "   " + FormatBytes(item.CompressedBytes);
        AuraToolsUi.AddText(row.transform, title + "\n" + detail, AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 60f, 1f);
        AuraToolsUi.AddButton(row.transform, "分析", () => MatchAnalysisPresenter.Show(host!, item), 76f);
        AuraToolsUi.AddButton(row.transform, "回放", () => Replay(item.RecordId), 76f);
        AuraToolsUi.AddButton(
            row.transform,
            item.Collection == MatchRecordCollections.Favorite ? "移回自动" : "收藏",
            () => Move(item),
            92f);
        AuraToolsUi.AddButton(row.transform, armedDeleteId == item.RecordId ? "确认删除" : "删除", () => Delete(item.RecordId), 86f);
    }

    private static void Replay(string recordId)
    {
        if (MatchReplayPlayer.TryStart(recordId, out var result))
        {
            if (host != null)
            {
                AuraToolsUi.CloseOverlay(host, OverlayName, "Match record replay started");
            }

            WitchUiManager.Instance?.CloseUI("SettingUI");
            ResetState();
            return;
        }

        message = result;
        Build();
    }

    private static void Move(MatchRecord item)
    {
        var destination = item.Collection == MatchRecordCollections.Favorite
            ? MatchRecordCollections.Auto
            : MatchRecordCollections.Favorite;
        MatchRecordStorage.Database.SetCollection(item.RecordId, destination);
        if (destination == MatchRecordCollections.Auto)
        {
            MatchRecordStorage.Database.EnforceAutoLimit(
                AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit);
        }

        message = destination == MatchRecordCollections.Favorite ? "已移入收藏对局。" : "已移回自动记录。";
        Build();
    }

    private static void ImportPackages()
    {
        try
        {
            MatchReplayPackageService.ImportInbox(out message);
            collection = MatchRecordCollections.Favorite;
            pageIndex = 0;
            Cursors.Clear();
            Cursors.Add(0);
        }
        catch (Exception ex)
        {
            message = "导入失败：" + ex.Message;
        }

        Build();
    }

    private static void PickPackage()
    {
        OptionalFileDialog.PickFileAsync(
            "导入 AuraTools 对局回放",
            new[]
            {
                new OptionalFileDialogFilter("AuraTools 回放包", "*.aurareplay"),
                new OptionalFileDialogFilter("所有文件", "*.*")
            },
            "aurareplay",
            MatchRecordStorage.ImportsDirectory,
            result =>
            {
                if (result.Selected)
                {
                    try
                    {
                        MatchReplayPackageService.Import(result.Path);
                        collection = MatchRecordCollections.Favorite;
                        pageIndex = 0;
                        Cursors.Clear();
                        Cursors.Add(0);
                        message = "回放包已导入收藏对局。";
                    }
                    catch (Exception ex)
                    {
                        message = "导入失败：" + ex.Message;
                    }

                    Build();
                }
                else if (result.Status != OptionalFileDialogStatus.Cancelled)
                {
                    message = "文件选择器不可用，可将回放包放入导入目录后再打开本页面。";
                    Build();
                }
            });
    }

    private static void Delete(string recordId)
    {
        if (!string.Equals(armedDeleteId, recordId, StringComparison.Ordinal))
        {
            armedDeleteId = recordId;
            message = "再次点击同一条记录的“确认删除”即可永久删除。";
            Build();
            return;
        }

        MatchRecordStorage.Database.Delete(recordId);
        armedDeleteId = "";
        message = "对局记录已删除。";
        Build();
    }

    private static void ClearCurrent()
    {
        if (!clearArmed)
        {
            clearArmed = true;
            message = "再次点击“确认清空”将删除当前分类中的全部回放。";
            Build();
            return;
        }

        var removed = MatchRecordStorage.Database.Clear(collection);
        clearArmed = false;
        pageIndex = 0;
        Cursors.Clear();
        Cursors.Add(0);
        message = "已清空 " + removed + " 条对局记录。";
        Build();
    }

    private static void SwitchCollection(string value)
    {
        collection = value;
        pageIndex = 0;
        Cursors.Clear();
        Cursors.Add(0);
        message = "";
        armedDeleteId = "";
        clearArmed = false;
        Build();
    }

    private static void NextPage(long cursor)
    {
        if (cursor <= 0)
        {
            return;
        }

        if (pageIndex + 1 >= Cursors.Count)
        {
            Cursors.Add(cursor);
        }

        pageIndex++;
        Build();
    }

    private static void PreviousPage()
    {
        if (pageIndex > 0)
        {
            pageIndex--;
            Build();
        }
    }

    private static long TotalDamage(string json)
    {
        try
        {
            return (AuraSharedJson.Deserialize<DamageMeterSnapshot>(json)?.Combatants ?? new List<CombatantDamageStat>())
                .Where(item => item != null)
                .Sum(item => Math.Max(0, item.TotalHpDamage) + Math.Max(0, item.TotalShieldDamage));
        }
        catch
        {
            return 0;
        }
    }

    private static string ResultLabel(string value)
    {
        return value.IndexOf("win", StringComparison.OrdinalIgnoreCase) >= 0 ? "胜利"
            : value.IndexOf("loss", StringComparison.OrdinalIgnoreCase) >= 0 ? "失败"
            : value.IndexOf("escape", StringComparison.OrdinalIgnoreCase) >= 0 ? "撤退"
            : value.IndexOf("restart", StringComparison.OrdinalIgnoreCase) >= 0 ? "重开"
            : value;
    }

    private static string LocalTime(string value)
    {
        return DateTime.TryParse(value, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : value;
    }

    private static string DatabaseSizeLabel()
    {
        try
        {
            var path = MatchRecordStorage.Database.DatabasePath;
            return File.Exists(path) ? "数据库 " + FormatBytes(new FileInfo(path).Length) : "数据库 0 B";
        }
        catch
        {
            return "";
        }
    }

    private static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024L)
        {
            return (value / (1024d * 1024d)).ToString("0.0") + " MB";
        }

        return value >= 1024L ? (value / 1024d).ToString("0.0") + " KB" : value + " B";
    }

    private static void ResetState()
    {
        host = null;
        body = null;
        message = "";
        armedDeleteId = "";
        clearArmed = false;
    }
}
