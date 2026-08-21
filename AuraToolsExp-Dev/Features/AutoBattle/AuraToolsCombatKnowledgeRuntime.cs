using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraShared.Core;
using AuraToolsExp.Dll.Infrastructure;
using Witch.Core;

namespace AuraToolsExp.Dll.Features.AutoBattle;

internal static class AuraToolsCombatKnowledgeRuntime
{
    private static readonly List<IDisposable> Registrations = new();
    private static bool initialized;
    private static bool packageLoadQueued;

    public static void Initialize()
    {
        if (initialized)
        {
            return;
        }
        initialized = true;

        AuraToolsAuthoritativeRoleSemantics.Initialize();
        Register(BuildVerifiedBasePackage(), "verified base-game rules");
        Registrations.Add(CombatSimulationRegistry.RegisterProvider(
            "witch.base-game",
            "verified-core-rules",
            new VerifiedBaseSimulationProvider(),
            100));
        BeginBundledPackageLoad();
    }

    public static CombatKnowledgeCoverageReport EvaluateCoverage(CombatStateObservation state)
    {
        return CombatKnowledgeRegistry.EvaluateCoverage(state);
    }

    public static string DescribeLoadedPackages()
    {
        var packages = CombatKnowledgeRegistry.SnapshotPackages();
        var actions = 0;
        var statuses = 0;
        var enemies = 0;
        var encounters = 0;
        var authoritative = 0;
        var nonAuthoritative = 0;
        for (var i = 0; i < packages.Count; i++)
        {
            actions += packages[i].Actions.Count;
            statuses += packages[i].Statuses.Count;
            enemies += packages[i].Enemies.Count;
            encounters += packages[i].Encounters.Count;
            authoritative += packages[i].Actions.FindAll(item =>
                item.Fidelity == CombatKnowledgeFidelity.Authoritative).Count;
            authoritative += packages[i].Statuses.FindAll(item =>
                item.Fidelity == CombatKnowledgeFidelity.Authoritative).Count;
            authoritative += packages[i].Enemies.FindAll(item =>
                item.Fidelity == CombatKnowledgeFidelity.Authoritative).Count;
            nonAuthoritative += packages[i].Actions.FindAll(item =>
                item.Fidelity != CombatKnowledgeFidelity.Authoritative).Count;
            nonAuthoritative += packages[i].Statuses.FindAll(item =>
                item.Fidelity != CombatKnowledgeFidelity.Authoritative).Count;
            nonAuthoritative += packages[i].Enemies.FindAll(item =>
                item.Fidelity != CombatKnowledgeFidelity.Authoritative).Count;
        }
        return "知识包 " + packages.Count
               + " · 动作 " + actions
               + " · Buff " + statuses
               + " · 敌人 " + enemies
               + " · 遭遇 " + encounters
               + " · 权威 " + authoritative
               + " · 待验证 " + nonAuthoritative
               + "（待验证内容可用于探索模拟，不会获得正式验证标记）";
    }

    public static bool TryExportBaseGameTables(
        out string exportedPath,
        out string message)
    {
        exportedPath = "";
        try
        {
            if (!TryCaptureBaseGameTables(out var export, out message))
            {
                return false;
            }

            var directory = BaseGameTableExportDirectory;
            Directory.CreateDirectory(directory);
            exportedPath = Path.Combine(
                directory,
                "witch-tables-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json");
            using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
            {
                storage.WriteTextAtomic(
                    exportedPath,
                    AuraSharedJson.Serialize(export),
                    createBackup: false);
            }
            message = "已导出 " + export.Tables.Sum(item => item.Value.Count)
                      + " 行游戏数据";
            AuraToolsLog.Info(
                "[AutoBattle][Knowledge] " + message + "：" + exportedPath);
            return true;
        }
        catch (Exception ex)
        {
            exportedPath = "";
            message = "导出失败，请查看 AuraToolsExp 日志";
            AuraToolsLog.Warn("[AutoBattle][Knowledge] 游戏数据表导出失败：" + ex);
            return false;
        }
    }

