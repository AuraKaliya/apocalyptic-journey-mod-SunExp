using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AuraCombatAi.Shared;

namespace AuraFoundationTrainer.ControlCenter;

internal sealed class TrainingDiagnosticsPanel
{
    private readonly TextBlock verdict = new();
    private readonly TextBlock verdictDetail = new();
    private readonly Dictionary<string, TextBlock> gateValues =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> metricValues =
        new(StringComparer.Ordinal);
    private readonly TrainingLossChart lossChart = new();
    private readonly TextBlock lossSummary = new();
    private readonly TextBlock dataSummary = new();
    private readonly StackPanel arenaRows = new();
    private readonly TextBlock searchSummary = new();
    private readonly Border failureBand = new();
    private readonly TextBlock failureSummary = new();

    public TrainingDiagnosticsPanel()
    {
        View = Build();
        Reset();
    }

    public UIElement View { get; }

    public void Reset()
    {
        verdict.Text = "等待训练数据";
        verdict.Foreground = TrainerTheme.Muted;
        verdictDetail.Text = "Epoch 指标、竞技场结果和正式验证会在这里汇合。";
        SetGate("data", "等待", TrainerTheme.Muted);
        SetGate("offline", "等待", TrainerTheme.Muted);
        SetGate("arena", "未运行", TrainerTheme.Muted);
        SetGate("validation", "未运行", TrainerTheme.Muted);
        lossChart.SetMetrics(Array.Empty<CombatPolicyValueEpochMetrics>());
        lossSummary.Text = "尚未记录同口径的训练集与验证集综合损失。";
        PresentMetrics(null);
        dataSummary.Text = "回放分布尚未生成。";
        arenaRows.Children.Clear();
        arenaRows.Children.Add(Muted("竞技场尚未运行。"));
        searchSummary.Text = "搜索与语义指标尚未生成。";
        failureBand.Visibility = Visibility.Collapsed;
    }

    public void PresentTelemetry(CombatCampaignFoundationTelemetry telemetry)
    {
        if (telemetry.ModelTrainingLoss > 0d
            && telemetry.ModelValidationLoss > 0d)
        {
            lossSummary.Text =
                $"当前 Epoch {telemetry.ModelEpoch} · "
                + $"训练 {Loss(telemetry.ModelTrainingLoss)} · "
                + $"验证 {Loss(telemetry.ModelValidationLoss)} · "
                + "曲线将在本轮训练结束后一次性绘制。";
        }

        verdict.Text = "训练进行中 · " + FriendlyPhase(telemetry.Phase);
        verdict.Foreground = TrainerTheme.Accent;
        verdictDetail.Text = telemetry.CurrentPhaseRequestedCampaigns > 0
            ? $"本阶段实测 {telemetry.CurrentPhaseCompletedCampaigns}/"
              + $"{telemetry.CurrentPhaseRequestedCampaigns} 场冒险，"
              + $"正式训练 {telemetry.RunCompletedCampaigns}/"
              + $"{telemetry.RunRequestedCampaigns}，"
              + $"模型 Epoch {telemetry.ModelEpoch}/{telemetry.ModelTotalEpochs}。"
            : $"正式训练已完成 {telemetry.RunCompletedCampaigns}/"
              + $"{telemetry.RunRequestedCampaigns} 场冒险，"
              + $"全阶段实测 {telemetry.RunExecutedCampaigns} 场，"
              + $"模型 Epoch {telemetry.ModelEpoch}/{telemetry.ModelTotalEpochs}。";
        SetGate("data", "监测中", TrainerTheme.Accent);
        SetGate("arena", "等待候选", TrainerTheme.Muted);
        SetGate("validation", "等待晋级", TrainerTheme.Muted);
        searchSummary.Text =
            $"教师覆盖 {Rate(telemetry.AuthoritativeTeacherOverrides, telemetry.AuthoritativeSelectedActionsAudited)} · "
            + $"选中动作语义不匹配 {Rate(telemetry.AuthoritativeSelectedSemanticMismatches, telemetry.AuthoritativeSelectedActionsAudited)} · "
            + $"根节点最大访问占比 {telemetry.RootMaximumVisitShareMean:P1}";
        failureBand.Visibility = Visibility.Collapsed;
    }

    public void PresentResult(ControllerWorkerResultSummary result)
    {
        var training = result.Training;
        if (training == null)
        {
            Reset();
            verdict.Text = "结果缺少训练诊断";
            verdict.Foreground = TrainerTheme.Warning;
            verdictDetail.Text = result.Message;
            return;
        }

        var iterationHistory = training.Iterations
            .SelectMany(item =>
                item.ModelEpochHistory
                ?? new List<CombatPolicyValueEpochMetrics>())
            .ToList();
        var history = iterationHistory.Count > 0
            ? iterationHistory
            : training.ModelEpochHistory;
        lossChart.SetMetrics(history);
        var latestIteration = training.Iterations.LastOrDefault();
        var selected = latestIteration == null
            ? LatestMetrics(history)
            : SelectedMetrics(latestIteration);
        PresentMetrics(selected);
        PresentVerdict(result, training);
        PresentGates(training);
        PresentLossSummary(training, selected);
        if (!string.IsNullOrWhiteSpace(result.TrainingMetricsPath)
            || !string.IsNullOrWhiteSpace(result.TrainingAnalysisPath))
        {
            lossSummary.Text +=
                "\r\n原始指标 "
                + (string.IsNullOrWhiteSpace(result.TrainingMetricsPath)
                    ? "—"
                    : result.TrainingMetricsPath)
                + "\r\n派生分析 "
                + (string.IsNullOrWhiteSpace(result.TrainingAnalysisPath)
                    ? "—"
                    : result.TrainingAnalysisPath);
        }
        PresentData(training, latestIteration);
        PresentArena(training);
        PresentSearch(training);
        AppendNanaStrategySummary(result);
        PresentFailures(training);
    }

