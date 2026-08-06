using System;
using System.Collections.Generic;
using System.Linq;

namespace AuraCombatAi.Shared;

public static class CombatGeneralizationRiskLevels
{
    public const string Healthy = "healthy";
    public const string Watch = "watch";
    public const string Overfit = "overfit";
    public const string Underfit = "underfit";
    public const string Insufficient = "insufficient";
}

public sealed class CombatGeneralizationAssessment
{
    public string ProtocolVersion { get; set; } =
        CombatGeneralizationAssessmentProtocol.Version;
    public string Level { get; set; } =
        CombatGeneralizationRiskLevels.Insufficient;
    public string Reason { get; set; } = "";
    public double AbsoluteGap { get; set; }
    public double RelativeGap { get; set; }
    public double ValidationRebound { get; set; }
    public double TestGap { get; set; }
    public bool TrainingValidationIntervalsSeparated { get; set; }
    public bool ValidationTestIntervalsSeparated { get; set; }
}

public static class CombatGeneralizationAssessmentProtocol
{
    public const string Version = "generalization-evidence-v1";
    public const double WatchAbsoluteGap = 0.05d;
    public const double OverfitAbsoluteGap = 0.08d;
    public const double WatchRelativeGap = 0.35d;
    public const double ValidationReboundRisk = 0.05d;
    public const double TestRegressionRisk = 0.08d;

    public static CombatGeneralizationAssessment Assess(
        CombatPolicyValueMetricSnapshot? training,
        CombatPolicyValueMetricSnapshot? validation,
        CombatPolicyValueMetricSnapshot? test = null,
        IReadOnlyList<CombatPolicyValueEpochMetrics>? history = null)
    {
        if (!Available(training) || !Available(validation))
        {
            return new CombatGeneralizationAssessment
            {
                Reason = "训练或验证统计证据不足"
            };
        }

        var trainingLoss = training!.CompositeLoss;
        var validationLoss = validation!.CompositeLoss;
        var gap = validationLoss - trainingLoss;
        var relativeGap = gap / Math.Max(0.000001d, trainingLoss);
        var points = (history ?? Array.Empty<CombatPolicyValueEpochMetrics>())
            .Where(item => !item.Calibrated && Available(item.Validation))
            .OrderBy(item => item.Iteration)
            .ThenBy(item => item.Epoch)
            .ToList();
        var bestValidation = points.Count == 0
            ? validationLoss
            : points.Min(item => item.Validation.CompositeLoss);
        var lastValidation = points.Count == 0
            ? validationLoss
            : points[points.Count - 1].Validation.CompositeLoss;
        var rebound = Math.Max(0d, lastValidation - bestValidation);
        var testAvailable = Available(test);
        var testGap = testAvailable ? test!.CompositeLoss - validationLoss : 0d;
        var trainValidationSeparated =
            Lower(validation) > Upper(training) + 0.0000001d;
        var validationTestSeparated = testAvailable
                                      && Lower(test) > Upper(validation)
                                         + 0.0000001d;
        var assessment = new CombatGeneralizationAssessment
        {
            AbsoluteGap = gap,
            RelativeGap = relativeGap,
            ValidationRebound = rebound,
            TestGap = testGap,
            TrainingValidationIntervalsSeparated = trainValidationSeparated,
            ValidationTestIntervalsSeparated = validationTestSeparated
        };

        if ((gap >= OverfitAbsoluteGap && trainValidationSeparated)
            || rebound >= ValidationReboundRisk
            || (testGap >= TestRegressionRisk && validationTestSeparated))
        {
            assessment.Level = CombatGeneralizationRiskLevels.Overfit;
            assessment.Reason = rebound >= ValidationReboundRisk
                ? "验证损失相对最佳点出现明确回升"
                : testGap >= TestRegressionRisk && validationTestSeparated
                    ? "测试集相对验证集出现有置信证据的退化"
                    : "训练/验证绝对差距较大且置信区间已分离";
            return assessment;
        }

        if (trainingLoss >= 0.25d
            && validationLoss >= 0.25d
            && Math.Abs(gap) <= WatchAbsoluteGap)
        {
            assessment.Level = CombatGeneralizationRiskLevels.Underfit;
            assessment.Reason = "训练与验证损失同时偏高且接近";
            return assessment;
        }

        if (gap >= WatchAbsoluteGap
            || (gap >= 0.03d && relativeGap >= WatchRelativeGap)
            || trainValidationSeparated
            || testGap >= WatchAbsoluteGap)
        {
            assessment.Level = CombatGeneralizationRiskLevels.Watch;
            assessment.Reason = "存在泛化差距，但尚不足以判定为过拟合";
            return assessment;
        }

        assessment.Level = CombatGeneralizationRiskLevels.Healthy;
        assessment.Reason = "未发现明确的过拟合或欠拟合证据";
        return assessment;
    }

    private static bool Available(CombatPolicyValueMetricSnapshot? value)
    {
        return value != null
               && value.FrameCount > 0
               && value.CompositeLoss > 0d
               && !double.IsNaN(value.CompositeLoss)
               && !double.IsInfinity(value.CompositeLoss);
    }

    private static double Lower(CombatPolicyValueMetricSnapshot? value)
    {
        if (value == null) return double.PositiveInfinity;
        if (value.CompositeLossCiLower > 0d) return value.CompositeLossCiLower;
        return value.CompositeLoss
               - 1.96d * Math.Max(0d, value.CompositeLossStandardError);
    }

    private static double Upper(CombatPolicyValueMetricSnapshot? value)
    {
        if (value == null) return double.NegativeInfinity;
        if (value.CompositeLossCiUpper > 0d) return value.CompositeLossCiUpper;
        return value.CompositeLoss
               + 1.96d * Math.Max(0d, value.CompositeLossStandardError);
    }
}
