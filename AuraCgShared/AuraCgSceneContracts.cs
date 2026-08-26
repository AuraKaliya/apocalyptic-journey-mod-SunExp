using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace AuraCg.Shared;

public static class AuraCgSceneProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumParticipants = 8;
    public const int MaximumIdentifierLength = 160;
    public const string DefaultLayoutId = "team-stage.v1";
}

[Serializable]
public sealed class AuraCgSceneAssetReference
{
    public string OwnerModId { get; set; } = "";

    public string AssetId { get; set; } = "";

    [JsonIgnore]
    public string QualifiedAssetId => string.IsNullOrWhiteSpace(OwnerModId)
        ? AssetId
        : OwnerModId + ":" + AssetId;

    public void Normalize()
    {
        OwnerModId = (OwnerModId ?? "").Trim();
        AssetId = (AssetId ?? "").Trim();
    }

    public bool IsValid(int maximumIdentifierLength = AuraCgSceneProtocol.MaximumIdentifierLength)
    {
        Normalize();
        return IsBoundedIdentifier(OwnerModId, maximumIdentifierLength)
               && IsBoundedIdentifier(AssetId, maximumIdentifierLength)
               && AssetId.IndexOf('/') < 0
               && AssetId.IndexOf('\\') < 0
               && !AssetId.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    public AuraCgSceneAssetReference Clone()
    {
        return new AuraCgSceneAssetReference
        {
            OwnerModId = OwnerModId,
            AssetId = AssetId
        };
    }

    private static bool IsBoundedIdentifier(string? value, int maximumIdentifierLength)
    {
        var text = (value ?? "").Trim();
        return text.Length > 0 && text.Length <= Math.Max(1, maximumIdentifierLength);
    }
}

[Serializable]
public sealed class AuraCgSceneParticipantPlan
{
    public int SeatIndex { get; set; }

    public string RoleId { get; set; } = "";

    public string RoleVariantId { get; set; } = "";

    public AuraCgSceneAssetReference RoleLayerAsset { get; set; } = new();

    public float CenterX { get; set; } = 0.5f;

    public float CenterY { get; set; } = 0.5f;

    public float Width { get; set; } = 0.25f;

    public float Height { get; set; } = 0.7f;

    public float Scale { get; set; } = 1f;

    public int ZIndex { get; set; }

    public bool MirrorX { get; set; }

    public void Normalize()
    {
        SeatIndex = Math.Max(0, SeatIndex);
        RoleId = (RoleId ?? "").Trim();
        RoleVariantId = (RoleVariantId ?? "").Trim();
        RoleLayerAsset ??= new AuraCgSceneAssetReference();
        RoleLayerAsset.Normalize();
        CenterX = Clamp01(CenterX);
        CenterY = Clamp01(CenterY);
        Width = Clamp(Width, 0.02f, 1f);
        Height = Clamp(Height, 0.02f, 1f);
        Scale = Clamp(Scale <= 0f ? 1f : Scale, 0.1f, 3f);
        ZIndex = Math.Max(-100, Math.Min(100, ZIndex));
    }

    public bool IsValid(int maximumIdentifierLength = AuraCgSceneProtocol.MaximumIdentifierLength)
    {
        Normalize();
        return RoleId.Length > 0
               && RoleId.Length <= maximumIdentifierLength
               && RoleVariantId.Length <= maximumIdentifierLength
               && RoleLayerAsset.IsValid(maximumIdentifierLength);
    }

    private static float Clamp01(float value)
    {
        return Clamp(value, 0f, 1f);
    }

    private static float Clamp(float value, float minimum, float maximum)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return minimum;
        return Math.Max(minimum, Math.Min(maximum, value));
    }
}

[Serializable]
public sealed class AuraCgScenePlan
{
    public int ProtocolVersion { get; set; } = AuraCgSceneProtocol.CurrentVersion;

    public string SceneId { get; set; } = "";

    public string SignalId { get; set; } = "";

    public string EventToken { get; set; } = "";

    public string LayoutId { get; set; } = AuraCgSceneProtocol.DefaultLayoutId;

    public string PresentationProfileId { get; set; } = "default";

    public int LogicalWidth { get; set; } = 1600;

    public int LogicalHeight { get; set; } = 900;

    public AuraCgSceneAssetReference BackgroundAsset { get; set; } = new();

    public List<AuraCgSceneParticipantPlan> Participants { get; set; } = new();

    [JsonIgnore]
    public string StableKey => SceneId
                               + "|" + SignalId
                               + "|" + EventToken
                               + "|" + BackgroundAsset.QualifiedAssetId
                               + "|" + string.Join(",", Participants
                                   .OrderBy(item => item.SeatIndex)
                                   .Select(item => item.SeatIndex.ToString(CultureInfo.InvariantCulture)
                                                   + ":" + item.RoleId
                                                   + ":" + item.RoleVariantId
                                                   + ":" + item.RoleLayerAsset.QualifiedAssetId));

