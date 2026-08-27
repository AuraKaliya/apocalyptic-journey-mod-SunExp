using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal sealed partial class MatchRecordDatabase
{
    internal string ResolveReplayAsset(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return "";
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare("SELECT file_path FROM replay_assets WHERE asset_sha256=? LIMIT 1;");
            query.Bind(1, sha256.Trim());
            return query.Read() ? ResolveStoredPath(query.Text(0)) : "";
        }
    }

    internal void CreateExportJob(MatchReplayExportJob job)
    {
        if (job == null || string.IsNullOrWhiteSpace(job.JobId) || string.IsNullOrWhiteSpace(job.RecordId))
            throw new ArgumentException("Replay export job identity is missing.", nameof(job));
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            job.Revision = 0;
            job.CreatedUtc = string.IsNullOrWhiteSpace(job.CreatedUtc) ? DateTime.UtcNow.ToString("O") : job.CreatedUtc;
            job.UpdatedUtc = DateTime.UtcNow.ToString("O");
            InsertExportJob(connection, job);
        }
    }

    internal bool UpdateExportJob(MatchReplayExportJob job)
    {
        if (job == null || string.IsNullOrWhiteSpace(job.JobId)) return false;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            var expected = job.Revision;
            job.UpdatedUtc = DateTime.UtcNow.ToString("O");
            using var update = connection.Prepare(
                "UPDATE replay_export_jobs SET state=?, revision=revision+1, updated_utc=?, progress=?, staging_path=?, "
                + "target_path=?, output_sha256=?, profile_id=?, message=?, error_code=?, cancel_requested=?, attempt_count=?, "
                + "width=?, height=?, frames_per_second=?, frame_count=?, audio_sample_frames=?, file_bytes=?, estimated_bytes=? "
                + "WHERE job_id=? AND revision=?;");
            BindExportJobUpdate(update, job);
            update.Bind(19, job.JobId);
            update.Bind(20, expected);
            update.Execute();
            if (connection.Changes <= 0) return false;
            job.Revision = expected + 1;
            return true;
        }
    }

    internal MatchReplayExportJob? LoadExportJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(ExportJobSelect + " WHERE job_id=? LIMIT 1;");
            query.Bind(1, jobId.Trim());
            return query.Read() ? ReadExportJob(query) : null;
        }
    }

    internal MatchReplayExportJob? LoadLatestExportJob()
    {
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(ExportJobSelect + " ORDER BY created_utc DESC LIMIT 1;");
            return query.Read() ? ReadExportJob(query) : null;
        }
    }

    internal IReadOnlyList<MatchReplayExportJob> LoadRecoverableExportJobs()
    {
        var result = new List<MatchReplayExportJob>();
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                ExportJobSelect + " WHERE state NOT IN ('Ready','Corrupt','Failed','Cancelled') ORDER BY created_utc;");
            while (query.Read()) result.Add(ReadExportJob(query));
        }
        return result;
    }

    internal bool CommitExportMedia(MatchReplayExportJob job, MatchMediaAsset asset)
    {
        if (job == null || asset == null
            || !string.Equals(job.JobId, asset.MediaId, StringComparison.Ordinal)
            || !string.Equals(job.RecordId, asset.RecordId, StringComparison.Ordinal))
            throw new InvalidDataException("Replay export commit identity mismatch.");
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                if (!Exists(connection, job.RecordId)) throw new InvalidDataException("Cannot commit media for a missing match record.");
                using (var insert = connection.Prepare(
                           "INSERT OR REPLACE INTO replay_media(media_id, record_id, media_kind, media_format, file_path, created_utc, "
                           + "media_state, duration_ms, width, height, frames_per_second, file_bytes, sha256, timeline_payload, error_text) "
                           + "VALUES(?, ?, 'Video', 'MP4', ?, ?, 'Ready', ?, ?, ?, ?, ?, ?, ?, '');"))
                {
                    insert.Bind(1, asset.MediaId);
                    insert.Bind(2, asset.RecordId);
                    insert.Bind(3, asset.FilePath);
                    insert.Bind(4, asset.CreatedUtc);
                    insert.Bind(5, asset.DurationMilliseconds);
                    insert.Bind(6, asset.Width);
                    insert.Bind(7, asset.Height);
                    insert.Bind(8, asset.FramesPerSecond);
                    insert.Bind(9, asset.FileBytes);
                    insert.Bind(10, asset.Sha256);
                    insert.Bind(11, MatchReplayPayload.Encode(asset.TimelineJson ?? ""));
                    insert.Execute();
                }
                var expected = job.Revision;
                job.State = MatchReplayExportStates.Ready;
                job.Progress = 1f;
                job.OutputPath = asset.FilePath;
                job.TargetPath = asset.FilePath;
                job.FileBytes = asset.FileBytes;
                job.OutputSha256 = asset.Sha256;
                job.UpdatedUtc = DateTime.UtcNow.ToString("O");
                using (var update = connection.Prepare(
                           "UPDATE replay_export_jobs SET state='Ready', revision=revision+1, updated_utc=?, progress=1, "
                           + "target_path=?, output_sha256=?, message=?, file_bytes=? WHERE job_id=? AND revision=? AND state='Committing';"))
                {
                    update.Bind(1, job.UpdatedUtc);
                    update.Bind(2, job.TargetPath);
                    update.Bind(3, job.OutputSha256);
                    update.Bind(4, job.Message ?? "");
                    update.Bind(5, job.FileBytes);
                    update.Bind(6, job.JobId);
                    update.Bind(7, expected);
                    update.Execute();
                    if (connection.Changes <= 0) { connection.Execute("ROLLBACK;"); return false; }
                }
                connection.Execute("COMMIT;");
                job.Revision = expected + 1;
                return true;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }
    }

    internal MatchMediaAsset? LoadMediaForDeletion(string mediaId)
    {
        if (string.IsNullOrWhiteSpace(mediaId)) return null;
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare(
                "SELECT media_id, record_id, media_kind, media_format, file_path, created_utc, media_state, duration_ms, width, height, "
                + "frames_per_second, file_bytes, sha256, timeline_payload, error_text FROM replay_media WHERE media_id=? LIMIT 1;");
            query.Bind(1, mediaId.Trim());
            return query.Read() ? ReadMedia(query) : null;
        }
    }

    internal IReadOnlyList<string> LoadAllMediaPaths()
    {
        var result = new List<string>();
        lock (gate)
        {
            EnsureInitialized();
            using var connection = Open();
            using var query = connection.Prepare("SELECT file_path FROM replay_media;");
            while (query.Read()) result.Add(query.Text(0));
        }
        return result;
    }

    private static MatchMediaAsset ReadMedia(WinSqliteConnection.WinSqliteStatement query) => new()
    {
        MediaId = query.Text(0),
        RecordId = query.Text(1),
        Kind = query.Text(2),
        Format = query.Text(3),
        FilePath = query.Text(4),
        CreatedUtc = query.Text(5),
        State = query.Text(6),
        DurationMilliseconds = query.Int64(7),
        Width = (int)query.Int64(8),
        Height = (int)query.Int64(9),
        FramesPerSecond = query.Double(10),
        FileBytes = query.Int64(11),
        Sha256 = query.Text(12),
        TimelineJson = MatchReplayPayload.Decode<string>(query.Blob(13)) ?? "",
        Error = query.Text(14)
    };

    private void SweepUnreferencedReplayAssets()
    {
        var candidates = new List<(string Sha256, string Path, string Staging)>();
        using (var connection = Open())
        using (var query = connection.Prepare(
                   "SELECT a.asset_sha256, a.file_path FROM replay_assets a "
                   + "LEFT JOIN replay_asset_refs r ON r.asset_sha256=a.asset_sha256 "
                   + "LEFT JOIN replay_pov_asset_refs p ON p.asset_sha256=a.asset_sha256 "
                   + "WHERE r.asset_sha256 IS NULL AND p.asset_sha256 IS NULL;"))
            while (query.Read())
            {
                var path = ResolveStoredPath(query.Text(1));
                candidates.Add((query.Text(0), path, path + ".delete.partial"));
            }
        if (candidates.Count == 0) return;
        var moved = new List<(string Original, string Staging)>();
        try
        {
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate.Path)) continue;
                if (File.Exists(candidate.Staging)) AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, candidate.Staging);
                AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, candidate.Path, candidate.Staging);
                moved.Add((candidate.Path, candidate.Staging));
            }
            using var connection = Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                foreach (var candidate in candidates)
                {
                    using var delete = connection.Prepare(
                        "DELETE FROM replay_assets WHERE asset_sha256=? "
                        + "AND NOT EXISTS(SELECT 1 FROM replay_asset_refs WHERE asset_sha256=?) "
                        + "AND NOT EXISTS(SELECT 1 FROM replay_pov_asset_refs WHERE asset_sha256=?);");
                    delete.Bind(1, candidate.Sha256);
                    delete.Bind(2, candidate.Sha256);
                    delete.Bind(3, candidate.Sha256);
                    delete.Execute();
                }
                connection.Execute("COMMIT;");
            }
            catch { TryRollback(connection); throw; }
            foreach (var pair in moved)
                try { if (File.Exists(pair.Staging)) AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, pair.Staging); } catch { }
        }
        catch
        {
            foreach (var pair in moved)
                try { if (File.Exists(pair.Staging) && !File.Exists(pair.Original)) AuraSharedFileStore.MoveFile(AuraToolsIds.ModId, pair.Staging, pair.Original); } catch { }
            throw;
        }
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream).Select(item => item.ToString("x2")));
    }

    private static void CommitAttachments(IEnumerable<AttachmentMove> moves)
    {
        foreach (var move in moves)
        {
            move.Transaction.Commit();
            move.Committed = true;
            move.Transaction.Dispose();
        }
    }

    private static void CleanupStaging(IEnumerable<AttachmentMove> moves)
    {
        foreach (var move in moves) try { move.Transaction.Dispose(); } catch { }
    }

    private static void CleanupCommittedAttachments(IEnumerable<AttachmentMove> moves)
    {
        foreach (var move in moves)
            try
            {
                if (move.Committed && File.Exists(move.FinalPath))
                    AuraSharedFileStore.DeleteFile(AuraToolsIds.ModId, move.FinalPath);
            }
            catch { }
    }

    private string AttachmentDirectory => Path.Combine(Path.GetDirectoryName(databasePath) ?? ".", "Attachments");

    private string ToStoredPath(string fullPath)
    {
        var root = Path.GetFullPath(Path.GetDirectoryName(databasePath) ?? ".")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(fullPath);
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replay asset path escapes the MatchRecords root.");
        return resolved.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    private string ResolveStoredPath(string storedPath)
    {
        var root = Path.GetFullPath(Path.GetDirectoryName(databasePath) ?? ".")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, (storedPath ?? "").Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replay asset path escapes the MatchRecords root.");
        return resolved;
    }

    private static string NormalizeExtension(string extension)
    {
        var value = (extension ?? "").Trim().ToLowerInvariant();
        return value is ".png" or ".wav" ? value : ".bin";
    }

    private sealed class AttachmentMove
    {
        internal AttachmentMove(AuraSharedFileWriteTransaction transaction, string finalPath)
        {
            Transaction = transaction;
            FinalPath = finalPath;
        }
        internal AuraSharedFileWriteTransaction Transaction { get; }
        internal string FinalPath { get; }
        internal bool Committed { get; set; }
    }

    private static void InsertExportJob(WinSqliteConnection connection, MatchReplayExportJob job)
    {
        using var insert = connection.Prepare(
            "INSERT INTO replay_export_jobs(job_id, record_id, state, revision, created_utc, updated_utc, progress, staging_path, "
            + "target_path, output_sha256, profile_id, message, error_code, cancel_requested, attempt_count, width, height, "
            + "frames_per_second, frame_count, audio_sample_frames, file_bytes, estimated_bytes) "
            + "VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);");
        insert.Bind(1, job.JobId); insert.Bind(2, job.RecordId); insert.Bind(3, job.State); insert.Bind(4, job.Revision);
        insert.Bind(5, job.CreatedUtc); insert.Bind(6, job.UpdatedUtc); insert.Bind(7, job.Progress); insert.Bind(8, job.StagingPath ?? "");
        insert.Bind(9, job.TargetPath ?? ""); insert.Bind(10, job.OutputSha256 ?? ""); insert.Bind(11, job.ProfileId ?? "");
        insert.Bind(12, job.Message ?? ""); insert.Bind(13, job.ErrorCode ?? ""); insert.Bind(14, job.CancelRequested ? 1 : 0);
        insert.Bind(15, job.AttemptCount); insert.Bind(16, job.Width); insert.Bind(17, job.Height); insert.Bind(18, job.FramesPerSecond);
        insert.Bind(19, job.FrameCount); insert.Bind(20, job.AudioSampleFrames); insert.Bind(21, job.FileBytes); insert.Bind(22, job.EstimatedBytes);
        insert.Execute();
    }

    private static void BindExportJobUpdate(WinSqliteConnection.WinSqliteStatement update, MatchReplayExportJob job)
    {
        update.Bind(1, job.State ?? MatchReplayExportStates.Failed); update.Bind(2, job.UpdatedUtc ?? ""); update.Bind(3, job.Progress);
        update.Bind(4, job.StagingPath ?? ""); update.Bind(5, job.TargetPath ?? ""); update.Bind(6, job.OutputSha256 ?? "");
        update.Bind(7, job.ProfileId ?? ""); update.Bind(8, job.Message ?? ""); update.Bind(9, job.ErrorCode ?? "");
        update.Bind(10, job.CancelRequested ? 1 : 0); update.Bind(11, job.AttemptCount); update.Bind(12, job.Width);
        update.Bind(13, job.Height); update.Bind(14, job.FramesPerSecond); update.Bind(15, job.FrameCount);
        update.Bind(16, job.AudioSampleFrames); update.Bind(17, job.FileBytes); update.Bind(18, job.EstimatedBytes);
    }

    private static MatchReplayExportJob ReadExportJob(WinSqliteConnection.WinSqliteStatement query) => new()
    {
        JobId = query.Text(0), RecordId = query.Text(1), State = query.Text(2), Revision = query.Int64(3),
        CreatedUtc = query.Text(4), UpdatedUtc = query.Text(5), Progress = (float)query.Double(6), StagingPath = query.Text(7),
        TargetPath = query.Text(8), OutputPath = query.Text(8), OutputSha256 = query.Text(9), ProfileId = query.Text(10),
        Message = query.Text(11), ErrorCode = query.Text(12), CancelRequested = query.Int64(13) != 0,
        AttemptCount = (int)query.Int64(14), Width = (int)query.Int64(15), Height = (int)query.Int64(16),
        FramesPerSecond = (int)query.Int64(17), FrameCount = query.Int64(18), AudioSampleFrames = query.Int64(19),
        FileBytes = query.Int64(20), EstimatedBytes = query.Int64(21)
    };

    private const string ExportJobSelect =
        "SELECT job_id, record_id, state, revision, created_utc, updated_utc, progress, staging_path, target_path, "
        + "output_sha256, profile_id, message, error_code, cancel_requested, attempt_count, width, height, frames_per_second, "
        + "frame_count, audio_sample_frames, file_bytes, estimated_bytes FROM replay_export_jobs";
}
