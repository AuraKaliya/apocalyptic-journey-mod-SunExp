using AuraShared.Core;

internal static partial class CoreTestSuite
{
    private sealed class MemberMessage { }
    private sealed class HostMessage { }
    public static void TestMultiplayerContracts()
    {
        var host = new AuraRpcSender("host", "", true, true, "native", true);
        var guest = new AuraRpcSender("guest", "", true, false, "native", true);
        AuraRpcAdmission.Register<MemberMessage>(AuraRpcPublication.MemberRequest);
        AuraRpcAdmission.RegisterAssembly("Terrias.Aura");
        AuraRpcAdmission.RegisterAssembly("Aura.Shared");
        AuraRpcAdmission.Register<HostMessage>(AuraRpcPublication.HostOnly);
        Assert(AuraRpcAdmission.TryAuthorize(typeof(MemberMessage).AssemblyQualifiedName!, guest, out var owned, out _) && owned,
            "bound members may submit registered requests");
        Assert(!AuraRpcAdmission.TryAuthorize(typeof(HostMessage).AssemblyQualifiedName!, guest, out _, out _), "members cannot publish host results");
        Assert(AuraRpcAdmission.TryAuthorize(typeof(HostMessage).AssemblyQualifiedName!, host, out _, out _), "real host can publish");
        Assert(!AuraRpcAdmission.TryAuthorize(typeof(MemberMessage).AssemblyQualifiedName!, AuraRpcSender.Unbound, out _, out _), "unbound requests fail closed");
        Assert(!AuraRpcAdmission.TryAuthorize("Terrias.Dll.NewMessage, Terrias.Aura", host, out owned, out _) && owned, "unregistered product commands fail closed");
        Assert(!AuraRpcAdmission.TryAuthorize("NewDomain.Rpc, aura.shared", host, out owned, out _) && owned, "assembly casing cannot bypass ownership");
        Assert(AuraRpcAdmission.TryAuthorize("Foreign.Rpc, ForeignMod", guest, out owned, out _) && !owned, "foreign RPC policy is preserved");
        using (AuraRpcReceiveContext.Enter(guest))
        {
            try { using var nested = AuraRpcReceiveContext.Enter(host); throw new InvalidOperationException("nested"); }
            catch (InvalidOperationException) { }
            Assert(ReferenceEquals(AuraRpcReceiveContext.Sender, guest), "nested exception restores the exact outer sender");
        }
        Assert(!AuraRpcReceiveContext.Sender.IsAvailable, "receive scope never leaks to the next message");

        var state = new AuraNetworkIdentityState();
        var adventure = Guid.NewGuid().ToString("N"); var first = Guid.NewGuid().ToString("N"); var second = Guid.NewGuid().ToString("N");
        state.BindBattle(adventure, first, 23, "native-init");
        Assert(state.BattleEpoch == 23 && state.MatchesBattle(first), "a peer adopts the authoritative epoch irrespective of its local history");
        state.BindAdventure(adventure);
        Assert(state.MatchesBattle(first), "repeated native save sync does not erase an active battle");
        state.BindBattle(adventure, second, 24, "native-restart");
        Assert(!state.MatchesBattle(first) && state.MatchesBattle(second), "restart invalidates old messages");
        state.ChangeRoom();
        Assert(state.AdventureId == "" && !state.BattleReady && !state.MatchesBattle(second), "room changes invalidate transient bindings");
        Assert(state.BindAdventure(adventure) && state.AdventureId == adventure, "continuing a save retains adventure identity in a new room");
        Assert(!state.BindAdventure("slot-1") && state.AdventureId == adventure, "invalid identity cannot replace a valid binding");

        var receipt = new AuraVersionedReceipt(); var request = Guid.NewGuid().ToString("N"); var hash = AuraVersionedReceipt.Hash("level=3");
        Assert(receipt.Decide(request, hash, 0) == AuraMutationDecision.Apply, "first update can commit");
        var committed = receipt.Commit(request, hash);
        Assert(receipt.Version == 0 && committed.Version == 1, "commit planning does not mutate the rollback baseline");
        var reopened = AuraSharedJson.Deserialize<AuraVersionedReceipt>(AuraSharedJson.Serialize(committed))!;
        Assert(reopened.Decide(request, hash, 0) == AuraMutationDecision.Duplicate, "durable receipt identifies a retry after restart");
        Assert(reopened.Decide(request, AuraVersionedReceipt.Hash("level=4"), 0) == AuraMutationDecision.TokenConflict, "same token cannot change payload");
        Assert(reopened.Decide(Guid.NewGuid().ToString("N"), hash, 0) == AuraMutationDecision.Conflict, "stale base versions cannot overwrite progress");
        for (var i = 0; i < 100; i++) reopened = reopened.Commit(Guid.NewGuid().ToString("N"), hash);
        Assert(reopened.Recent.Count == 64, "receipt retention is bounded");
        var json = "{\"角色\":\"乌娜\",\"value\":3}";
        Assert(AuraBoundedCompressedJson.Decode(AuraBoundedCompressedJson.Encode(json, 1024), 1024) == json, "compressed role payload round-trips Unicode");
        var rejected = false;
        try { AuraBoundedCompressedJson.Decode(AuraBoundedCompressedJson.Encode(new string('x', 100000), 1000), 100); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "decompression stops at the declared decoded budget");
    }
}
