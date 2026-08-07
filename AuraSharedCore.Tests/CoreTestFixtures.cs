using AuraShared.Core;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraOnline.Shared;
using AuraDirector.Shared;
using AuraRole.Shared;
using AuraGameData.Shared;
using Newtonsoft.Json.Linq;
internal static partial class CoreTestSuite
{
    public static int assertions;
    public static string tempRoot = "";
    public static string sourceRoot = "";
    public static void Assert(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + name);
        }
        assertions++;
    }
    
    public static bool OperationLogContains(string root, string kind, string phase)
    {
        var directory = Path.Combine(root, "Logs", "Operations");
        if (!Directory.Exists(directory))
        {
            return false;
        }
    
        return Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
            .SelectMany(File.ReadAllLines)
            .Any(line =>
            {
                var json = JObject.Parse(line);
                return string.Equals(json["kind"]?.Value<string>(), kind, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(json["phase"]?.Value<string>(), phase, StringComparison.OrdinalIgnoreCase);
            });
    }
    
    public static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
    
    sealed class FakeLobbyPlayer
    {
        public FakeLobbyPlayer(string id, string name, IEnumerable<FakeLobbyMod> mods)
        {
            Id = id;
            Name = name;
            Mods = mods.ToList();
        }
    
        public string Id { get; }
    
        public string Name { get; }
    
        public List<FakeLobbyMod> Mods { get; }
    }
    
    sealed class FakeLobbyMod
    {
        public FakeLobbyMod(string modName, string modVersion, bool enabled)
        {
            ModName = modName;
            ModVersion = modVersion;
            Enabled = enabled;
        }
    
        public string ModName { get; }
    
        public string ModVersion { get; }
    
        public bool Enabled { get; }
    }
    
    sealed class PoolValue
    {
        public PoolValue(string name)
        {
            Name = name;
        }
    
        public string Name { get; }
    
        public bool IsValid { get; set; } = true;
    }
    
    sealed class FakeGameDataSource : IAuraGameDataSource
    {
        private readonly IReadOnlyList<AuraGameDataDefinition> definitions;
    
        public FakeGameDataSource(params AuraGameDataDefinition[] definitions)
        {
            this.definitions = definitions.Select(value => value.Clone()).ToList();
        }
    
        public long Revision { get; private set; } = 1;
    
        public int CaptureCount { get; private set; }
    
        public AuraGameDataSourceSnapshot Capture()
        {
            CaptureCount++;
            return new AuraGameDataSourceSnapshot(Revision, definitions);
        }
    
        public void Invalidate()
        {
            Revision++;
        }
    }
    
    sealed class DelayedGameDataSource : IAuraGameDataSource
    {
        private readonly IReadOnlyList<AuraGameDataDefinition> definitions;
        private bool complete;
    
        public DelayedGameDataSource(params AuraGameDataDefinition[] definitions)
        {
            this.definitions = definitions.Select(value => value.Clone()).ToList();
        }
    
        public long Revision { get; private set; } = 1;
    
        public AuraGameDataSourceSnapshot Capture()
        {
            return new AuraGameDataSourceSnapshot(
                Revision,
                complete ? definitions : Array.Empty<AuraGameDataDefinition>(),
                complete);
        }
    
        public void CompleteCapture()
        {
            complete = true;
        }
    
        public void Invalidate()
        {
            Revision++;
            complete = false;
        }
    }
}
