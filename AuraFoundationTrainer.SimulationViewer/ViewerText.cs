namespace AuraFoundationTrainer.SimulationViewer;

internal static class ViewerText
{
    public static string Difficulty(string value) => value.ToLowerInvariant() switch
    {
        "normal" => "普通",
        "advanced" => "高级",
        _ => string.IsNullOrWhiteSpace(value) ? "未知" : value
    };

    public static string RewardKind(string value) => value.ToLowerInvariant() switch
    {
        "card" => "卡牌",
        "relic" => "遗物",
        "blessing" => "祝福",
        _ => string.IsNullOrWhiteSpace(value) ? "未知" : value
    };

    public static string EntityType(string value) => value.ToLowerInvariant() switch
    {
        "card" => "卡牌",
        "relic" => "遗物",
        "blessing" => "祝福",
        "enemy" => "敌人",
        "buff" or "status" => "BUFF",
        "encounter" => "场景",
        _ => value
    };

    public static string Outcome(string value) => value.ToLowerInvariant() switch
    {
        "victory" or "win" => "胜利",
        "defeat" or "loss" => "失败",
        "invalid" => "无效",
        _ => value
    };

    public static string Promotion(string value) => value.ToLowerInvariant() switch
    {
        "significant-improvement" => "显著提升",
        "equivalent-noninferior" => "等效且不劣",
        "absolute-qualified-best" => "绝对合格候选",
        "experimental-absolute-qualified" => "实机测试候选",
        "experimental-runtime-test" => "运行时安全实机测试候选",
        "diagnostic-unqualified-candidate" => "诊断候选",
        "offline-rejected" => "离线门禁拒绝",
        _ => value
    };

    public static string DifferenceCategory(string value) =>
        value.ToLowerInvariant() switch
        {
            "search-selection" => "搜索选择差异",
            "risk-calibration" => "风险校准差异",
            "policy-value-ranking" => "策略价值排序差异",
            _ => value
        };
}
