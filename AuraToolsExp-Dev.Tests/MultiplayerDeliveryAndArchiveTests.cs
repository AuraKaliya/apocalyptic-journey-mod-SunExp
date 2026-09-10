using AuraToolsExp.Dll.Features.AdventureArchive;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.DamageMeter.Network;
using AuraToolsExp.Dll.Features.DamageMeter.Storage;

internal static partial class AuraToolsTestSuite
{
    public static void TestMultiplayerDeliveryAndArchiveIdentity()
    {
        var outbox = new DamageSubmissionOutbox();
        for (var i = 1; i <= 64; i++) Assert(outbox.Add(Event(i)), "accepted events enter the unconfirmed outbox");
        Assert(outbox.Batch(24, _ => true).Count == 24 && outbox.Batch(64, _ => true).Count == 64,
            "local send preferences do not change the common receive limit");
        Assert(outbox.Batch(64, values => values.Count <= 7).Count == 7, "byte budget can split below the event limit");
        Assert(outbox.Count == 64, "building/sending a batch cannot release unconfirmed data");
        Assert(!outbox.Acknowledge(65) && outbox.Count == 64, "invalid acknowledgements cannot erase data");
        Assert(outbox.Acknowledge(24) && outbox.Count == 40 && outbox.Batch(64, _ => true)[0].ReporterSequence == 25,
            "partial confirmation retains the exact unconfirmed suffix");
        var receiver = new DamageReceiveStream();
        Assert(receiver.Add(Event(2)) && receiver.Next == null, "out-of-order arrival does not skip a sequence gap");
        Assert(receiver.Add(Event(1)) && receiver.Next?.ReporterSequence == 1, "a retry fills the missing prefix");
        receiver.ConfirmNext();
        Assert(receiver.Next?.ReporterSequence == 2, "buffered successor drains after prefix confirmation");
        receiver.ConfirmNext();
        Assert(receiver.Add(Event(1)) && receiver.Next == null && receiver.Through == 2, "duplicate delivery never reapplies a confirmed event");
        Assert(receiver.Complete(3) && !receiver.IsComplete, "final marker waits for the final event");
        Assert(receiver.Add(Event(3)), "final event remains admissible during drain");
        receiver.ConfirmNext();
        Assert(receiver.IsComplete && !receiver.Add(Event(4)), "completed streams cannot receive extra events");

        var root = Path.Combine(Path.GetTempPath(), "AuraArchiveNetwork-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "records.sqlite3");
        var networkId = Guid.NewGuid().ToString("N");
        try
        {
            using (var sql = new WinSqliteConnection(path))
            {
                sql.Execute("CREATE TABLE battle_records(record_id TEXT PRIMARY KEY, adventure_id TEXT, started_utc TEXT, content_sha256 TEXT);");
                using var insert = sql.Prepare("INSERT INTO battle_records VALUES('host-replay',?,'2026-09-07','immutable-root');");
                insert.Bind(1, networkId); insert.Execute();
            }
            var database = new AdventureArchiveDatabase(path);
            database.Begin(new AdventureArchiveRecord { AdventureId = "guest-local", PerspectivePlayerId = "guest", AssociationState = "Pending" });
            database.AppendEvent("guest-local", new AdventureArchiveEvent { Kind = "start", Title = "start", DedupeKey = "start" });
            Assert(database.Load("guest-local")!.BattleRecordIds.Count == 0, "pending local archive cannot guess a host replay association");
            Assert(database.BindPendingAdventure("guest-local", networkId, "guest") == "guest-local", "binding preserves the local archive identity");
            Assert(database.Load("guest-local")!.BattleRecordIds.SequenceEqual(new[] { "host-replay" }), "canonical network identity links replicated replay to guest archive");
            database.Begin(new AdventureArchiveRecord { AdventureId = "guest-reconnect", PerspectivePlayerId = "guest", AssociationState = "Pending" });
            database.AppendEvent("guest-reconnect", new AdventureArchiveEvent { Kind = "choice", Title = "choice", DedupeKey = "choice" });
            Assert(database.BindPendingAdventure("guest-reconnect", networkId, "guest") == "guest-local", "reconnecting pending segment merges into its existing perspective");
            Assert(database.Load("guest-reconnect") == null && database.Load("guest-local")!.Events.Count == 2,
                "pending segment is removed only after its events are preserved");
            database.Begin(new AdventureArchiveRecord { AdventureId = "other-perspective", NetworkAdventureId = networkId, PerspectivePlayerId = "other", AssociationState = "Ready" });
            Assert(database.FindForNetworkAdventure(networkId, "guest") == "guest-local"
                && database.FindForNetworkAdventure(networkId, "other") == "other-perspective", "player perspectives are not merged");
            database.Begin(new AdventureArchiveRecord { AdventureId = "legacy-unlinked" });
            Assert(database.Load("legacy-unlinked")!.BattleRecordIds.Count == 0, "legacy records are not matched by coincidental time or level");
            using var verify = new WinSqliteConnection(path);
            using var hash = verify.Prepare("SELECT content_sha256 FROM battle_records WHERE record_id='host-replay';");
            Assert(hash.Read() && hash.Text(0) == "immutable-root", "archive migration never rewrites sealed replay roots");
            var history = new DamageHistoryDatabase(Path.Combine(root, "damage.sqlite3"));
            var partial = new DamageFightRecord { SessionId = "closed", Snapshot = new DamageMeterSnapshot { SessionId = "closed", ServerSequence = 1, IsComplete = false } };
            var firstStored = history.AppendFight(networkId, partial)!;
            var full = new DamageFightRecord { SessionId = "closed", Snapshot = new DamageMeterSnapshot { SessionId = "closed", ServerSequence = 2, IsComplete = true } };
            var repaired = history.AppendFight(networkId, full);
            Assert(repaired?.Sequence == firstStored.Sequence && history.CountFights(networkId) == 1,
                "late authoritative completion repairs an incomplete history row without duplicating the fight");
            Assert(history.AppendFight(networkId, partial) == null, "an older partial snapshot cannot replace completed history");
        }
        finally { Directory.Delete(root, true); }
    }

    private static DamageEvent Event(long sequence) => new()
    { SessionId = "battle", ReporterPlayerId = "guest", ReporterSequence = sequence, HpDamage = 10, TargetInstanceId = "enemy", SourceInstanceId = "guest" };
}
