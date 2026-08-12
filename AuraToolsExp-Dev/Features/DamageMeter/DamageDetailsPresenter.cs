using System;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Features.Settings;
using AuraUi.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AuraToolsExp.Dll.Features.DamageMeter;

internal static class DamageDetailsPresenter
{
    internal static void Show(string instanceId, DamageLedger ledger, DamageMeterSettings settings)
    {
        AuraToolsDamageMeterUi.EnsureRoot();
        var stat = ledger.Combatants.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
        if (stat == null)
        {
            return;
        }

        Show(stat, Math.Max(1, ledger.AveragingRoundCount), "本场战斗", stat.DisplayCurrentRound(true));
    }

    internal static void Show(CombatantDamageStat stat, int averagingRounds, string scopeLabel, long currentRound)
    {
        AuraToolsDamageMeterUi.EnsureRoot();
        if (AuraToolsDamageMeterUi.Root == null || stat == null)
        {
            return;
        }

        AuraToolsDamageMeterUi.CloseDetails();
        var overlay = AuraToolsDamageMeterUi.CreateRect(
            "AuraToolsDamageMeterDetails",
            AuraToolsDamageMeterUi.Root.transform,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);
        AuraToolsDamageMeterUi.AddPanel(overlay, new Color(0f, 0f, 0f, 0.35f));
        var blocker = overlay.AddComponent<Button>();
        blocker.targetGraphic = overlay.GetComponent<Image>();
        blocker.onClick.AddListener(AuraToolsDamageMeterUi.CloseDetails);

        var window = AuraToolsDamageMeterUi.CreateRect(
            "Window", overlay.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(500f, 440f));
        AuraToolsDamageMeterUi.AddPanel(window, new Color(0.04f, 0.035f, 0.06f, 0.98f));
        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        RenderHeader(window.transform, stat.DisplayName);
        RenderSummary(window.transform, stat, Math.Max(1, averagingRounds), scopeLabel, currentRound);
        RenderRows(window.transform, stat);
    }

    private static void RenderHeader(Transform parent, string displayName)
    {
        var header = AuraToolsDamageMeterUi.CreateLayout("Header", parent);
        AuraToolsDamageMeterUi.SetHeight(header, 36f);
        var layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        AuraToolsDamageMeterUi.AddText(header.transform, displayName + " 伤害明细", 16,
            TextAnchor.MiddleLeft, AuraToolsUi.Accent, 32f, 1f);
        AuraToolsDamageMeterUi.AddButton(header.transform, "关闭", AuraToolsDamageMeterUi.CloseDetails, 74f, 32f);
    }

    private static void RenderSummary(
        Transform parent,
        CombatantDamageStat stat,
        int averagingRounds,
        string scopeLabel,
        long currentRound)
    {
        var summary = scopeLabel == "本轮冒险"
            ? scopeLabel + " " + stat.DisplayTotal(true)
              + "　 平均DPT " + stat.AveragePerCompletedRound(true, averagingRounds).ToString("0.0")
              + "\nHP伤害 " + stat.TotalHpDamage
              + "　 护盾伤害 " + stat.TotalShieldDamage
            : "本回合 " + currentRound
              + "　 " + scopeLabel + " " + stat.DisplayTotal(true)
              + "　 平均DPT " + stat.AveragePerCompletedRound(true, averagingRounds).ToString("0.0")
              + "\nHP伤害 " + stat.TotalHpDamage
              + "　 护盾伤害 " + stat.TotalShieldDamage
              + "　 最高单回合 " + stat.HighestRound(true);
        AuraToolsDamageMeterUi.AddText(parent, summary, 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 48f, 1f);
    }

    private static void RenderRows(Transform parent, CombatantDamageStat stat)
    {
        var content = AuraToolsDamageMeterUi.CreateLayout("Content", parent);
        content.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var layout = content.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        foreach (var detail in stat.Details.Values
                     .OrderByDescending(item => item.HpDamage + item.ShieldDamage)
                     .Take(12))
        {
            var row = AuraToolsDamageMeterUi.CreateLayout("Detail-" + detail.Key, content.transform);
            AuraToolsDamageMeterUi.SetHeight(row, 32f);
            AuraToolsDamageMeterUi.AddPanel(row, AuraToolsUi.Row);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(8, 8, 2, 2);
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            AuraToolsDamageMeterUi.AddText(row.transform, detail.Label, 13, TextAnchor.MiddleLeft, AuraToolsUi.Text, 28f, 1f);
            AuraToolsDamageMeterUi.AddText(row.transform, ConfidenceLabel(detail.Confidence), 11,
                TextAnchor.MiddleCenter, AuraToolsUi.MutedText, 28f, 0f, 60f);
            AuraToolsDamageMeterUi.AddText(
                row.transform,
                (detail.HpDamage + detail.ShieldDamage).ToString(),
                13, TextAnchor.MiddleRight, AuraToolsUi.Accent, 28f, 0f, 86f);
        }
    }

    private static string ConfidenceLabel(DamageAttributionConfidence confidence)
    {
        return confidence switch
        {
            DamageAttributionConfidence.Exact => "精确",
            DamageAttributionConfidence.Derived => "推导",
            DamageAttributionConfidence.Mixed => "混合",
            _ => "未知"
        };
    }
}
