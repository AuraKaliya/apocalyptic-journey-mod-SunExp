using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SunExp.Dll.Infrastructure;
using Witch.Core;
using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace SunExp.Dll.GameApi;

public static class RoleSkillApi
{
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static bool RefreshFightSkills(string source)
    {
        try
        {
            EnsureCurrentCareerSkillTimes();

            var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
            if (fightUi == null)
            {
                return false;
            }

            ClearSkillItems(fightUi);
            ResetSkillBranches(fightUi);
            fightUi.InitSkill();
            fightUi.UpdateSkill();
            SunExpLog.Info("[Polymorph] refreshed fight skills from " + source + ".");
            return true;
        }
        catch (Exception ex)
        {
            SunExpLog.Warn("[Polymorph] failed to refresh fight skills from " + source + ": " + ex.Message);
            return false;
        }
    }

    public static void EnsureCurrentCareerSkillTimes()
    {
        try
        {
            foreach (var skillId in CurrentCareerSkillIds())
            {
                EnsureSkillTime(skillId);
            }
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[Polymorph] skill time sync skipped: " + ex.Message);
        }
    }

    public static string[] CurrentCareerSkillIds()
    {
        var rawIds = CurrentCareerRawSkillIds();
        var ids = new List<string>(rawIds.Length);
        foreach (var rawId in rawIds)
        {
            var id = NormalizeSkillId(rawId);
            if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids.ToArray();
    }

    public static string[] CurrentCareerRawSkillIds()
    {
        try
        {
            var data = RoleTable.Instance?.Career?.data;
            if (data == null)
            {
                return Array.Empty<string>();
            }

            return new[]
                {
                    DictionaryUtil.Get(data, "Skill1"),
                    DictionaryUtil.Get(data, "Skill2")
                };
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool IsCurrentCareerSkill(IDataConfig? config)
    {
        var id = NormalizeSkillId(CardConfigApi.Id(config));
        if (string.IsNullOrWhiteSpace(id) || id == "unknown")
        {
            return false;
        }

        foreach (var current in CurrentCareerSkillIds())
        {
            if (string.Equals(id, current, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeSkillId(string? cardId)
    {
        var value = (cardId ?? "").Trim();
        return value.Length == 0 ? "" : value.Replace("*", "");
    }

    public static void LogCurrentSkillDiagnostics(string source)
    {
        try
        {
            var raw = string.Join("|", CurrentCareerRawSkillIds());
            var normalized = string.Join("|", CurrentCareerSkillIds());
            var times = new List<string>();
            foreach (var id in CurrentCareerSkillIds())
            {
                times.Add(id + "=" + PlayerApi.GetSkillTime(id));
            }

            SunExpLog.Info("[Polymorph] skill diagnostics from " + source
                + ": career=" + PlayerApi.GetCurrentCareerId()
                + ", raw=" + raw
                + ", normalized=" + normalized
                + ", times=" + string.Join("|", times));
        }
        catch (Exception ex)
        {
            SunExpLog.Debug("[Polymorph] skill diagnostics skipped from " + source + ": " + ex.Message);
        }
    }

    public static void SetCurrentCareerSkillTimes(int cooldown)
    {
        foreach (var skillId in CurrentCareerSkillIds())
        {
            if (!string.IsNullOrWhiteSpace(skillId))
            {
                PlayerApi.SetSkillTime(skillId, cooldown);
            }
        }
    }

    private static void EnsureSkillTime(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return;
        }

        PlayerApi.SetSkillTime(cardId, PlayerApi.GetSkillTime(cardId));
    }

    private static void ClearSkillItems(FightUI fightUi)
    {
        var field = fightUi.GetType().GetField("skillItems", InstanceFlags);
        if (field?.GetValue(fightUi) is IList list)
        {
            list.Clear();
        }
    }

    private static void ResetSkillBranches(FightUI fightUi)
    {
        var root = fightUi.transform;
        var skillOne = root.Find("Left/Skill1");
        var skillTwo = root.Find("Left/Skill2");
        var data = RoleTable.Instance?.Career?.data;
        var hasFirst = !string.IsNullOrWhiteSpace(DictionaryUtil.Get(data, "ActionImage1"));
        var hasSecond = !string.IsNullOrWhiteSpace(DictionaryUtil.Get(data, "ActionImage2"));

        if (skillOne != null)
        {
            skillOne.gameObject.SetActive(hasFirst && !hasSecond);
            ResetSkillSlot(skillOne);
        }

        if (skillTwo != null)
        {
            skillTwo.gameObject.SetActive(hasSecond);
            ResetSkillSlot(skillTwo.Find("Skill1"));
            ResetSkillSlot(skillTwo.Find("Skill2"));
        }
    }

    private static void ResetSkillSlot(Transform? slot)
    {
        if (slot == null)
        {
            return;
        }

        var icon = slot.Find("Icon");
        if (icon != null)
        {
            icon.gameObject.SetActive(false);
        }

        var cd = slot.Find("CD");
        if (cd != null)
        {
            cd.gameObject.SetActive(false);
        }

        var skillItem = slot.GetComponent<SkillItem>();
        if (skillItem != null)
        {
            skillItem.enabled = false;
        }
    }
}
