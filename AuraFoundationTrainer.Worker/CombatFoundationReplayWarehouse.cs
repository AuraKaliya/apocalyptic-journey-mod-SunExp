using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.Worker;

internal sealed class CombatFoundationReplayWarehouse
{
    private const string IndexFileName = "replay-index-v1.jsonl";

    private readonly object gate = new();
    private readonly string rootPath;
    private readonly string episodeRootPath;
    private readonly string indexPath;
    private readonly Dictionary<string, ReplayWarehouseEntry> entries =
        new(StringComparer.Ordinal);

    public CombatFoundationReplayWarehouse(string path)
    {
        rootPath = Path.GetFullPath(path);
        episodeRootPath = Path.Combine(rootPath, "episodes");
        indexPath = Path.Combine(rootPath, IndexFileName);
        Directory.CreateDirectory(episodeRootPath);
        LoadIndex();
    }

    public CombatFoundationReplayArchiveReport Archive(
        int iteration,
        IReadOnlyList<CombatEpisode> episodes)
    {
        var report = new CombatFoundationReplayArchiveReport
        {
            Iteration = iteration,
            WarehousePath = rootPath
        };
        lock (gate)
        {
            foreach (var episode in episodes ?? Array.Empty<CombatEpisode>())
            {
                if (episode == null)
                {
                    continue;
                }
                var key = StableKey(episode);
                if (entries.ContainsKey(key))
                {
                    report.DuplicateEpisodes++;
                    continue;
                }
                try
                {
                    var hash = HashKey(key);
                    var relativePath = Path.Combine(
                        "episodes",
                        hash[..2],
                        hash + ".json.gz");
                    var path = Path.Combine(rootPath, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var json = SerializeCompact(episode);
                    CombatFoundationCheckpointStorage.WriteAtomicStream(
                        path,
                        output =>
                        {
                            using var gzip = new GZipStream(
                                output,
                                CompressionLevel.Fastest,
                                leaveOpen: true);
                            using var writer = new StreamWriter(
                                gzip,
                                new UTF8Encoding(false),
                                64 * 1024,
                                leaveOpen: false);
                            writer.Write(json);
                        },
                        retainBackup: false);
                    var entry = CreateEntry(
                        iteration,
                        key,
                        relativePath,
                        episode,
                        new FileInfo(path).Length);
                    AppendIndex(entry);
                    entries.Add(key, entry);
                    report.ArchivedEpisodes++;
                    report.ArchivedBytes += entry.StoredBytes;
                }
                catch (Exception ex)
                {
                    report.Error = string.IsNullOrWhiteSpace(report.Error)
                        ? ex.Message
                        : report.Error + " | " + ex.Message;
                }
            }
        }
        return report;
    }

    public IReadOnlyList<CombatEpisode> Load(
        int iteration,
        IReadOnlyCollection<string> excludedKeys,
        int episodeLimit,
        long bytesLimit)
    {
        if (episodeLimit <= 0 || bytesLimit <= 0L)
        {
            return Array.Empty<CombatEpisode>();
        }
        lock (gate)
        {
            var excluded = new HashSet<string>(
                excludedKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            var candidates = entries.Values
                .Where(entry => !excluded.Contains(entry.Key))
                .ToList();
            var selected = new List<ReplayWarehouseEntry>(episodeLimit);
            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
            var hardQuota = Math.Max(1, episodeLimit / 2);
            var successQuota = Math.Max(1, episodeLimit / 4);
            var diversityQuota = Math.Max(
                0,
                episodeLimit - hardQuota - successQuota);
            AddCandidates(
                candidates.Where(entry => entry.Hard),
                hardQuota,
                iteration,
                selected,
                selectedKeys);
            AddCandidates(
                candidates.Where(entry => entry.Successful),
                successQuota,
                iteration,
                selected,
                selectedKeys);
            var diverse = candidates
                .GroupBy(
                    entry => entry.DifficultyId + "|" + entry.ScenarioId,
                    StringComparer.Ordinal)
                .SelectMany(group => group.OrderBy(entry =>
                    StableOrder(entry.Key, iteration)))
                .ToList();
            AddCandidates(
                diverse,
                diversityQuota,
                iteration,
                selected,
                selectedKeys);
            AddCandidates(
                candidates,
                episodeLimit - selected.Count,
                iteration,
                selected,
                selectedKeys);

            var result = new List<CombatEpisode>(selected.Count);
            var residentBytes = 0L;
            foreach (var entry in selected)
            {
                if (result.Count >= episodeLimit
                    || residentBytes + entry.EstimatedResidentBytes
                       > bytesLimit)
                {
                    continue;
                }
                var episode = ReadEpisode(entry);
                if (episode == null)
                {
                    continue;
                }
                result.Add(episode);
                residentBytes += Math.Max(
                    entry.EstimatedResidentBytes,
                    CombatFoundationReplaySampler.EstimateResidentBytes(
                        episode));
            }
            return result;
        }
    }

    private void LoadIndex()
    {
        if (!File.Exists(indexPath))
        {
            return;
        }
        foreach (var line in File.ReadLines(indexPath))
        {
            try
            {
                var entry = JsonConvert.DeserializeObject<ReplayWarehouseEntry>(
                    line);
                if (entry == null
                    || string.IsNullOrWhiteSpace(entry.Key)
                    || string.IsNullOrWhiteSpace(entry.RelativePath))
                {
                    continue;
                }
                var path = Path.Combine(rootPath, entry.RelativePath);
                if (File.Exists(path))
                {
                    entries[entry.Key] = entry;
                }
            }
            catch (JsonException)
            {
                // A truncated final append is ignored; all earlier rows remain.
            }
        }
    }

    private void AppendIndex(ReplayWarehouseEntry entry)
    {
        Directory.CreateDirectory(rootPath);
        using var stream = new FileStream(
            indexPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            16 * 1024,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false),
            16 * 1024,
            leaveOpen: false);
        writer.WriteLine(JsonConvert.SerializeObject(entry, Formatting.None));
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private CombatEpisode? ReadEpisode(ReplayWarehouseEntry entry)
    {
        try
        {
            var path = Path.Combine(rootPath, entry.RelativePath);
            using var input = File.OpenRead(path);
            using var gzip = new GZipStream(
                input,
                CompressionMode.Decompress,
                leaveOpen: false);
            using var reader = new StreamReader(
                gzip,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            using var jsonReader = new JsonTextReader(reader);
            return JsonSerializer.CreateDefault().Deserialize<CombatEpisode>(
                jsonReader);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ReplayWarehouseEntry CreateEntry(
        int iteration,
        string key,
        string relativePath,
        CombatEpisode episode,
        long storedBytes)
    {
        var campaign = episode.Campaign ?? new CombatCampaignEpisodeMetadata();
        var successful = campaign.FinalBossVictory;
        var lowHp = episode.FinalPlayerMaxHp > 0
                    && episode.FinalPlayerHp
                       <= Math.Max(1, episode.FinalPlayerMaxHp / 3);
        return new ReplayWarehouseEntry
        {
            Key = key,
            RelativePath = relativePath.Replace('\\', '/'),
            DifficultyId = campaign.DifficultyId ?? "",
            ScenarioId = episode.ScenarioId ?? "",
            Successful = successful,
            Hard = !successful || lowHp,
            TrainingIteration = Math.Max(
                iteration,
                campaign.TrainingIteration),
            Frames = episode.Frames?.Count ?? 0,
            EstimatedResidentBytes = CombatFoundationReplaySampler
                .EstimateResidentBytes(episode),
            StoredBytes = Math.Max(0L, storedBytes),
            CurriculumStage = campaign.CurriculumStage ?? "",
            CreatedUtc = DateTime.UtcNow
        };
    }

    private static void AddCandidates(
        IEnumerable<ReplayWarehouseEntry> source,
        int count,
        int iteration,
        ICollection<ReplayWarehouseEntry> selected,
        ISet<string> selectedKeys)
    {
        if (count <= 0)
        {
            return;
        }
        foreach (var entry in source
                     .OrderByDescending(item => item.TrainingIteration)
                     .ThenBy(item => StableOrder(item.Key, iteration)))
        {
            if (!selectedKeys.Add(entry.Key))
            {
                continue;
            }
            selected.Add(entry);
            count--;
            if (count <= 0)
            {
                break;
            }
        }
    }

    private static string StableKey(CombatEpisode episode)
    {
        return (episode.JourneyRunId ?? "")
               + "|"
               + episode.JourneyBattleIndex.ToString("D4")
               + "|"
               + episode.Seed.ToString("D20")
               + "|"
               + (episode.ScenarioId ?? "")
               + "|"
               + (episode.EpisodeId ?? "");
    }

    private static string StableOrder(string key, int iteration)
    {
        return HashKey(iteration + "|" + key);
    }

    private static string HashKey(string value)
    {
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static string SerializeCompact(object value)
    {
        return JsonConvert.SerializeObject(
            value,
            Formatting.None,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                FloatFormatHandling = FloatFormatHandling.DefaultValue,
                ContractResolver = WorkerCompactEpisodeContractResolver.Instance
            });
    }

    private sealed class ReplayWarehouseEntry
    {
        public string Key { get; set; } = "";

        public string RelativePath { get; set; } = "";

        public string DifficultyId { get; set; } = "";

        public string ScenarioId { get; set; } = "";

        public bool Successful { get; set; }

        public bool Hard { get; set; }

        public int TrainingIteration { get; set; }

        public int Frames { get; set; }

        public long EstimatedResidentBytes { get; set; }

        public long StoredBytes { get; set; }

        public string CurriculumStage { get; set; } = "";

        public DateTime CreatedUtc { get; set; }
    }
}
