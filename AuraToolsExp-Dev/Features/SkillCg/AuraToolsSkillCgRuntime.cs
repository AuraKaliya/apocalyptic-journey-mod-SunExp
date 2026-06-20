using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuraToolsExp.Dll.Config;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;
using UnityEngine.UI;
using Witch;
using Witch.Core;
using Witch.Mod;
using Witch.UI.Window;
using AuraToolsExp.Dll.Features.SkillCg.Arbiter;
using Settings = AuraToolsExp.Dll.Features.Settings;

namespace AuraToolsExp.Dll.Features.SkillCg;

public static class AuraToolsSkillCgRuntime
{
    private static long actionSequence;

    public static void Initialize(ModConfig modConfig)
    {
        SkillCgArbiterRuntime.Initialize(modConfig, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = AuraToolsConfigService.SkillCg.MaxQueueLength,
            MaxRequestAgeSeconds = AuraToolsConfigService.SkillCg.MaxRequestAgeSeconds,
            DuplicateWindowSeconds = AuraToolsConfigService.SkillCg.DuplicateWindowSeconds
        });
        SkillCgArbiterRuntime.RegisterProvider(modConfig, AuraToolsIds.ModId, new AuraToolsSkillCgProvider());

        RegisterBefore(modConfig, "FightUI.CallActionAnimation", BeforeCallActionAnimation);
        RegisterAfter(modConfig, "Fight_Start.Init", OnFightStart);
        RegisterAfter(modConfig, "FightInit.Init", OnFightStart);
        RegisterAfter(modConfig, "Fight_Win.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Escape.ResetStates", OnFightEnded);
        RegisterAfter(modConfig, "Fight_Loss.Init", OnFightEnded);
        AuraToolsConfigService.Changed += Reconfigure;
    }

    private static void Reconfigure()
    {
        SkillCgArbiterRuntime.Initialize(null, AuraToolsIds.ModId, new SkillCgArbiterOptions
        {
            MaxQueueLength = AuraToolsConfigService.SkillCg.MaxQueueLength,
            MaxRequestAgeSeconds = AuraToolsConfigService.SkillCg.MaxRequestAgeSeconds,
            DuplicateWindowSeconds = AuraToolsConfigService.SkillCg.DuplicateWindowSeconds
        });
    }

    private static void BeforeCallActionAnimation(ModHookContext context)
    {
        try
        {
            if (!AuraToolsConfigService.Root.SkillCg.Enabled || !AuraToolsConfigService.SkillCg.Enabled)
            {
                return;
            }

            var scriptExecutor = context.Arguments != null && context.Arguments.Length > 0
                ? context.Arguments[0] as IScriptExecutor
                : null;
            var trigger = BuildTriggerContext(scriptExecutor);
            if (trigger == null)
            {
                return;
            }

            SkillCgArbiterRuntime.Trigger(AuraToolsConfigService.SkillCg, AuraToolsIds.ModId, trigger);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[SkillCG] trigger failed: " + ex.Message);
        }
    }

    private static SkillCgTriggerContext? BuildTriggerContext(IScriptExecutor? scriptExecutor)
    {
        var dataConfig = scriptExecutor?.dataConfig;
        if (dataConfig == null || dataConfig.Type != DataType.Card || dataConfig.data == null)
        {
            return null;
        }

        var cardId = ReadData(dataConfig, "Id");
        if (string.IsNullOrWhiteSpace(cardId))
        {
            cardId = dataConfig.InstanceID ?? "";
        }

        if (string.IsNullOrWhiteSpace(cardId))
        {
            return null;
        }

        var action = ReadData(dataConfig, "Action");
        var owner = scriptExecutor?.Self as StatusManager;
        var ownerInstanceId = owner?.InstanceId ?? "";
        AuraToolsSkillCgProvider.RememberOwnerRole(ownerInstanceId, ReadStatusRoleId(owner));

        return new SkillCgTriggerContext
        {
            ActionSequence = ++actionSequence,
            Action = action,
            CardId = cardId,
            OwnerInstanceId = ownerInstanceId,
            CreatedAt = Time.unscaledTime
        };
    }

    private static string ReadData(IDataConfig dataConfig, string key)
    {
        try
        {
            return dataConfig.data.TryGetValue(key, out var value) ? value ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    private static string ReadStatusRoleId(StatusManager? status)
    {
        try
        {
            var father = status?.fatherObject;
            var id = ReflectionUtil.ReadString(father, "Id", "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }
        catch
        {
        }

        return ReadCurrentCareerId();
    }

    internal static string ReadCurrentCareerId()
    {
        return ReadDataId(RoleTable.Instance?.Career ?? GameEntryUI.career);
    }

    private static string ReadDataId(IDataConfig? data)
    {
        try
        {
            if (data?.data != null && data.data.TryGetValue("Id", out var id))
            {
                return id ?? "";
            }
        }
        catch
        {
        }

        return "";
    }

    private static void OnFightStart(ModHookContext context)
    {
        actionSequence = 0;
        AuraToolsSkillCgProvider.ClearOwnerRoles();
        SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "fight start");
    }

    private static void OnFightEnded(ModHookContext context)
    {
        SkillCgArbiterRuntime.Clear(AuraToolsIds.ModId, "fight ended");
        AuraToolsSkillCgProvider.ClearOwnerRoles();
    }

    private static void RegisterBefore(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        try
        {
            modConfig.AddMethodHookBefore(target, action);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Hook before failed: " + target + " -> " + ex.Message);
        }
    }

    private static void RegisterAfter(ModConfig modConfig, string target, Action<ModHookContext> action)
    {
        try
        {
            modConfig.AddMethodHookAfter(target, action);
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("Hook after failed: " + target + " -> " + ex.Message);
        }
    }
}

public sealed class AuraToolsSkillCgProvider
{
    private static readonly Dictionary<string, string> OwnerRoleIds = new(StringComparer.OrdinalIgnoreCase);

    public string ProviderId => AuraToolsIds.ModId + ".SkillCG.Provider";

    public string OwnerModId => AuraToolsIds.ModId;

    public int Priority => 0;

    public static void RememberOwnerRole(string ownerInstanceId, string roleId)
    {
        var normalized = RoleCatalog.NormalizeRoleId(roleId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ownerInstanceId))
        {
            OwnerRoleIds[ownerInstanceId] = normalized;
        }
    }

    public static void ClearOwnerRoles()
    {
        OwnerRoleIds.Clear();
    }

    public IEnumerable<SkillCgRequest> BuildRequests(object context)
    {
        if (context is not SkillCgTriggerContext trigger)
        {
            yield break;
        }

        if (!AuraToolsConfigService.Root.SkillCg.Enabled || !AuraToolsConfigService.SkillCg.Enabled)
        {
            yield break;
        }

        var roleId = ResolveRoleId(trigger);
        foreach (var role in MatchingRoles(roleId))
        {
            foreach (var rule in role.Rules)
            {
                if (!RuleMatches(rule, trigger))
                {
                    continue;
                }

                var imagePath = AuraToolsConfigService.ResolveConfiguredPath(rule.Image);
                if (!File.Exists(imagePath))
                {
                    AuraToolsLog.Warn("[SkillCG] image missing: " + rule.Image);
                    continue;
                }

                var requestCardId = trigger.CardId;
                yield return new SkillCgRequest
                {
                    ProviderId = string.IsNullOrWhiteSpace(rule.ProviderId)
                        ? AuraToolsIds.ModId + ".SkillCG." + role.RoleId + "." + requestCardId
                        : rule.ProviderId,
                    OwnerModId = AuraToolsIds.ModId,
                    CardId = requestCardId,
                    OwnerInstanceId = trigger.OwnerInstanceId,
                    ImagePath = imagePath,
                    ImageResource = rule.Image,
                    Priority = rule.Priority,
                    FadeIn = rule.FadeIn,
                    Hold = rule.Hold,
                    FadeOut = rule.FadeOut,
                    CreatedAt = Time.unscaledTime,
                    ActionSequence = trigger.ActionSequence,
                    DisableSync = !AuraToolsConfigService.SkillCg.SyncRemote
                };
            }
        }
    }

    private static IEnumerable<SkillCgRoleSettings> MatchingRoles(string roleId)
    {
        var normalizedRoleId = RoleCatalog.NormalizeRoleId(roleId);
        foreach (var pair in AuraToolsConfigService.SkillCg.Roles)
        {
            var role = pair.Value;
            if (role == null || !role.Enabled)
            {
                continue;
            }

            if (string.Equals(role.RoleId, "*", StringComparison.Ordinal)
                || string.Equals(pair.Key, "*", StringComparison.Ordinal)
                || string.Equals(RoleCatalog.NormalizeRoleId(role.RoleId), normalizedRoleId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(RoleCatalog.NormalizeRoleId(pair.Key), normalizedRoleId, StringComparison.OrdinalIgnoreCase))
            {
                yield return role;
            }
        }
    }

    private static string ResolveRoleId(SkillCgTriggerContext trigger)
    {
        if (!string.IsNullOrWhiteSpace(trigger.OwnerInstanceId)
            && OwnerRoleIds.TryGetValue(trigger.OwnerInstanceId, out var roleId)
            && !string.IsNullOrWhiteSpace(roleId))
        {
            return RoleCatalog.NormalizeRoleId(roleId);
        }

        return RoleCatalog.NormalizeRoleId(AuraToolsSkillCgRuntime.ReadCurrentCareerId());
    }

    private static bool RuleMatches(SkillCgRuleSettings rule, SkillCgTriggerContext trigger)
    {
        if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Image))
        {
            return false;
        }

        return Matches(rule.CardId, trigger.CardId)
               && Matches(rule.Action, trigger.Action);
    }

    private static bool Matches(string pattern, string value)
    {
        return string.Equals(pattern, "*", StringComparison.Ordinal)
               || string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
    }

}

