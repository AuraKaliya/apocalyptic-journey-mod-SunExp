using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace AuraCombatAi.Shared;

public static class CombatGameValidationProtocol
{
    public const int SchemaVersion = 1;
    public const string ValidationMode = "witch-game-host";

    public static string NormalizeId(string value)
    {
        return (value ?? "").Trim();
    }

    public static string BuildCompatibilityKey(
        string profile,
        string modelId,
        string modelArtifactHash,
        string gameBuild,
        string campaignId,
        string campaignVersion,
        string rulesetHash,
        string nativePackageHash)
    {
        var canonical = string.Join(
            "\n",
            new[]
            {
                "combat-game-validation-key-v2",
                NormalizeId(profile).ToLowerInvariant(),
                NormalizeId(modelId),
                NormalizeHash(modelArtifactHash),
                NormalizeId(campaignId),
                NormalizeId(campaignVersion),
                NormalizeHash(rulesetHash),
                NormalizeHash(nativePackageHash)
            });
        return Sha256(canonical);
    }

    public static string BuildReceiptHash(CombatGameValidationReport report)
    {
        if (report == null)
        {
            return "";
        }

        var caseSummary = string.Join(
            "\n",
            (report.Cases ?? new List<CombatGameValidationCaseResult>())
                .OrderBy(item => item.CaseId, StringComparer.Ordinal)
                .Select(item => string.Join(
                    "|",
                    NormalizeId(item.CaseId),
                    NormalizeId(item.LevelId),
                    item.Attempts.ToString(CultureInfo.InvariantCulture),
                    item.Wins.ToString(CultureInfo.InvariantCulture),
                    item.Losses.ToString(CultureInfo.InvariantCulture),
                    item.InvalidRuns.ToString(CultureInfo.InvariantCulture),
                    item.Decisions.ToString(CultureInfo.InvariantCulture))));
        return Sha256(
            string.Join(
                "\n",
                new[]
                {
                    report.CompatibilityKey ?? "",
                    report.Passed ? "passed" : "failed",
                    report.Completed ? "complete" : "incomplete",
                    report.StartedUtc ?? "",
                    report.CompletedUtc ?? "",
                    caseSummary
                }));
    }

    public static bool ValidateRequest(
        CombatGameValidationRequest? request,
        out string reason)
    {
        if (request == null)
        {
            reason = "验证请求为空";
            return false;
        }
        if (request.SchemaVersion != SchemaVersion)
        {
            reason = "验证协议版本不受支持";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.RequestId)
            || string.IsNullOrWhiteSpace(request.Profile)
            || string.IsNullOrWhiteSpace(request.ModelId)
            || string.IsNullOrWhiteSpace(request.ModelArtifactHash))
        {
            reason = "模型身份字段不完整";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.GameBuild)
            || string.IsNullOrWhiteSpace(request.CampaignId)
            || string.IsNullOrWhiteSpace(request.CampaignVersion)
            || string.IsNullOrWhiteSpace(request.RulesetHash)
            || string.IsNullOrWhiteSpace(request.NativePackageHash))
        {
            reason = "游戏与权威语义版本字段不完整";
            return false;
        }
        if (request.Cases == null
            || request.Cases.Count == 0
            || request.Cases.Any(item =>
                item == null
                || string.IsNullOrWhiteSpace(item.CaseId)
                || string.IsNullOrWhiteSpace(item.LevelId)
                || item.Repetitions <= 0))
        {
            reason = "验证用例为空或无效";
            return false;
        }
        if (request.MaximumActionsPerBattle <= 0
            || request.BattleTimeoutSeconds <= 0
            || request.MinimumDecisionsPerBattle <= 0)
        {
            reason = "验证资源上限无效";
            return false;
        }

        reason = "验证请求有效";
        return true;
    }

    public static bool ValidateReport(
        CombatGameValidationRequest request,
        CombatGameValidationReport? report,
        out string reason)
    {
        if (!ValidateRequest(request, out reason))
        {
            return false;
        }
        if (report == null
            || report.SchemaVersion != SchemaVersion
            || !report.Completed
            || !report.Passed)
        {
            reason = report?.FailureReason ?? "游戏主体验证尚未通过";
            return false;
        }

        var expectedKey = BuildCompatibilityKey(
            request.Profile,
            request.ModelId,
            request.ModelArtifactHash,
            request.GameBuild,
            request.CampaignId,
            request.CampaignVersion,
            request.RulesetHash,
            request.NativePackageHash);
        if (!FixedEquals(expectedKey, report.CompatibilityKey))
        {
            reason = "验证回执与当前模型或权威语义版本不匹配";
            return false;
        }
        if (report.Cases == null
            || request.Cases.Any(expected =>
            {
                var actual = report.Cases.FirstOrDefault(item =>
                    string.Equals(item.CaseId, expected.CaseId, StringComparison.Ordinal));
                return actual == null
                       || actual.Attempts < expected.Repetitions
                       || actual.InvalidRuns > request.MaximumInvalidRuns
                       || actual.Decisions
                       < request.MinimumDecisionsPerBattle * expected.Repetitions
                       || (expected.Required && actual.Wins < expected.MinimumWins);
            }))
        {
            reason = "游戏主体验证用例的动作覆盖、结果或无效运行未达门槛";
            return false;
        }
        if (!FixedEquals(BuildReceiptHash(report), report.ReceiptHash))
        {
            reason = "验证回执摘要无效";
            return false;
        }

        reason = "游戏主体回执与当前模型及权威语义一致；游戏补丁版本仅作诊断元数据";
        return true;
    }

    private static string NormalizeHash(string value)
    {
        return NormalizeId(value).ToLowerInvariant();
    }

    private static string Sha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(
            sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))
                .Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static bool FixedEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? "");
        var b = Encoding.UTF8.GetBytes(right ?? "");
        var difference = a.Length ^ b.Length;
        var length = Math.Max(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            var leftByte = a.Length == 0 ? 0 : a[i % a.Length];
            var rightByte = b.Length == 0 ? 0 : b[i % b.Length];
            difference |= leftByte ^ rightByte;
        }
        return difference == 0;
    }
}

