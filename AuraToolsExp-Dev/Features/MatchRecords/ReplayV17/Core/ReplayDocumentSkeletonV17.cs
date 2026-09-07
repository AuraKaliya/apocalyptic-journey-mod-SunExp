using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal static class ReplayDocumentSkeletonV17
{
    // Journals and checkpoints are stored separately. Never copy the complete
    // graph merely to erase those large lists immediately afterwards.
    internal static ReplayDocumentEnvelopeV17 Create(ReplayDocumentEnvelopeV17 envelope) => new()
    {
        DeclaredDocumentRoot = envelope.DeclaredDocumentRoot,
        Document = new ReplayDocumentV17
        {
            Header = envelope.Document.Header,
            InitialState = envelope.Document.InitialState,
            Presentation = envelope.Document.Presentation,
            Assets = envelope.Document.Assets.Select(ReplayCanonicalJsonV17.Clone).ToList()
        }
    };
}