public static class AuraToolsSkillCgEditor
{
    private static Transform? roleContent;
    private static Text? hintText;

    public static void Show(Transform parent)
    {
        var window = Settings.AuraToolsUi.CreateOverlay("AuraTools.SkillCgEditor", parent, "技能CG配置", RefreshAndSave);
        var toolbar = Settings.AuraToolsUi.CreateLayout("Toolbar", window.transform);
        Settings.AuraToolsUi.SetFixedHeight(toolbar, Settings.AuraToolsUi.ToolbarHeight);
        var toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 10f;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;
        toolbarLayout.childForceExpandHeight = false;
        hintText = Settings.AuraToolsUi.AddText(toolbar.transform, "提示：图片会复制到 ModsData/AuraToolsExp/Resources/SkillCG/Roles/{角色ID}/ 下。", 14, TextAnchor.MiddleLeft, Settings.AuraToolsUi.MutedText, 34f, 1f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "扫描角色", () => RefreshRoles(true), 92f);
        Settings.AuraToolsUi.AddButton(toolbar.transform, "保存", RefreshAndSave, 78f);

        roleContent = Settings.AuraToolsUi.CreateScroll(window.transform, "SkillCgRoles");
        RefreshRoles(false);
    }

    private static void RefreshRoles(bool forceScan)
    {
        EnsureRoleEntries(forceScan);
        RefreshRows();
    }

