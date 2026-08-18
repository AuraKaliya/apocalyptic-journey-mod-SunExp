using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AuraGameData.Shared.GameApi;
using Terrias.Dll.GameApi;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

public static class SpiritHoverStatusComposer
{
    public static string Compose(string statusId, string baseText, bool authoritative)
    {
        var normalizedStatusId = statusId ?? "";
        var state = CompanionBattleStateStore.Find(normalizedStatusId);
        if (state == null) return baseText ?? "";
        var visible = authoritative
            ? SpiritVisibleStatusService.Capture(SpiritStateStore.Find(normalizedStatusId)?.Spirit)
            : state.VisibleStatusSnapshot();
        if (visible.Count == 0) return baseText ?? "";

        var builder = new StringBuilder(baseText ?? "");
        var appendedHeader = false;
        foreach (var item in visible.Take(SpiritSystemContract.MaximumVisibleStatuses))
        {
            if (string.Equals(item.Kind, "Buff", StringComparison.Ordinal))
            {
                if (authoritative) continue;
                AppendHeader(builder, ref appendedHeader);
                var presentation = BuffPresentation(item.Id);
                builder.Append("<color=#9FD8FF>").Append(presentation.Name).Append("</color>");
                if (item.Stacks > 0) builder.Append(" ×").Append(item.Stacks);
                if (presentation.Description.Length > 0) builder.Append("  ").Append(presentation.Description);
                builder.AppendLine();
                continue;
            }
            if (!string.Equals(item.Kind, "Mechanic", StringComparison.Ordinal)) continue;
            var passive = SpiritTrainingRegistry.FindPassive(item.Id);
            if (passive == null) continue;
            AppendHeader(builder, ref appendedHeader);
            builder.Append("<color=#FFD36A>").Append(passive.DisplayName).Append("</color>");
            if (item.Maximum > 0) builder.Append("  ").Append(Math.Min(item.Value, item.Maximum)).Append('/').Append(item.Maximum);
            if (!string.IsNullOrWhiteSpace(passive.Description)) builder.Append("  ").Append(passive.Description);
            builder.AppendLine();
        }
        return builder.ToString().TrimEnd();
    }

    private static void AppendHeader(StringBuilder builder, ref bool appended)
    {
        if (appended) return;
        if (builder.Length > 0 && builder[builder.Length - 1] != '\n') builder.AppendLine();
        builder.AppendLine();
        appended = true;
    }

    private static (string Name, string Description) BuffPresentation(string buffId)
    {
        try
        {
            IReadOnlyDictionary<string, string>? row = AuraGameDataHostApi.CopyRow(DataType.Buff, buffId);
            if (row != null)
            {
                var name = First(row, "Name", "Name0", "Name_zh-Hans");
                var description = First(row, "Description", "Description1", "Des");
                return (name.Length > 0 ? name : buffId, description);
            }
        }
        catch
        {
            // A missing optional presentation row must not prevent the tooltip.
        }
        return (buffId ?? "", "");
    }

    private static string First(IReadOnlyDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return "";
    }
}
