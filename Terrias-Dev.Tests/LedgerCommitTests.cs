using System;
using System.IO;
using Terrias.Dll.Mechanics;

internal static partial class Program
{
    private static void TestLedgerCommitFailures()
    {
        var stored = "";
        True(EndlessAbyssLedgerCodec.TryCommitClaim("first", () => stored, value => stored = value, 512), "new claim commits");
        False(EndlessAbyssLedgerCodec.TryCommitClaim("first", () => stored, value => stored = value, 512), "existing claim is not repeated");
        foreach (var invalid in new[] { "{broken", "null", "{}", "{\"Entries\":null}", "{\"Entries\":[3]}", "{\"Entries\":[],\"Entries\":[]}" })
        {
            var writes = 0;
            var rejected = false;
            try { EndlessAbyssLedgerCodec.TryCommitClaim("first", () => invalid, _ => writes++, 512); }
            catch (InvalidDataException) { rejected = true; }
            True(rejected && writes == 0, "damaged ledger is preserved and cannot authorize a claim: " + invalid);
        }
        var failed = false;
        try { EndlessAbyssLedgerCodec.TryCommitClaim("next", () => stored, _ => throw new IOException("disk unavailable"), 512); }
        catch (IOException) { failed = true; }
        True(failed && !EndlessAbyssLedgerCodec.Read(stored).Entries.Contains("next"), "write failure is not reported as a successful claim");
        var readFailed = false;
        try { EndlessAbyssLedgerCodec.TryCommitClaim("next", () => throw new IOException("read unavailable"), _ => throw new Exception("must not write"), 512); }
        catch (IOException) { readFailed = true; }
        True(readFailed, "unavailable storage is not treated as a new ledger");
    }
}