    private static void EnsureRoleEntries(bool forceScan)
    {
        foreach (var role in RoleCatalog.GetRoles(forceScan))
        {
            if (AuraToolsConfigService.SkillCg.Roles.ContainsKey(role.Id))
            {
                if (string.IsNullOrWhiteSpace(AuraToolsConfigService.SkillCg.Roles[role.Id].DisplayName))
                {
                    AuraToolsConfigService.SkillCg.Roles[role.Id].DisplayName = role.DisplayName;
                }

                continue;
            }

            AuraToolsConfigService.SkillCg.Roles[role.Id] = new SkillCgRoleSettings
            {
                Enabled = false,
                RoleId = role.Id,
                DisplayName = role.DisplayName
            };
        }
    }

    private static void RefreshRows()
    {
        if (roleContent == null)
        {
            return;
        }

        Settings.AuraToolsUi.ClearChildren(roleContent);
        foreach (var pair in AuraToolsConfigService.SkillCg.Roles.OrderBy(pair => pair.Value.DisplayName).ThenBy(pair => pair.Key))
        {
            CreateRoleRow(pair.Key, pair.Value);
        }
    }

    private static void CreateRoleRow(string key, SkillCgRoleSettings role)
    {
        var box = Settings.AuraToolsUi.CreateLayout("Role-" + key, roleContent!);
        Settings.AuraToolsUi.AddPanelImage(box, Settings.AuraToolsUi.Panel);
        var boxElement = Settings.AuraToolsUi.EnsureLayoutElement(box);
        boxElement.minHeight = 112f;
        boxElement.flexibleHeight = 0f;
        var layout = box.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 8, 8);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = Settings.AuraToolsUi.CreateLayout("Header", box.transform);
        Settings.AuraToolsUi.SetFixedHeight(header, Settings.AuraToolsUi.ModuleHeaderHeight);
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;
        Settings.AuraToolsUi.AddToggle(header.transform, role.Enabled, value => role.Enabled = value);
        Settings.AuraToolsUi.AddText(header.transform, RoleDisplayName(role), Settings.AuraToolsUi.ModuleTitleFontSize, TextAnchor.MiddleLeft, Settings.AuraToolsUi.Accent, Settings.AuraToolsUi.TextMinHeight, 1f);
        Settings.AuraToolsUi.AddButton(header.transform, "打开目录", () => FileResourceUtil.OpenDirectory(FileResourceUtil.RoleSkillCgDirectory(role.RoleId)), 92f, 30f);
        Settings.AuraToolsUi.AddButton(header.transform, "添加规则", () =>
        {
            var dir = FileResourceUtil.RoleSkillCgDirectory(role.RoleId);
            var relative = AuraToolsConfigService.ToDataRelativePath(Path.Combine(dir, "skill_cg_" + (role.Rules.Count + 1) + ".png"));
            var defaultSkill = DefaultActiveSkillForNewRule(role);
            role.Rules.Add(new SkillCgRuleSettings
            {
                Enabled = true,
                CardId = defaultSkill,
                Action = "*",
                Image = relative,
                ProviderId = AuraToolsIds.ModId + ".SkillCG." + FileResourceUtil.SafeFolderName(role.RoleId) + "." + (role.Rules.Count + 1)
            });
            RefreshRows();
        }, 92f, 30f);