    public static bool TryExportAndInstallRuntimeKnowledgePackage(
        out string installedPath,
        out string message)
    {
        installedPath = "";
        try
        {
            if (!TryCaptureBaseGameTables(out var export, out message))
            {
                return false;
            }

            var package = BuildRuntimeTableKnowledgePackage(export);
            var directory = KnowledgeDirectory;
            Directory.CreateDirectory(directory);
            installedPath = RuntimeKnowledgePackagePath;
            using (var storage = new AuraSharedStorageCoordinator(AuraSharedPaths.RootDirectory))
            {
                storage.WriteTextAtomic(
                    installedPath,
                    AuraSharedJson.Serialize(package),
                    createBackup: true);
            }

            Register(package, installedPath);
            message = "已导出并安装当前版本知识包：动作 "
                      + package.Actions.Count
                      + "、Buff " + package.Statuses.Count
                      + "、敌人 " + package.Enemies.Count
                      + "、遭遇 " + package.Encounters.Count;
            AuraToolsLog.Info("[AutoBattle][Knowledge] " + message);
            return true;
        }
        catch (Exception ex)
        {
            installedPath = "";
            message = "知识包导出/安装失败，请查看 AuraToolsExp 日志";
            AuraToolsLog.Warn("[AutoBattle][Knowledge] runtime package export failed: " + ex);
            return false;
        }
    }

    public static void RequestPackageReload()
    {
        BeginBundledPackageLoad();
    }

    public static void OpenKnowledgeDirectory()
    {
        Directory.CreateDirectory(KnowledgeDirectory);
        FileResourceUtil.OpenDirectory(KnowledgeDirectory);
    }

    public static void OpenBaseGameTableExportDirectory()
    {
        Directory.CreateDirectory(BaseGameTableExportDirectory);
        FileResourceUtil.OpenDirectory(BaseGameTableExportDirectory);
    }

    private static string KnowledgeDirectory => Path.Combine(
        AuraToolsPaths.ConfigDirectory,
        "combat-knowledge");

    private static string BaseGameTableExportDirectory => Path.Combine(
        KnowledgeDirectory,
        "table-exports");

    private static string RuntimeKnowledgePackagePath => Path.Combine(
        KnowledgeDirectory,
        "runtime-tables.json");

    public static bool HasAuthoritativeCoverage(
        CombatStateObservation state,
        out string reason)
    {
        var report = EvaluateCoverage(state);
        if (report.IsAuthoritative)
        {
            reason = report.Summary;
            return true;
        }

        reason = report.Summary;
        if (report.UnknownDefinitions.Count > 0)
        {
            reason += "; unknown=" + string.Join(",", report.UnknownDefinitions);
        }
        if (report.NonAuthoritativeDefinitions.Count > 0)
        {
            reason += "; non-authoritative="
                      + string.Join(",", report.NonAuthoritativeDefinitions);
        }
        return false;
    }

