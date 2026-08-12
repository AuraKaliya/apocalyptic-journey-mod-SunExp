using AuraToolsExp.Dll.Features.MatchRecords.Model;
using AuraToolsExp.Dll.Features.MatchRecords.Playback;
using AuraToolsExp.Dll.Features.MatchRecords.Recording;

internal static partial class AuraToolsTestSuite
{
    public static void TestMatchReplayRuntimeCore()
    {
        var root = Path.Combine(Path.GetTempPath(), "AuraTools-ReplayBuffer-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var buffer = new MatchReplayWorkingBuffer(32 * 1024, 64 * 1024, root))
            {
                var random = new Random(8122026);
                for (var index = 1; index <= 600; index++)
                {
                    var payload = new byte[2048];
                    random.NextBytes(payload);
                    buffer.Add(new MatchReplayEvent
                    {
                        Sequence = index,
                        TurnIndex = 1 + index / 20,
                        Kind = MatchReplayEventKinds.ActionCommand,
                        TypeName = "Action." + index,
                        Payload = payload
                    });
                }

                Assert(buffer.EventCount == 600 && buffer.ChunkCount > 10,
                    "long replay recording is divided into many bounded chunks");
                Assert(buffer.BufferedBytes < 128 * 1024,
                    "long replay recording keeps compressed in-memory chunks within the configured working budget");
                Assert(Directory.Exists(root) && Directory.GetFiles(root, "*.work").Length > 0,
                    "old compressed chunks spill to a temporary working file after the memory budget is reached");
                var chunks = buffer.Complete();
                var events = MatchReplayChunker.Decode(chunks);
                Assert(events.Count == 600 && events[^1].Sequence == 600,
                    "spilled replay chunks reconstruct the complete match only at finalization");
            }

            Assert(!Directory.Exists(root), "discarding or completing a replay removes its temporary work directory");

            var first = new List<MatchReplayEvent>
            {
                ReplayEvent(1, 1, 25, MatchSemanticCategories.Card),
                ReplayEvent(2, 1, 80000, MatchSemanticCategories.Damage),
                ReplayEvent(3, 2, 90000, MatchSemanticCategories.Status)
            };
            var second = first.Select(item => new MatchReplayEvent
            {
                Sequence = item.Sequence,
                TurnIndex = item.TurnIndex,
                ElapsedMilliseconds = item.ElapsedMilliseconds * 1000,
                Kind = item.Kind,
                Semantic = item.Semantic
            }).ToList();
            Assert(MatchReplayPresentationSchedule.Build(first, MatchReplayPresentationModes.Standard)
                    .SequenceEqual(MatchReplayPresentationSchedule.Build(second, MatchReplayPresentationModes.Standard)),
                "presentation scheduling ignores original player and network wait intervals");
            Assert(MatchReplayPresentationSchedule.Build(first, MatchReplayPresentationModes.Compact)[^1]
                   < MatchReplayPresentationSchedule.Build(first, MatchReplayPresentationModes.Showcase)[^1],
                "presentation modes change deterministic action pacing without changing the command stream");

            var legacy = new MatchRecord
            {
                ReplayProtocol = 2,
                GameBuild = "old-game",
                ToolBuild = "old-tool",
                ModFingerprint = "old-mod"
            };
            Assert(MatchReplayCompatibility.Evaluate(legacy, first).CanPlay,
                "protocol v2 replay remains playable without exact build or MVID equality");
            legacy.RequiredCapabilities.Add("future-required-capability");
            Assert(!MatchReplayCompatibility.Evaluate(legacy, first).CanPlay,
                "an unknown required capability blocks playback while retaining analysis access");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static MatchReplayEvent ReplayEvent(long sequence, int turn, long elapsed, string category)
    {
        return new MatchReplayEvent
        {
            Sequence = sequence,
            TurnIndex = turn,
            ElapsedMilliseconds = elapsed,
            Kind = MatchReplayEventKinds.ActionCommand,
            Semantic = new MatchSemanticEvent { Category = category }
        };
    }
}