        foreach (var rule in role.Rules)
        {
            CreateRuleBlock(box.transform, role, rule);
        }
    }

    private static string RoleDisplayName(SkillCgRoleSettings role)
    {
        var displayName = string.IsNullOrWhiteSpace(role.DisplayName)
            ? RoleCatalog.GetDisplayName(role.RoleId)
            : role.DisplayName.Trim();
        return string.IsNullOrWhiteSpace(displayName) ? role.RoleId : displayName;
    }

    private static void CreateRuleBlock(Transform parent, SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        rule.Action = "*";
        var block = Settings.AuraToolsUi.CreateLayout("RuleBlock-" + rule.ProviderId, parent);
        Settings.AuraToolsUi.SetFixedHeight(block, Settings.AuraToolsUi.RuleBlockHeight);
        Settings.AuraToolsUi.AddImage(block, Settings.AuraToolsUi.Row);
        var blockLayout = block.AddComponent<VerticalLayoutGroup>();
        blockLayout.padding = new RectOffset(8, 8, 5, 5);
        blockLayout.spacing = 6f;
        blockLayout.childControlWidth = true;
        blockLayout.childControlHeight = true;
        blockLayout.childForceExpandWidth = true;
        blockLayout.childForceExpandHeight = false;

        var top = Settings.AuraToolsUi.CreateLayout("RuleTop", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(top, Settings.AuraToolsUi.ButtonHeight);
        var topLayout = top.AddComponent<HorizontalLayoutGroup>();
        topLayout.spacing = 8f;
        topLayout.childControlWidth = true;
        topLayout.childControlHeight = true;
        topLayout.childForceExpandWidth = false;
        topLayout.childForceExpandHeight = false;

        Settings.AuraToolsUi.AddToggle(top.transform, rule.Enabled, value => rule.Enabled = value);
        Settings.AuraToolsUi.AddText(top.transform, "\u6280\u80fd", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 42f);
        var skillOptions = BuildSkillOptions(role, rule);
        Settings.AuraToolsUi.AddSelectButton(top.transform, skillOptions.Select(option => option.Label).ToList(), SelectedOptionIndex(skillOptions, rule.CardId), index =>
        {
            if (index >= 0 && index < skillOptions.Count)
            {
                rule.CardId = skillOptions[index].Id;
                rule.Action = "*";
            }
        }, 300f);
        Settings.AuraToolsUi.AddText(top.transform, "\u4f18\u5148", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 52f);
        Settings.AuraToolsUi.AddInput(top.transform, rule.Priority.ToString(), value =>
        {
            if (int.TryParse(value, out var priority))
            {
                rule.Priority = priority;
            }
        }, 80f);
        Settings.AuraToolsUi.AddButton(top.transform, "\u5220\u9664", () =>
        {
            role.Rules.Remove(rule);
            RefreshRows();
        });

        var bottom = Settings.AuraToolsUi.CreateLayout("RuleBottom", block.transform);
        Settings.AuraToolsUi.SetFixedHeight(bottom, Settings.AuraToolsUi.ButtonHeight);
        var bottomLayout = bottom.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.spacing = 8f;
        bottomLayout.childControlWidth = true;
        bottomLayout.childControlHeight = true;
        bottomLayout.childForceExpandWidth = false;
        bottomLayout.childForceExpandHeight = false;

        Settings.AuraToolsUi.AddText(bottom.transform, "\u56fe\u7247", Settings.AuraToolsUi.HintFontSize, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, Settings.AuraToolsUi.TextMinHeight, 0f, 42f);
        Settings.AuraToolsUi.AddInput(bottom.transform, rule.Image, value => ApplyRuleImagePath(role, rule, value, false, false), 560f);
        Settings.AuraToolsUi.AddButton(bottom.transform, "选择图片", () => PickRuleImage(role, rule), 92f);
        Settings.AuraToolsUi.AddButton(bottom.transform, "\u6253\u5f00\u76ee\u5f55", () => FileResourceUtil.OpenDirectory(FileResourceUtil.RoleSkillCgDirectory(role.RoleId)), 92f);
    }

    private static void CreateRuleRow(Transform parent, SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var row = Settings.AuraToolsUi.CreateLayout("Rule-" + rule.ProviderId, parent);
        var rowElement = row.AddComponent<LayoutElement>();
        rowElement.minHeight = Settings.AuraToolsUi.RoleRowHeight;
        rowElement.preferredHeight = Settings.AuraToolsUi.RoleRowHeight;
        Settings.AuraToolsUi.AddImage(row, Settings.AuraToolsUi.Row);
        var layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        Settings.AuraToolsUi.AddToggle(row.transform, rule.Enabled, value => rule.Enabled = value);
        Settings.AuraToolsUi.AddText(row.transform, "卡牌", 12, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, 28f, 0f, 34f);
        Settings.AuraToolsUi.AddInput(row.transform, rule.CardId, value => rule.CardId = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim(), 160f);
        Settings.AuraToolsUi.AddText(row.transform, "动作", 12, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, 28f, 0f, 34f);
        Settings.AuraToolsUi.AddInput(row.transform, rule.Action, value => rule.Action = string.IsNullOrWhiteSpace(value) ? "*" : value.Trim(), 100f);
        Settings.AuraToolsUi.AddText(row.transform, "图片", 12, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, 28f, 0f, 34f);
        Settings.AuraToolsUi.AddInput(row.transform, rule.Image, value => ApplyRuleImagePath(role, rule, value, false, false), 320f);
        Settings.AuraToolsUi.AddText(row.transform, "优先", 12, TextAnchor.MiddleCenter, Settings.AuraToolsUi.MutedText, 28f, 0f, 42f);
        Settings.AuraToolsUi.AddInput(row.transform, rule.Priority.ToString(), value =>
        {
            if (int.TryParse(value, out var priority))
            {
                rule.Priority = priority;
            }
        }, 80f);
        Settings.AuraToolsUi.AddButton(row.transform, "删除", () =>
        {
            role.Rules.Remove(rule);
            RefreshRows();
        }, 60f, 28f);
    }

    private sealed class SkillDropdownOption
    {
        public string Id { get; set; } = "";

        public string Label { get; set; } = "";
    }

    private static string DefaultActiveSkillForNewRule(SkillCgRoleSettings role)
    {
        var used = new HashSet<string>(
            role.Rules
                .Select(rule => rule.CardId),
            StringComparer.OrdinalIgnoreCase);
        foreach (var skill in RoleCatalog.GetRoleSkills(role.RoleId))
        {
            if (!used.Contains(skill.Id))
            {
                return skill.Id;
            }
        }

        return RoleCatalog.GetRoleSkills(role.RoleId).FirstOrDefault()?.Id ?? "*";
    }

    private static List<SkillDropdownOption> BuildSkillOptions(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var options = new List<SkillDropdownOption>
        {
            new()
            {
                Id = "*",
                Label = "任意技能"
            }
        };

        foreach (var skill in RoleCatalog.GetRoleSkills(role.RoleId))
        {
            if (options.Any(option => string.Equals(option.Id, skill.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            options.Add(new SkillDropdownOption
            {
                Id = skill.Id,
                Label = SkillLabel(skill.Id, skill.DisplayName)
            });
        }

        var current = rule.CardId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(current)
            && !options.Any(option => string.Equals(option.Id, current, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new SkillDropdownOption
            {
                Id = current,
                Label = "自定义技能"
            });
        }

        return options;
    }

    private static int SelectedOptionIndex(IReadOnlyList<SkillDropdownOption> options, string value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Id, value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private static string SkillLabel(string id, string displayName)
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        return name;
    }

    private static void PickRuleImage(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var directory = FileResourceUtil.RoleSkillCgDirectory(role.RoleId);
        SetHint("正在打开图片选择器...");
        OptionalFileDialog.PickImageFileAsync(directory, result =>
        {
            if (result.Selected)
            {
                ApplyRuleImagePath(role, rule, result.Path, true, true);
                return;
            }

            if (result.Status == OptionalFileDialogStatus.Cancelled)
            {
                SetHint("已取消选择图片。");
                return;
            }

            AuraToolsLog.Warn("[SkillCG] image picker unavailable: " + result.Message);
            SetHint("无法打开系统文件选择器；请使用路径输入框修改，或先把图片放进角色目录。");
        });
    }

    private static void ApplyRuleImagePath(SkillCgRoleSettings role, SkillCgRuleSettings rule, string path, bool refresh, bool save)
    {
        var trimmed = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            rule.Image = "";
            SetHint("已清空图片路径。");
        }
        else
        {
            var imported = FileResourceUtil.ImportImagePath(
                trimmed,
                FileResourceUtil.RoleSkillCgDirectory(role.RoleId),
                RuleImageBaseName(role, rule),
                out var message);
            rule.Image = string.IsNullOrWhiteSpace(imported) ? trimmed : imported;
            if (string.IsNullOrWhiteSpace(imported))
            {
                AuraToolsLog.Warn("[SkillCG] image path kept as typed: " + message);
                SetHint(message + " 已保留输入路径。");
            }
            else
            {
                role.Enabled = true;
                rule.Enabled = true;
                SetHint(message + " " + rule.Image);
            }
        }

        if (save)
        {
            AuraToolsConfigService.SkillCg.Normalize();
            AuraToolsConfigService.SaveSkillCg();
        }

        if (refresh)
        {
            RefreshRows();
        }
    }

    private static string RuleImageBaseName(SkillCgRoleSettings role, SkillCgRuleSettings rule)
    {
        var index = role.Rules.IndexOf(rule);
        return index >= 0 ? "skill_cg_" + (index + 1) : "skill_cg";
    }

    private static void SetHint(string message)
    {
        if (hintText != null)
        {
            hintText.text = message;
        }
    }

    private static void RefreshAndSave()
    {
        foreach (var role in AuraToolsConfigService.SkillCg.Roles.Values)
        {
            foreach (var rule in role.Rules)
            {
                rule.Action = "*";
            }
        }

        AuraToolsConfigService.SkillCg.Normalize();
        AuraToolsConfigService.SaveSkillCg();
        SetHint("已保存技能CG配置。");
    }
}
