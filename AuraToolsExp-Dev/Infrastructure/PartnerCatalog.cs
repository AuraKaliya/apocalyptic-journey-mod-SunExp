using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using Witch.Core;

namespace AuraToolsExp.Dll.Infrastructure;

public sealed class PartnerInfo
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public List<string> BlessingIds { get; set; } = new();
}

public static class PartnerCatalog
{
    private static readonly object Gate = new();
    private static List<PartnerInfo> cached = new();
    private static float lastScanRealtime;

    public static IReadOnlyList<PartnerInfo> GetPartners(bool forceRefresh = false)
    {
        lock (Gate)
        {
            if (!forceRefresh
                && cached.Count > 0
                && UnityEngine.Time.realtimeSinceStartup - lastScanRealtime < 10f)
            {
                return cached.ToList();
            }

            cached = ScanPartners();
            lastScanRealtime = UnityEngine.Time.realtimeSinceStartup;
            return cached.ToList();
        }
    }

    public static string GetDisplayName(string partnerId)
    {
        var normalized = (partnerId ?? "").Trim();
        return GetPartners()
            .FirstOrDefault(item => string.Equals(
                item.Id,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            ?.DisplayName ?? normalized;
    }

    public static IReadOnlyList<string> GetBlessingIds(
        string partnerId,
        bool forceRefresh = false)
    {
        var normalized = (partnerId ?? "").Trim();
        return GetPartners(forceRefresh)
            .FirstOrDefault(item => string.Equals(
                item.Id,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            ?.BlessingIds.ToList() ?? new List<string>();
    }

    private static List<PartnerInfo> ScanPartners()
    {
        var result = new List<PartnerInfo>();
        try
        {
            var query = AuraGameDataHostApi.Query(
                DataType.Partner,
                includeAllCandidates: true);
            foreach (var item in query.Items.Where(item =>
                         item.Enabled && !item.Retired))
            {
                var row = item.Fields;
                result.Add(new PartnerInfo
                {
                    Id = item.Id,
                    DisplayName = ResolveDisplayName(item.Id, row),
                    OwnerModId = item.OwnerModId,
                    BlessingIds = SplitIds(
                        row.TryGetValue("Bless", out var bless) ? bless : "")
                });
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Partner scan failed: " + ex.Message);
        }

        return result
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .ToList();
    }

    private static string ResolveDisplayName(
        string id,
        IReadOnlyDictionary<string, string> row)
    {
        try
        {
            var localized = (row as IDictionary<string, string>)?.Localize("Name") ?? "";
            if (!string.IsNullOrWhiteSpace(localized)
                && !string.Equals(
                    localized,
                    "Name",
                    StringComparison.OrdinalIgnoreCase))
            {
                return localized;
            }
        }
        catch
        {
        }

        return row.TryGetValue("Name", out var name)
               && !string.IsNullOrWhiteSpace(name)
            ? name
            : id;
    }

    private static List<string> SplitIds(string value)
    {
        return (value ?? "")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
