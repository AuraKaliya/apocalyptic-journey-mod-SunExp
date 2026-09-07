using AuraReplay.Presentation.Shared;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Storage;

internal static partial class AuraToolsTestSuite
{
    internal static void TestMatchReplayModuleCompatibility()
    {
        var envelope = BuildReplayV17();
        var required = envelope.Document.Presentation.Modules.Single();
        required.Portability = AuraReplayPresentationPortability.ProviderRequired;
        required.BuildIdentity = "1.0.0.0+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert(ReplayDocumentFinalizerV17.FinalizeAndValidate(envelope).IsValid,
            "a provider-required replay fixture is sealed with its original build provenance");
        var before = ReplayPayloadV17.Encode(envelope);
        var decoded = ReplayPayloadV17.Decode<ReplayDocumentEnvelopeV17>(before);
        var recorded = decoded.Document.Presentation.Modules.Single();
        var current = new AuraReplayPresentationModuleDescriptor
        {
            OwnerModId = recorded.OwnerModId,
            TypeId = recorded.TypeId,
            SchemaVersion = recorded.SchemaVersion,
            Portability = recorded.Portability,
            RendererCapability = recorded.RendererCapability,
            BuildIdentity = "1.0.0.0+bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
        };
        Assert(ReplayPresentationModuleCompatibilityV17.FindUnsatisfied(
                   decoded.Document.Presentation.Modules, new[] { current }) == null,
            "the actual module preflight accepts a rebuilt provider with the same event and renderer contract");
        Assert(before.SequenceEqual(ReplayPayloadV17.Encode(decoded))
               && ReplayDocumentValidatorV17.Validate(decoded).IsValid
               && recorded.BuildIdentity == required.BuildIdentity,
            "compatibility never rewrites the sealed document, roots or historical build identity");
        Assert(ReferenceEquals(recorded, ReplayPresentationModuleCompatibilityV17.FindUnsatisfied(
                   decoded.Document.Presentation.Modules, Array.Empty<AuraReplayPresentationModuleDescriptor>())),
            "a required provider must still be installed");
        foreach (var change in new Action<AuraReplayPresentationModuleDescriptor>[]
                 {
                     item => item.OwnerModId = "OtherOwner",
                     item => item.TypeId = "OtherModule",
                     item => item.SchemaVersion++,
                     item => item.Portability = AuraReplayPresentationPortability.Portable,
                     item => item.RendererCapability = "different-renderer.v2"
                 })
        {
            var incompatible = ReplayCanonicalJsonV17.Clone(current);
            change(incompatible);
            Assert(ReferenceEquals(recorded, ReplayPresentationModuleCompatibilityV17.FindUnsatisfied(
                       decoded.Document.Presentation.Modules, new[] { incompatible })),
                "module preflight continues to reject incompatible identity, schema, portability or renderer capability");
        }
        var portable = BuildReplayV17().Document.Presentation.Modules;
        Assert(ReplayPresentationModuleCompatibilityV17.FindUnsatisfied(
                   portable, Array.Empty<AuraReplayPresentationModuleDescriptor>()) == null,
            "portable recorded presentation remains independent of an installed provider");
    }
}
