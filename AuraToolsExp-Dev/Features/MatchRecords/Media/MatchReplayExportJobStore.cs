using System;
using System.IO;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class MatchReplayExportJobStore
{
    private static string DirectoryPath => Path.Combine(MatchRecordStorage.RootDirectory, "ExportJobs");
    private static string CurrentPath => Path.Combine(DirectoryPath, "current.json");

    internal static MatchReplayExportJob? Load()
    {
        try
        {
            if (!File.Exists(CurrentPath)) return null;
            return AuraSharedJson.Deserialize<MatchReplayExportJob>(File.ReadAllText(CurrentPath, Encoding.UTF8));
        }
        catch
        {
            return null;
        }
    }

    internal static void Save(MatchReplayExportJob job)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = CurrentPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, AuraSharedJson.SerializeCompact(job), Encoding.UTF8);
        try
        {
            if (File.Exists(CurrentPath))
            {
                File.Replace(temporary, CurrentPath, null);
            }
            else
            {
                File.Move(temporary, CurrentPath);
            }
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