    private UIElement Build()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = TrainerTheme.Window
        };
        var root = new StackPanel
        {
            Margin = new Thickness(2, 0, 12, 18),
            MaxWidth = 1040,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        scroll.Content = root;

        verdict.FontSize = 20;
        verdict.FontWeight = FontWeights.SemiBold;
        verdict.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(verdict);
        verdictDetail.Margin = new Thickness(0, 5, 0, 14);
        verdictDetail.Foreground = TrainerTheme.Muted;
        verdictDetail.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(verdictDetail);

        var gates = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var index = 0; index < 4; index++)
        {
            gates.ColumnDefinitions.Add(new ColumnDefinition());
        }
        AddGate(gates, 0, "data", "数据健康");
        AddGate(gates, 1, "offline", "离线泛化");
        AddGate(gates, 2, "arena", "竞技场");
        AddGate(gates, 3, "validation", "正式验证");
        root.Children.Add(gates);

        root.Children.Add(Heading("损失曲线"));
        var chartBorder = Band();
        var chartPanel = new DockPanel();
        var chartControls = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var emaToggle = new CheckBox
        {
            Content = "EMA 平滑（α=0.30）",
            IsChecked = true,
            Foreground = TrainerTheme.Text,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "仅辅助观察趋势；最佳 Epoch、提前停止和晋级仍使用原始指标。"
        };
        emaToggle.Checked += (_, _) => lossChart.SetEmaVisible(true);
        emaToggle.Unchecked += (_, _) => lossChart.SetEmaVisible(false);
        DockPanel.SetDock(emaToggle, Dock.Right);
        chartControls.Children.Add(emaToggle);
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0)
        };
        legend.Children.Add(Legend("训练损失", TrainerTheme.Accent));
        legend.Children.Add(Legend("验证损失与 95% CI", TrainerTheme.Warning));
        legend.Children.Add(Legend("校准后选中模型", TrainerTheme.Success));
        chartControls.Children.Add(legend);
        DockPanel.SetDock(chartControls, Dock.Top);
        chartPanel.Children.Add(chartControls);
        chartPanel.Children.Add(lossChart);
        chartBorder.Child = chartPanel;
        root.Children.Add(chartBorder);
        lossSummary.Margin = new Thickness(2, 7, 0, 14);
        lossSummary.Foreground = TrainerTheme.Muted;
        lossSummary.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(lossSummary);

        var detailGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition());
        detailGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var metricBand = Band(new Thickness(0, 0, 7, 0));
        metricBand.Child = BuildMetricTable();
        detailGrid.Children.Add(metricBand);
        var dataBand = Band(new Thickness(7, 0, 0, 0));
        var dataPanel = new StackPanel();
        dataPanel.Children.Add(Subheading("回放与样本分布"));
        dataSummary.TextWrapping = TextWrapping.Wrap;
        dataSummary.LineHeight = 22;
        dataPanel.Children.Add(dataSummary);
        dataBand.Child = dataPanel;
        Grid.SetColumn(dataBand, 1);
        detailGrid.Children.Add(dataBand);
        root.Children.Add(detailGrid);

        root.Children.Add(Heading("竞技场与晋级"));
        var arenaBand = Band();
        arenaBand.Child = arenaRows;
        root.Children.Add(arenaBand);

        root.Children.Add(Heading("搜索与语义健康"));
        var searchBand = Band();
        searchSummary.TextWrapping = TextWrapping.Wrap;
        searchSummary.LineHeight = 22;
        searchBand.Child = searchSummary;
        root.Children.Add(searchBand);

        failureBand.Background = TrainerTheme.Surface;
        failureBand.BorderBrush = TrainerTheme.Danger;
        failureBand.BorderThickness = new Thickness(2, 1, 1, 1);
        failureBand.CornerRadius = new CornerRadius(4);
        failureBand.Padding = new Thickness(14, 12, 14, 12);
        failureBand.Margin = new Thickness(0, 14, 0, 0);
        var failurePanel = new StackPanel();
        failurePanel.Children.Add(Subheading("无效战役诊断", TrainerTheme.Danger));
        failureSummary.TextWrapping = TextWrapping.Wrap;
        failureSummary.LineHeight = 21;
        failurePanel.Children.Add(failureSummary);
        failureBand.Child = failurePanel;
        root.Children.Add(failureBand);
        return scroll;
    }

    private Grid BuildMetricTable()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1.5, GridUnitType.Star)
        });
        for (var index = 0; index < 3; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }
        AddMetricCell(grid, 0, 0, "指标", true);
        AddMetricCell(grid, 0, 1, "训练", true);
        AddMetricCell(grid, 0, 2, "验证", true);
        AddMetricCell(grid, 0, 3, "差值", true);
        var rows = new[]
        {
            ("loss", "综合损失"),
            ("ce", "策略交叉熵"),
            ("critical", "关键决策准确率"),
            ("value", "价值 MAE"),
            ("win", "胜利 Brier"),
            ("death", "死亡 Brier"),
            ("hp", "生命 MAE"),
            ("turn", "回合 Huber")
        };
        for (var index = 0; index < rows.Length; index++)
        {
            AddMetricRow(grid, index + 1, rows[index].Item1, rows[index].Item2);
        }
        return grid;
    }

    private void PresentVerdict(
        ControllerWorkerResultSummary result,
        ControllerTrainingResultSummary training)
    {
        if (training.InvalidTrainingCampaigns > 0)
        {
            verdict.Text = "训练已隔离：发现无效战役";
            verdict.Foreground = TrainerTheme.Danger;
            verdictDetail.Text =
                "无效战役的全部轨迹未进入底模；本轮不可恢复，也未发布候选模型。";
        }
        else if (training.FormalModelBlocked)
        {
            verdict.Text = "训练已阻断：教师或协议故障";
            verdict.Foreground = TrainerTheme.Danger;
            verdictDetail.Text = string.IsNullOrWhiteSpace(
                training.FormalModelBlockReason)
                ? training.Message
                : training.FormalModelBlockReason;
        }
        else if (training.AcceptancePassed)
        {
            verdict.Text = "训练通过：底模可发布";
            verdict.Foreground = TrainerTheme.Success;
            verdictDetail.Text = training.Message;
        }
        else if (training.QualifiedCandidateCount > 0
                 || training.AbsoluteQualifiedBestModel != null)
        {
            verdict.Text = "已有合格候选，正式验证未通过";
            verdict.Foreground = TrainerTheme.Warning;
            verdictDetail.Text = training.Message;
        }
        else if (training.BestPendingArenaCandidate != null)
        {
            verdict.Text = "训练已推进：最佳候选等待竞技场";
            verdict.Foreground = TrainerTheme.Accent;
            verdictDetail.Text =
                $"已保留第 {training.BestPendingArenaCandidate.SourceIteration} 轮的离线安全候选；"
                + "后续竞技场将验证该候选，而不是强制使用最新模型。"
                + (string.IsNullOrWhiteSpace(training.Message)
                    ? ""
                    : " " + training.Message);
        }
        else if (!training.Iterations.Any(item => item.ArenaEvaluationRan))
        {
            verdict.Text = "训练已推进：竞技场尚未到计划轮次";
            verdict.Foreground = TrainerTheme.Accent;
            verdictDetail.Text = training.Message;
        }
        else
        {
            var lastArena = training.Iterations.Last(item =>
                item.ArenaEvaluationRan);
            verdict.Text = "竞技场未形成合格候选";
            verdict.Foreground = TrainerTheme.Warning;
            verdictDetail.Text = PrimaryArenaReason(lastArena)
                                 + (string.IsNullOrWhiteSpace(result.Message)
                                     ? ""
                                     : " " + result.Message);
        }
    }

    private void PresentGates(ControllerTrainingResultSummary training)
    {
        var dataHealthy = training.Preflight.Passed
                          && training.InvalidTrainingCampaigns == 0
                          && training.TerminalConsistencyViolations == 0
                          && training.FeatureLeakageViolations == 0;
        SetGate(
            "data",
            dataHealthy ? "通过" : "阻断",
            dataHealthy ? TrainerTheme.Success : TrainerTheme.Danger);
        var selected = training.Iterations.LastOrDefault();
        var metric = selected == null
            ? LatestMetrics(training.ModelEpochHistory)
            : SelectedMetrics(selected);
        SetGate(
            "offline",
            metric == null ? "无数据" : OfflineGate(metric),
            metric == null ? TrainerTheme.Muted : OfflineBrush(metric));
        var arenaIterations = training.Iterations
            .Where(item => item.ArenaEvaluationRan)
            .ToList();
        var lastArena = arenaIterations.LastOrDefault();
        var pending = training.BestPendingArenaCandidate;
        var arenaState = training.QualifiedCandidateCount > 0
                         || training.AbsoluteQualifiedBestModel != null
            ? "已有合格候选"
            : pending != null
                ? $"待验证 I{pending.SourceIteration}"
                : lastArena == null
                    ? "计划中"
                    : lastArena.ArenaScreeningDiagnosticOnly
                        ? "诊断筛选阻断"
                        : lastArena.ArenaConfirmationPairs > 0
                            ? "正式确认未合格"
                            : lastArena.FormalArenaConfirmationScheduled
                                ? "正式确认未触发"
                                : "筛选未通过";
        SetGate(
            "arena",
            arenaState,
            training.QualifiedCandidateCount > 0
            || training.AbsoluteQualifiedBestModel != null
                ? TrainerTheme.Success
                : pending != null || lastArena == null
                    ? TrainerTheme.Accent
                    : TrainerTheme.Warning);
        var validationNotRun =
            training.Validation.NormalStatus == "not-run"
            && training.Validation.AdvancedStatus == "not-run";
        SetGate(
            "validation",
            training.Validation.Passed
                ? "通过"
                : validationNotRun
                    ? training.QualifiedCandidateCount > 0
                      || training.AbsoluteQualifiedBestModel != null
                        ? "待正式验证"
                        : "等待合格候选"
                    : "未通过",
            training.Validation.Passed
                ? TrainerTheme.Success
                : validationNotRun
                    ? TrainerTheme.Muted
                    : TrainerTheme.Danger);
    }

    private void PresentLossSummary(
        ControllerTrainingResultSummary training,
        CombatPolicyValueEpochMetrics? selected)
    {
        if (selected == null)
        {
            lossSummary.Text =
                $"本结果来自旧版协议；仅保留最佳验证损失 "
                + $"{Loss(training.ModelBestValidationLoss)}，没有逐 Epoch 历史。";
            return;
        }
        lossSummary.Text =
            $"选中模型 I{selected.Iteration}:E{selected.Epoch} · "
            + $"训练 {Loss(selected.Training.CompositeLoss)} · "
            + $"验证 {Loss(selected.Validation.CompositeLoss)} · "
            + $"95% CI {ConfidenceInterval(selected.Validation)} · "
            + $"泛化差 {Gap(selected):+0.000;-0.000;0.000} · "
            + $"最佳验证 {Loss(training.ModelBestValidationLoss)}"
            + (training.ModelEarlyStopped ? " · 已提前停止" : "")
            + (string.IsNullOrWhiteSpace(selected.TrainingMeasurement)
                ? ""
                : $" · 训练口径 {selected.TrainingMeasurement}");
    }

    private void PresentData(
        ControllerTrainingResultSummary training,
        CombatCampaignFoundationIteration? iteration)
    {
        if (iteration == null || iteration.TrainingReplayEpisodes <= 0)
        {
            dataSummary.Text =
                $"生成回放 {training.GeneratedReplayEpisodes:N0}\r\n"
                + $"专家回放 {training.LoadedExpertReplayEpisodes:N0}\r\n"
                + "尚无可评价的训练窗口。";
            return;
        }
        var total = Math.Max(1, iteration.TrainingReplayEpisodes);
        var advancedShare =
            iteration.TrainingReplayAdvancedEpisodes / (double)total;
        var advancedDefeatShare =
            iteration.TrainingReplayAdvancedDefeatEpisodes / (double)total;
        var successShare =
            iteration.TrainingReplaySuccessfulEpisodes / (double)total;
        var shortfall = iteration.TrainingReplayQuotaShortfalls.Count == 0
            ? "无"
            : string.Join(
                "，",
                iteration.TrainingReplayQuotaShortfalls
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => item.Key + " " + item.Value));
        var strategyShortfall =
            iteration.TeacherStudentPoolQuotaShortfalls.Count == 0
                ? "无"
                : string.Join(
                    "，",
                    iteration.TeacherStudentPoolQuotaShortfalls
                        .OrderBy(item => item.Key, StringComparer.Ordinal)
                        .Select(item => item.Key + " " + item.Value));
        dataSummary.Text =
            $"训练窗口 {iteration.TrainingReplayEpisodes:N0} 条\r\n"
            + $"高级难度 {advancedShare:P1} / 目标 "
            + $"{iteration.EffectiveMinimumAdvancedReplayShare:P1}\r\n"
            + $"高级失败 {advancedDefeatShare:P1} / 目标 "
            + $"{iteration.TrainingReplayTargetAdvancedDefeatShare:P1}\r\n"
            + $"成功样本 {successShare:P1}\r\n"
            + $"专家回放 {training.LoadedExpertReplayEpisodes:N0}\r\n"
            + $"回放配额缺口：{shortfall}\r\n"
            + $"教师/学生帧池 选中/封顶后/原始 "
            + $"{iteration.TeacherStudentPoolSelectedFrames:N0}/"
            + $"{iteration.TeacherStudentPoolSourceFrames:N0}/"
            + $"{iteration.TeacherStudentPoolAvailableSourceFrames:N0} · "
            + $"不安全结束回合 {iteration.TeacherStudentPoolUnsafeEndTurnFrames:N0}\r\n"
            + (iteration.StrategyQuotaRepairAttempted
                ? $"拟合前配额修复：候选 {iteration.StrategyQuotaRepairSourceEpisodes:N0} 条，"
                  + $"补入 {iteration.StrategyQuotaRepairAddedEpisodes:N0} 条；"
                  + $"定向采集 {iteration.StrategyQuotaCollectionCampaigns:N0} 场/"
                  + $"{iteration.StrategyQuotaCollectionEpisodes:N0} 条\r\n"
                : "拟合前配额修复：未触发\r\n")
            + $"累计教师语料 {iteration.TransformerTeacher.FrameCount:N0} · "
            + $"本轮 {iteration.TransformerTeacher.CurrentFrameCount:N0} · "
            + $"复用 {iteration.TransformerTeacher.ReusedCorpusFrames:N0} · "
            + $"去重 {iteration.TransformerTeacher.DeduplicatedCorpusFrames:N0}\r\n"
            + $"教师执行：{(iteration.TransformerTeacher.TrainingRefreshed ? "重训并标注" : "稳定教师仅标注")}"
            + $" · 原因 {iteration.TransformerTeacher.RefreshReason}"
            + $" · 新待训 {iteration.TransformerTeacher.RefreshFreshPendingFrames:N0}/"
            + $"{iteration.TransformerTeacher.RefreshMinimumFreshFrames:N0}"
            + $" · 最大间隔 {iteration.TransformerTeacher.RefreshInterval} 轮\r\n"
            + $"策略配额缺口：{strategyShortfall}\r\n"
            + $"行为进展：{(iteration.BehavioralProductiveProgress ? "是" : "否")} · "
            + $"数据管线进展：{(iteration.DataPipelineProgress ? "是" : "否")} · "
            + $"仅数据连续轮：{iteration.ConsecutiveDataOnlyIterations}\r\n"
            + $"推理 batch 填充 {iteration.InferenceHealth.AverageBatchFill:P1} · "
            + $"超时 flush {iteration.InferenceHealth.TimeoutFlushRate:P1} · "
            + $"直接回退 {iteration.InferenceHealth.DirectFallbackRate:P1}"
            + (iteration.InferenceHealth.RevalidationRequired
                ? $" · 下轮重测（{iteration.InferenceHealth.Reason}）"
                : "");
    }

    private void PresentArena(ControllerTrainingResultSummary training)
    {
        arenaRows.Children.Clear();
        var iterations = training.Iterations
                         ?? new List<CombatCampaignFoundationIteration>();
        var trainingOnly = iterations.Count(item => item.TrainingOnlyIteration);
        var arenaIterations = iterations
            .Where(item => item.ArenaEvaluationRan)
            .ToList();
        arenaRows.Children.Add(Muted(
            "模型槽：最新训练 "
            + ModelId(training.LatestTrainingModel)
            + " · 待验证 "
            + (training.BestPendingArenaCandidate == null
                ? "无"
                : "I"
                  + training.BestPendingArenaCandidate.SourceIteration
                  + "/"
                  + ModelId(training.BestPendingArenaCandidate.Model))
            + " · 工作模型 "
            + ModelId(training.WorkingChampion)
            + " · 已验证合格 "
            + ModelId(training.AbsoluteQualifiedBestModel)));
        if (trainingOnly > 0)
        {
            arenaRows.Children.Add(Muted(
                $"{trainingOnly} 轮为 training-only：只训练并更新待验证候选，不计作竞技场失败。"));
        }
        if (arenaIterations.Count == 0)
        {
            arenaRows.Children.Add(Muted(
                training.BestPendingArenaCandidate == null
                    ? "候选模型尚未进入计划中的竞技场轮次。"
                    : $"第 {training.BestPendingArenaCandidate.SourceIteration} 轮候选已保留，等待下一次竞技场。"));
            return;
        }
        foreach (var item in arenaIterations)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(90)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var status = new TextBlock
            {
                Text = "第 " + item.Iteration + " 轮",
                FontWeight = FontWeights.SemiBold,
                Foreground = item.Promoted
                             || item.AbsoluteQualificationGatePassed
                    ? TrainerTheme.Success
                    : TrainerTheme.Warning
            };
            row.Children.Add(status);
            var detail = new TextBlock
            {
                Text =
                    $"验证候选 I{Math.Max(1, item.ArenaCandidateSourceIteration)}"
                    + (item.ArenaCandidateSelectedFromPendingBank
                        ? "（历史待验证最佳）"
                        : "（本轮模型）")
                    + $" · 筛选 {item.ArenaScreeningPairs} 对"
                    + (item.ArenaScreeningDiagnosticOnly ? "（仅诊断）" : "")
                    + $" · 正式确认 {item.ArenaConfirmationPairs} 对"
                    + (item.FormalArenaConfirmationScheduled
                        ? "（已计划）"
                        : "（未计划）")
                    + $" · 节省 {item.ArenaScreeningPairsSaved + item.ArenaConfirmationPairsSaved} 对\r\n"
                    + $"普通 候选 {item.CandidateNormalWinRate:P1} / 对照 {item.ChampionNormalWinRate:P1} · "
                    + $"高级 候选 {item.CandidateAdvancedWinRate:P1} / 对照 {item.ChampionAdvancedWinRate:P1}\r\n"
                    + $"分数差 {item.CandidateScoreGain:+0.0;-0.0;0.0} · "
                    + $"深度差 {item.CandidateDepthGain:+0.000;-0.000;0.000} · "
                    + $"配对独占胜 {item.CandidateOnlyWins}:{item.ChampionOnlyWins} · "
                    + $"分歧 {item.ArenaDiscordantPairs} · "
                    + $"证据分类 {item.PairedEvidenceKind} · 退化上界 {item.PairedRegressionWilsonUpperBound:P1} · "
                    + $"不劣 {PassMark(item.NonInferiorityGatePassed)} · "
                    + $"绝对合格 {PassMark(item.AbsoluteQualificationGatePassed)} · "
                    + $"门禁(相对证据/普通/高难/离线头/配额/碰撞) "
                    + $"{PassMark(item.ArenaEvidenceGatePassed)}/"
                    + $"{PassMark(item.AbsoluteNormalGatePassed)}/"
                    + $"{PassMark(item.AbsoluteAdvancedGatePassed)}/"
                    + $"{PassMark(item.OfflineHeadRegressionGatePassed)}/"
                    + $"{PassMark(item.StrategyQuotaGatePassed)}/"
                    + $"{PassMark(item.FeatureCollisionGatePassed)} · "
                    + $"碰撞 状态 {item.StateFeatureCollisionRate:P1} / 动作 {item.ActionFeatureCollisionRate:P1} · "
                    + (item.QualifiedCandidateSelected
                        ? "已选为合格最佳模型"
                        : item.AbsoluteQualificationGatePassed
                            ? "已进入合格候选池"
                            : item.Promoted
                                ? "已晋级"
                                : PrimaryArenaReason(item)),
                Foreground = TrainerTheme.Text,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(detail, 1);
            row.Children.Add(detail);
            arenaRows.Children.Add(row);
        }
    }

    private static string PrimaryArenaReason(
        CombatCampaignFoundationIteration item)
    {
        if (!item.OfflineHeadRegressionGatePassed)
        {
            return "离线多头回退超过阈值，筛选仅用于诊断。";
        }
        if (!item.StrategyQuotaGatePassed)
        {
            return "策略标签配额仍有缺口，未进入正式确认。";
        }
        if (!item.FeatureCollisionGatePassed)
        {
            return "特征碰撞率超过门槛，未进入正式确认。";
        }
        if (!item.AbsoluteAdvancedGatePassed)
        {
            return "高级难度绝对门槛未通过。";
        }
        if (item.FormalArenaConfirmationScheduled
            && item.ArenaConfirmationPairs == 0)
        {
            return "正式确认已计划，但筛选未满足启动条件。";
        }
        if (!item.ArenaEvidenceGatePassed)
        {
            return "正式证据不足，分歧样本未达到要求。";
        }
        return PromotionReasonText(item.PromotionReason);
    }

    private static string PromotionReasonText(string reason)
    {
        return reason switch
        {
            "no-iterative-gain" => "相对工作模型没有形成迭代收益。",
            "absolute-advanced-gate" => "高级难度绝对门槛未通过。",
            "insufficient-discordant-pairs" => "正式确认的分歧样本不足。",
            "regression-or-incomplete-arena" => "竞技场回退或证据不完整。",
            "advanced-target-not-improved" => "高级难度目标没有改善。",
            "no-meaningful-gain" => "收益未达到有意义改善阈值。",
            "offline-head-regression" => "离线多头回退超过阈值。",
            "strategy-quota-shortfall" => "策略标签配额仍有缺口。",
            "feature-collision-gate" => "特征碰撞率超过门槛。",
            "scheduled-training-continuation" => "训练续跑轮，不执行竞技场。",
            _ => string.IsNullOrWhiteSpace(reason)
                ? "尚未形成合格证据。"
                : reason
        };
    }

    private static string ModelId(ControllerModelIdentity? model)
    {
        if (string.IsNullOrWhiteSpace(model?.ModelId)) return "无";
        return model.ModelId.Length <= 14
            ? model.ModelId
            : model.ModelId.Substring(0, 14) + "…";
    }

    private static string PassMark(bool passed)
    {
        return passed ? "通过" : "未过";
    }

    private void PresentSearch(ControllerTrainingResultSummary training)
    {
        searchSummary.Text =
            $"教师覆盖 {Rate(training.AuthoritativeTeacherOverrides, training.AuthoritativeSelectedActionsAudited)}\r\n"
            + $"选中动作语义不匹配 "
            + $"{Rate(training.AuthoritativeSelectedSemanticMismatches, training.AuthoritativeSelectedActionsAudited)}\r\n"
            + $"决策前实演语义 无效 {training.Preflight.SourceProjectionInvalidRate:P2}"
            + $" / 偏差 {training.Preflight.SourceProjectionMismatchRate:P2}"
            + $"（门槛 {CombatFoundationSemanticGateProtocol.MaximumSourceProjectionInvalidRate:P0}"
            + $" / {CombatFoundationSemanticGateProtocol.MaximumSourceProjectionMismatchRate:P0}）\r\n"
            + $"选中决策前实演 无效 {training.Preflight.SelectedSourceProjectionInvalidActions}"
            + $" / 偏差 {training.Preflight.SelectedSourceProjectionUnexplainedMismatchActions}\r\n"
            + $"根节点最大访问占比 {training.RootMaximumVisitShareMean:P1}\r\n"
            + $"终局一致性错误 {training.TerminalConsistencyViolations} · "
            + $"特征泄漏 {training.FeatureLeakageViolations} · "
            + $"无效训练战役 {training.InvalidTrainingCampaigns}";
    }

    private void AppendNanaStrategySummary(
        ControllerWorkerResultSummary result)
    {
        var metrics = result.RoleStrategyMetrics;
        if (metrics == null
            || Metric(
                metrics,
                "nana.role-strategy-observed-frames") <= 0d)
        {
            return;
        }
        searchSummary.Text += "\r\n\r\n奈奈角色策略门禁 "
                              + (result.RoleStrategyGatePassed
                                  ? "通过"
                                  : "未通过")
                              + $"\r\n动作覆盖 {Metric(metrics, "nana.role-strategy-frame-coverage"):P1}"
                              + $" · 安全成长窗口 {Metric(metrics, "nana.safe-growth-window-frames"):N0}"
                              + $" · 成长铺垫 {Metric(metrics, "nana.selected-growth-builders"):N0}"
                              + $"\r\n厄运解放 {Metric(metrics, "nana.devours"):N0}"
                              + $" · 过早解放率 {Metric(metrics, "nana.premature-devour-rate"):P1}"
                              + $" · 单次厄运中位数 {Metric(metrics, "nana.devour-doom-gain.median"):0.0}"
                              + $" · 单次生命成长中位数 {Metric(metrics, "nana.devour-max-hp-gain.median"):0.0}"
                              + $"\r\n首次化身厄运中位数 {Metric(metrics, "nana.first-transform-doom.median"):0.0}"
                              + $" · 旅程最终生命均值/最大值 {Metric(metrics, "final-max-hp.mean"):0.0}/"
                              + $"{Metric(metrics, "final-max-hp.maximum"):0.0}"
                              + (string.IsNullOrWhiteSpace(
                                      result.RoleStrategyGateFailureReason)
                                  ? ""
                                  : "\r\n"
                                    + result.RoleStrategyGateFailureReason);
    }

    private static double Metric(
        IReadOnlyDictionary<string, double> metrics,
        string key)
    {
        return metrics.TryGetValue(key, out var value)
               && !double.IsNaN(value)
               && !double.IsInfinity(value)
            ? value
            : 0d;
    }

    private void PresentFailures(ControllerTrainingResultSummary training)
    {
        if (training.TrainingFailures.Count == 0
            && training.TrainingFailureCounts.Count == 0)
        {
            failureBand.Visibility = Visibility.Collapsed;
            return;
        }
        failureBand.Visibility = Visibility.Visible;
        var first = training.TrainingFailures.FirstOrDefault();
        var counts = string.Join(
            "\r\n",
            training.TrainingFailureCounts
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Take(5)
                .Select(item => $"{item.Value} × {ShortReason(item.Key)}"));
        failureSummary.Text = first == null
            ? counts
            : $"难度 {first.DifficultyId} · 种子 {first.WorldSeed} · "
              + $"第 {first.CompletedBattles + 1} 场战斗\r\n"
              + counts;
    }

    private void PresentMetrics(CombatPolicyValueEpochMetrics? metrics)
    {
        if (metrics == null)
        {
            foreach (var value in metricValues.Values)
            {
                value.Text = "—";
            }
            return;
        }
        SetMetric("loss", metrics.Training.CompositeLoss, metrics.Validation.CompositeLoss);
        SetMetric("ce", metrics.Training.PolicyCrossEntropy, metrics.Validation.PolicyCrossEntropy);
        SetMetric(
            "critical",
            metrics.Training.CriticalPolicyAccuracy,
            metrics.Validation.CriticalPolicyAccuracy,
            percent: true);
        SetMetric("value", metrics.Training.ValueMae, metrics.Validation.ValueMae);
        SetMetric("win", metrics.Training.Brier, metrics.Validation.Brier);
        SetMetric("death", metrics.Training.DeathBrier, metrics.Validation.DeathBrier);
        SetMetric("hp", metrics.Training.HpMae, metrics.Validation.HpMae);
        SetMetric("turn", metrics.Training.TurnHuber, metrics.Validation.TurnHuber);
    }

    private void SetMetric(
        string key,
        double training,
        double validation,
        bool percent = false)
    {
        var format = percent ? "P1" : "0.000";
        metricValues[key + ":training"].Text = training.ToString(
            format,
            CultureInfo.InvariantCulture);
        metricValues[key + ":validation"].Text = validation.ToString(
            format,
            CultureInfo.InvariantCulture);
        metricValues[key + ":gap"].Text = (validation - training).ToString(
            percent ? "+0.0%;-0.0%;0.0%" : "+0.000;-0.000;0.000",
            CultureInfo.InvariantCulture);
    }

    private void AddMetricRow(Grid grid, int row, string key, string label)
    {
        AddMetricCell(grid, row, 0, label, false);
        foreach (var (suffix, column) in new[]
                 {
                     ("training", 1),
                     ("validation", 2),
                     ("gap", 3)
                 })
        {
            var value = AddMetricCell(grid, row, column, "—", false);
            metricValues[key + ":" + suffix] = value;
        }
    }

    private static TextBlock AddMetricCell(
        Grid grid,
        int row,
        int column,
        string text,
        bool header)
    {
        while (grid.RowDefinitions.Count <= row)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
        }
        var block = new TextBlock
        {
            Text = text,
            Foreground = header ? TrainerTheme.Muted : TrainerTheme.Text,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(4, 4, 8, 4),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
        return block;
    }

    private void AddGate(Grid grid, int column, string key, string label)
    {
        var border = new Border
        {
            Background = TrainerTheme.Surface,
            BorderBrush = TrainerTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(
                column == 0 ? 0 : 5,
                0,
                column == 3 ? 0 : 5,
                0)
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TrainerTheme.Muted,
            FontSize = 12
        });
        var value = new TextBlock
        {
            Text = "等待",
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 4, 0, 0)
        };
        gateValues[key] = value;
        panel.Children.Add(value);
        border.Child = panel;
        Grid.SetColumn(border, column);
        grid.Children.Add(border);
    }

    private void SetGate(string key, string text, Brush brush)
    {
        gateValues[key].Text = text;
        gateValues[key].Foreground = brush;
    }

    private static Border Band(Thickness? margin = null)
    {
        return new Border
        {
            Background = TrainerTheme.Surface,
            BorderBrush = TrainerTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = margin ?? new Thickness(0)
        };
    }

    private static TextBlock Heading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrainerTheme.Accent,
            Margin = new Thickness(0, 8, 0, 7)
        };
    }

    private static TextBlock Subheading(
        string text,
        Brush? brush = null)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = brush ?? TrainerTheme.Text,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    private static TextBlock Muted(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = TrainerTheme.Muted,
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static UIElement Legend(string text, Brush brush)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 18, 0)
        };
        panel.Children.Add(new Border
        {
            Width = 18,
            Height = 3,
            Background = brush,
            Margin = new Thickness(0, 8, 6, 0),
            VerticalAlignment = VerticalAlignment.Top
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = TrainerTheme.Muted
        });
        return panel;
    }

    private static CombatPolicyValueEpochMetrics? LatestMetrics(
        IEnumerable<CombatPolicyValueEpochMetrics>? source)
    {
        var list = (source ?? Array.Empty<CombatPolicyValueEpochMetrics>())
            .ToList();
        if (list.Count == 0)
        {
            return null;
        }
        var iteration = list.Max(item => item.Iteration);
        var current = list.Where(item => item.Iteration == iteration).ToList();
        return current.FirstOrDefault(item => item.Calibrated)
               ?? current.OrderByDescending(item => item.Epoch).FirstOrDefault();
    }

    private static CombatPolicyValueEpochMetrics? SelectedMetrics(
        CombatCampaignFoundationIteration iteration)
    {
        var history = iteration.ModelEpochHistory
                      ?? new List<CombatPolicyValueEpochMetrics>();
        return history.FirstOrDefault(item =>
                   item.Calibrated
                   && item.Epoch == iteration.TuningSelectedEpoch)
               ?? history.FirstOrDefault(item => item.Calibrated)
               ?? history
                   .Where(item => item.Epoch == iteration.TuningSelectedEpoch)
                   .OrderByDescending(item => item.Epoch)
                   .FirstOrDefault()
               ?? LatestMetrics(history);
    }

    private static string OfflineGate(CombatPolicyValueEpochMetrics metrics)
    {
        var assessment = CombatGeneralizationAssessmentProtocol.Assess(
            metrics.Training,
            metrics.Validation);
        return assessment.Level switch
        {
            CombatGeneralizationRiskLevels.Healthy => "健康",
            CombatGeneralizationRiskLevels.Watch => "观察",
            CombatGeneralizationRiskLevels.Overfit => "过拟合风险",
            CombatGeneralizationRiskLevels.Underfit => "欠拟合风险",
            _ => "证据不足"
        };
    }

    private static Brush OfflineBrush(CombatPolicyValueEpochMetrics metrics)
    {
        return CombatGeneralizationAssessmentProtocol.Assess(
            metrics.Training,
            metrics.Validation).Level switch
        {
            CombatGeneralizationRiskLevels.Healthy => TrainerTheme.Success,
            CombatGeneralizationRiskLevels.Watch => TrainerTheme.Warning,
            CombatGeneralizationRiskLevels.Insufficient => TrainerTheme.Muted,
            _ => TrainerTheme.Danger
        };
    }

    private static double Gap(CombatPolicyValueEpochMetrics metrics)
    {
        return metrics.Validation.CompositeLoss
               - metrics.Training.CompositeLoss;
    }

    private static string Loss(double value)
    {
        return value > 0d && !double.IsNaN(value) && !double.IsInfinity(value)
            ? value.ToString("0.0000", CultureInfo.InvariantCulture)
            : "—";
    }

    private static string ConfidenceInterval(
        CombatPolicyValueMetricSnapshot metrics)
    {
        return metrics.RunCount > 1
               && metrics.CompositeLossCiUpper
               >= metrics.CompositeLossCiLower
            ? $"[{metrics.CompositeLossCiLower:0.0000}, "
              + $"{metrics.CompositeLossCiUpper:0.0000}]"
            : "样本组不足";
    }

    private static string Rate(long numerator, long denominator)
    {
        return denominator <= 0 ? "—" : (numerator / (double)denominator).ToString("P1");
    }

    private static string FriendlyPhase(string phase)
    {
        return phase switch
        {
            "self-play" => "自博弈",
            "replay-selection" => "回放采样",
            "model-training" => "模型训练",
            "arena-screening" => "竞技场筛选",
            "arena-confirmation" => "竞技场确认",
            "validation" => "正式验证",
            _ => phase
        };
    }

    private static string ShortReason(string value)
    {
        const int maximum = 150;
        return value.Length <= maximum
            ? value
            : value[..maximum] + "…";
    }
}
