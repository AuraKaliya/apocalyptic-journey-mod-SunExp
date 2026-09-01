using System;
using System.Collections.Generic;
using System.Linq;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Application;

public static class SpiritArtifactApplicationService
{
    private static readonly object Gate = new();

    public static bool IsReady => SpiritArtifactRegistry.IsReady && SpiritCollectionApi.EnsureProfileBoundForArtifact();

    public static SpiritArtifactInventory Inventory()
    {
        if (!IsReady) return new SpiritArtifactInventory();
        ReconcilePreparedDraw();
        return SpiritReadModelStore.Current().Collection.ArtifactInventory.Clone();
    }

    public static SpiritArtifactLoadoutView LoadoutView(string spiritUid)
    {
        if (!IsReady) return new SpiritArtifactLoadoutView();
        var collection = SpiritReadModelStore.Current().Collection;
        var spirit = collection.Instances.FirstOrDefault(value => Same(value.SpiritUid, spiritUid));
        return SpiritArtifactLoadoutResolver.Resolve(collection, spirit);
    }

    public static IReadOnlyList<SpiritArtifactInstance> PendingReveal(string token = "")
    {
        var collection = SpiritReadModelStore.Current().Collection;
        var receipt = string.IsNullOrWhiteSpace(token)
            ? collection.ArtifactInventory.PendingReveals.FirstOrDefault()
            : collection.ArtifactInventory.PendingReveals.FirstOrDefault(value => Same(value.Token, token));
        if (receipt == null) return Array.Empty<SpiritArtifactInstance>();
        var ids = new HashSet<string>(receipt.ArtifactUids, StringComparer.Ordinal);
        return collection.ArtifactInventory.Artifacts.Where(value => ids.Contains(value.ArtifactUid))
            .Select(value => value.Clone()).ToArray();
    }

    public static SpiritArtifactOperationResult DrawTen()
    {
        lock (Gate)
        {
            if (!IsReady) return Failure("圣遗物系统或账号档案尚未就绪。");
            ReconcilePreparedDraw();
            var token = Guid.NewGuid().ToString("N");
            SpiritArtifactOperationResult prepared;
            using (var random = new SpiritArtifactCryptoRandom())
            {
                prepared = SpiritCollectionService.ArtifactTransaction(
                    document => SpiritArtifactInventoryService.PrepareDraw(document, random, token),
                    result => result.Success);
            }
            if (!prepared.Success) return prepared;

            var cost = SpiritArtifactRegistry.DrawRules.TruthCost;
            if (!TruthCurrencyApi.TrySpendAndRecord(cost, token, out var debitReason))
            {
                SpiritCollectionService.ArtifactTransaction(
                    document => SpiritArtifactInventoryService.CancelPreparedDraw(document, token),
                    _ => true);
                return Failure(debitReason);
            }

            try
            {
                var committed = SpiritCollectionService.ArtifactTransaction(
                    document => SpiritArtifactInventoryService.CommitPreparedDraw(document, token),
                    result => result.Success);
                if (committed.Success) return committed;
                TruthCurrencyApi.RefundAndRemoveRecord(cost, token);
                SpiritCollectionService.ArtifactTransaction(
                    document => SpiritArtifactInventoryService.CancelPreparedDraw(document, token),
                    _ => true);
                return committed;
            }
            catch (Exception ex)
            {
                TruthCurrencyApi.RefundAndRemoveRecord(cost, token);
                try
                {
                    SpiritCollectionService.ArtifactTransaction(
                        document => SpiritArtifactInventoryService.CancelPreparedDraw(document, token),
                        _ => true);
                }
                catch { }
                TerriasLog.Error("[SpiritArtifact] draw commit failed", ex);
                return Failure("圣遗物入库失败，真理之晶已经退还。");
            }
        }
    }

