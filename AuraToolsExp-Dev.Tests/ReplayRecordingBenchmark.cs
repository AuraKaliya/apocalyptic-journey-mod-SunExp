using System.Diagnostics;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using Newtonsoft.Json;

internal static partial class AuraToolsTestSuite
{
    internal static void MeasureReplayRecording(string input, string output)
    {
        var envelope = JsonConvert.DeserializeObject<ReplayDocumentEnvelopeV17>(File.ReadAllText(input))!;
        var states = envelope.Document.TruthCheckpoints.Select(item => item.State).ToList();
        if (states.Count == 0) throw new InvalidOperationException("A recording benchmark requires real checkpoint states.");
        Run(1);
        GC.Collect();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var finalStateHash = Run(5);
        timer.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var result = new
        {
            recordId = envelope.Document.Header.RecordId,
            transactions = states.Count * 5,
            milliseconds = timer.Elapsed.TotalMilliseconds,
            allocatedBytes = bytes,
            finalStateHash,
            scope = "recording journal, state reconciliation and transaction-ledger snapshots; excludes Unity and finalization"
        };
        File.WriteAllText(output, JsonConvert.SerializeObject(result, Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(result));

        string Run(int loops)
        {
            var journal = new ReplayJournalBuilderV17(envelope.Document.Header, envelope.Document.InitialState);
            var ledger = new ReplayTransactionLedgerV17();
            for (var i = 0; i < 16; i++) ledger.Begin("open" + i, "Passive", "actor", "source" + i);
            long ticks = 0;
            for (var loop = 0; loop < loops; loop++)
            foreach (var state in states)
            {
                var observed = ReplayStateReducerV17.Normalize(state);
                var id = journal.StartTransaction(ReplayTransactionKindsV17.Passive, ++ticks,
                    observed.RoundSequence, observed.ActorTurnSequence, observed.ActiveActorId);
                _ = ledger.OpenEntries;
                var diff = ReplayStateReducerV17.CreateDiff(journal.CurrentState, observed);
                if (diff.HasChanges) journal.ApplyObservedState(id, observed, ++ticks);
                journal.AddPresentation(id, ReplayEventTypesV17.DamageTextPresented,
                    new ReplayPresentationMessageV17 { Kind = "Damage", DisplayText = "1", DurationTicks = 100_000 }, ++ticks);
                journal.CompleteTransaction(id, ++ticks);
                _ = ReplayDurableJournalPrefixV17.LastDurableSequence(journal.Document, Array.Empty<string>(), Array.Empty<long>());
            }
            return ReplayCanonicalJsonV17.StateHash(journal.CurrentState);
        }
    }
}