public sealed class CombatGameValidationRequest
{
    public int SchemaVersion { get; set; } = CombatGameValidationProtocol.SchemaVersion;

    public string RequestId { get; set; } = "";

    public string Profile { get; set; } = "balanced";

    public string ModelId { get; set; } = "";

    public string ModelArtifactHash { get; set; } = "";

    public string GameBuild { get; set; } = "";

    public string CampaignId { get; set; } = "";

    public string CampaignVersion { get; set; } = "";

    public string RulesetHash { get; set; } = "";

    public string NativePackageHash { get; set; } = "";

    public string CreatedUtc { get; set; } = "";

    public string ValidationMode { get; set; } = CombatGameValidationProtocol.ValidationMode;

    public bool HidePresentation { get; set; } = true;

    public int MaximumActionsPerBattle { get; set; } = 400;

    public int MinimumDecisionsPerBattle { get; set; } = 1;

    public int BattleTimeoutSeconds { get; set; } = 180;

    public int MaximumInvalidRuns { get; set; }

    public List<CombatGameValidationCase> Cases { get; set; } = new();
}

public sealed class CombatGameValidationCase
{
    public string CaseId { get; set; } = "";

    public string LevelId { get; set; } = "";

    public string EncounterId { get; set; } = "";

    public int Repetitions { get; set; } = 1;

    public int MinimumWins { get; set; } = 1;

    public bool Required { get; set; } = true;
}

public sealed class CombatGameValidationReport
{
    public int SchemaVersion { get; set; } = CombatGameValidationProtocol.SchemaVersion;

    public string RequestId { get; set; } = "";

    public string ModelId { get; set; } = "";

    public string CompatibilityKey { get; set; } = "";

    public string ValidationMode { get; set; } = CombatGameValidationProtocol.ValidationMode;

    public bool PresentationVisible { get; set; }

    public bool Completed { get; set; }

    public bool Passed { get; set; }

    public string FailureReason { get; set; } = "";

    public string StartedUtc { get; set; } = "";

    public string CompletedUtc { get; set; } = "";

    public int TotalDecisions { get; set; }

    public List<CombatGameValidationCaseResult> Cases { get; set; } = new();

    public string ReceiptHash { get; set; } = "";
}

public sealed class CombatGameValidationCaseResult
{
    public string CaseId { get; set; } = "";

    public string LevelId { get; set; } = "";

    public int Attempts { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Escapes { get; set; }

    public int InvalidRuns { get; set; }

    public int Decisions { get; set; }

    public string LastDiagnostic { get; set; } = "";
}
