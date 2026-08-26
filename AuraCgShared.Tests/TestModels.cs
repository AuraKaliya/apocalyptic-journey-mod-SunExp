namespace AuraCg.Shared;

internal static class SkillCgArbiterRuntime
{
    public const string SkillCgKind = "skill";
    public const string CardUseCgKind = "cardUse";
    public const string FeastCgKind = "feast";
}

internal static class SkillCgMediaTypes
{
    public const string Image = "image";
    public const string Sequence = "sequence";
    public const string Scene = "scene";
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

public sealed class AuraCgRegistryEntry
{
    public string CgId { get; set; } = "";
    public string OwnerModId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Kind { get; set; } = "skill";
    public string SubjectType { get; set; } = "";
    public List<string> SubjectIds { get; set; } = new();
    public List<string> Signals { get; set; } = new();
    public AuraCgMatchSpec Match { get; set; } = new();
    public List<string> TargetRoleIds { get; set; } = new();
    public List<string> CardIds { get; set; } = new();
    public List<string> SkillIds { get; set; } = new();
    public AuraCgMediaSpec Media { get; set; } = new();
    public AuraCgPresentationSpec DefaultPresentation { get; set; } = new();
    public AuraCgSceneTemplateSpec? Scene { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;

    public string QualifiedCgId => string.IsNullOrWhiteSpace(OwnerModId)
        ? CgId
        : OwnerModId + ":" + CgId;

    public void Normalize(string fallbackOwner)
    {
        OwnerModId = string.IsNullOrWhiteSpace(OwnerModId) ? fallbackOwner : OwnerModId.Trim();
        CgId = (CgId ?? "").Trim();
        Kind = string.IsNullOrWhiteSpace(Kind) ? "skill" : Kind.Trim();
        TargetRoleIds = Clean(TargetRoleIds);
        CardIds = Clean(CardIds);
        SkillIds = Clean(SkillIds);
        if (string.Equals(Kind, "skill", StringComparison.OrdinalIgnoreCase)
            && SkillIds.Count == 0
            && CardIds.Count > 0)
        {
            SkillIds = new List<string>(CardIds);
            CardIds.Clear();
        }

        if (string.IsNullOrWhiteSpace(SubjectType))
        {
            SubjectType = string.Equals(Kind, "cardUse", StringComparison.OrdinalIgnoreCase)
                ? AuraCgSubjectTypes.Card
                : string.Equals(Kind, "skill", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(Kind, "feast", StringComparison.OrdinalIgnoreCase)
                    ? AuraCgSubjectTypes.Role
                    : AuraCgSubjectTypes.Event;
        }
        SubjectType = AuraCgSubjectTypes.Normalize(SubjectType);
        SubjectIds = Clean(SubjectIds);
        if (SubjectIds.Count == 0)
        {
            SubjectIds = string.Equals(SubjectType, AuraCgSubjectTypes.Card, StringComparison.OrdinalIgnoreCase)
                ? new List<string>(CardIds)
                : string.Equals(SubjectType, AuraCgSubjectTypes.Role, StringComparison.OrdinalIgnoreCase)
                    ? new List<string>(TargetRoleIds)
                    : new List<string> { "*" };
        }

        Signals = Clean(Signals).Select(value => value.ToLowerInvariant()).ToList();
        if (Signals.Count == 0)
        {
            Signals.Add(string.Equals(Kind, "cardUse", StringComparison.OrdinalIgnoreCase)
                ? AuraCgSignals.CardUsePresentationCommitted
                : string.Equals(Kind, "feast", StringComparison.OrdinalIgnoreCase)
                    ? AuraCgSignals.RoleFeastCompleted
                    : string.Equals(Kind, "skill", StringComparison.OrdinalIgnoreCase)
                        ? AuraCgSignals.RoleSkillCommitted
                        : AuraCgSignals.BattleOpening);
        }

        Match ??= new AuraCgMatchSpec();
        Match.Facts ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(Kind, "skill", StringComparison.OrdinalIgnoreCase)
            && SkillIds.Count > 0
            && !Match.Facts.ContainsKey("skillId"))
        {
            Match.Facts["skillId"] = new List<string>(SkillIds);
        }
        Match.Normalize();
        Scene?.Normalize();
    }

    private static List<string> Clean(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class AuraCgMediaSpec
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

public sealed class AuraCgPresentationSpec
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

public sealed class SkillCgTriggerContext
{
    public string SignalId { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string TriggerKind { get; set; } = "";
    public long ActionSequence { get; set; }
    public string Action { get; set; } = "";
    public string CardId { get; set; } = "";
    public string SkillId { get; set; } = "";
    public string EventToken { get; set; } = "";
    public string OwnerInstanceId { get; set; } = "";
    public string OwnerRoleId { get; set; } = "";
    public string BattleId { get; set; } = "";
    public string ModeId { get; set; } = "";
    public string Outcome { get; set; } = "";
    public AuraCgScenePlan? ScenePlan { get; set; }
    public Dictionary<string, string> Facts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> Metrics { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float CreatedAt { get; set; }
}

public sealed class SkillCgRequest
{
    public string ProviderId { get; set; } = "";
    public string OwnerModId { get; set; } = "";
    public string SignalId { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string CardId { get; set; } = "";
    public string TriggerKind { get; set; } = "";
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
    public bool Exclusive { get; set; }
    public AuraCgScenePlan? ScenePlan { get; set; }
    public string IssuerPlayerId { get; set; } = "";
    public string SkillCgPlayId { get; set; } = "";

    public string QualifiedProviderId => string.IsNullOrWhiteSpace(OwnerModId)
        || ProviderId.Contains(":", StringComparison.Ordinal)
            ? ProviderId
            : OwnerModId + ":" + ProviderId;

    public string DuplicateKey => OwnerInstanceId
                                  + "|" + SignalId
                                  + "|" + SubjectType
                                  + "|" + SubjectId
                                  + "|" + TriggerKind
                                  + "|" + CardId
                                  + "|" + ImagePath
                                  + "|" + MediaType
                                  + "|" + FrameSeconds.ToString("0.###")
                                  + "|" + AlphaMode
                                  + "|" + FlashMode
                                  + "|" + FlashAtSeconds.ToString("0.###")
                                  + "|" + FlashStartFrame
                                  + "|" + FlashEndFrame
                                  + "|" + FlashPulseEveryFrames
                                  + "|" + PresentationMode
                                  + "|" + FitMode
                                  + "|" + FocusX.ToString("0.###")
                                  + "|" + FocusY.ToString("0.###")
                                  + "|" + SafeScale.ToString("0.###")
                                  + "|" + (ScenePlan?.StableKey ?? "");
}

internal static class SkillCgFlashModes
{
    public const string Screen = "screen";
    public const string MaskedInvert = "maskedInvert";
    public const string ScreenBwPulse = "screenBwPulse";
    public const string HybridBwPulse = "hybridBwPulse";
}

internal sealed class SkillCgNetworkEvent
{
    public string ProviderId { get; set; } = "";
    public string OwnerModId { get; set; } = "";
    public string CgId { get; set; } = "";
    public string SignalId { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string CardId { get; set; } = "";
    public string TriggerKind { get; set; } = "";
    public string OwnerInstanceId { get; set; } = "";
    public long ActionSequence { get; set; }
    public string EventToken { get; set; } = "";
    public string IssuerPlayerId { get; set; } = "";
    public string SkillCgPlayId { get; set; } = "";
    public AuraCgScenePlan? ScenePlan { get; set; }
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
