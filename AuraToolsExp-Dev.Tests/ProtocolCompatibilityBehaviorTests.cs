using AuraCg.Shared;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;

internal static partial class AuraToolsTestSuite
{
    public static void TestProtocolCompatibilityContracts()
    {
        var contract = new AuraToolsProtocolContract(
            "test-feature",
            currentVersion: 2,
            minimumSupportedVersion: 1,
            new[] { "base.v1", "optional.v1" });
        var futureCompatible = contract.Negotiate(
            remoteCurrentVersion: 3,
            remoteMinimumSupportedVersion: 2,
            new[] { "base.v1" });
        Assert(futureCompatible.Compatible
               && futureCompatible.Degraded
               && futureCompatible.NegotiatedVersion == 2,
            "protocol negotiation accepts an overlapping future range without product-version equality");
        Assert(!contract.Negotiate(
                remoteCurrentVersion: 3,
                remoteMinimumSupportedVersion: 2).Compatible,
            "a future protocol must explicitly declare its required capability baseline");

        var missingCapability = contract.Negotiate(
            remoteCurrentVersion: 2,
            remoteMinimumSupportedVersion: 1,
            new[] { "future-required.v1" });
        Assert(!missingCapability.Compatible
               && missingCapability.MissingCapabilities
                   .SequenceEqual(new[] { "future-required.v1" }),
            "protocol negotiation rejects only the feature that requires an unknown capability");

        var noOverlap = contract.Negotiate(
            remoteCurrentVersion: 4,
            remoteMinimumSupportedVersion: 3);
        Assert(!noOverlap.Compatible,
            "protocol negotiation rejects non-overlapping ranges");

        var mixedLobby = AuraToolsPeerCompatibility.Evaluate(new[]
        {
            new AuraToolsPeerModState
            {
                PlayerId = "host",
                PlayerName = "Host",
                ToolEnabled = true
            },
            new AuraToolsPeerModState
            {
                PlayerId = "client-a",
                PlayerName = "Client A",
                ToolEnabled = true
            },
            new AuraToolsPeerModState
            {
                PlayerId = "client-b",
                PlayerName = "Client B",
                ToolEnabled = false
            }
        });
        Assert(!mixedLobby.Compatible
               && mixedLobby.MissingPeers.SequenceEqual(
                   new[] { "Client B" }),
            "custom AuraTools RPC is disabled when any lobby peer cannot deserialize the tool command type");
        Assert(AuraToolsPeerCompatibility.Evaluate(new[]
            {
                new AuraToolsPeerModState { PlayerId = "solo" }
            }).Compatible,
            "local and single-player tools do not require a network peer gate");
        Assert(!AuraToolsPeerCompatibility.Evaluate(new[]
            {
                new AuraToolsPeerModState
                {
                    PlayerId = "host",
                    ToolEnabled = true
                },
                new AuraToolsPeerModState()
            }).Compatible,
            "an unidentified multiplayer peer fails closed instead of bypassing the RPC type-presence gate");

        var ledger = new DamageLedger();
        ledger.StartFight("compat-session", sharedEnabled: true);
        ledger.StartRound(1);
        var legacyEvent = new DamageEvent
        {
            ProtocolVersion = DamageMeterProtocol.MinimumReadableVersion,
            SessionId = "compat-session",
            ReporterPlayerId = "legacy-player",
            ReporterSequence = 1,
            ServerSequence = 1,
            RoundIndex = 1,
            SourceInstanceId = "legacy-source",
            TargetInstanceId = "target",
            HpDamage = 12
        };
        Assert(!ledger.Apply(legacyEvent),
            "damage ledger keeps the live network event protocol strict when an old peer cannot consume the response");

        var unsupportedEvent = legacyEvent.Copy();
        unsupportedEvent.ProtocolVersion = DamageMeterProtocol.Version;
        Assert(ledger.Apply(unsupportedEvent),
            "damage ledger accepts the current live event protocol");

        var legacySnapshot = ledger.CreateSnapshot();
        legacySnapshot.ProtocolVersion =
            DamageMeterProtocol.MinimumReadableVersion;
        legacySnapshot.MinimumProtocolVersion = 0;
        Assert(new DamageLedger().ApplySnapshot(legacySnapshot),
            "damage snapshots migrate from the supported legacy protocol range");

        var presentation = new AuraCgScenePlan
        {
            ProtocolVersion = AuraCgSceneProtocol.CurrentVersion,
            SceneId = "settlement",
            SignalId = "aura.adventure.settlement.entering",
            EventToken = "settlement-1",
            BackgroundAsset = new AuraCgSceneAssetReference
            {
                OwnerModId = "AuraToolsExp",
                AssetId = "event.background.settlement"
            },
            Participants = new List<AuraCgSceneParticipantPlan>
            {
                new()
                {
                    RoleId = "career_1",
                    RoleLayerAsset = new AuraCgSceneAssetReference
                    {
                        OwnerModId = "AuraToolsExp",
                        AssetId = "role.idle"
                    }
                }
            }
        };
        Assert(presentation.IsValid()
               && presentation.ProtocolVersion == AuraCgSceneProtocol.CurrentVersion,
            "event scene compatibility is owned by the unified CG protocol, independent from damage data");
    }
}
