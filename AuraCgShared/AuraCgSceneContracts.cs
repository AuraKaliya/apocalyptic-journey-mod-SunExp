using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace AuraCg.Shared;

public static class AuraCgSceneProtocol
{
    public const int CurrentVersion = 2;
    public const int MaximumParticipants = 8;
    public const int MaximumIdentifierLength = 160;
    public const string DefaultLayoutId = "team-poster.v3";
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

    public bool MotionEnabled { get; set; } = true;

    public AuraCgSceneAssetReference BackgroundAsset { get; set; } = new();

    public List<AuraCgSceneParticipantPlan> Participants { get; set; } = new();

    [JsonIgnore]
    public string StableKey => SceneId
                               + "|" + LayoutId + "|" + PresentationProfileId
                               + "|" + LogicalWidth + "x" + LogicalHeight
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
               && string.Equals(LayoutId, AuraCgSceneProtocol.DefaultLayoutId, StringComparison.Ordinal)
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

    public string RoleLayerAssetPrefix { get; set; } = "role.portrait.";

    public bool Exclusive { get; set; } = true;

    public void Normalize()
    {
        // Old registered tableau templates are read once into the current portrait layout.
        LayoutId = AuraCgSceneProtocol.DefaultLayoutId;
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
            ? "role.portrait."
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
        var slots = AuraCgAdaptiveTeamLayout.Resolve(
            template.LayoutId,
            plan.SceneId,
            participants.Count);
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

internal static class AuraCgAdaptiveTeamLayout
{
    public static IReadOnlyList<AuraCgTeamSceneSlot> Resolve(string layoutId, string presentationProfileId, int participantCount)
    {
        var count = Math.Max(1, Math.Min(AuraCgSceneProtocol.MaximumParticipants, participantCount));
        var profile = (presentationProfileId ?? "").ToLowerInvariant();
        var offset = profile.Contains("defeat") ? -0.015f : profile.Contains("opening") ? 0.015f : 0f;
        var result = new List<AuraCgTeamSceneSlot>(count);
        if (count == 1) result.Add(Slot(0.60f, 0.52f + offset, 0.22f, 0.29f, 10));
        else if (count == 2)
        {
            result.Add(Slot(0.30f, 0.49f + offset, 0.18f, 0.23f, 20));
            result.Add(Slot(0.71f, 0.58f + offset, 0.18f, 0.22f, 10));
        }
        else if (count == 3)
        {
            result.Add(Slot(0.20f, 0.46f + offset, 0.15f, 0.20f, 20));
            result.Add(Slot(0.50f, 0.66f + offset, 0.14f, 0.17f, 0));
            result.Add(Slot(0.80f, 0.47f + offset, 0.15f, 0.20f, 21));
        }
        else if (count == 4)
        {
            result.Add(Slot(0.193f, 0.50f + offset, 0.13f, 0.145f, 20));
            result.Add(Slot(0.39f, 0.671f + offset, 0.12f, 0.135f, 0));
            result.Add(Slot(0.589f, 0.435f + offset, 0.13f, 0.155f, 21));
            result.Add(Slot(0.828f, 0.667f + offset, 0.12f, 0.125f, 1));
        }
        else
        {
            var frontCount = (count + 1) / 2;
            var backCount = count / 2;
            for (var index = 0; index < count; index++)
            {
                var front = index % 2 == 0;
                var rowIndex = index / 2;
                var rowCount = front ? frontCount : backCount;
                var left = rowCount == 2 ? 0.34f : rowCount == 3 ? (front ? 0.17f : 0.23f) : 0.13f;
                var right = rowCount == 2 ? 0.67f : rowCount == 3 ? (front ? 0.83f : 0.79f) : 0.87f;
                var x = left + (right - left) * rowIndex / Math.Max(1, rowCount - 1);
                var y = front ? 0.36f + (rowIndex % 2) * 0.035f : 0.70f;
                var height = count >= 7 ? (front ? 0.135f : 0.11f) : (front ? 0.16f : 0.13f);
                result.Add(Slot(x, y + offset, count >= 7 ? 0.10f : 0.12f, height, front ? 20 + rowIndex : rowIndex));
            }
        }
        return result;
    }

    private static AuraCgTeamSceneSlot Slot(float x, float y, float width, float height, int zIndex) =>
        new(x, y, width, height, 1f, zIndex, false);
}