    public void Normalize()
    {
        if (ProtocolVersion <= 0)
        {
            ProtocolVersion = AuraCgSceneProtocol.CurrentVersion;
        }
        SceneId = (SceneId ?? "").Trim();
        SignalId = (SignalId ?? "").Trim().ToLowerInvariant();
        EventToken = (EventToken ?? "").Trim();
        LayoutId = string.IsNullOrWhiteSpace(LayoutId)
            ? AuraCgSceneProtocol.DefaultLayoutId
            : LayoutId.Trim();
        PresentationProfileId = string.IsNullOrWhiteSpace(PresentationProfileId)
            ? "default"
            : PresentationProfileId.Trim();
        LogicalWidth = Math.Max(1, Math.Min(8192, LogicalWidth));
        LogicalHeight = Math.Max(1, Math.Min(8192, LogicalHeight));
        BackgroundAsset ??= new AuraCgSceneAssetReference();
        BackgroundAsset.Normalize();
        Participants = (Participants ?? new List<AuraCgSceneParticipantPlan>())
            .Where(item => item != null)
            .Take(AuraCgSceneProtocol.MaximumParticipants)
            .ToList();
        foreach (var participant in Participants)
        {
            participant.Normalize();
        }

        Participants = Participants
            .GroupBy(item => item.SeatIndex)
            .Select(group => group.First())
            .OrderBy(item => item.SeatIndex)
            .ToList();
    }

    public bool IsValid(int maximumIdentifierLength = AuraCgSceneProtocol.MaximumIdentifierLength)
    {
        Normalize();
        return ProtocolVersion == AuraCgSceneProtocol.CurrentVersion
               && IsBounded(SceneId, maximumIdentifierLength)
               && IsBounded(SignalId, maximumIdentifierLength)
               && IsBounded(EventToken, maximumIdentifierLength)
               && IsBounded(LayoutId, maximumIdentifierLength)
               && IsBounded(PresentationProfileId, maximumIdentifierLength)
               && BackgroundAsset.IsValid(maximumIdentifierLength)
               && Participants.Count > 0
               && Participants.Count <= AuraCgSceneProtocol.MaximumParticipants
               && Participants.All(item => item.IsValid(maximumIdentifierLength));
    }

    private static bool IsBounded(string? value, int maximumIdentifierLength)
    {
        var text = (value ?? "").Trim();
        return text.Length > 0 && text.Length <= Math.Max(1, maximumIdentifierLength);
    }
}

public sealed class AuraCgSceneParticipantSource
{
    public int Order { get; set; }

    public string PlayerId { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string RoleVariantId { get; set; } = "";

    public AuraCgSceneAssetReference? RoleLayerAsset { get; set; }
}

public sealed class AuraCgSceneSourceSnapshot
{
    public string SceneId { get; set; } = "";

    public string EventToken { get; set; } = "";

    public List<AuraCgSceneParticipantSource> Participants { get; set; } = new();
}

public sealed class AuraCgSceneTemplateSpec
{
    public string LayoutId { get; set; } = AuraCgSceneProtocol.DefaultLayoutId;

    public string PresentationProfileId { get; set; } = "default";

    public int LogicalWidth { get; set; } = 1600;

    public int LogicalHeight { get; set; } = 900;

    public int MaximumParticipants { get; set; } = AuraCgSceneProtocol.MaximumParticipants;

    public AuraCgSceneAssetReference BackgroundAsset { get; set; } = new();

    public string RoleLayerOwnerModId { get; set; } = "";

    public string RoleLayerAssetPrefix { get; set; } = "role.idle.";

    public bool Exclusive { get; set; } = true;

