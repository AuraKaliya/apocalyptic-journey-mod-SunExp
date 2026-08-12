using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Analysis;
using AuraToolsExp.Dll.Features.MatchRecords.Media;
using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Portability;
using AuraToolsExp.Dll.Features.MatchRecords.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchAnalysis()
    {
        var snapshot = new DamageMeterSnapshot
        {
            CompletedRoundCount = 2,
            Combatants = new List<CombatantDamageStat>
            {
                new()
                {
                    InstanceId = "role-1",
                    DisplayName = "角色一",
                    Team = DamageTeam.Friendly,
                    TotalHpDamage = 180,
                    TotalShieldDamage = 20,
                    Rounds = new List<DamageRoundStat>
                    {
                        new() { RoundIndex = 1, HpDamage = 60, ShieldDamage = 10 },
                        new() { RoundIndex = 2, HpDamage = 120, ShieldDamage = 10 }
                    }
                }
            }
        };
        var events = new List<MatchReplayEvent>
        {
            Event(1, 1, MatchSemanticCategories.Card, "UseCard", "card-a", "卡牌A", 0),
            Event(2, 1, MatchSemanticCategories.Damage, "Normal", "", "伤害", 70),
            Event(3, 2, MatchSemanticCategories.Card, "UseCard", "card-a", "卡牌A", 0),
            Event(4, 2, MatchSemanticCategories.Damage, "Normal", "", "伤害", 130)
        };
        var report = MatchAnalysisBuilder.Build(new MatchRecord
        {
            RecordId = "analysis-record",
            TurnCount = 2,
            StatisticsJson = AuraSharedJson.SerializeCompact(snapshot)
        }, events);
        Assert(report.TotalDamage == 200
               && report.BestTurnIndex == 2
               && report.BestTurnDamage == 130,
            "analysis uses authoritative DPT round totals for overview and best-turn facts");
        Assert(report.CardUseCount == 2
               && report.Cards.Single().Uses == 2
               && report.Cards.Single().ObservedFollowUpDamage == 200,
            "analysis aggregates card usage and labels only observed follow-up damage");
        Assert(report.KeyMoments.Any(item => item.EventSequence == 4),
            "analysis exposes replay-addressable key moments");
    }

    public static void TestMjpegAviWriter()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-Avi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var jpeg = Convert.FromBase64String(
                "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAX/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAH/AP/EABQQAQAAAAAAAAAAAAAAAAAAABD/2gAIAQEAAQUCf//EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQMBAT8Bf//EABQRAQAAAAAAAAAAAAAAAAAAABD/2gAIAQIBAT8Bf//EABQQAQAAAAAAAAAAAAAAAAAAABD/2gAIAQEABj8Cf//EABQQAQAAAAAAAAAAAAAAAAAAABD/2gAIAQEAAT8hf//aAAwDAQACAAMAAAAQ/wD/xAAUEQEAAAAAAAAAAAAAAAAAAAAQ/9oACAEDAQE/EH//xAAUEQEAAAAAAAAAAAAAAAAAAAAQ/9oACAECAQE/EH//xAAUEAEAAAAAAAAAAAAAAAAAAAAQ/9oACAEBAAE/EH//2Q==");
            var frames = new List<string>();
            for (var index = 0; index < 3; index++)
            {
                var path = Path.Combine(root, "frame-" + index + ".jpg");
                File.WriteAllBytes(path, jpeg);
                frames.Add(path);
            }

            var output = Path.Combine(root, "replay.avi");
            MjpegAviWriter.Write(output, frames, 1, 1, 30, null);
            var bytes = File.ReadAllBytes(output);
            var ascii = System.Text.Encoding.ASCII.GetString(bytes);
            Assert(ascii.StartsWith("RIFF", StringComparison.Ordinal)
                   && ascii.Contains("AVI ", StringComparison.Ordinal)
                   && ascii.Contains("MJPG", StringComparison.Ordinal)
                   && ascii.Contains("movi", StringComparison.Ordinal)
                   && ascii.Contains("idx1", StringComparison.Ordinal),
                "built-in exporter emits an indexed MJPEG AVI without requiring FFmpeg");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    public static void TestMatchReplayPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-Package-" + Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "source");
        var targetRoot = Path.Combine(root, "target");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(targetRoot);
        try
        {
            var source = new MatchRecordDatabase(Path.Combine(sourceRoot, "records.sqlite3"));
            source.Initialize();
            MatchRecordStorage.Configure(source, sourceRoot);
            var events = new List<MatchReplayEvent>
            {
                Event(1, 1, MatchSemanticCategories.Card, "UseCard", "card-a", "卡牌A", 0),
                Event(2, 1, MatchSemanticCategories.Damage, "Normal", "", "伤害", 70)
            };
            var record = new MatchRecord
            {
                RecordId = "portable-record",
                SessionId = "portable-session",
                LevelId = "portable-level",
                Result = "Win",
                StartedUtc = "2026-08-12T00:00:00Z",
                EndedUtc = "2026-08-12T00:01:00Z",
                EventCount = events.Count,
                TurnCount = 1,
                GameBuild = "game",
                ToolBuild = "tool",
                ModFingerprint = "fingerprint",
                InitialState = new MatchReplayInitialState { LevelId = "portable-level" }
            };
            Assert(source.Save(record, MatchReplayChunker.Build(events, 32 * 1024)),
                "portable replay fixture is stored before export");
            source.SaveAnalysis(MatchAnalysisBuilder.Build(record, events));
            var package = MatchReplayPackageService.Export(record.RecordId);
            Assert(File.Exists(package) && Path.GetExtension(package) == ".aurareplay",
                "portable export creates the canonical aurareplay bundle");

            var target = new MatchRecordDatabase(Path.Combine(targetRoot, "records.sqlite3"));
            target.Initialize();
            MatchRecordStorage.Configure(target, targetRoot);
            var imported = MatchReplayPackageService.Import(package);
            Assert(imported.Collection == MatchRecordCollections.Favorite
                   && target.Count(MatchRecordCollections.Favorite) == 1
                   && MatchReplayChunker.Decode(target.LoadChunks(imported.RecordId)).Single(item => item.Sequence == 2).Semantic?.Value == 70
                   && target.GetAnalysis(imported.RecordId)?.RecordId == imported.RecordId,
                "portable import verifies and restores metadata, semantic chunks, and analysis into favorites");

            var corrupt = Path.Combine(root, "corrupt.aurareplay");
            File.Copy(package, corrupt);
            using (var archive = System.IO.Compression.ZipFile.Open(corrupt, System.IO.Compression.ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("record.bin")!;
                using var stream = entry.Open();
                stream.Position = 0;
                stream.WriteByte(0xff);
            }

            var rejected = false;
            try
            {
                MatchReplayPackageService.Import(corrupt);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert(rejected && target.Count(MatchRecordCollections.Favorite) == 1,
                "portable import rejects a checksum-corrupted bundle before adding a record");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MatchReplayEvent Event(
        long sequence,
        int turn,
        string category,
        string action,
        string source,
        string label,
        long value)
    {
        return new MatchReplayEvent
        {
            Sequence = sequence,
            TurnIndex = turn,
            ElapsedMilliseconds = sequence * 100,
            Kind = MatchReplayEventKinds.ActionCommand,
            Semantic = new MatchSemanticEvent
            {
                Category = category,
                Action = action,
                SourceId = source,
                Label = label,
                Value = value,
                IsKeyEvent = value >= 100
            }
        };
    }
}