    public static bool HasPlayerEquivalentReadiness(
        CombatStateObservation state,
        out string reason)
    {
        if (state == null
            || state.InformationBoundaryVersion < 2
            || string.IsNullOrWhiteSpace(state.ObservationId))
        {
            reason = "玩家观察协议不是 v2";
            return false;
        }
        if (state.Actions.Any(action =>
                string.IsNullOrWhiteSpace(action.ActionToken)
                || !string.Equals(
                    action.ObservationId,
                    state.ObservationId,
                    StringComparison.Ordinal)))
        {
            reason = "动作令牌未绑定当前公开观察";
            return false;
        }

        var report = EvaluateCoverage(state);
        var unsupported = state.Actions.Count(action =>
            action.Kind != CombatActionKind.EndTurn
            && action.SemanticFidelity == CombatKnowledgeFidelity.Unsupported);
        var unresolvedInteractions = state.Actions
            .Where(action =>
                action.Kind != CombatActionKind.EndTurn
                && action.Legal
                && action.Semantics?.OpensInteraction == true
                && (action.Semantics.Interaction == null
                    || !action.Semantics.Interaction.EffectsComplete))
            .Select(action => action.SourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        reason = "player-equivalent-v2; "
                 + report.Summary
                 + "; unsupported-current-actions="
                 + unsupported;
        if (unresolvedInteractions.Count > 0)
        {
            reason += "; unresolved-interactions="
                      + string.Join(",", unresolvedInteractions);
            return false;
        }
        return true;
    }

    private static void BeginBundledPackageLoad()
    {
        if (packageLoadQueued)
        {
            return;
        }
        packageLoadQueued = true;
        var paths = new[]
        {
            Path.Combine(
                KnowledgeDirectory,
                "base-game.json"),
            RuntimeKnowledgePackagePath,
            Path.Combine(
                AuraToolsPaths.BundledConfigDirectory,
                "combat-knowledge.base-game.json")
        };
        var accepted = AuraSharedBackgroundWorkScheduler.Queue(
            new AuraSharedBackgroundWorkRequest<List<KnowledgePackageLoad>>
            {
                OwnerId = AuraToolsIds.ModId + ".AutoBattle",
                Key = "AutoBattle.CombatKnowledge.Load",
                Source = "AutoBattle.CombatKnowledge.Load",
                Kind = AuraSharedBackgroundWorkKind.Io,
                Work = _ => ReadPackages(paths),
                ApplyOnMainThread = loaded =>
                {
                    packageLoadQueued = false;
                    foreach (var item in loaded)
                    {
                        if (item.Package != null)
                        {
                            Register(item.Package, item.Path);
                        }
                        else if (!string.IsNullOrWhiteSpace(item.Error))
                        {
                            AuraToolsLog.Warn(
                                "[AutoBattle][Knowledge] 知识包加载失败："
                                + item.Path
                                + "；"
                                + item.Error);
                        }
                    }
                },
                OnFailedOnMainThread = ex =>
                {
                    packageLoadQueued = false;
                    AuraToolsLog.Warn(
                        "[AutoBattle][Knowledge] 后台知识包加载失败：" + ex);
                }
            });
        if (!accepted)
        {
            packageLoadQueued = false;
            AuraToolsLog.Warn("[AutoBattle][Knowledge] 后台知识包任务未能提交");
        }
    }

    private static List<KnowledgePackageLoad> ReadPackages(
        IEnumerable<string> paths)
    {
        var result = new List<KnowledgePackageLoad>();
        foreach (var path in paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                var package = AuraSharedJson.Deserialize<CombatKnowledgePackage>(
                    File.ReadAllText(path));
                result.Add(new KnowledgePackageLoad
                {
                    Path = path,
                    Package = package,
                    Error = package == null ? "知识包为空" : ""
                });
            }
            catch (Exception ex)
            {
                result.Add(new KnowledgePackageLoad
                {
                    Path = path,
                    Error = ex.Message
                });
            }
        }
        return result;
    }

    private static void Register(CombatKnowledgePackage package, string source)
    {
        var currentBuild = "";
        try
        {
            currentBuild = GameConfigManager.Version ?? "";
        }
        catch
        {
        }
        if (!string.IsNullOrWhiteSpace(currentBuild)
            && !string.Equals(
                NormalizeGameBuild(currentBuild),
                NormalizeGameBuild(package.GameBuild),
                StringComparison.OrdinalIgnoreCase))
        {
            AuraToolsLog.Info(
                "[AutoBattle][Knowledge] 接受跨版本知识包 " + source
                + "：package=" + package.GameBuild
                + " runtime=" + currentBuild
                + "；兼容性由知识协议与定义校验决定");
        }

        var registration = CombatKnowledgeRegistry.RegisterPackage(package, out var errors);
        if (errors.Count > 0)
        {
            registration.Dispose();
            AuraToolsLog.Warn(
                "[AutoBattle][Knowledge] 拒绝知识包 " + source + "："
                + string.Join("；", errors));
            return;
        }
        Registrations.Add(registration);
        AuraToolsLog.Info(
            "[AutoBattle][Knowledge] 已加载 " + package.PackageId
            + " build=" + package.GameBuild
            + " actions=" + package.Actions.Count
            + " statuses=" + package.Statuses.Count
            + " enemies=" + package.Enemies.Count);
    }

    private sealed class KnowledgePackageLoad
    {
        public string Path { get; set; } = "";

        public CombatKnowledgePackage? Package { get; set; }

        public string Error { get; set; } = "";
    }

    internal static string NormalizeGameBuild(string value)
    {
        var normalized = (value ?? "").Trim();
        if (normalized.Length > 1
            && (normalized[0] == 'v' || normalized[0] == 'V')
            && char.IsDigit(normalized[1]))
        {
            normalized = normalized.Substring(1);
        }
        return normalized;
    }