    public void Normalize()
    {
        LayoutId = string.IsNullOrWhiteSpace(LayoutId)
            ? AuraCgSceneProtocol.DefaultLayoutId
            : LayoutId.Trim();
        PresentationProfileId = string.IsNullOrWhiteSpace(PresentationProfileId)
            ? "default"
            : PresentationProfileId.Trim();
        LogicalWidth = Math.Max(1, Math.Min(8192, LogicalWidth));
        LogicalHeight = Math.Max(1, Math.Min(8192, LogicalHeight));
        MaximumParticipants = Math.Max(1, Math.Min(AuraCgSceneProtocol.MaximumParticipants, MaximumParticipants));
        BackgroundAsset ??= new AuraCgSceneAssetReference();
        BackgroundAsset.Normalize();
        RoleLayerOwnerModId = (RoleLayerOwnerModId ?? "").Trim();
        RoleLayerAssetPrefix = string.IsNullOrWhiteSpace(RoleLayerAssetPrefix)
            ? "role.idle."
            : RoleLayerAssetPrefix.Trim();
    }
}

internal static class AuraCgTeamScenePlanner
{
    public static AuraCgScenePlan? Build(
        AuraCgSceneSourceSnapshot? source,
        AuraCgSceneTemplateSpec? template,
        string signalId)
    {
        source ??= new AuraCgSceneSourceSnapshot();
        template ??= new AuraCgSceneTemplateSpec();
        template.Normalize();
        var participants = (source.Participants ?? new List<AuraCgSceneParticipantSource>())
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.RoleId))
            .GroupBy(item => string.IsNullOrWhiteSpace(item.PlayerId)
                    ? "role:" + item.RoleId + ":" + item.Order.ToString(CultureInfo.InvariantCulture)
                    : "player:" + item.PlayerId,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Order).First())
            .OrderBy(item => item.Order)
            .ThenBy(item => item.PlayerId, StringComparer.OrdinalIgnoreCase)
            .Take(template.MaximumParticipants)
            .ToList();
        if (participants.Count == 0 || !template.BackgroundAsset.IsValid())
        {
            return null;
        }

        var plan = new AuraCgScenePlan
        {
            SceneId = string.IsNullOrWhiteSpace(source.SceneId)
                ? "team-scene"
                : source.SceneId.Trim(),
            SignalId = (signalId ?? "").Trim(),
            EventToken = string.IsNullOrWhiteSpace(source.EventToken)
                ? "scene-" + Guid.NewGuid().ToString("N")
                : source.EventToken.Trim(),
            LayoutId = template.LayoutId,
            PresentationProfileId = template.PresentationProfileId,
            LogicalWidth = template.LogicalWidth,
            LogicalHeight = template.LogicalHeight,
            BackgroundAsset = template.BackgroundAsset.Clone()
        };
        var slots = AuraCgTeamSceneLayout.Resolve(participants.Count);
        for (var index = 0; index < participants.Count; index++)
        {
            var sourceParticipant = participants[index];
            var slot = slots[index];
            var roleId = sourceParticipant.RoleId.Trim();
            var layer = sourceParticipant.RoleLayerAsset?.Clone() ?? new AuraCgSceneAssetReference
            {
                OwnerModId = template.RoleLayerOwnerModId,
                AssetId = template.RoleLayerAssetPrefix + NormalizeAssetSegment(roleId)
            };
            plan.Participants.Add(new AuraCgSceneParticipantPlan
            {
                SeatIndex = index,
                RoleId = roleId,
                RoleVariantId = (sourceParticipant.RoleVariantId ?? "").Trim(),
                RoleLayerAsset = layer,
                CenterX = slot.CenterX,
                CenterY = slot.CenterY,
                Width = slot.Width,
                Height = slot.Height,
                Scale = slot.Scale,
                ZIndex = slot.ZIndex,
                MirrorX = slot.MirrorX
            });
        }

        plan.Normalize();
        return plan.IsValid() ? plan : null;
    }

    private static string NormalizeAssetSegment(string value)
    {
        var chars = (value ?? "").Trim()
            .Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? char.ToLowerInvariant(character)
                : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }
}

internal readonly struct AuraCgTeamSceneSlot
{
    public AuraCgTeamSceneSlot(
        float centerX,
        float centerY,
        float width,
        float height,
        float scale,
        int zIndex,
        bool mirrorX)
    {
        CenterX = centerX;
        CenterY = centerY;
        Width = width;
        Height = height;
        Scale = scale;
        ZIndex = zIndex;
        MirrorX = mirrorX;
    }

    public float CenterX { get; }
    public float CenterY { get; }
    public float Width { get; }
    public float Height { get; }
    public float Scale { get; }
    public int ZIndex { get; }
    public bool MirrorX { get; }
}

internal static class AuraCgTeamSceneLayout
{
    public static IReadOnlyList<AuraCgTeamSceneSlot> Resolve(int participantCount)
    {
        var count = Math.Max(1, Math.Min(AuraCgSceneProtocol.MaximumParticipants, participantCount));
        var result = new List<AuraCgTeamSceneSlot>(count);
        if (count == 1)
        {
            result.Add(Slot(0.5f, 0.47f, 0.38f, 0.82f, 0));
            return result;
        }

        var lowerCount = Math.Min(4, count);
        AddRow(result, lowerCount, 0.43f, 0.30f, 0.72f, 10);
        var upperCount = count - lowerCount;
        if (upperCount > 0)
        {
            AddRow(result, upperCount, 0.62f, 0.25f, 0.58f, 0);
        }

        return result;
    }

    private static void AddRow(
        ICollection<AuraCgTeamSceneSlot> output,
        int count,
        float centerY,
        float width,
        float height,
        int baseZ)
    {
        var start = count == 1 ? 0.5f : 0.16f;
        var step = count <= 1 ? 0f : 0.68f / (count - 1);
        for (var index = 0; index < count; index++)
        {
            output.Add(Slot(
                start + step * index,
                centerY,
                width,
                height,
                baseZ + index,
                index >= count / 2));
        }
    }

    private static AuraCgTeamSceneSlot Slot(
        float x,
        float y,
        float width,
        float height,
        int zIndex,
        bool mirror = false)
    {
        return new AuraCgTeamSceneSlot(x, y, width, height, 1f, zIndex, mirror);
    }
}
