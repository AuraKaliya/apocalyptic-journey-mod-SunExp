using System.IO;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static class MatchRecordStorage
{
    private static readonly object Gate = new();
    private static MatchRecordDatabase? database;

    internal static MatchRecordDatabase Database
    {
        get
        {
            lock (Gate)
            {
                if (database == null)
                {
                    var history = DamageHistoryStorage.Database;
                    database = new MatchRecordDatabase(history.DatabasePath);
                    database.Initialize();
                    var recovered = database.RecoverFinalizingCapturesV17();
                    if (recovered > 0)
                        AuraToolsLog.Info("[MatchRecords] recovered " + recovered + " finalizing v17 replay(s).");
                }

                return database;
            }
        }
    }

    internal static string RootDirectory => Path.GetDirectoryName(Database.DatabasePath) ?? ".";

    internal static string ExportsDirectory => Ensure("Exports");

    internal static string ImportsDirectory => Ensure("Imports");

    internal static string MediaDirectory => Ensure("Media");

    internal static string TemporaryDirectory => Ensure("Temporary");

    private static string Ensure(string name)
    {
        var path = Path.Combine(RootDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
