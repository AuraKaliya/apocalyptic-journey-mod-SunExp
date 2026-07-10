using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraCg.Shared;
using Newtonsoft.Json;
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
            SkillCgExpLog.WarnOnce("config-missing", "SkillCGConfig.json not found; using built-in official role skill CG rules.");
            return CreateDefault();
        }

        try
        {
            var text = File.ReadAllText(path);
            var config = JsonConvert.DeserializeObject<SkillCgConfig>(text) ?? CreateDefault();
            if (config.enabled && (config.rules == null || config.rules.Length == 0))
            {
                SkillCgExpLog.WarnOnce("config-rules-empty", "SkillCGConfig.json loaded with no rules; using built-in official role skill CG rules.");
                config.rules = CreateDefault().rules;
            }

            config.Normalize(modDirectory);
            SkillCgExpLog.InfoOnce("config-loaded", "SkillCGConfig loaded: path=" + path + ", rules=" + config.rules.Length + ", summary=" + BuildRuleSummary(config.rules));
            return config;
        }
        catch (Exception ex)
        {
            SkillCgExpLog.WarnOnce("config-load-failed", "SkillCGConfig.json failed to load; using defaults. error=" + ex.Message);
            return CreateDefault();
        }
    }

    private static string BuildRuleSummary(IEnumerable<SkillCgRule> rules)
    {
        var summary = rules
            .Select(rule => rule.cardId + "->" + Path.GetFileName(rule.image))
            .ToArray();
        return summary.Length == 0 ? "<none>" : string.Join("|", summary);
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
                    providerId = "SkillCGExp.AmeliaSkillCG",
                    cardId = "careercard_1",
                    action = "*",
                    image = "CG_\u963F\u7C73\u8389\u5A05.png",
                    priority = 10,
                    fadeIn = 0.35f,
                    hold = 1.0f,
                    fadeOut = 0.45f,
                    enabled = true
                },
                new SkillCgRule
                {
                    providerId = "SkillCGExp.AdelaSkillCG",
                    cardId = "careercard_4",
                    action = "*",
                    image = "CG_\u963F\u9EDB\u62C9.png",
                    priority = 10,
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
    public string ResolvedImagePath { get; private set; } = "";

    public void Normalize(string modDirectory)
    {
        providerId = string.IsNullOrWhiteSpace(providerId) ? "SkillCGExp.Rule." + Math.Abs(GetHashCode()) : providerId.Trim();
        cardId = string.IsNullOrWhiteSpace(cardId) ? "*" : cardId.Trim();
        action = string.IsNullOrWhiteSpace(action) ? "Skill" : action.Trim();
        ownerInstanceId = ownerInstanceId?.Trim() ?? "";
        image = string.IsNullOrWhiteSpace(image) ? "CG.png" : image.Trim();
        ResolvedImagePath = ResolveImagePath(modDirectory, image);
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

    private static string ResolveImagePath(string modDirectory, string value)
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
                ImagePath = rule.ResolvedImagePath,
                ImageResource = rule.image,
                Priority = rule.priority,
                FadeIn = rule.fadeIn,
                Hold = rule.hold,
                FadeOut = rule.fadeOut,
                CreatedAt = Time.unscaledTime,
                ActionSequence = trigger.ActionSequence,
                // This prototype has no shared registry manifest, so its file-backed rules remain local-only.
                DisableSync = true
            };
        }
    }
}