    private static bool TryCaptureBaseGameTables(
        out BaseGameTableExport export,
        out string message)
    {
        export = new BaseGameTableExport();
        var manager = GameConfigManager.Instance;
        if (manager == null)
        {
            message = "游戏数据表尚未初始化，请进入主界面后重试";
            AuraToolsLog.Warn("[AutoBattle][Knowledge] " + message);
            return false;
        }

        export.GameBuild = GameConfigManager.Version;
        export.ExportedAtUtc = DateTime.UtcNow;
        foreach (var type in RelevantTables)
        {
            var table = manager.GetTable(type);
            export.Tables[type.ToString()] = table?.Getlines()
                .Select(row => new Dictionary<string, string>(
                    row,
                    StringComparer.OrdinalIgnoreCase))
                .ToList()
                ?? new List<Dictionary<string, string>>();
        }

        message = "已读取 " + export.Tables.Sum(item => item.Value.Count) + " 行游戏数据";
        return true;
    }

    private static CombatKnowledgePackage BuildRuntimeTableKnowledgePackage(
        BaseGameTableExport export)
    {
        var canonical = AuraSharedJson.SerializeCompact(export);
        var provenance = "runtime table export; build=" + export.GameBuild;
        var package = new CombatKnowledgePackage
        {
            OwnerId = "witch.base-game.local",
            PackageId = "runtime-table-inventory",
            GameBuild = string.IsNullOrWhiteSpace(export.GameBuild)
                ? "unknown"
                : export.GameBuild,
            SourceHash = Sha256(canonical),
            GeneratedAtUtc = export.ExportedAtUtc
        };

        var actions = new Dictionary<string, CombatKnowledgeActionDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var tableName in new[] { "Card", "EnemyCard", "PartnerCard" })
        {
            foreach (var row in Rows(export, tableName))
            {
                var id = Value(row, "Id", "CardId", "ID");
                if (string.IsNullOrWhiteSpace(id) || actions.ContainsKey(id)) continue;
                actions[id] = new CombatKnowledgeActionDefinition
                {
                    SourceId = id,
                    DisplayName = DisplayName(row, id),
                    Fidelity = CombatKnowledgeFidelity.Unsupported,
                    Confidence = 1d,
                    BaseCost = Integer(row, "Cost", "Energy", "Mana"),
                    TableFields = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase),
                    Provenance = provenance + "; table=" + tableName
                };
            }
        }
        package.Actions = actions.Values.OrderBy(item => item.SourceId, StringComparer.OrdinalIgnoreCase).ToList();

        package.Statuses = Rows(export, "Buff")
            .Select(row => new { Row = row, Id = Value(row, "Id", "BuffId", "ID") })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CombatKnowledgeStatusDefinition
            {
                StatusId = group.Key,
                DisplayName = DisplayName(group.First().Row, group.Key),
                Fidelity = CombatKnowledgeFidelity.Unsupported,
                TableFields = new Dictionary<string, string>(group.First().Row, StringComparer.OrdinalIgnoreCase),
                Provenance = provenance + "; table=Buff"
            })
            .OrderBy(item => item.StatusId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        package.Enemies = Rows(export, "Enemy")
            .Select(row => new { Row = row, Id = Value(row, "Id", "EnemyId", "ID") })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CombatKnowledgeEnemyDefinition
            {
                EnemyId = group.Key,
                DisplayName = DisplayName(group.First().Row, group.Key),
                Fidelity = CombatKnowledgeFidelity.Unsupported,
                MaxHp = Integer(group.First().Row, "MaxHp", "Hp", "HP"),
                TableFields = new Dictionary<string, string>(group.First().Row, StringComparer.OrdinalIgnoreCase),
                Provenance = provenance + "; table=Enemy"
            })
            .OrderBy(item => item.EnemyId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        package.Encounters = Rows(export, "Level")
            .Select(row => new { Row = row, Id = Value(row, "Id", "LevelId", "ID") })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CombatKnowledgeEncounterDefinition
            {
                EncounterId = group.Key,
                Fidelity = CombatKnowledgeFidelity.Unsupported,
                EnemyIds = EnemyIds(group.First().Row),
                Provenance = provenance + "; table=Level"
            })
            .OrderBy(item => item.EncounterId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        package.Inventory = new CombatKnowledgeInventory
        {
            DiscoveredActions = package.Actions.Count,
            DiscoveredStatuses = package.Statuses.Count,
            DiscoveredEnemies = package.Enemies.Count,
            DiscoveredEncounters = package.Encounters.Count,
            UnsupportedScripts = package.Actions.Count + package.Statuses.Count + package.Enemies.Count
        };
        return package;
    }

    private static IEnumerable<Dictionary<string, string>> Rows(
        BaseGameTableExport export,
        string tableName)
    {
        return export.Tables.TryGetValue(tableName, out var rows)
            ? rows
            : Enumerable.Empty<Dictionary<string, string>>();
    }

    private static string Value(
        IReadOnlyDictionary<string, string> row,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }
        return "";
    }

    private static string DisplayName(
        IReadOnlyDictionary<string, string> row,
        string fallback)
    {
        return Value(row, "Name", "DisplayName", "Title", "Msg", "CN") is { Length: > 0 } value
            ? value
            : fallback;
    }

    private static int Integer(
        IReadOnlyDictionary<string, string> row,
        params string[] keys)
    {
        return int.TryParse(Value(row, keys), out var value) ? value : 0;
    }

    private static List<string> EnemyIds(IReadOnlyDictionary<string, string> row)
    {
        return row
            .Where(item => item.Key.IndexOf("enemy", StringComparison.OrdinalIgnoreCase) >= 0)
            .SelectMany(item => (item.Value ?? "").Split('|', ',', ';', '，', '；'))
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Sha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))
            .Select(item => item.ToString("x2")));
    }

    private static CombatKnowledgePackage BuildVerifiedBasePackage()
    {
        const string provenance =
            "Witch 1.0.24605918 decompile: AllScripts.cs + current Managed contract";
        return new CombatKnowledgePackage
        {
            OwnerId = "witch.base-game",
            PackageId = "verified-core-rules",
            GameBuild = "1.0.24605918",
            SourceHash = "witch-1.0.24605918-verified-core-v3",
            GeneratedAtUtc = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc),
            Inventory = new CombatKnowledgeInventory
            {
                DiscoveredActions = 932,
                DiscoveredStatuses = 137,
                DiscoveredEnemies = 56,
                AuthoritativeActions = 1,
                AuthoritativeStatuses = 10
            },
            Actions =
            {
                new CombatKnowledgeActionDefinition
                {
                    SourceId = "elementscard_1",
                    DisplayName = "海洋之梦",
                    Fidelity = CombatKnowledgeFidelity.Authoritative,
                    Confidence = 1d,
                    Roles = { "free-setup", "draw", "damage-scaling" },
                    Semantics = new CombatActionSemantics
                    {
                        Draw = 1d,
                        Buff = 2d,
                        Scaling = 4d,
                        PersistentValue = 4d,
                        DamageMultiplierGain = 0.04d,
                        StateChanges =
                        {
                            ["status:buff_elements"] = 2d,
                            ["PercentDamage"] = 0.04d
                        }
                    },
                    Provenance = provenance
                }
            },
            Statuses =
            {
                Status(
                    "buff_elements",
                    "元素",
                    provenance,
                    triggers: new[] { "ActionAfter:add buff_extraordinary = level * 2" }),
                Status(
                    "buff_extraordinary",
                    "超凡",
                    provenance,
                    modifiers: new Dictionary<string, double>
                    {
                        ["PercentDamage"] = 0.01d
                    }),
                Status(
                    "buff_vulnerability",
                    "易伤",
                    provenance,
                    modifiers: new Dictionary<string, double>
                    {
                        ["AttackedPercentDamage"] = 0.1d
                    }),
                Status(
                    "buff_impregnable",
                    "坚不可摧",
                    provenance,
                    modifiers: new Dictionary<string, double>
                    {
                        ["AttackedPercentDamage"] = -0.1d
                    }),
                Status(
                    "buff_weak",
                    "虚弱",
                    provenance,
                    modifiers: new Dictionary<string, double>
                    {
                        ["DefaultDamage"] = -1d
                    }),
                Status(
                    "buff_resilient",
                    "韧性",
                    provenance,
                    modifiers: new Dictionary<string, double>
                    {
                        ["AttackedDefaultDamage"] = -1d
                    }),
                Status(
                    "buff_fast",
                    "迅捷",
                    provenance,
                    modifiers: new Dictionary<string, double>
                    {
                        ["RoundCard"] = 1d
                    }),
                Status(
                    "buff_burn",
                    "灼烧",
                    provenance,
                    triggers: new[] { "StartRound:direct hp loss from current hp and level" }),
                Status(
                    "buff_thorns",
                    "荆棘",
                    provenance,
                    triggers: new[] { "Hurt:normal damage to event source by level" }),
                Status(
                    "buff_rebirth",
                    "重生",
                    provenance,
                    triggers: new[] { "Dead:revive when level >= 30, then consume" })
            }
        };
    }

    private static CombatKnowledgeStatusDefinition Status(
        string id,
        string name,
        string provenance,
        Dictionary<string, double>? modifiers = null,
        string[]? triggers = null)
    {
        return new CombatKnowledgeStatusDefinition
        {
            StatusId = id,
            DisplayName = name,
            Fidelity = CombatKnowledgeFidelity.Authoritative,
            DynamicModifiersPerStack = modifiers
                ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
            Triggers = triggers == null ? new List<string>() : new List<string>(triggers),
            Provenance = provenance
        };
    }

    private static readonly DataType[] RelevantTables =
    {
        DataType.Card,
        DataType.CardPack,
        DataType.Career,
        DataType.Enemy,
        DataType.EnemyCard,
        DataType.KeyWords,
        DataType.EnchTag,
        DataType.Buff,
        DataType.Level,
        DataType.Partner,
        DataType.PartnerCard,
        DataType.Relic,
        DataType.Bless,
        DataType.Hard
    };

    private sealed class BaseGameTableExport
    {
        public string GameBuild { get; set; } = "";

        public DateTime ExportedAtUtc { get; set; }

        public Dictionary<string, List<Dictionary<string, string>>> Tables { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class VerifiedBaseSimulationProvider : ICombatRulesetProvider
    {
        public void RegisterDefinitions(CombatRulesetBuilder builder)
        {
            builder.RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "witch.base-game",
                StatusId = "buff_extraordinary",
                DisplayName = "超凡",
                Fidelity = CombatRuleFidelity.Authoritative,
                DecayAtRoundEnd = false,
                DynamicModifiersPerStack = { ["PercentDamage"] = 0.01d }
            });
            builder.RegisterStatus(new CombatStatusDefinition
            {
                OwnerModId = "witch.base-game",
                StatusId = "buff_elements",
                DisplayName = "元素",
                Fidelity = CombatRuleFidelity.Authoritative,
                DecayAtRoundEnd = false,
                Triggers =
                {
                    new CombatStatusTriggerDefinition
                    {
                        TriggerId = "elements-to-extraordinary",
                        EventKind = CombatSimulationEventKind.ActionResolved,
                        Effects =
                        {
                            new CombatSimulationEffectDefinition
                            {
                                Kind = CombatSimulationEffectKind.AddStatus,
                                Target = CombatSimulationTarget.Self,
                                DefinitionId = "buff_extraordinary",
                                Amount = 2,
                                ScaleWithStatusStacks = true
                            }
                        }
                    }
                }
            });
            RegisterModifierStatus(
                builder,
                "buff_vulnerability",
                "易伤",
                "AttackedPercentDamage",
                0.1d);
            RegisterModifierStatus(
                builder,
                "buff_impregnable",
                "坚不可摧",
                "AttackedPercentDamage",
                -0.1d);
            RegisterModifierStatus(builder, "buff_weak", "虚弱", "DefaultDamage", -1d);
            RegisterModifierStatus(
                builder,
                "buff_resilient",
                "韧性",
                "AttackedDefaultDamage",
                -1d);
            RegisterModifierStatus(builder, "buff_fast", "迅捷", "RoundCard", 1d);
            builder.RegisterCard(new CombatCardDefinition
            {
                OwnerModId = "witch.base-game",
                CardId = "elementscard_1",
                DisplayName = "海洋之梦",
                Cost = 0,
                Fidelity = CombatRuleFidelity.Authoritative,
                Effects =
                {
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.Draw,
                        Target = CombatSimulationTarget.Self,
                        Amount = 1
                    },
                    new CombatSimulationEffectDefinition
                    {
                        Kind = CombatSimulationEffectKind.AddStatus,
                        Target = CombatSimulationTarget.Self,
                        DefinitionId = "buff_elements",
                        Amount = 2
                    }
                }
            });
        }

        private static void RegisterModifierStatus(
            CombatRulesetBuilder builder,
            string id,
            string name,
            string key,
            double amount)
        {
            var definition = new CombatStatusDefinition
            {
                OwnerModId = "witch.base-game",
                StatusId = id,
                DisplayName = name,
                Fidelity = CombatRuleFidelity.Authoritative,
                DecayAtRoundEnd = false
            };
            definition.DynamicModifiersPerStack[key] = amount;
            builder.RegisterStatus(definition);
        }
    }
}
