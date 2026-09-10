using System;
namespace Terrias.Dll.Contracts;

public sealed class RuntimeHandAttachmentSpec
{
    public string OwnerStatusId { get; set; } = "";
    public string[] NativeTags { get; set; } = Array.Empty<string>();

    public string[] SpecialTags { get; set; } = Array.Empty<string>();

    public string[] Markers { get; set; } = Array.Empty<string>();

    public bool TemporaryWhiteRadiance { get; set; }

    public string Token { get; set; } = "";

    public string Source { get; set; } = "";
}
