using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraShared.Core;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.PixelEmoji;

public static class PixelEmojiLibraryStore
{
    private const string FileName = "PixelEmojiLibrary.json";
    private static readonly object Gate = new();
    private static PixelEmojiLibrary library = new();
    private static long revision;
    private static bool loaded;
    private static bool readOnly;

    public static string DataDirectory => AuraSharedPaths.OwnerSystemDataDirectory(AuraToolsIds.ModId, "PixelEmoji");

    public static string RenderedDirectory => Path.Combine(DataDirectory, "Rendered");

    public static IReadOnlyList<PixelEmojiDocument> GetItems()
    {
        lock (Gate)
        {
            EnsureLoadedNoLock();
            return library.Items.Select(Clone).ToList();
        }
    }

    public static PixelEmojiDocument? Find(string itemId)
    {
        lock (Gate)
        {
            EnsureLoadedNoLock();
            var item = library.Items.FirstOrDefault(value => string.Equals(value.Id, itemId, StringComparison.OrdinalIgnoreCase));
            return item == null ? null : Clone(item);
        }
    }

    public static bool Save(
        string itemId,
        string name,
        IReadOnlyList<byte[]> frames,
        PixelEmojiPlaybackMode playbackMode,
        out PixelEmojiDocument saved,
        out string error)
    {
        saved = new PixelEmojiDocument();
        error = "";
        if (!PixelEmojiAnimationCodec.IsValidFrames(frames)
            || !PixelEmojiAnimationCodec.IsValidPlaybackMode(playbackMode))
        {
            error = "作品动画数据无效。";
            return false;
        }

        var sourceFrames = PixelEmojiAnimationCodec.CloneFrames(frames);

        lock (Gate)
        {
            EnsureLoadedNoLock();
            if (readOnly)
            {
                error = "作品库来自更新版本，当前仅可查看，不能覆盖。";
                return false;
            }
            var id = (itemId ?? "").Trim();
            var existing = library.Items.FirstOrDefault(value => string.Equals(value.Id, id, StringComparison.OrdinalIgnoreCase));
            var now = DateTime.UtcNow.Ticks;
            if (existing == null)
            {
                if (library.Items.Count >= PixelEmojiLibrary.MaximumItems)
                {
                    error = "作品库已达到 256 个上限。";
                    return false;
                }

                existing = new PixelEmojiDocument
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CreatedUtcTicks = now
                };
                library.Items.Add(existing);
            }

            existing.Name = name;
            existing.PaletteVersion = PixelEmojiCodec.PaletteVersion;
            existing.FramesBase64 = PixelEmojiAnimationCodec.EncodeFrames(sourceFrames);
            existing.PixelsBase64 = existing.FramesBase64[0];
            existing.PlaybackMode = playbackMode;
            existing.ModifiedUtcTicks = now;
            if (!existing.TryNormalize())
            {
                error = "作品数据无法标准化。";
                return false;
            }

            library.Normalize();
            if (!WriteNoLock(Array.Empty<string>(), out error))
            {
                ReloadNoLock();
                return false;
            }

            saved = Clone(existing);
            return true;
        }
    }

    public static bool Delete(string itemId, out string error)
    {
        error = "";
        lock (Gate)
        {
            EnsureLoadedNoLock();
            if (readOnly)
            {
                error = "作品库来自更新版本，当前仅可查看，不能覆盖。";
                return false;
            }
            var removed = library.Items.RemoveAll(value => string.Equals(value.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                error = "作品不存在。";
                return false;
            }

            if (!WriteNoLock(new[] { itemId }, out error))
            {
                ReloadNoLock();
                return false;
            }
        }

        try
        {
            DeleteRenderedOutput(itemId);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[PixelEmoji] failed to remove rendered cache: " + ex.Message);
        }

        return true;
    }

    public static string RenderedItemDirectory(string itemId)
    {
        return Path.Combine(RenderedDirectory, SafeItemId(itemId));
    }

    public static string RenderedFramePath(string itemId, string name, int frameNumber)
    {
        return Path.Combine(RenderedItemDirectory(itemId), PixelEmojiExportPolicy.FrameFileName(name, frameNumber));
    }

    public static bool WriteRenderedSequence(
        string itemId,
        string name,
        IReadOnlyList<byte[]> pngFrames,
        out string error)
    {
        error = "";
        if (pngFrames == null
            || pngFrames.Count < PixelEmojiAnimationCodec.MinimumFrames
            || pngFrames.Count > PixelEmojiAnimationCodec.MaximumFrames
            || pngFrames.Any(bytes => bytes == null || bytes.Length == 0))
        {
            error = "PNG序列数据无效。";
            return false;
        }

        var target = RenderedItemDirectory(itemId);
        var staging = target + ".staging-" + Guid.NewGuid().ToString("N");
        var backup = target + ".backup-" + Guid.NewGuid().ToString("N");
        var movedExisting = false;
        try
        {
            Directory.CreateDirectory(RenderedDirectory);
            Directory.CreateDirectory(staging);
            for (var index = 0; index < pngFrames.Count; index++)
            {
                AuraSharedFileStore.WriteAllBytes(
                    AuraToolsIds.ModId,
                    Path.Combine(staging, PixelEmojiExportPolicy.FrameFileName(name, index + 1)),
                    pngFrames[index]);
            }

            if (Directory.Exists(target))
            {
                AuraSharedFileStore.MoveDirectory(AuraToolsIds.ModId, target, backup);
                movedExisting = true;
            }
            AuraSharedFileStore.MoveDirectory(AuraToolsIds.ModId, staging, target);
            if (movedExisting) TryDeleteDirectory(backup);

            var legacy = LegacyRenderedPath(itemId);
            try
            {
                if (File.Exists(legacy)) File.Delete(legacy);
            }
            catch (Exception ex)
            {
                AuraToolsLog.Warn("[PixelEmoji] failed to remove legacy rendered image: " + ex.Message);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try
            {
                if (movedExisting && !Directory.Exists(target) && Directory.Exists(backup))
                {
                    AuraSharedFileStore.MoveDirectory(AuraToolsIds.ModId, backup, target);
                }
            }
            catch (Exception restoreEx)
            {
                error += "；旧序列恢复失败：" + restoreEx.Message;
            }
            return false;
        }
        finally
        {
            TryDeleteDirectory(staging);
            if (Directory.Exists(target)) TryDeleteDirectory(backup);
        }
    }

    private static void EnsureLoadedNoLock()
    {
        if (loaded)
        {
            return;
        }

        ReloadNoLock();
    }

    private static void ReloadNoLock()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(RenderedDirectory);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[PixelEmoji] data directory initialization failed: " + ex.Message);
        }

        var snapshot = AuraSharedConfigStore.ReadOwner(
            AuraToolsIds.ModId,
            AuraToolsPaths.ConfigSystem,
            FileName,
            new PixelEmojiLibrary());
        library = snapshot.Value ?? new PixelEmojiLibrary();
        readOnly = snapshot.Found
                   && (snapshot.SchemaVersion
                       > PixelEmojiLibrary.CurrentSchemaVersion
                       || library.SchemaVersion
                       > PixelEmojiLibrary.CurrentSchemaVersion);
        if (readOnly)
        {
            library.Items ??= new List<PixelEmojiDocument>();
            revision = snapshot.Revision;
            loaded = true;
            AuraToolsLog.Warn(
                "[PixelEmoji] newer library schema opened read-only: envelope="
                + snapshot.SchemaVersion
                + ", value=" + library.SchemaVersion);
            return;
        }
        library.Normalize();
        revision = snapshot.Revision;
        loaded = true;
    }

    private static bool WriteNoLock(IReadOnlyCollection<string> deletedIds, out string error)
    {
        if (readOnly)
        {
            error = "作品库来自更新版本，当前仅可查看，不能覆盖。";
            return false;
        }
        var result = AuraSharedConfigStore.WriteOwner(
            AuraToolsIds.ModId,
            AuraToolsPaths.ConfigSystem,
            FileName,
            library,
            revision,
            PixelEmojiLibrary.CurrentSchemaVersion);
        if (result.Conflict)
        {
            var pending = library.Items.Select(Clone).ToList();
            ReloadNoLock();
            if (deletedIds != null && deletedIds.Count > 0)
            {
                library.Items.RemoveAll(item => deletedIds.Any(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase)));
            }
            foreach (var item in pending)
            {
                var current = library.Items.FirstOrDefault(value => string.Equals(value.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                if (current == null)
                {
                    library.Items.Add(item);
                }
                else if (item.ModifiedUtcTicks >= current.ModifiedUtcTicks)
                {
                    library.Items.Remove(current);
                    library.Items.Add(item);
                }
            }
            library.Normalize();
            result = AuraSharedConfigStore.WriteOwner(
                AuraToolsIds.ModId,
                AuraToolsPaths.ConfigSystem,
                FileName,
                library,
                revision,
                PixelEmojiLibrary.CurrentSchemaVersion);
        }

        if (!result.Success)
        {
            error = string.IsNullOrWhiteSpace(result.Message) ? "作品库写入失败。" : result.Message;
            return false;
        }

        revision = result.Revision;
        error = "";
        return true;
    }

    private static PixelEmojiDocument Clone(PixelEmojiDocument value)
    {
        return new PixelEmojiDocument
        {
            Id = value.Id,
            Name = value.Name,
            PaletteVersion = value.PaletteVersion,
            PixelsBase64 = value.PixelsBase64,
            FramesBase64 = (value.FramesBase64 ?? new List<string>()).ToList(),
            PlaybackMode = value.PlaybackMode,
            CreatedUtcTicks = value.CreatedUtcTicks,
            ModifiedUtcTicks = value.ModifiedUtcTicks
        };
    }

    private static string SafeItemId(string itemId)
    {
        var safe = new string((itemId ?? "").Where(char.IsLetterOrDigit).Take(64).ToArray());
        return safe.Length == 0 ? "unknown" : safe;
    }

    private static string LegacyRenderedPath(string itemId)
    {
        return Path.Combine(RenderedDirectory, SafeItemId(itemId) + ".png");
    }

    private static void DeleteRenderedOutput(string itemId)
    {
        var directory = RenderedItemDirectory(itemId);
        TryDeleteDirectory(directory);
        var legacy = LegacyRenderedPath(itemId);
        if (File.Exists(legacy)) File.Delete(legacy);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[PixelEmoji] failed to remove rendered directory: " + ex.Message);
        }
    }
}
