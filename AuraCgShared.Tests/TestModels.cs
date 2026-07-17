namespace AuraCg.Shared;

internal static class SkillCgMediaTypes
{
    public const string Image = "image";
    public const string Sequence = "sequence";
}

internal static class SkillCgAlphaModes
{
    public const string None = "none";
    public const string BlackKey = "blackKey";

    public static string Normalize(string? value)
    {
        var mode = value?.Trim() ?? "";
        return string.Equals(mode, BlackKey, StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "lumaKey", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "black", StringComparison.OrdinalIgnoreCase)
            ? BlackKey
            : None;
    }
}

internal sealed class AuraCgRegistryEntry
{
    public string CgId { get; set; } = "";
    public string OwnerModId { get; set; } = "";
    public string Kind { get; set; } = "skill";
    public List<string> TargetRoleIds { get; set; } = new();
    public List<string> CardIds { get; set; } = new();
    public AuraCgMediaSpec Media { get; set; } = new();
    public AuraCgPresentationSpec DefaultPresentation { get; set; } = new();
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
}

internal sealed class AuraCgMediaSpec
{
    public string Type { get; set; } = SkillCgMediaTypes.Image;
    public string Resource { get; set; } = "";
    public string FallbackImage { get; set; } = "";
    public string BundlePath { get; set; } = "";
    public string BundleAssetPrefix { get; set; } = "";
    public float FrameSeconds { get; set; }
    public string AlphaMode { get; set; } = "";
    public float KeyThreshold { get; set; }
    public float KeySoftness { get; set; }
    public float FlashAtSeconds { get; set; }
    public float FlashDuration { get; set; }
    public string FlashMode { get; set; } = "";
    public int FlashStartFrame { get; set; }
    public int FlashEndFrame { get; set; }
    public int FlashPulseEveryFrames { get; set; }
    public float FlashStrength { get; set; }
}

internal sealed class AuraCgPresentationSpec
{
    public float FadeIn { get; set; }
    public float Hold { get; set; }
    public float FadeOut { get; set; }
    public string Mode { get; set; } = "";
    public string Fit { get; set; } = "";
    public float FocusX { get; set; }
    public float FocusY { get; set; }
    public float SafeScale { get; set; }
}

internal sealed class SkillCgTriggerContext
{
    public long ActionSequence { get; set; }
    public string Action { get; set; } = "";
    public string CardId { get; set; } = "";
    public string EventToken { get; set; } = "";
    public string OwnerInstanceId { get; set; } = "";
    public string OwnerRoleId { get; set; } = "";
}

internal sealed class SkillCgRequest
{
    public string ProviderId { get; set; } = "";
    public string OwnerModId { get; set; } = "";
    public string CardId { get; set; } = "";
    public string OwnerInstanceId { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string ImageResource { get; set; } = "";
    public string BundlePath { get; set; } = "";
    public string BundleAssetPrefix { get; set; } = "";
    public string MediaType { get; set; } = "";
    public float FrameSeconds { get; set; }
    public string AlphaMode { get; set; } = "";
    public float KeyThreshold { get; set; }
    public float KeySoftness { get; set; }
    public float FlashAtSeconds { get; set; }
    public float FlashDuration { get; set; }
    public string FlashMode { get; set; } = "";
    public int FlashStartFrame { get; set; }
    public int FlashEndFrame { get; set; }
    public int FlashPulseEveryFrames { get; set; }
    public float FlashStrength { get; set; }
    public int Priority { get; set; }
    public float FadeIn { get; set; }
    public float Hold { get; set; }
    public float FadeOut { get; set; }
    public string PresentationMode { get; set; } = "";
    public string FitMode { get; set; } = "";
    public float FocusX { get; set; }
    public float FocusY { get; set; }
    public float SafeScale { get; set; }
    public float CreatedAt { get; set; }
    public long ActionSequence { get; set; }
    public string EventToken { get; set; } = "";
    public bool DisableSync { get; set; }
}

internal sealed class SkillCgNetworkEvent
{
    public string ProviderId { get; set; } = "";
    public string OwnerModId { get; set; } = "";
    public string CgId { get; set; } = "";
    public string CardId { get; set; } = "";
    public string OwnerInstanceId { get; set; } = "";
    public long ActionSequence { get; set; }
    public string EventToken { get; set; } = "";
    public string IssuerPlayerId { get; set; } = "";
    public string SkillCgPlayId { get; set; } = "";
}

internal sealed class SkillCgPlaybackSnapshot
{
    public string IssuerPlayerId { get; set; } = "";
    public string SkillCgPlayId { get; set; } = "";
    public string OwnerStatusId { get; set; } = "";
    public string CardId { get; set; } = "";
    public long ActionSequence { get; set; }
    public string FightToken { get; set; } = "";
    public List<SkillCgNetworkEvent> Events { get; set; } = new();
}
