using System;
using System.Collections.Generic;
using System.IO;
using SkillCGExp.Dll.Hooks;
using SkillCGExp.Dll.Infrastructure;
using UnityEngine;

namespace SkillCGExp.Dll.Config;

[Serializable]
public sealed class SkillCgConfig
{
    public bool enabled = true;
    public int maxQueueLength = 8;
    public float maxRequestAgeSeconds = 6f;
    public float duplicateWindowSeconds = 0.2f;
    public SkillCgRule[] rules = Array.Empty<SkillCgRule>();

    public static SkillCgConfig Load(string modDirectory)
    {
        var path = Path.Combine(modDirectory, "SkillCGConfig.json");
        if (!File.Exists(path))
        {
            SkillCgExpLog.WarnOnce("config-missing", "SkillCGConfig.json not found; using the built-in CG.png wildcard skill rule.");
            return CreateDefault();
        }

        try
        {
            var text = File.ReadAllText(path);
            var config = JsonUtility.FromJson<SkillCgConfig>(text) ?? CreateDefault();
            config.Normalize(modDirectory);
            return config;
        }
        catch (Exception ex)
        {
            SkillCgExpLog.WarnOnce("config-load-failed", "SkillCGConfig.json failed to load; using defaults. error=" + ex.Message);
            return CreateDefault();
        }
    }

    public void Normalize(string modDirectory)
    {
        maxQueueLength = Mathf.Clamp(maxQueueLength, 1, 30);
        maxRequestAgeSeconds = Mathf.Clamp(maxRequestAgeSeconds, 0.5f, 30f);
        duplicateWindowSeconds = Mathf.Clamp(duplicateWindowSeconds, 0.02f, 2f);
        rules ??= Array.Empty<SkillCgRule>();

        foreach (var rule in rules)
        {
            rule.Normalize(modDirectory);
        }
    }

    private static SkillCgConfig CreateDefault()
    {
        return new SkillCgConfig
        {
            enabled = true,
            maxQueueLength = 8,
            maxRequestAgeSeconds = 6f,
            duplicateWindowSeconds = 0.2f,
            rules = new[]
            {
                new SkillCgRule
                {
                    providerId = "SkillCGExp.DefaultStaticCG",
                    cardId = "*",
                    action = "Skill",
                    image = "CG.png",
                    priority = 0,
                    fadeIn = 0.35f,
                    hold = 1.0f,
                    fadeOut = 0.45f,
                    enabled = true
                }
            }
        };
    }
}

[Serializable]
public sealed class SkillCgRule
{
    public bool enabled = true;
    public string providerId = "";
    public string cardId = "*";
    public string action = "Skill";
    public string ownerInstanceId = "";
    public string image = "CG.png";
    public int priority;
    public float fadeIn = 0.35f;
    public float hold = 1.0f;
    public float fadeOut = 0.45f;

    public void Normalize(string modDirectory)
    {
        providerId = string.IsNullOrWhiteSpace(providerId) ? "SkillCGExp.Rule." + Math.Abs(GetHashCode()) : providerId.Trim();
        cardId = string.IsNullOrWhiteSpace(cardId) ? "*" : cardId.Trim();
        action = string.IsNullOrWhiteSpace(action) ? "Skill" : action.Trim();
        ownerInstanceId = ownerInstanceId?.Trim() ?? "";
        image = NormalizePath(modDirectory, image);
        fadeIn = Mathf.Max(0f, fadeIn);
        hold = Mathf.Max(0f, hold);
        fadeOut = Mathf.Max(0f, fadeOut);
    }

    public bool Matches(SkillCgTriggerContext context)
    {
        if (!enabled || context == null)
        {
            return false;
        }

        if (!MatchesText(action, context.Action))
        {
            return false;
        }

        if (!MatchesText(cardId, context.CardId))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(ownerInstanceId)
            || MatchesText(ownerInstanceId, context.OwnerInstanceId);
    }

    private static bool MatchesText(string pattern, string value)
    {
        return string.Equals(pattern, "*", StringComparison.Ordinal)
            || string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string modDirectory, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Path.Combine(modDirectory, "CG.png");
        }

        return Path.IsPathRooted(value)
            ? value
            : Path.Combine(modDirectory, value.Replace('/', Path.DirectorySeparatorChar));
    }
}

public sealed class ConfigSkillCgProvider
{
    private readonly List<SkillCgRule> rules;

    public ConfigSkillCgProvider(IEnumerable<SkillCgRule> rules)
    {
        this.rules = new List<SkillCgRule>(rules);
    }

    public string ProviderId => "SkillCGExp.ConfigProvider";

    public string OwnerModId => "SkillCGExp";

    public int Priority => 0;

    public IEnumerable<SkillCgRequest> BuildRequests(object context)
    {
        if (context is not SkillCgTriggerContext trigger)
        {
            yield break;
        }

        foreach (var rule in rules)
        {
            if (!rule.Matches(trigger))
            {
                continue;
            }

            yield return new SkillCgRequest
            {
                ProviderId = rule.providerId,
                OwnerModId = OwnerModId,
                CardId = trigger.CardId,
                OwnerInstanceId = trigger.OwnerInstanceId,
                ImagePath = rule.image,
                Priority = rule.priority,
                FadeIn = rule.fadeIn,
                Hold = rule.hold,
                FadeOut = rule.fadeOut,
                CreatedAt = Time.unscaledTime,
                ActionSequence = trigger.ActionSequence
            };
        }
    }
}
