using AuraToolsExp.Dll.Features.DamageMeter.Storage;

namespace AuraToolsExp.Dll.Features.AdventureArchive;

internal static class AdventureArchiveStorage
{
    private static readonly object Gate = new();
    private static AdventureArchiveDatabase? database;

    internal static AdventureArchiveDatabase Database
    {
        get
        {
            lock (Gate)
            {
                if (database == null)
                {
                    database = new AdventureArchiveDatabase(DamageHistoryStorage.Database.DatabasePath);
                    database.Initialize();
                }
                return database;
            }
        }
    }
}
