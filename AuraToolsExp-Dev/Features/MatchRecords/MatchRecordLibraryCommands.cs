using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Portability;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;
using AuraToolsExp.Dll.Infrastructure;
using AuraUi.Shared;

namespace AuraToolsExp.Dll.Features.MatchRecords;

internal static partial class MatchRecordLibraryPresenter
{
    private static void SaveMetadata()
    {
        var id = editingId; var tags = editingTags; var notes = editingNotes;
        RunLibraryOperation("LibrarySaveMetadata", () => MatchRecordStorage.Database.UpdateMetadata(id, tags, notes), saved =>
        {
            message = saved ? "标签和备注已保存。" : "记录不存在，未保存。";
            if (saved && editingId == id) editingId = "";
        });
    }

    private static void ExportSelected()
    {
        var ids = SelectedIds.ToArray();
        RunLibraryOperation("LibraryExport", () =>
        {
            var exported = new List<string>(); var errors = new List<string>();
            foreach (var id in ids)
            {
                try { MatchReplayPackageService.Export(id); exported.Add(id); }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            return (exported, errors);
        }, result =>
        {
            SelectedIds.ExceptWith(result.exported);
            message = "已导出 " + result.exported.Count + " 条回放。";
            if (result.errors.Count > 0) message += "失败 " + result.errors.Count + " 条：" + string.Join("；", result.errors.Take(2));
        });
    }

    private static void Move(MatchRecord item)
    {
        var destination = item.Collection == MatchRecordCollections.Favorite ? MatchRecordCollections.Auto : MatchRecordCollections.Favorite;
        var id = item.RecordId;
        var limit = AuraToolsConfigService.MatchExperience.MatchRecords.Replay.AutoRecordLimit;
        RunLibraryOperation("LibraryMove", () =>
        {
            var store = MatchRecordStorage.Database;
            if (store.Get(id) == null) throw new InvalidOperationException("对局记录已不存在。");
            if (!store.SetCollection(id, destination)) throw new InvalidOperationException("对局记录已不存在。");
            if (destination == MatchRecordCollections.Auto) store.EnforceAutoLimit(limit);
            return destination;
        }, value => message = value == MatchRecordCollections.Favorite ? "已移入收藏。" : "已移回自动记录。");
    }

    private static void ImportPackages()
    {
        var directory = MatchRecordStorage.ImportsDirectory;
        RunLibraryOperation("LibraryScanImports", () =>
        {
            var paths = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.aurareplay") : Array.Empty<string>();
            return paths.Length == 0 ? null : MatchReplayPackageService.Inspect(paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First());
        }, preview =>
        {
            pendingImportPreview = preview; pendingImportPath = preview?.Path ?? "";
            message = preview == null ? "导入目录中没有回放包。" : preview.CompatibilityMessage + " 请确认当前预览后导入。";
        });
    }

    private static void PickPackage()
    {
        OptionalFileDialog.PickFileAsync("导入 AuraTools 对局回放",
            new[] { new OptionalFileDialogFilter("AuraTools 回放包", "*.aurareplay"), new OptionalFileDialogFilter("所有文件", "*.*") },
            "aurareplay", MatchRecordStorage.ImportsDirectory, result =>
            {
                if (result.Selected)
                    RunLibraryOperation("LibraryInspect", () => MatchReplayPackageService.Inspect(result.Path), preview =>
                    {
                        pendingImportPreview = preview; pendingImportPath = result.Path;
                        message = preview.CompatibilityMessage + " 请确认后导入。";
                    });
                else if (result.Status != OptionalFileDialogStatus.Cancelled)
                { message = "文件选择器不可用，可将回放包放入导入目录后扫描。"; RefreshStatus(); }
            });
    }

    private static void ConfirmImport()
    {
        if (pendingImportPreview == null || string.IsNullOrWhiteSpace(pendingImportPath)) return;
        var path = pendingImportPath;
        var directory = MatchRecordStorage.ImportsDirectory;
        RunLibraryOperation("LibraryImport", () =>
        {
            MatchReplayPackageService.Import(path);
            var warning = "";
            var inbox = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var sourceDirectory = Path.GetFullPath(Path.GetDirectoryName(path) ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(inbox, sourceDirectory, StringComparison.OrdinalIgnoreCase))
                try
                {
                    var completed = Path.Combine(inbox, "Imported"); Directory.CreateDirectory(completed);
                    AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, path, UniqueLibraryPath(Path.Combine(completed, Path.GetFileName(path))));
                }
                catch (Exception ex) { warning = " 源文件归档失败：" + ex.Message; }
            return warning;
        }, warning =>
        {
            collection = MatchRecordCollections.Favorite; ResetPaging();
            pendingImportPath = ""; pendingImportPreview = null;
            message = "回放包已写入收藏。" + warning;
        });
    }

    private static void Delete(string id)
    {
        if (!string.Equals(armedDeleteId, id, StringComparison.Ordinal))
        {
            armedDeleteId = id; message = "再次点击该记录的“确认删除”即可永久删除。";
            RefreshRecordRow(id); RefreshStatus(); return;
        }
        RunLibraryOperation("LibraryDelete", () => MatchRecordStorage.Database.Delete(id), removed =>
        {
            SelectedIds.Remove(id); armedDeleteId = "";
            message = removed ? "对局记录已删除。" : "该记录已不存在。";
        });
    }

    private static void ClearCurrent()
    {
        if (!clearArmed)
        {
            clearArmed = true; message = "再次点击将清空当前分类中已经结束的记录。"; Build(); return;
        }
        var selectedCollection = collection;
        RunLibraryOperation("LibraryClear", () => selectedCollection == AdventureCollection
            ? DamageHistoryStorage.Database.ClearAdventures() : MatchRecordStorage.Database.Clear(selectedCollection), removed =>
        {
            clearArmed = false; SelectedIds.Clear(); ResetPaging();
            message = "已清空 " + removed + " 条记录。";
        });
    }
}
