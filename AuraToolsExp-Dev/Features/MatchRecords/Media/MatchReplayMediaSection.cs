using System;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Features.Settings;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayMediaSection
{
    internal static void Build(Transform overlayHost, Transform parent, MatchRecord record, Action<string> notify)
    {
        var actions = Row("MediaActions", parent, AuraToolsUi.ToolbarHeight);
        AuraToolsUi.AddButton(actions, "导入目录", () => FileResourceUtil.OpenDirectory(MatchRecordStorage.ImportsDirectory), 92f);
        AuraToolsUi.AddButton(actions, "导入视频", () => PickVideo(record.RecordId, notify), 92f);
        AuraToolsUi.AddText(actions, "媒体库只接收经过完整解码验证的 MP4",
            AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.MutedText,
            AuraToolsUi.TextMinHeight, 1f);

        var allAssets = MatchRecordStorage.Database.LoadMedia(record.RecordId);
        var assets = allAssets.Where(item =>
                string.Equals(item.Format, "MP4", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.State, MatchMediaStates.Ready, StringComparison.Ordinal))
            .ToList();
        var legacyCount = allAssets.Count - assets.Count;
        if (legacyCount > 0)
        {
            AuraToolsUi.AddText(parent, legacyCount + " 个旧格式或损坏媒体等待迁移扫描处理。",
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.WarningText, 44f, 1f);
        }
        if (assets.Count == 0)
        {
            AuraToolsUi.AddText(parent, "本对局尚无视频媒体。结构化回放仍可正常使用。",
                AuraToolsUi.BodyFontSize, TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 72f, 1f);
            return;
        }

        foreach (var asset in assets)
        {
            var row = Row("Media-" + asset.MediaId, parent, 58f, withBackground: true);
            AuraToolsUi.AddText(row,
                asset.Format + "   " + FormatBytes(asset.FileBytes)
                + (asset.Width > 0 ? "   " + asset.Width + "x" + asset.Height : "")
                + (asset.DurationMilliseconds > 0 ? "   " + TimeSpan.FromMilliseconds(asset.DurationMilliseconds).ToString(@"mm\:ss") : ""),
                AuraToolsUi.HintFontSize, TextAnchor.MiddleLeft, AuraToolsUi.Text, 44f, 1f);
            var current = asset;
            AuraToolsUi.AddButton(row, "播放", () => MatchReplayVideoPlayer.Show(overlayHost, current), 76f);
            AuraToolsUi.AddButton(row, "位置", () =>
            {
                var directory = Path.GetDirectoryName(MatchReplayMediaStore.ResolvePath(current.FilePath));
                if (!string.IsNullOrWhiteSpace(directory)) FileResourceUtil.OpenDirectory(directory);
            }, 72f);
            AuraToolsUi.AddButton(row, "删除", () =>
            {
                MatchReplayMediaStore.Delete(current.MediaId);
                notify("媒体文件已删除。");
            }, 72f);
        }
    }

    private static Transform Row(string name, Transform parent, float height, bool withBackground = false)
    {
        var row = AuraToolsUi.CreateLayout(name, parent);
        AuraToolsUi.SetFixedHeight(row, height);
        if (withBackground) AuraToolsUi.AddImage(row, AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = withBackground ? new RectOffset(10, 10, 6, 6) : new RectOffset();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return row.transform;
    }

    private static string FormatBytes(long value)
    {
        return value >= 1024L * 1024L ? (value / (1024d * 1024d)).ToString("0.0") + " MB"
            : value >= 1024L ? (value / 1024d).ToString("0.0") + " KB"
            : value + " B";
    }

    private static void PickVideo(string recordId, Action<string> notify)
    {
        OptionalFileDialog.PickFileAsync(
            "导入对局视频",
            new[]
            {
                new OptionalFileDialogFilter("MP4 视频", "*.mp4"),
                new OptionalFileDialogFilter("所有文件", "*.*")
            },
            "mp4",
            MatchRecordStorage.ImportsDirectory,
            result =>
            {
                if (result.Selected)
                {
                    try
                    {
                        MatchReplayMediaStore.ImportFile(recordId, result.Path);
                        notify("视频已归一化为固定 MP4 profile、完整解码验证并导入；普通视频没有结构化回合跳转信息。");
                    }
                    catch (Exception ex)
                    {
                        notify("视频导入失败：" + ex.Message);
                    }
                }
                else if (result.Status != OptionalFileDialogStatus.Cancelled)
                {
                    notify("文件选择器不可用，可使用导入目录作为后备方式。");
                }
            });
    }
}
