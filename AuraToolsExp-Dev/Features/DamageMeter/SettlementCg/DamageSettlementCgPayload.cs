using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;

namespace AuraToolsExp.Dll.Features.DamageMeter.SettlementCg;

[Serializable]
public sealed class DamageSettlementCgPayload
{
    public int ProtocolVersion { get; set; } = DamageMeterProtocol.Version;

    public string AdventureId { get; set; } = "";

    public string BackgroundResource { get; set; } = "";

    public string EndedUtc { get; set; } = "";

    public List<DamageSettlementCgEntry> Entries { get; set; } = new();
}

[Serializable]
public sealed class DamageSettlementCgEntry
{
    public int Rank { get; set; }

    public string InstanceId { get; set; } = "";

    public string PlayerId { get; set; } = "";

    public string PlayerDisplayName { get; set; } = "";

    public string RoleId { get; set; } = "";

    public string RoleDisplayName { get; set; } = "";

    public string PreparedClipKey { get; set; } = "";

    public long TotalDamage { get; set; }

    public double Dps { get; set; }
}

public static class DamageSettlementCgBuilder
{
    public static DamageSettlementCgPayload Build(
        OutOfRunDamageHistoryRecord record,
        DamageSettlementCgSettings settings)
    {
        record ??= new OutOfRunDamageHistoryRecord();
        settings ??= new DamageSettlementCgSettings();
        settings.Normalize();

        var entries = (record.TeamMembers ?? new List<OutOfRunTeamMemberSnapshot>())
            .Where(member => member != null)
            .Where(member => !string.IsNullOrWhiteSpace(member.RoleId)
                             || !string.IsNullOrWhiteSpace(member.InstanceId)
                             || !string.IsNullOrWhiteSpace(member.PlayerId))
            .OrderByDescending(member => Math.Max(0, member.TotalDamage))
            .ThenByDescending(member => member.Dps)
            .ThenBy(member => member.RoleDisplayName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.PlayerId ?? "", StringComparer.OrdinalIgnoreCase)
            .Take(DamageSettlementCgLayout.MaxSlots)
            .Select((member, index) => new DamageSettlementCgEntry
            {
                Rank = index + 1,
                InstanceId = member.InstanceId ?? "",
                PlayerId = string.IsNullOrWhiteSpace(member.PlayerId) ? member.InstanceId ?? "" : member.PlayerId ?? "",
                PlayerDisplayName = member.PlayerDisplayName ?? "",
                RoleId = member.RoleId ?? "",
                RoleDisplayName = member.RoleDisplayName ?? "",
                TotalDamage = Math.Max(0, member.TotalDamage),
                Dps = Math.Max(0d, member.Dps)
            })
            .ToList();

        return new DamageSettlementCgPayload
        {
            AdventureId = record.AdventureId ?? "",
            BackgroundResource = settings.BackgroundResource,
            EndedUtc = record.EndedUtc ?? "",
            Entries = entries
        };
    }
}

public static class DamageSettlementCgLayout
{
    public const int MaxSlots = 4;

    private static readonly DamageSettlementCgPoint[] SlotOrigins =
    {
        new(650f, 280f),
        new(450f, 360f),
        new(910f, 400f),
        new(1100f, 500f)
    };

    public static DamageSettlementCgLayoutResult Calculate(
        float viewportWidth,
        float viewportHeight,
        DamageSettlementCgSettings settings)
    {
        settings ??= new DamageSettlementCgSettings();
        settings.Normalize();
        var baseWidth = Math.Max(1f, settings.BaseWidth);
        var baseHeight = Math.Max(1f, settings.BaseHeight);
        var width = Math.Max(1f, viewportWidth);
        var height = Math.Max(1f, viewportHeight);
        var scale = Math.Max(width / baseWidth, height / baseHeight);
        var imageWidth = baseWidth * scale;
        var imageHeight = baseHeight * scale;
        var offsetX = (width - imageWidth) * 0.5f;
        var offsetY = (height - imageHeight) * 0.5f;
        var slotSize = Math.Max(1f, settings.SlotSize) * scale;

        return new DamageSettlementCgLayoutResult
        {
            Scale = scale,
            Background = new DamageSettlementCgRect(offsetX, offsetY, imageWidth, imageHeight),
            Slots = SlotOrigins
                .Select((origin, index) => new DamageSettlementCgSlotLayout
                {
                    Rank = index + 1,
                    Rect = new DamageSettlementCgRect(
                        offsetX + origin.X * scale,
                        offsetY + origin.Y * scale,
                        slotSize,
                        slotSize)
                })
                .ToList()
        };
    }
}

public sealed class DamageSettlementCgLayoutResult
{
    public float Scale { get; set; } = 1f;

    public DamageSettlementCgRect Background { get; set; } = new(0f, 0f, 1f, 1f);

    public List<DamageSettlementCgSlotLayout> Slots { get; set; } = new();

    public DamageSettlementCgSlotLayout? SlotForRank(int rank)
    {
        return Slots.FirstOrDefault(slot => slot.Rank == rank);
    }
}

public sealed class DamageSettlementCgSlotLayout
{
    public int Rank { get; set; }

    public DamageSettlementCgRect Rect { get; set; } = new(0f, 0f, 1f, 1f);
}

public readonly struct DamageSettlementCgPoint
{
    public DamageSettlementCgPoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float X { get; }

    public float Y { get; }
}

public readonly struct DamageSettlementCgRect
{
    public DamageSettlementCgRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public float X { get; }

    public float Y { get; }

    public float Width { get; }

    public float Height { get; }
}
