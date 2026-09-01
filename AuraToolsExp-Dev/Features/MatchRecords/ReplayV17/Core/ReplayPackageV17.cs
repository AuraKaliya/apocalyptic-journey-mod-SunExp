using System.Collections.Generic;
using System.Linq;

namespace AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Core;

internal sealed class ReplayPackageManifestV17
{
    public string Format { get; set; } = "AuraTools.MatchReplay";
    public int PackageVersion { get; set; } = ReplayProtocolV17.PackageVersion;
    public int DocumentVersion { get; set; } = ReplayProtocolV17.DocumentVersion;
    public string ExportedUtc { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string DocumentRoot { get; set; } = "";
    public string TruthRoot { get; set; } = "";
    public string PresentationRoot { get; set; } = "";
    public List<ReplayPackageEntryV17> Entries { get; set; } = new();
}

internal sealed class ReplayPackageEntryV17
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
}
