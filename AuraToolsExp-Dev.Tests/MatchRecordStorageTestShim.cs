namespace AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static class MatchRecordStorage
{
    internal static MatchRecordDatabase Database { get; private set; } = null!;

    internal static string RootDirectory { get; private set; } = "";

    internal static string ExportsDirectory => Ensure("Exports");

    internal static string ImportsDirectory => Ensure("Imports");

    internal static string MediaDirectory => Ensure("Media");

    internal static string TemporaryDirectory => Ensure("Temporary");

    internal static void Configure(MatchRecordDatabase database, string root)
    {
        Database = database;
        RootDirectory = root;
    }

    private static string Ensure(string name)
    {
        var path = Path.Combine(RootDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
