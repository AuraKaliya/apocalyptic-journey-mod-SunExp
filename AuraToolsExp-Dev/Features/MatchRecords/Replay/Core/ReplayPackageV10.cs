using System.Collections.Generic;

namespace AuraToolsExp.Dll.Features.MatchRecords.Replay.Core;

internal sealed class ReplayPackageManifestV10
{
    public string Format { get; set; } = "AuraTools.MatchReplay";

    public int PackageVersion { get; set; } = ReplayProtocolV10.PackageVersion;

    public int DocumentVersion { get; set; } = ReplayProtocolV10.DocumentVersion;

    public string ExportedUtc { get; set; } = "";

    public string RecordId { get; set; } = "";

    public string DocumentSha256 { get; set; } = "";

    public List<ReplayPackageEntryV10> Entries { get; set; } = new();
}

internal sealed class ReplayPackageEntryV10
{
    public string Path { get; set; } = "";

    public string Kind { get; set; } = "";

    public long ByteLength { get; set; }

    public long LogicalByteLength { get; set; }

    public string Sha256 { get; set; } = "";
}
