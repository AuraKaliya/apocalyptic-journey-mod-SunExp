using AuraToolsExp.Dll.Features.MatchRecords.Model;

namespace AuraToolsExp.Dll.Features.MatchRecords.Media;

internal static class ReplayLegacyMediaTranscoder
{
    internal static MatchMediaAsset NormalizeAndImport(string recordId, string source)
    {
        return MatchReplayMediaStore.ImportLegacyFile(recordId, source);
    }
}
