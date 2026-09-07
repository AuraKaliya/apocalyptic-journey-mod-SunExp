using System;
using System.Collections.Generic;
using Data.Save;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Network;

namespace Terrias.Dll.Mechanics;

public readonly struct ElementalMagicRange
{
    public ElementalMagicRange(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public int Minimum { get; }

    public int Maximum { get; }
}

public static class ElementalMagicService
{
    public const int ProtocolVersion = 1;

    private static readonly Dictionary<string, ElementalEnemyMagicSnapshot> PendingSnapshots = new(StringComparer.Ordinal);
    private static int battleEpoch;

    public static int BattleEpoch => battleEpoch;

    public static void BeginBattle()
    {
        battleEpoch = battleEpoch == int.MaxValue ? 1 : battleEpoch + 1;
        PendingSnapshots.Clear();
    }

    public static void EndBattle()
    {
        PendingSnapshots.Clear();
    }

    public static ElementalMagicRange RangeForRarity(int rarity)
    {
        return rarity switch
        {
            2 => new ElementalMagicRange(8, 20),
            3 => new ElementalMagicRange(15, 40),
            _ => new ElementalMagicRange(1, 10)
        };
    }

    public static int Read(IStatusManager? source)
    {
        if (source?.fatherObject is Enemy enemy)
        {
            return EnsureEnemyMagic(enemy, "ElementalMagicService.Read");
        }

        var vars = FightManager.Instance?.TempVarsMap;
        if (vars == null || vars.Count == 0)
        {
            vars = RoleTable.Instance?.VarsMap;
        }

        return vars != null && vars.TryGetValue("Strength", out var magic)
            ? Math.Max(0, magic)
            : 0;
    }

    public static void ObserveEnemy(Enemy? enemy, string source)
    {
        if (enemy?.Status == null)
        {
            return;
        }

        var statusId = enemy.Status.InstanceId ?? "";
        if (statusId.Length > 0 && PendingSnapshots.TryGetValue(statusId, out var pending))
        {
            PendingSnapshots.Remove(statusId);
            ApplyNetworkSnapshot(pending, source + ":pending");
            return;
        }

        EnsureEnemyMagic(enemy, source);
    }

    public static void ApplyNetworkSnapshot(ElementalEnemyMagicSnapshot? snapshot, string source)
    {
        if (snapshot == null
            || snapshot.ProtocolVersion != ProtocolVersion
            || snapshot.BattleEpoch != battleEpoch
            || snapshot.Magic <= 0
            || string.IsNullOrWhiteSpace(snapshot.StatusId))
        {
            return;
        }

        var status = StatusApi.FindById(snapshot.StatusId);
        if (status == null)
        {
            PendingSnapshots[snapshot.StatusId] = snapshot;
            return;
        }

        StatusApi.SetDynamicFloat(status, TerriasIds.ElementalEnemyMagicKey, snapshot.Magic);
        StatusApi.SetDynamicFloat(status, TerriasIds.ElementalEnemyMagicRarityKey, snapshot.Rarity);
        TerriasLog.Debug("[ElementalMagic] applied snapshot; status="
            + snapshot.StatusId
            + ", rarity="
            + snapshot.Rarity
            + ", magic="
            + snapshot.Magic
            + ", source="
            + source
            + ".");
    }

    private static int EnsureEnemyMagic(Enemy enemy, string source)
    {
        var status = enemy.Status;
        var existing = (int)StatusApi.DynamicFloat(status, TerriasIds.ElementalEnemyMagicKey);
        if (existing > 0)
        {
            return existing;
        }

        var rarity = NormalizeRarity(DictionaryUtil.GetInt(enemy.data, "Rarity", 1));
        var range = RangeForRarity(rarity);
        if (TerriasNetworkQueries.IsClientOnly())
        {
            TerriasLog.Debug("[ElementalMagic] client awaited authoritative roll; status="
                + (status?.InstanceId ?? "")
                + ", rarity="
                + rarity
                + ".");
            return range.Minimum;
        }

        var rolled = UnityEngine.Random.Range(range.Minimum, range.Maximum + 1);
        StatusApi.SetDynamicFloat(status, TerriasIds.ElementalEnemyMagicKey, rolled);
        StatusApi.SetDynamicFloat(status, TerriasIds.ElementalEnemyMagicRarityKey, rarity);
        TerriasLog.Info("[ElementalMagic] rolled enemy magic; status="
            + (status?.InstanceId ?? "")
            + ", rarity="
            + rarity
            + ", range="
            + range.Minimum
            + "-"
            + range.Maximum
            + ", magic="
            + rolled
            + ", source="
            + source
            + ".");

        if (TerriasNetworkQueries.IsServer() && TerriasNetworkQueries.HasRemotePlayers())
        {
            TerriasNetworkRuntime.Send(
                new RpcElementalEnemyMagicSnapshot(new ElementalEnemyMagicSnapshot
                {
                    ProtocolVersion = ProtocolVersion,
                    BattleEpoch = battleEpoch,
                    StatusId = status?.InstanceId ?? "",
                    Rarity = rarity,
                    Magic = rolled
                }),
                "ElementalMagicService.EnsureEnemyMagic");
        }

        return rolled;
    }

    private static int NormalizeRarity(int rarity)
    {
        return rarity is >= 1 and <= 3 ? rarity : 1;
    }
}

[Serializable]
public sealed class ElementalEnemyMagicSnapshot
{
    public int ProtocolVersion { get; set; } = ElementalMagicService.ProtocolVersion;

    public int BattleEpoch { get; set; }

    public string StatusId { get; set; } = "";

    public int Rarity { get; set; }

    public int Magic { get; set; }
}