    public static SpiritArtifactOperationResult SetTarget(string poolId, string setId)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactInventoryService.SetTarget(document, poolId, setId),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult Upgrade(string artifactUid)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        using var random = new SpiritArtifactCryptoRandom();
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactInventoryService.Upgrade(document, artifactUid, random),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult ToggleLock(string artifactUid)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactInventoryService.ToggleLock(document, artifactUid),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult SetLock(IReadOnlyCollection<string> artifactUids, bool locked)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactInventoryService.SetLock(document, artifactUids, locked),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult Dismantle(IReadOnlyCollection<string> artifactUids)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactInventoryService.Dismantle(document, artifactUids),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult Equip(string spiritUid, string artifactUid)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactInventoryService.Equip(document, spiritUid, artifactUid),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult Unequip(string spiritUid, string slotId)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactInventoryService.Unequip(document, spiritUid, slotId),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult SaveCurrentPreset(string spiritUid, string name = "")
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document =>
            {
                var spirit = document.Instances.FirstOrDefault(value => Same(value.SpiritUid, spiritUid));
                var resolvedName = string.IsNullOrWhiteSpace(name)
                    ? SpiritArtifactPresetService.SuggestName(document, spirit?.ArtifactLoadout)
                    : name;
                return SpiritArtifactPresetService.SaveCurrent(document, spiritUid, resolvedName);
            },
            result => result.Success);
    }

    public static SpiritArtifactOperationResult SavePreset(SpiritArtifactPreset preset)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        var draft = preset?.Clone() ?? new SpiritArtifactPreset();
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactPresetService.Save(document, draft),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult DeletePreset(string presetUid)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactPresetService.Delete(document, presetUid),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult MovePreset(string presetUid, int delta)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactPresetService.Move(document, presetUid, delta),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult ApplyPreset(string spiritUid, string presetUid)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactPresetService.Apply(document, spiritUid, presetUid),
            result => result.Success);
    }

    public static SpiritArtifactOperationResult AcknowledgeReveal(string token)
    {
        if (!IsReady) return Failure("圣遗物系统尚未就绪。");
        return SpiritCollectionService.ArtifactTransaction(
            document => SpiritArtifactInventoryService.AcknowledgeReveal(document, token),
            result => result.Success);
    }

    public static string EquippedSpiritUid(string artifactUid)
    {
        if (!IsReady) return "";
        var document = SpiritReadModelStore.Current().Collection;
        return SpiritArtifactInventoryService.EquippedSpiritUid(document, artifactUid);
    }

    public static int TruthBalance() => TruthCurrencyApi.Balance();

    public static UnityEngine.Sprite? TruthCurrencySprite() => TruthCurrencyApi.CurrencySprite();

    public static void ReconcilePreparedDraw()
    {
        if (!SpiritArtifactRegistry.IsReady || !SpiritCollectionApi.EnsureProfileBoundForArtifact()) return;
        var pending = SpiritReadModelStore.Current().Collection.ArtifactInventory.PreparedDraw;
        if (pending == null || string.IsNullOrWhiteSpace(pending.Token)) return;
        try
        {
            if (TruthCurrencyApi.HasDebitToken(pending.Token))
            {
                var committed = SpiritCollectionService.ArtifactTransaction(
                    document => SpiritArtifactInventoryService.CommitPreparedDraw(document, pending.Token),
                    result => result.Success);
                if (committed.Success)
                {
                    TerriasLog.Info("[SpiritArtifact] recovered committed draw token=" + pending.Token + ".");
                }
                else
                {
                    TruthCurrencyApi.RefundAndRemoveRecord(pending.TruthCost, pending.Token);
                    SpiritCollectionService.ArtifactTransaction(
                        document => SpiritArtifactInventoryService.CancelPreparedDraw(document, pending.Token),
                        _ => true);
                    TerriasLog.Warn("[SpiritArtifact] refunded invalid prepared draw token=" + pending.Token
                                    + ": " + committed.Reason);
                }
            }
            else
            {
                SpiritCollectionService.ArtifactTransaction(
                    document => SpiritArtifactInventoryService.CancelPreparedDraw(document, pending.Token),
                    _ => true);
                TerriasLog.Warn("[SpiritArtifact] canceled uncharged prepared draw token=" + pending.Token + ".");
            }
        }
        catch (Exception ex)
        {
            TerriasLog.Error("[SpiritArtifact] prepared draw reconciliation failed", ex);
        }
    }

    private static SpiritArtifactOperationResult Failure(string reason) => new() { Reason = reason ?? "圣遗物操作失败。" };
    private static bool Same(string? left, string? right) => string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
}
