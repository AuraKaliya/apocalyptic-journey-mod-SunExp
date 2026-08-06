using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraToolsExp.Dll.Features.AutoBattle;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.ControlCenter;

internal sealed class MainWindow : Window
{
    private readonly Dictionary<string, TextBox> inputs =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> toggles =
        new(StringComparer.Ordinal);
    private static readonly TimeSpan RunningRefreshInterval =
        TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleRefreshInterval =
        TimeSpan.FromSeconds(5);
    private const int ProgressTabIndex = 1;
    private const int DiagnosticsTabIndex = 2;
    private const uint FlashStop = 0;
    private const uint FlashAll = 3;
    private const uint FlashTimerNoForeground = 12;
    private readonly DispatcherTimer timer;
    private readonly string[] launchArguments;
    private ControllerSettings settings = new();
    private ControllerSession? session;
    private Process? workerProcess;
    private TextBox modRootInput = null!;
    private TextBox dataRootInput = null!;
    private TextBox gamePresetIdInput = null!;
    private TextBox gamePresetNameInput = null!;
    private TextBox preferredDeckMinimumInput = null!;
    private TextBox preferredDeckMaximumInput = null!;
    private ComboBox roleInput = null!;
    private ComboBox familiarInput = null!;
    private WrapPanel cardPackPanel = null!;
    private TextBlock gameSubjectStatus = null!;
    private CombatGameSubjectCatalog gameSubjectCatalog = new();
    private readonly Dictionary<string, CheckBox> cardPackToggles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> profileButtons =
        new(StringComparer.Ordinal);
    private static readonly int[] GradientShardPresets =
        { 0, 1, 2, 4, 8, 12, 16, 24, 32 };
    private string selectedProfile = "balanced";
    private TabControl tabs = null!;
    private ScrollViewer parametersScroll = null!;
    private TextBlock environmentStatus = null!;
    private TextBlock recentResultStatus = null!;
    private TextBlock recentResultDetails = null!;
    private TextBlock runStatus = null!;
    private TextBlock progressPrimary = null!;
    private TextBlock progressSecondary = null!;
    private ProgressBar progressBar = null!;
    private TextBox logBox = null!;
    private ComboBox gradientShardInput = null!;
    private ComboBox parallelismProfileInput = null!;
    private ComboBox inferenceModeInput = null!;
    private ComboBox autoTuneObjectiveInput = null!;
    private ComboBox governanceProfileInput = null!;
    private ComboBox transformerBackendInput = null!;
    private TextBlock transformerRuntimeStatus = null!;
    private CombatTransformerRuntimeProbe? transformerRuntimeProbe;
    private Button startButton = null!;
    private Button freshStartButton = null!;
    private Button cancelButton = null!;
    private Button continueButton = null!;
    private Button selectedCheckpointButton = null!;
    private Button openButton = null!;
    private ComboBox checkpointInput = null!;
    private ComboBox checkpointResumeModeInput = null!;
    private TextBlock checkpointDetails = null!;
    private TrainingDiagnosticsPanel diagnostics = null!;
    private string cachedJobPath = "";
    private long cachedJobLength = -1;
    private DateTime cachedJobLastWriteUtc = DateTime.MinValue;
    private CombatFoundationWorkerJob? cachedJob;
    private string presentedResultPath = "";
    private long presentedResultLength = -1;
    private DateTime presentedResultLastWriteUtc = DateTime.MinValue;
    private ControllerWorkerResultSummary? presentedResult;
    private bool completionNotificationArmed;

    public MainWindow(string[] args)
    {
        launchArguments = args ?? Array.Empty<string>();
        Title = "Aura 底模训练控制台";
        Width = 1120;
        Height = 820;
        MinWidth = 920;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TrainerTheme.Apply(this);
        Content = BuildUi();
        Loaded += async (_, _) =>
        {
            parametersScroll.ScrollToTop();
            await RefreshTransformerRuntimeStatusAsync();
            RefreshCheckpointCatalog();
        };
        LoadSettings();
        ApplySettingsToUi();
        ValidateEnvironment();
        TryAttachLastSession();
        timer = new DispatcherTimer
        {
            Interval = RunningRefreshInterval
        };
        timer.Tick += (_, _) => RefreshRunState();
        timer.Start();
        Closing += (_, _) =>
        {
            timer.Stop();
            PullSettingsFromUi();
            SaveSettings();
        };
    }

    private UIElement BuildUi()
    {
        var root = new DockPanel();
        var header = new Border
        {
            Background = TrainerTheme.Header,
            BorderBrush = TrainerTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 18, 24, 16)
        };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Aura 外部底模训练器",
            FontSize = 23,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrainerTheme.Text
        });
        heading.Children.Add(new TextBlock
        {
            Text = "独立训练 · 模拟校准 · 受控验收",
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = TrainerTheme.Muted
        });
        header.Child = heading;
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        tabs = new TabControl
        {
            Margin = new Thickness(20, 16, 20, 20)
        };
        tabs.Items.Add(new TabItem
        {
            Header = "训练配置",
            Content = TrainerTheme.ContentSurface(BuildParametersTab())
        });
        tabs.Items.Add(new TabItem
        {
            Header = "运行监控",
            Content = TrainerTheme.ContentSurface(BuildProgressTab())
        });
        diagnostics = new TrainingDiagnosticsPanel();
        tabs.Items.Add(new TabItem
        {
            Header = "训练诊断",
            Content = TrainerTheme.ContentSurface(diagnostics.View)
        });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildParametersTab()
    {
        parametersScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = TrainerTheme.Window
        };
        var panel = new StackPanel
        {
            Margin = new Thickness(2, 0, 12, 0),
            MaxWidth = 920,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        parametersScroll.Content = panel;

        panel.Children.Add(Section("运行环境"));
        modRootInput = AddPathRow(panel, "MOD 目录", BrowseModRoot);
        dataRootInput = AddPathRow(panel, "ModsData 目录", BrowseDataRoot);
        environmentStatus = Hint(panel, "");
        Hint(
            panel,
            "默认按控制台 EXE 的相对位置自动定位；“选择”仅用于本次运行的临时覆盖。");

        panel.Children.Add(Section("最近一次训练"));
        var recentResultPanel = new StackPanel();
        recentResultStatus = new TextBlock
        {
            Text = "暂无训练结果",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrainerTheme.Muted,
            TextWrapping = TextWrapping.Wrap
        };
        recentResultDetails = new TextBlock
        {
            Text = "训练结束后，这里会持续显示验收状态和胜场摘要。",
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = TrainerTheme.Muted,
            TextWrapping = TextWrapping.Wrap
        };
        recentResultPanel.Children.Add(recentResultStatus);
        recentResultPanel.Children.Add(recentResultDetails);
        panel.Children.Add(new Border
        {
            Background = TrainerTheme.Surface,
            BorderBrush = TrainerTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12, 14, 12),
            Child = recentResultPanel
        });

        BuildGameSubjectSection(panel);

        panel.Children.Add(Section("工作量与性能"));
        AddGovernanceProfileSelect(panel);
        AddProfileSelect(panel);
        AddNumber(
            panel,
            "AdditionalIterationsOnResume",
            "恢复后追加轮数",
            0,
            20);
        AddNumber(panel, "Iterations", "训练轮数", 1, 20);
        AddNumber(panel, "TrainingCampaignsPerIteration", "每轮训练冒险", 2, 1000);
        AddNumber(panel, "PreflightCampaignsPerDifficulty", "预检冒险/难度", 1, 100);
        AddNumber(panel, "ArenaCampaignsPerDifficulty", "竞技场/难度", 1, 100);
        AddNumber(
            panel,
            "ArenaConfirmationCampaignsPerDifficulty",
            "确认竞技场/难度",
            0,
            200);
        AddNumber(panel, "NormalValidationCampaigns", "普通隔离验证", 10, 1000);
        AddNumber(panel, "AdvancedValidationCampaigns", "高级隔离验证", 10, 1000);
        AddNumber(panel, "ValidationEarlyStopBatchSize", "验证检查批次", 1, 128);
        AddNumber(
            panel,
            "CapabilityProbeCampaignsPerDifficulty",
            "能力探针/难度",
            0,
            128);
        AddNumber(
            panel,
            "CapabilityProbeTeacherCampaignsPerDifficulty",
            "教师诊断探针/难度",
            0,
            128);
        AddNumber(panel, "CapabilityProbeBatchSize", "能力探针检查批次", 1, 128);
        AddToggle(
            panel,
            "RequireCapabilityProbeBaselineGain",
            "能力探针要求超过规则基线");
        AddNumber(
            panel,
            "CapabilityProbeMinimumVictoryGain",
            "能力探针最少胜场增益",
            1,
            64);
        AddDouble(
            panel,
            "CapabilityProbeMinimumDepthGain",
            "能力探针深度诊断阈值（不用于晋级）");
        AddNumber(panel, "MaximumDegreeOfParallelism", "CPU 并行度", 1, 64);

        panel.Children.Add(Section("模型训练"));
        AddNumber(panel, "ModelEpochs", "最大 Epoch", 5, 200);
        AddNumber(panel, "ModelMinimumEpochs", "最小 Epoch", 1, 200);
        AddNumber(panel, "ModelEarlyStoppingPatience", "早停耐心", 1, 30);
        AddDouble(panel, "ModelEarlyStoppingMinimumDelta", "早停最小增益");
        AddNumber(panel, "ModelBatchSize", "Minibatch", 8, 512);
        AddExecutionProfileSelect(panel);
        AddInferenceModeSelect(panel);
        AddNumber(panel, "InferenceParallelism", "推理并行上下文", 0, 64);
        AddNumber(panel, "InferenceLaneCount", "推理 Lane 数（0 自动）", 0, 64);
        AddNumber(panel, "InferenceBatchSize", "推理批大小（0 自动）", 0, 32);
        AddNumber(
            panel,
            "ThreadPoolMinimumWorkerThreads",
            "线程池最低工作线程",
            0,
            256);
        AddNumber(
            panel,
            "CheckpointSerializationParallelism",
            "Checkpoint 序列化线程",
            0,
            2);
        AddToggle(panel, "ReuseAutoTuneCache", "复用 Auto-Tune 测量缓存");
        AddAutoTuneObjectiveSelect(panel);
        AddNumber(
            panel,
            "AutoTuneSampleCampaigns",
            "Auto-Tune 样本战役",
            4,
            64);
        AddDouble(
            panel,
            "AutoTuneThroughputTolerance",
            "Auto-Tune 吞吐容差");
        AddGradientShardSelect(panel);
        AddNumber(panel, "MinimumEpisodes", "最少训练 Episodes", 2, 1000);
        AddNumber(panel, "ModelReplayEpisodeLimit", "Replay 上限", 64, 20000);
        AddNumber(panel, "ModelRetainedCandidates", "Top-K 候选", 1, 5);
        AddToggle(panel, "EnableFrameStratification", "启用帧分层再平衡");
        AddDouble(panel, "ModelMaximumFrameStratumWeight", "帧分层最大权重");
        AddToggle(panel, "EnableEndTurnSpecialization", "启用结束回合专项训练");
        AddDouble(panel, "ModelEndTurnFrameWeight", "结束回合帧权重");
        AddDouble(
            panel,
            "ModelMaximumUnsafeEndTurnFrameShare",
            "不安全结束回合最大占比");
        AddDouble(
            panel,
            "ModelUnsafeEndTurnRiskAuxiliaryShare",
            "结束回合风险辅助占比");
        AddNumber(
            panel,
            "ModelMinimumValidationRunGroups",
            "最少验证运行组",
            1,
            256);
        AddNumber(
            panel,
            "ModelMinimumTestRunGroups",
            "最少测试运行组",
            1,
            256);
        AddDouble(panel, "ModelPolicyTargetTemperature", "策略目标温度");
        AddDouble(
            panel,
            "ModelMaximumPolicyTargetProbability",
            "策略目标概率上限");
        AddDouble(panel, "ModelLearningRate", "学习率");
        AddNumber(panel, "ModelMaximumFramesPerEpisode", "Frames per episode", 8, 512);
        AddDouble(panel, "ModelL2", "L2");
        AddNumber(panel, "ModelStateDimensions", "状态维度", 16, 2048);
        AddNumber(panel, "ModelActionDimensions", "动作维度", 16, 2048);
        AddNumber(panel, "ModelHiddenDimensions", "隐藏维度", 8, 1024);

        panel.Children.Add(Section("Transformer 教师蒸馏"));
        AddTransformerBackendSelect(panel);
        AddTransformerPythonRow(panel);
        transformerRuntimeStatus = Hint(panel, "等待检测 Python/PyTorch 运行时……");
        AddNumber(panel, "TransformerTeacherEpochs", "教师 Epoch", 1, 100);
        AddNumber(panel, "TransformerTeacherBatchSize", "教师 Minibatch", 8, 512);
        AddNumber(panel, "TransformerTeacherStateDimensions", "教师状态维度", 32, 2048);
        AddNumber(panel, "TransformerTeacherActionDimensions", "教师动作维度", 32, 2048);
        AddNumber(panel, "TransformerTeacherHiddenDimensions", "教师隐藏维度", 32, 512);
        AddNumber(panel, "TransformerTeacherLayers", "Transformer 层数", 1, 6);
        AddNumber(panel, "TransformerTeacherAttentionHeads", "注意力头数", 1, 16);
        AddNumber(panel, "TransformerTeacherFeedForwardDimensions", "前馈网络维度", 32, 4096);
        AddNumber(panel, "TransformerTeacherHistoryLength", "历史决策窗口", 1, 32);
        AddNumber(panel, "TransformerTeacherMinimumFrames", "教师最少 Frames", 64, 100000);
        AddNumber(panel, "TransformerTeacherMaximumFrames", "教师最多 Frames", 64, 100000);
        AddToggle(
            panel,
            "TransformerTeacherEnableWarmStart",
            "复用上一轮 Transformer 权重");
        AddNumber(
            panel,
            "TransformerTeacherCpuRefreshInterval",
            "CPU 教师刷新间隔",
            1,
            8);
        AddNumber(panel, "TransformerTeacherCpuEpochs", "CPU 冷启动 Epoch", 1, 100);
        AddNumber(
            panel,
            "TransformerTeacherCpuIncrementalEpochs",
            "CPU 增量 Epoch",
            1,
            100);
        AddNumber(panel, "TransformerTeacherCpuFinalEpochs", "CPU 最终 Epoch", 1, 100);
        AddToggle(
            panel,
            "TransformerTeacherEnableAdaptiveRefresh",
            "按数据漂移刷新教师");
        AddDouble(
            panel,
            "TransformerTeacherAdaptiveRefreshDriftThreshold",
            "教师数据漂移阈值");
        AddToggle(
            panel,
            "TransformerTeacherEnableFixedAnchorValidation",
            "启用固定锚点验证");
        AddDouble(
            panel,
            "TransformerTeacherMaximumHeadRegression",
            "教师单头最大回退");
        AddNumber(
            panel,
            "TransformerTeacherIncrementalEpochs",
            "教师增量 Epoch",
            1,
            100);
        AddNumber(
            panel,
            "TransformerTeacherFinalEpochs",
            "教师最终 Epoch",
            1,
            100);
        AddNumber(panel, "TransformerTeacherCpuThreads", "教师 CPU 线程（0 自动）", 0, 64);
        AddNumber(
            panel,
            "TransformerTeacherCpuInteropThreads",
            "教师 Interop 线程（0 自动）",
            0,
            8);
        AddNumber(
            panel,
            "TransformerTeacherMicroBatchSize",
            "教师设备微批次（0 自动）",
            0,
            512);
        AddNumber(
            panel,
            "TransformerTeacherDataLoaderWorkers",
            "教师数据加载进程（0 自动）",
            0,
            8);
        AddNumber(
            panel,
            "TransformerTeacherPrefetchBatches",
            "教师预取批次",
            1,
            8);
        AddToggle(
            panel,
            "TransformerTeacherEnablePinnedMemory",
            "GPU 使用锁页内存");
        AddToggle(
            panel,
            "TransformerTeacherEnableMixedPrecision",
            "GPU 使用自动混合精度");
        AddDouble(panel, "TransformerDistillationWeight", "教师蒸馏权重");

        panel.Children.Add(Section("课程、探索与验收"));
        AddToggle(panel, "EnableCurriculum", "启用课程难度");
        AddToggle(panel, "EnableStratifiedReplay", "启用分层回放");
        AddToggle(panel, "EnablePrioritizedReplay", "启用优先级回放");
        AddToggle(panel, "EnableHardSeedCurriculum", "启用困难种子课程");
        AddToggle(
            panel,
            "EnableCounterfactualHardEncounters",
            "启用困难遭遇反事实教师");
        AddToggle(panel, "EnableSuccessCaseArchive", "启用成功案例库");
        AddToggle(panel, "EnableArenaRecovery", "启用竞技场恢复");
        AddToggle(panel, "EnableTuningArena", "启用 Top-K 调优竞技场");
        AddToggle(panel, "EnableProgressiveTuning", "启用渐进式调优筛选");
        AddToggle(panel, "EnableOfflineTuningGate", "启用离线支配筛选");
        AddToggle(panel, "EnableSequentialArenaStop", "启用竞技场提前拒绝");
        AddToggle(panel, "EnableEarlyValidationStop", "启用验证提前停止");
        AddNumber(panel, "ArenaInvalidRetryCount", "无效竞技场重试", 0, 3);
        AddDouble(panel, "ArenaInvalidRateLimit", "无效竞技场率上限");
        AddNumber(panel, "TuningNormalCampaigns", "普通调优冒险", 0, 64);
        AddNumber(panel, "TuningAdvancedCampaigns", "高级调优冒险", 0, 64);
        AddNumber(
            panel,
            "TuningScreeningNormalCampaigns",
            "普通调优初筛冒险",
            0,
            64);
        AddNumber(
            panel,
            "TuningScreeningAdvancedCampaigns",
            "高级调优初筛冒险",
            0,
            64);
        AddNumber(panel, "TuningFinalistCount", "调优决选模型数", 1, 8);
        AddNumber(panel, "TuningInterval", "调优间隔轮数", 1, 8);
        AddNumber(panel, "ArenaEvaluationBatchSize", "竞技场检查批次", 1, 64);
        AddNumber(
            panel,
            "MaximumConsecutiveRejectedIterations",
            "连续拒绝停止阈值",
            0,
            8);
        AddDouble(panel, "NormalAcceptanceRate", "普通验收率");
        AddDouble(panel, "AdvancedAcceptanceRate", "高级验收率");
        AddNumber(panel, "MinimumArenaDiscordantPairs", "竞技场最少分歧对", 1, 128);
        AddDouble(panel, "MaximumOfflineHeadRegression", "离线单头最大回退");
        AddDouble(panel, "MaximumStateFeatureCollisionRate", "状态特征最大碰撞率");
        AddDouble(panel, "MaximumActionFeatureCollisionRate", "动作特征最大碰撞率");
        AddDouble(panel, "SuccessExpertReplayShare", "成功教师回放占比");
        AddDouble(panel, "HardSeedReplayShare", "困难种子占比");
        AddDouble(
            panel,
            "MinimumAdvancedReplayShare",
            "高级回放最低占比");
        AddDouble(
            panel,
            "MinimumAdvancedDefeatReplayShare",
            "高级失败回放最低占比");
        AddDouble(panel, "SelfPlayExplorationProbability", "自博弈探索率");
        AddDouble(panel, "SelfPlayExplorationTemperature", "探索温度");

        panel.Children.Add(Section("复现种子"));
        AddUlong(panel, "RunSeed", "RunSeed（0 自动生成）");
        AddUlong(panel, "TrainingSeedStart", "训练种子起点");
        AddUlong(panel, "ArenaSeedStart", "竞技场种子起点");
        AddUlong(panel, "TuningSeedStart", "调优种子起点");
        AddUlong(panel, "ValidationSeedStart", "验证种子起点");

        panel.Children.Add(Section("检查点续训"));
        var checkpointRow = NewRow();
        checkpointRow.Children.Add(Label("检查点", 100));
        checkpointInput = new ComboBox
        {
            Width = 540,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            DisplayMemberPath = nameof(ControllerCheckpointChoice.Label)
        };
        checkpointInput.SelectionChanged += (_, _) =>
            UpdateCheckpointDetails();
        checkpointRow.Children.Add(checkpointInput);
        checkpointRow.Children.Add(ActionButton("刷新", RefreshCheckpointCatalog));
        panel.Children.Add(checkpointRow);
        var checkpointModeRow = NewRow();
        checkpointModeRow.Children.Add(Label("恢复方式", 100));
        checkpointResumeModeInput = new ComboBox
        {
            Width = 260,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            DisplayMemberPath = nameof(ControllerResumeModeChoice.Label),
            SelectedValuePath = nameof(ControllerResumeModeChoice.Id),
            ItemsSource = new[]
            {
                new ControllerResumeModeChoice
                {
                    Id = CombatFoundationCheckpointResumeModes.Exact,
                    Label = "精确续训（保留优化器与 epoch）"
                },
                new ControllerResumeModeChoice
                {
                    Id = CombatFoundationCheckpointResumeModes.ModelBranch,
                    Label = "模型分支（重置优化器与 epoch）"
                }
            },
            SelectedIndex = 0
        };
        checkpointModeRow.Children.Add(checkpointResumeModeInput);
        selectedCheckpointButton = ActionButton(
            "从所选检查点继续",
            StartSelectedCheckpointTraining,
            TrainerButtonTone.Primary);
        checkpointModeRow.Children.Add(selectedCheckpointButton);
        panel.Children.Add(checkpointModeRow);
        checkpointDetails = Hint(panel, "尚未发现可选检查点");

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 20)
        };
        startButton = ActionButton(
            "恢复兼容训练",
            StartTraining,
            TrainerButtonTone.Primary);
        freshStartButton = ActionButton("重新开始", FreshStartTraining);
        continueButton = ActionButton(
            "以上轮 Champion 开新一代",
            ContinueTraining);
        cancelButton = ActionButton(
            "安全取消",
            CancelTraining,
            TrainerButtonTone.Danger);
        openButton = ActionButton("打开运行目录", OpenRunDirectory);
        actions.Children.Add(startButton);
        actions.Children.Add(freshStartButton);
        actions.Children.Add(continueButton);
        actions.Children.Add(cancelButton);
        actions.Children.Add(openButton);
        panel.Children.Add(actions);
        return parametersScroll;
    }

    private void BuildGameSubjectSection(Panel panel)
    {
        panel.Children.Add(Section("游戏主体"));
        var presetRow = NewRow();
        presetRow.Children.Add(Label("预设 ID", 100));
        gamePresetIdInput = Input(190);
        presetRow.Children.Add(gamePresetIdInput);
        presetRow.Children.Add(Label("显示名称", 88));
        gamePresetNameInput = Input(220);
        presetRow.Children.Add(gamePresetNameInput);
        panel.Children.Add(presetRow);

        var identityRow = NewRow();
        identityRow.Children.Add(Label("角色", 100));
        roleInput = ChoiceInput(260);
        identityRow.Children.Add(roleInput);
        identityRow.Children.Add(Label("使魔", 88));
        familiarInput = ChoiceInput(260);
        identityRow.Children.Add(familiarInput);
        panel.Children.Add(identityRow);

        var deckRow = NewRow();
        deckRow.Children.Add(Label("牌组倾向下限", 100));
        preferredDeckMinimumInput = Input(110);
        preferredDeckMinimumInput.ToolTip = "范围 1–80";
        deckRow.Children.Add(preferredDeckMinimumInput);
        deckRow.Children.Add(Label("牌组倾向上限", 120));
        preferredDeckMaximumInput = Input(110);
        preferredDeckMaximumInput.ToolTip = "范围 1–80";
        deckRow.Children.Add(preferredDeckMaximumInput);
        panel.Children.Add(deckRow);

        panel.Children.Add(Label("开启奖励卡包", 240));
        cardPackPanel = new WrapPanel
        {
            Margin = new Thickness(0, 4, 0, 4),
            MaxWidth = 900
        };
        panel.Children.Add(cardPackPanel);
        gameSubjectStatus = Hint(panel, "");
        Hint(
            panel,
            "完整预设会冻结角色技能与初始状态、使魔固有祝福、奖励卡包和牌组倾向；训练与验证使用同一份快照。");
    }

    private UIElement BuildProgressTab()
    {
        var panel = new Grid
        {
            Margin = new Thickness(2, 0, 2, 0),
            Background = TrainerTheme.Window
        };
        for (var i = 0; i < 5; i++)
        {
            panel.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
        }
        panel.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        runStatus = new TextBlock
        {
            Text = "尚未开始训练",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrainerTheme.Text,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(runStatus, 0);
        panel.Children.Add(runStatus);
        progressPrimary = ProgressText();
        Grid.SetRow(progressPrimary, 1);
        panel.Children.Add(progressPrimary);
        progressSecondary = ProgressText();
        Grid.SetRow(progressSecondary, 2);
        panel.Children.Add(progressSecondary);
        progressBar = new ProgressBar
        {
            Height = 18,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 12, 0, 12)
        };
        Grid.SetRow(progressBar, 3);
        panel.Children.Add(progressBar);
        var logTitle = new TextBlock
        {
            Text = "运行信息",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrainerTheme.Accent,
            Margin = new Thickness(0, 12, 0, 6)
        };
        Grid.SetRow(logTitle, 4);
        panel.Children.Add(logTitle);
        logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 220,
            Background = TrainerTheme.Input,
            Foreground = TrainerTheme.Text,
            BorderBrush = TrainerTheme.Border
        };
        Grid.SetRow(logBox, 5);
        panel.Children.Add(logBox);
        return panel;
    }

    private void RefreshCheckpointCatalog()
    {
        try
        {
            var path = ResolveCheckpointCatalogPath();
            var catalog = !string.IsNullOrWhiteSpace(path)
                          && CombatFoundationPathRuntime.FileExists(path)
                ? Deserialize<CombatFoundationCheckpointCatalog>(
                    CombatFoundationCheckpointStorage.ReadAllTextShared(path))
                : null;
            var choices = (catalog?.Entries
                           ?? new List<CombatFoundationCheckpointCatalogEntry>())
                .Where(item => item != null
                               && CombatFoundationPathRuntime.FileExists(
                                   item.CheckpointPath)
                               && CombatFoundationPathRuntime.FileExists(
                                   item.EpisodeSnapshotPath))
                .OrderByDescending(item => item.Recommended)
                .ThenByDescending(item => item.CreatedUtc)
                .Select(item => new ControllerCheckpointChoice
                {
                    Entry = item,
                    Label = (item.Recommended ? "推荐 · " : "")
                            + item.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm")
                            + " · 迭代 " + item.NextIteration
                            + " · val-best "
                            + (item.BestValidationEpoch > 0
                                ? item.BestValidationEpoch
                                : item.BestEpoch)
                            + " · deploy "
                            + (item.DeploymentSelectedEpoch > 0
                                ? item.DeploymentSelectedEpoch
                                : item.BestEpoch)
                            + " · val " + item.ValidationLoss.ToString("0.0000")
                })
                .ToList();
            checkpointInput.ItemsSource = choices;
            checkpointInput.SelectedIndex = choices.Count == 0 ? -1 : 0;
            selectedCheckpointButton.IsEnabled = choices.Count > 0
                                                 && !IsWorkerRunning();
            UpdateCheckpointDetails();
        }
        catch (Exception ex)
        {
            checkpointInput.ItemsSource = null;
            selectedCheckpointButton.IsEnabled = false;
            checkpointDetails.Text = "检查点目录读取失败：" + ex.Message;
        }
    }

    private string ResolveCheckpointCatalogPath()
    {
        if (string.IsNullOrWhiteSpace(settings.LastRunDirectory))
        {
            return "";
        }
        var priorJobPath = Path.Combine(
            settings.LastRunDirectory,
            "foundation-worker-job.json");
        if (!CombatFoundationPathRuntime.FileExists(priorJobPath))
        {
            return "";
        }
        var prior = Deserialize<CombatFoundationWorkerJob>(
            CombatFoundationCheckpointStorage.ReadAllTextShared(priorJobPath));
        if (!string.IsNullOrWhiteSpace(prior?.CheckpointCatalogPath))
        {
            return prior.CheckpointCatalogPath;
        }
        return string.IsNullOrWhiteSpace(prior?.CheckpointPath)
            ? ""
            : Path.Combine(
                Path.GetDirectoryName(prior.CheckpointPath)!,
                CombatFoundationCheckpointCatalogProtocol.CatalogFileName);
    }

    private void UpdateCheckpointDetails()
    {
        if (checkpointInput.SelectedItem is not ControllerCheckpointChoice choice)
        {
            checkpointDetails.Text = "尚未发现可选检查点";
            return;
        }
        var item = choice.Entry;
        var anchor = item.SelectionAnchorMetrics?.FrameCount > 0
            ? item.SelectionAnchorMetrics.CompositeLoss.ToString("0.0000")
            : "尚无固定锚点结果";
        checkpointDetails.Text =
            "训练 " + item.TrainingLoss.ToString("0.0000")
            + " · 验证 " + item.ValidationLoss.ToString("0.0000")
            + " · 泛化差 " + item.GeneralizationGap.ToString("+0.0000;-0.0000;0.0000")
            + " · 固定锚点 " + anchor
            + " · " + CheckpointRiskLabel(item.Risk)
            + " · " + (item.QualityGatesPassed ? "质量门禁通过" : "质量门禁未完整通过");
    }

    private static string CheckpointRiskLabel(string risk)
    {
        return risk switch
        {
            "overfit" => "过拟合风险",
            "underfit" => "欠拟合风险",
            "balanced" => "拟合状态平衡",
            _ => "拟合证据不足"
        };
    }

    private void StartSelectedCheckpointTraining()
    {
        try
        {
            if (checkpointInput.SelectedItem is not ControllerCheckpointChoice choice)
            {
                throw new InvalidOperationException("请先选择一个检查点");
            }
            PullSettingsFromUi();
            ValidateEnvironment(throwOnFailure: true);
            var mode = Convert.ToString(
                           checkpointResumeModeInput.SelectedValue,
                           CultureInfo.InvariantCulture)
                       ?? CombatFoundationCheckpointResumeModes.Exact;
            StartWorker(
                initialChampion: null,
                continueGeneration: false,
                resumeFromCheckpoint: true,
                requireCompatibleResume: true,
                resetCheckpointOnFreshStart: false,
                requestedStartMode: "selected-" + mode,
                resumeCheckpointPath: choice.Entry.CheckpointPath,
                resumeMode: mode);
        }
        catch (Exception ex)
        {
            AppendLog("无法从所选检查点启动：" + ex.Message);
            MessageBox.Show(
                this,
                ex.Message,
                "无法从检查点启动",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void StartTraining()
    {
        try
        {
            PullSettingsFromUi();
            ValidateEnvironment(throwOnFailure: true);
            if (!ConfirmTrainingConfigurationChange())
            {
                return;
            }
            StartWorker(
                initialChampion: null,
                continueGeneration: false,
                resumeFromCheckpoint: true,
                requireCompatibleResume: true,
                resetCheckpointOnFreshStart: false,
                requestedStartMode: "resume-required");
        }
        catch (Exception ex)
        {
            AppendLog("无法启动训练：" + ex.Message);
            MessageBox.Show(this, ex.Message, "无法启动训练", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool ConfirmTrainingConfigurationChange()
    {
        if (string.IsNullOrWhiteSpace(settings.LastRunDirectory))
        {
            return true;
        }
        var resultPath = Path.Combine(
            settings.LastRunDirectory,
            "foundation-worker-result.json");
        var jobPath = Path.Combine(
            settings.LastRunDirectory,
            "foundation-worker-job.json");
        if (!File.Exists(resultPath) || !File.Exists(jobPath))
        {
            return true;
        }
        var result = Deserialize<CombatFoundationWorkerResult>(
            CombatFoundationCheckpointStorage.ReadAllTextShared(resultPath));
        if (result?.Resumable != true)
        {
            return true;
        }
        var prior = Deserialize<CombatFoundationWorkerJob>(
            CombatFoundationCheckpointStorage.ReadAllTextShared(jobPath));
        if (prior?.Request == null)
        {
            return true;
        }

        var current = settings.Parameters;
        var differences = new List<string>();
        var priorCampaign = prior.Request.TrainingCampaign;
        var currentGameParameterHash =
            CombatGameSubjectPresetRuntime.ComputeHash(
                settings.GameSubject,
                priorCampaign.Player?.Deck);
        AddDifference(
            differences,
            "游戏主体",
            priorCampaign.Player?.GameParameterHash ?? "",
            currentGameParameterHash);
        AddDifference(
            differences,
            "决策档位",
            prior.Request.DecisionProfile,
            current.DecisionProfile);
        AddDifference(
            differences,
            "学习率",
            prior.Request.Training.LearningRate,
            current.ModelLearningRate);
        AddDifference(
            differences,
            "高级调优样本",
            prior.Request.TuningAdvancedCampaigns,
            current.TuningAdvancedCampaigns);
        AddDifference(
            differences,
            "困难种子占比",
            prior.Request.HardSeedReplayShare,
            current.HardSeedReplayShare);
        AddDifference(
            differences,
            "普通验收线",
            prior.Request.NormalAcceptanceRate,
            current.NormalAcceptanceRate);
        AddDifference(
            differences,
            "高级验收线",
            prior.Request.AdvancedAcceptanceRate,
            current.AdvancedAcceptanceRate);
        AddDifference(
            differences,
            "验证检查批次",
            prior.Request.ValidationEarlyStopBatchSize,
            current.ValidationEarlyStopBatchSize);
        var priorWeights = SerializeOrdered(prior.Request.HardEncounterWeights);
        var currentWeights = SerializeOrdered(current.HardEncounterWeights);
        AddDifference(
            differences,
            "困难遭遇分布",
            priorWeights,
            currentWeights);
        if (differences.Count == 0)
        {
            return true;
        }

        MessageBox.Show(
            this,
            "检测到上一轮可恢复任务与当前训练参数不同，不能按恢复模式启动。"
            + Environment.NewLine
            + Environment.NewLine
            + string.Join(Environment.NewLine, differences.Take(8))
            + Environment.NewLine
            + Environment.NewLine
            + "如需采用当前参数，请使用“重新开始”。",
            "没有兼容的恢复任务",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private void FreshStartTraining()
    {
        try
        {
            PullSettingsFromUi();
            ValidateEnvironment(throwOnFailure: true);
            var confirmed = MessageBox.Show(
                this,
                "将从随机初始化开始，并替换当前游戏主体的活动检查点。"
                + Environment.NewLine
                + "历史运行结果、累计教师语料和成功案例库不会删除。"
                + Environment.NewLine
                + Environment.NewLine
                + "是否继续？",
                "确认重新开始",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmed != MessageBoxResult.Yes)
            {
                return;
            }
            StartWorker(
                initialChampion: null,
                continueGeneration: false,
                resumeFromCheckpoint: false,
                requireCompatibleResume: false,
                resetCheckpointOnFreshStart: true,
                requestedStartMode: "fresh");
        }
        catch (Exception ex)
        {
            AppendLog("无法重新开始训练：" + ex.Message);
            MessageBox.Show(
                this,
                ex.Message,
                "无法重新开始训练",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void AddDifference<T>(
        ICollection<string> destination,
        string label,
        T prior,
        T current)
    {
        if (EqualityComparer<T>.Default.Equals(prior, current))
        {
            return;
        }
        destination.Add(label + "：" + prior + " -> " + current);
    }

    private static string SerializeOrdered(
        IReadOnlyDictionary<string, double>? values)
    {
        return string.Join(
            ", ",
            (values ?? new Dictionary<string, double>())
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item =>
                item.Key + "=" + item.Value.ToString("0.###")));
    }

    private void ContinueTraining()
    {
        try
        {
            PullSettingsFromUi();
            ValidateEnvironment(throwOnFailure: true);
            var resultPath = Path.Combine(
                settings.LastRunDirectory,
                "foundation-worker-result.json");
            if (!File.Exists(resultPath))
            {
                throw new InvalidOperationException("上一轮没有 Worker 结果");
            }
            var result = Deserialize<CombatFoundationWorkerResult>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    resultPath));
            var accepted = string.Equals(
                result?.CompletionKind,
                "training-accepted",
                StringComparison.Ordinal);
            var rejectedResumable = result?.Resumable == true
                                    && string.Equals(
                                        result.CompletionKind,
                                        "training-rejected-resumable",
                                        StringComparison.Ordinal);
            var champion = accepted
                ? result?.Training?.Champion
                : result?.Training?.WorkingChampion
                  ?? result?.Training?.Champion;
            if (champion == null || (!accepted && !rejectedResumable))
            {
                throw new InvalidOperationException(
                    "只有已通过验收的上一轮 Champion 才能作为新一轮起点");
            }
            var priorJobPath = Path.Combine(
                settings.LastRunDirectory,
                "foundation-worker-job.json");
            var priorJob = File.Exists(priorJobPath)
                ? Deserialize<CombatFoundationWorkerJob>(
                    CombatFoundationCheckpointStorage.ReadAllTextShared(
                        priorJobPath))
                : null;
            var priorCampaign = priorJob?.Request?.TrainingCampaign;
            if (priorCampaign == null
                || !string.Equals(
                    priorCampaign.Player?.GameParameterHash,
                    CombatGameSubjectPresetRuntime.ComputeHash(
                        settings.GameSubject,
                        priorCampaign.Player?.Deck),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "当前游戏主体与上一轮 Champion 不一致；请开始新的训练任务");
            }
            StartWorker(
                champion,
                continueGeneration: true,
                resumeFromCheckpoint: false,
                requireCompatibleResume: false,
                resetCheckpointOnFreshStart: true,
                requestedStartMode: "champion-seed");
        }
        catch (Exception ex)
        {
            AppendLog("无法继续迭代：" + ex.Message);
            MessageBox.Show(this, ex.Message, "无法继续迭代", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void StartWorker(
        CombatPolicyValueNetworkDefinition? initialChampion,
        bool continueGeneration,
        bool resumeFromCheckpoint,
        bool requireCompatibleResume,
        bool resetCheckpointOnFreshStart,
        string requestedStartMode,
        string resumeCheckpointPath = "",
        string resumeMode = CombatFoundationCheckpointResumeModes.Exact)
    {
        if (IsWorkerRunning())
        {
            throw new InvalidOperationException("已有训练进程正在运行");
        }
        var workerPath = Path.Combine(
            settings.ModRoot,
            "TrainingWorker",
            "AuraFoundationTrainer.Worker.exe");
        var campaignPath = CampaignPath(settings.ModRoot);
        var rulesetPath = Path.Combine(
            settings.ModRoot,
            "Config",
            "combat-simulation",
            "witch-base-evaluation-v2.ruleset.json");
        var trainingCampaign = Deserialize<CombatCampaignDefinition>(
                                   CombatFoundationCheckpointStorage
                                       .ReadAllTextShared(campaignPath))
                               ?? throw new InvalidOperationException("无法克隆训练战役");
        trainingCampaign.TraceLevel = CombatSimulationTraceLevel.Summary;
        trainingCampaign.RequireAuthoritativeRules = true;
        CombatGameSubjectPresetRuntime.Apply(
            settings.GameSubject,
            trainingCampaign);
        AuraToolsRoleCampaignStrategy.Apply(trainingCampaign);
        var validationCampaign = Deserialize<CombatCampaignDefinition>(
                                     CombatFoundationCheckpointStorage
                                         .ReadAllTextShared(campaignPath))
                                 ?? throw new InvalidOperationException("无法克隆验证战役");
        validationCampaign.TraceLevel = CombatSimulationTraceLevel.Full;
        validationCampaign.FullTraceFinalEncounterOnly = true;
        validationCampaign.RequireAuthoritativeRules = true;
        CombatGameSubjectPresetRuntime.Apply(
            settings.GameSubject,
            validationCampaign);
        AuraToolsRoleCampaignStrategy.Apply(validationCampaign);
        var rulesetDocument = Deserialize<CombatRulesetDocument>(
                                  CombatFoundationCheckpointStorage
                                      .ReadAllTextShared(rulesetPath))
                              ?? throw new InvalidOperationException("无法读取规则集");
        var rulesetBuild = CombatSimulationRegistry.BuildRuleset(rulesetDocument);
        if (!rulesetBuild.Success)
        {
            throw new InvalidOperationException(
                "规则集构建失败：" + string.Join("；", rulesetBuild.Errors.Take(5)));
        }
        var packageAudit = AuraToolsNativeProgramPackageAudit.Validate(
            trainingCampaign,
            rulesetBuild.Ruleset);
        if (!packageAudit.Success)
        {
            throw new InvalidOperationException(
                "权威程序包校验失败："
                + string.Join("；", packageAudit.Errors.Take(5)));
        }

        var parameters = settings.Parameters.Normalized();
        if (parameters.RunSeed == 0UL || continueGeneration)
        {
            parameters.RunSeed = GenerateRunSeed();
        }
        if (continueGeneration)
        {
            settings.ContinueGeneration++;
            var offset = checked((ulong)settings.ContinueGeneration * 10_000_000UL);
            parameters.TrainingSeedStart = checked(10_000UL + offset);
            parameters.ArenaSeedStart = checked(1_000_000UL + offset);
            parameters.TuningSeedStart = checked(1_500_000UL + offset);
            parameters.ValidationSeedStart = checked(2_000_000UL + offset);
            ApplySettingsToUi();
        }
        var jobId = "foundation-controller-"
                    + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var resultsRoot = TrainingResultsRoot(settings.DataRoot);
        var resultDirectory = Path.Combine(resultsRoot, jobId);
        Directory.CreateDirectory(resultDirectory);
        var checkpointRoot = Path.Combine(
            resultsRoot,
            "foundation-controller-checkpoint",
            trainingCampaign.Player.GameParameterHash);
        Directory.CreateDirectory(checkpointRoot);
        var profile = BuildProfile(parameters.DecisionProfile);
        var job = CombatFoundationWorkerJobFactory.Create(
            new CombatFoundationWorkerJobBuildRequest
            {
                JobId = jobId,
                ResultDirectory = resultDirectory,
                SuccessArchiveDirectory = Path.Combine(
                    resultsRoot,
                    "foundation-success-cases"),
                CheckpointPath = Path.Combine(
                    checkpointRoot,
                    CombatFoundationWorkerProtocol.CheckpointFileName),
                CheckpointEpisodesPath = Path.Combine(
                    checkpointRoot,
                    CombatFoundationWorkerProtocol.CheckpointEpisodesFileName),
                ExpectedRulesetHash = rulesetBuild.Ruleset.RulesetHash,
                Parameters = parameters,
                Profile = profile,
                TrainingCampaign = trainingCampaign,
                ValidationCampaign = validationCampaign,
                Ruleset = rulesetDocument,
                InitialChampion = initialChampion
            });
        job.ResumeFromCheckpoint = resumeFromCheckpoint;
        job.RequireCompatibleResume = requireCompatibleResume;
        job.ResetCheckpointOnFreshStart = resetCheckpointOnFreshStart;
        job.RequestedStartMode = requestedStartMode;
        job.CheckpointCatalogPath = Path.Combine(
            checkpointRoot,
            CombatFoundationCheckpointCatalogProtocol.CatalogFileName);
        job.ModelSelectionAnchorPath = Path.Combine(
            checkpointRoot,
            CombatFoundationCheckpointCatalogProtocol.SelectionAnchorFileName);
        job.ResumeCheckpointPath = resumeCheckpointPath;
        job.ResumeMode = CombatFoundationCheckpointResumeModes.Normalize(
            resumeMode);
        var jobPath = Path.Combine(resultDirectory, "foundation-worker-job.json");
        WriteAtomic(jobPath, Serialize(job));
        TryDelete(job.CancellationPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = CombatFoundationPathRuntime.ForExternalProcess(workerPath),
            WorkingDirectory = CombatFoundationPathRuntime.ForFileSystem(
                Path.GetDirectoryName(workerPath)!),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--job");
        startInfo.ArgumentList.Add(
            CombatFoundationPathRuntime.ForExternalProcess(jobPath));
        workerProcess = Process.Start(startInfo)
                        ?? throw new InvalidOperationException("Worker 进程未能启动");
        session = new ControllerSession
        {
            JobId = jobId,
            JobPath = jobPath,
            ResultDirectory = resultDirectory,
            ProcessId = workerProcess.Id,
            StartedUtc = DateTime.UtcNow
        };
        ResetPollingCache();
        completionNotificationArmed = true;
        timer.Interval = RunningRefreshInterval;
        settings.LastRunDirectory = resultDirectory;
        SaveSession();
        SaveSettings();
        AppendLog(
            "训练已启动：模式="
            + requestedStartMode
            + "，"
            + jobId
            + "，主体="
            + trainingCampaign.Player.RoleId
            + "+"
            + trainingCampaign.Player.PartnerId
            + "，卡包="
            + trainingCampaign.EnabledRewardCardPackIds.Count
            + "，预计冒险 "
            + parameters.EstimatedCampaigns()
            + "，PID="
            + workerProcess.Id);
        recentResultStatus.Text = "训练运行中 · " + jobId;
        recentResultStatus.Foreground = TrainerTheme.Accent;
        recentResultDetails.Text =
            "完成后将自动切换到运行监控页，并显示验收结果。";
        tabs.SelectedIndex = ProgressTabIndex;
        RefreshRunState();
    }

    private void CancelTraining()
    {
        try
        {
            var job = ReadCurrentJob();
            if (job == null || !IsWorkerRunning())
            {
                AppendLog("当前没有可取消的训练进程");
                return;
            }
            WriteAtomic(job.CancellationPath, DateTime.UtcNow.ToString("O"));
            AppendLog("已请求安全取消；Worker 将保存可恢复检查点");
        }
        catch (Exception ex)
        {
            AppendLog("取消请求失败：" + ex.Message);
        }
    }

    private void RefreshRunState()
    {
        if (session == null)
        {
            timer.Interval = IdleRefreshInterval;
            SetIdleButtons();
            return;
        }
        var job = ReadCurrentJob();
        if (job == null)
        {
            SetIdleButtons();
            return;
        }
        var running = IsWorkerRunning();
        timer.Interval = running
            ? RunningRefreshInterval
            : IdleRefreshInterval;
        startButton.IsEnabled = !running;
        freshStartButton.IsEnabled = !running;
        continueButton.IsEnabled = !running;
        selectedCheckpointButton.IsEnabled = !running
            && checkpointInput.Items.Count > 0;
        cancelButton.IsEnabled = running;
        openButton.IsEnabled = Directory.Exists(session.ResultDirectory);
        if (TryGetFileIdentity(
                job.ResultPath,
                out var resultLength,
                out var resultLastWriteUtc))
        {
            if (string.Equals(
                    presentedResultPath,
                    job.ResultPath,
                    StringComparison.OrdinalIgnoreCase)
                && presentedResultLength == resultLength
                && presentedResultLastWriteUtc == resultLastWriteUtc)
            {
                TryShowCompletionNotification(running);
                return;
            }
            try
            {
                var result = ReadResultSummaryStreaming(job.ResultPath);
                if (result != null)
                {
                    PresentResult(result);
                    presentedResult = result;
                    presentedResultPath = job.ResultPath;
                    presentedResultLength = resultLength;
                    presentedResultLastWriteUtc = resultLastWriteUtc;
                    TryShowCompletionNotification(running);
                    return;
                }
            }
            catch (IOException)
            {
            }
            catch (JsonException ex)
            {
                AppendLog("结果文件暂不可读：" + ex.Message);
            }
        }
        if (File.Exists(job.ProgressPath))
        {
            try
            {
                var progress = Deserialize<CombatFoundationWorkerProgress>(
                    CombatFoundationCheckpointStorage.ReadAllTextShared(
                        job.ProgressPath));
                if (progress?.Telemetry != null)
                {
                    PresentTelemetry(progress.Telemetry, running, progress);
                }
            }
            catch (IOException)
            {
                // Worker replaces the file atomically; the next tick retries.
            }
            catch (JsonException ex)
            {
                AppendLog("进度文件暂不可读：" + ex.Message);
            }
        }
    }

    private void PresentTelemetry(
        CombatCampaignFoundationTelemetry telemetry,
        bool running,
        CombatFoundationWorkerProgress progress)
    {
        var runIteration = telemetry.RunTotalIterations > 0
            ? Math.Max(0, telemetry.RunIteration)
            : Math.Max(0, telemetry.Iteration);
        var runIterations = telemetry.RunTotalIterations > 0
            ? telemetry.RunTotalIterations
            : Math.Max(1, telemetry.TotalIterations);
        var globalIterationText = telemetry.RunStartIteration > 1
            ? "（模型全局第 "
              + telemetry.Iteration
              + "/"
              + telemetry.TotalIterations
              + " 轮）"
            : "";
        runStatus.Text = (running ? "运行中 · " : "")
                         + FriendlyStage(telemetry.Stage)
                         + " · 本次第 "
                         + runIteration
                         + "/"
                         + runIterations
                         + " 轮"
                         + globalIterationText;
        runStatus.Foreground =
            running ? TrainerTheme.Accent : TrainerTheme.Text;
        var transformerTeacherPhase = string.Equals(
            telemetry.Phase,
            "transformer-teacher",
            StringComparison.Ordinal);
        var total = Math.Max(
            1,
            telemetry.RunRequestedCampaigns > 0
                ? telemetry.RunRequestedCampaigns
                : telemetry.RequestedCampaigns);
        var completed = telemetry.RunRequestedCampaigns > 0
            ? telemetry.RunCompletedCampaigns
            : telemetry.CompletedCampaigns;
        progressBar.Value = Math.Max(
            0,
            Math.Min(
                100,
                transformerTeacherPhase
                && telemetry.TransformerTeacherTotalFrames > 0
                    ? telemetry.TransformerTeacherCompletedFrames * 100d
                      / telemetry.TransformerTeacherTotalFrames
                    : completed * 100d / total));
        progressPrimary.Text = transformerTeacherPhase
            ? "教师阶段 "
              + FriendlyTeacherStage(telemetry.TransformerTeacherStage)
              + " · Frames "
              + telemetry.TransformerTeacherCompletedFrames
              + "/"
              + telemetry.TransformerTeacherTotalFrames
              + " · "
              + telemetry.TransformerTeacherMessage
            : $"本次冒险 {completed}/{total} · "
              + $"累计 {telemetry.CompletedCampaigns}/{telemetry.RequestedCampaigns} · "
              + $"本次战斗 {telemetry.RunCompletedBattles}"
              + $"/累计 {telemetry.CompletedBattles} · 深度 "
              + $"{telemetry.MaximumActiveBattleDepth}/{telemetry.MaximumCompletedBattleDepth}/37";
        var executionSummary =
            $"{telemetry.GovernanceProfile} · "
            + $"{telemetry.ParallelismProfile}/{telemetry.InferenceExecutionMode}";
        if (string.Equals(
                telemetry.ParallelismProfile,
                CombatFoundationExecutionProfileNames.Auto,
                StringComparison.Ordinal)
            && telemetry.AutoTune?.SelectedParallelism > 0)
        {
            executionSummary += " -> "
                                + telemetry.AutoTune.SelectedParallelism
                                + (telemetry.AutoTune.CacheHit
                                    ? " (cached)"
                                    : " (measured)");
        }
        progressSecondary.Text = transformerTeacherPhase
            ? executionSummary + " · "
              + $"Python CPU {telemetry.TransformerTeacherCpuPercent:0.0}% · "
              + $"CPU时 {telemetry.TransformerTeacherProcessCpuSeconds:0}s · "
              + "内存 "
              + FormatBytes(telemetry.TransformerTeacherWorkingSetBytes)
              + "/峰值 "
              + FormatBytes(telemetry.TransformerTeacherPeakWorkingSetBytes)
              + " · Epoch "
              + telemetry.TransformerTeacherEpoch
              + "/"
              + telemetry.TransformerTeacherTotalEpochs
              + " · "
              + $"{telemetry.TransformerTeacherFramesPerSecond:0.0} Frames/秒 · "
              + (telemetry.TransformerTeacherWarmStarted
                  ? telemetry.TransformerTeacherTrainingEnabled
                      ? "热启动增量训练"
                      : "复用权重标注"
                  : "全量初始化训练")
              + " · ETA "
              + FormatDuration(telemetry.EstimatedRemainingSeconds)
            : executionSummary + " · "
              + $"CPU {telemetry.CpuUtilizationPercent:0.0}% · "
              + $"分配 {telemetry.AllocationMegabytesPerSecond:0} MB/s · "
              + $"Epoch {telemetry.ModelEpoch}/{telemetry.ModelTotalEpochs} · "
              + $"训练损失 {FormatLoss(telemetry.ModelTrainingLoss)} · "
              + $"验证损失 {FormatLoss(telemetry.ModelValidationLoss)} · "
              + $"最佳 {FormatLoss(telemetry.ModelBestValidationLoss)} · "
              + $"并行 {telemetry.ActiveCampaigns}/{telemetry.EffectiveParallelism} · "
              + $"{telemetry.CampaignsPerSecond:0.00} 冒险/秒 · "
              + $"ETA {FormatDuration(telemetry.EstimatedRemainingSeconds)}";
        logBox.Text =
            $"阶段：{telemetry.Stage} / {telemetry.Phase}\r\n"
            + $"搜索：本次 {telemetry.RunSearchSimulations:N0} / "
            + $"累计 {telemetry.SearchSimulations:N0} 次，"
            + $"{telemetry.SearchSimulationsPerSecond:N0}/秒，"
            + $"提前停止 {telemetry.SearchEarlyStops}\r\n"
            + $"决策：avg={AverageSearchMilliseconds(telemetry):0.00}ms，"
            + $"model={telemetry.SearchModelEvaluations:N0}，"
            + $"cache={telemetry.SearchModelCacheHits:N0}，"
            + $"candidates={telemetry.SearchRetainedCandidates:N0}/"
            + $"{telemetry.SearchOriginalCandidates:N0}，"
            + $"deadline={telemetry.SearchTimeBudgetStops}，"
            + $"modelBudget={telemetry.SearchModelBudgetStops}\r\n"
            + $"线程：active={telemetry.ActiveCampaigns}，"
            + $"peak={telemetry.PeakConcurrentCampaigns}，"
            + $"observed={telemetry.ObservedWorkerThreads}\r\n"
            + $"推理批处理：requests={telemetry.InferenceRequests:N0}，"
            + $"batches={telemetry.InferenceBatchEvaluations:N0}，"
            + $"avg={telemetry.InferenceAverageBatchSize:0.00}，"
            + $"timeouts={telemetry.InferenceTimeoutFlushes:N0}，"
            + $"fallback={telemetry.InferenceDirectFallbackRequests:N0}，"
            + $"adaptive={telemetry.InferenceAdaptiveFallbackActivations:N0}，"
            + $"wait={telemetry.InferenceAverageWaitMicroseconds:0.0}us\r\n"
            + PerformanceProbeSummary(telemetry)
            + "\r\n"
            + $"GC：{telemetry.Gen0Collections}/"
            + $"{telemetry.Gen1Collections}/{telemetry.Gen2Collections}\r\n"
            + ProgressFreshnessText(progress);
        diagnostics.PresentTelemetry(telemetry);
    }

    private static string ProgressFreshnessText(
        CombatFoundationWorkerProgress progress)
    {
        var now = DateTime.UtcNow;
        var telemetryUtc = progress.TelemetryUpdatedUtc == default
            ? progress.UpdatedUtc
            : progress.TelemetryUpdatedUtc;
        var heartbeatUtc = progress.UpdatedUtc == default
            ? telemetryUtc
            : progress.UpdatedUtc;
        var age = Math.Max(0d, (now - telemetryUtc).TotalSeconds);
        return "数据更新："
               + telemetryUtc.ToLocalTime().ToString("HH:mm:ss")
               + "（"
               + age.ToString("0.0")
               + " 秒前，序号 "
               + progress.TelemetrySequence
               + "） · 心跳："
               + heartbeatUtc.ToLocalTime().ToString("HH:mm:ss")
               + (progress.HeartbeatOnly ? "（仅存活）" : "");
    }

    private static string PerformanceProbeSummary(
        CombatCampaignFoundationTelemetry telemetry)
    {
        var phase = string.IsNullOrWhiteSpace(telemetry.Phase)
            ? "unknown"
            : telemetry.Phase;
        var elapsed = telemetry.PhaseElapsedSeconds.TryGetValue(
            phase,
            out var phaseElapsed)
            ? Math.Max(0d, phaseElapsed)
            : 0d;
        var workerCpu = telemetry.PhaseCpuSeconds.TryGetValue(
            phase,
            out var phaseCpu)
            ? Math.Max(0d, phaseCpu)
            : 0d;
        var externalCpu = telemetry.PhaseExternalCpuSeconds.TryGetValue(
            phase,
            out var phaseExternal)
            ? Math.Max(0d, phaseExternal)
            : 0d;
        var utilization = elapsed <= 0d
            ? 0d
            : (workerCpu + externalCpu)
              / elapsed
              / Math.Max(1, Environment.ProcessorCount)
              * 100d;
        var peak = telemetry.PhasePeakConcurrentWork.TryGetValue(
            phase,
            out var phasePeak)
            ? Math.Max(0, phasePeak)
            : 0;
        var threads = telemetry.PhaseObservedWorkerThreads.TryGetValue(
            phase,
            out var phaseThreads)
            ? Math.Max(0, phaseThreads)
            : 0;
        return $"性能探针：{phase} wall={elapsed:0.0}s，"
               + $"CPU={utilization:0.0}%，peak={peak}，threads={threads}";
    }

    private static double AverageSearchMilliseconds(
        CombatCampaignFoundationTelemetry telemetry)
    {
        return telemetry.PolicyDecisions <= 0
            ? 0d
            : telemetry.SearchMillisecondsTotal / telemetry.PolicyDecisions;
    }

    private void PresentResult(ControllerWorkerResultSummary result)
    {
        var accepted = result.ModelAccepted
                       || string.Equals(
                           result.CompletionKind,
                           "training-accepted",
                           StringComparison.Ordinal);
        var semanticRejected = result.Training?.SemanticGatePassed == false;
        runStatus.Text = accepted
            ? "训练完成 · 底模已通过隔离验收"
            : result.Cancelled
                ? "训练已取消"
                : semanticRejected
                    ? "训练已安全拒绝 · 语义门禁未通过"
                    : "训练结束 · " + result.CompletionKind;
        runStatus.Foreground = accepted
            ? TrainerTheme.Success
            : result.Cancelled
                ? TrainerTheme.Warning
                : TrainerTheme.Danger;
        progressBar.Value = 100;
        progressPrimary.Text = result.Message;
        if (result.Training != null)
        {
            progressSecondary.Text =
                $"普通 {result.Training.Validation.NormalVictories}/"
                + $"{result.Training.Validation.NormalCampaigns}"
                + $"（LB {result.Training.Validation.NormalWilsonLowerBound:P1}） · "
                + $"高级 {result.Training.Validation.AdvancedVictories}/"
                + $"{result.Training.Validation.AdvancedCampaigns}"
                + $"（LB {result.Training.Validation.AdvancedWilsonLowerBound:P1}） · "
                + $"无效 {result.Training.Validation.InvalidCampaigns} · "
                + $"无效果动作 "
                + $"{result.Training.Validation.NoEffectActionAttempts} · "
                + $"交互契约失败 "
                + $"{result.Training.Validation.InteractiveActionContractFailures} · "
                + $"语义隔离 {result.Training.SemanticRejectedCampaigns}/"
                + $"{result.Training.DiscardedSemanticEpisodes}";
        }
        logBox.Text =
            $"完成类型：{result.CompletionKind}\r\n"
            + $"启动模式：{StartModeText(result)}\r\n"
            + $"Worker 已完成：{result.WorkerCompleted}\r\n"
            + $"训练成功：{result.TrainingSucceeded}\r\n"
            + $"模型已接受：{result.ModelAccepted}\r\n"
            + $"执行 epoch：{result.EpochsExecuted}\r\n"
            + $"验证最优 epoch：{(result.BestValidationEpoch > 0 ? result.BestValidationEpoch : result.SelectedEpoch)}\r\n"
            + $"部署选择 epoch：{(result.DeploymentSelectedEpoch > 0 ? result.DeploymentSelectedEpoch : result.SelectedEpoch)}\r\n"
            + $"持久化回放：{result.PersistedReplayEpisodes}\r\n"
            + $"检查点大小：{result.CheckpointBytes:N0} bytes\r\n"
            + $"运行时：{result.Runtime}\r\n"
            + $"规则集：{result.RulesetHash}\r\n"
            + $"可恢复：{result.Resumable}\r\n"
            + $"检查点：{result.CheckpointPath}\r\n"
            + $"检查点写入失败：{result.CheckpointWriteFailures}\r\n"
            + (string.IsNullOrWhiteSpace(result.CheckpointWarning)
                ? ""
                : $"检查点提示：{result.CheckpointWarning}\r\n")
            + $"训练指标：{result.TrainingMetricsPath}\r\n"
            + $"训练分析：{result.TrainingAnalysisPath}\r\n"
            + $"指标写入失败：{result.TrainingMetricWriteFailures}\r\n"
            + (string.IsNullOrWhiteSpace(result.TrainingMetricWarning)
                ? ""
                : $"指标提示：{result.TrainingMetricWarning}\r\n")
            + $"待验底模包：{result.ModelPackagePath}\r\n"
            + $"结果目录：{session?.ResultDirectory}";
        recentResultStatus.Text = runStatus.Text;
        recentResultStatus.Foreground = runStatus.Foreground;
        recentResultDetails.Text = ResultSummary(result);
        diagnostics.PresentResult(result);
        RefreshCheckpointCatalog();
    }

    private void TryShowCompletionNotification(bool running)
    {
        if (running
            || !completionNotificationArmed
            || presentedResult == null)
        {
            return;
        }
        completionNotificationArmed = false;
        tabs.SelectedIndex = DiagnosticsTabIndex;
        FlashTaskbar(start: true);
        PlayCompletionSound(presentedResult);
        var accepted = string.Equals(
            presentedResult.CompletionKind,
            "training-accepted",
            StringComparison.Ordinal);
        var title = accepted
            ? "训练完成并通过验收"
            : presentedResult.Cancelled
                ? "训练已取消"
                : presentedResult.Resumable
                    ? "训练未通过，可恢复"
                    : "训练结束";
        var icon = accepted
            ? MessageBoxImage.Information
            : presentedResult.Cancelled
                ? MessageBoxImage.Warning
                : MessageBoxImage.Exclamation;
        MessageBox.Show(
            this,
            ResultSummary(presentedResult),
            title,
            MessageBoxButton.OK,
            icon);
        FlashTaskbar(start: false);
    }

    private static void PlayCompletionSound(ControllerWorkerResultSummary result)
    {
        if (string.Equals(
                result.CompletionKind,
                "training-accepted",
                StringComparison.Ordinal))
        {
            SystemSounds.Asterisk.Play();
            return;
        }
        SystemSounds.Exclamation.Play();
    }

    private void FlashTaskbar(bool start)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }
        var info = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            WindowHandle = handle,
            Flags = start
                ? FlashAll | FlashTimerNoForeground
                : FlashStop,
            Count = start ? 5u : 0u,
            Timeout = 0
        };
        FlashWindowEx(ref info);
    }

    private static string ResultSummary(ControllerWorkerResultSummary result)
    {
        var validation = result.Training?.Validation;
        var validationText = validation == null
            ? "未生成验证摘要"
            : $"普通 {validation.NormalVictories}/{validation.NormalCampaigns}"
              + $"（计划 {validation.NormalPlannedCampaigns}，"
              + $"LB {validation.NormalWilsonLowerBound:P1}） · "
              + $"高级 {validation.AdvancedVictories}/{validation.AdvancedCampaigns}"
              + $"（计划 {validation.AdvancedPlannedCampaigns}，"
              + $"LB {validation.AdvancedWilsonLowerBound:P1}） · "
              + $"无效 {validation.InvalidCampaigns}";
        var teacher = result.Training?.Iterations
            .LastOrDefault(item => item.TransformerTeacher?.Requested == true)
            ?.TransformerTeacher;
        var teacherText = "";
        if (teacher != null)
        {
            teacherText = !teacher.Success
                ? Environment.NewLine
                  + "Transformer 教师未完成 · "
                  + teacher.Message
                : Environment.NewLine
                  + (teacher.Applied
                      ? "Transformer 教师已应用 "
                      : "Transformer 教师未应用 ")
                  + teacher.EffectiveBackend
                  + " · 标注 "
                  + teacher.AnnotatedFrames
                  + "/"
                  + teacher.FrameCount
                  + " · CE "
                  + FormatLoss(teacher.ValidationPolicyCrossEntropy)
                  + "（均匀 "
                  + FormatLoss(
                      teacher.ValidationUniformPolicyCrossEntropy)
                  + "） · Dynamics MSE "
                  + FormatLoss(teacher.ValidationDynamicsMse)
                  + " · Outcome MAE "
                  + FormatLoss(teacher.ValidationOutcomeMae)
                  + " · 模型 "
                  + teacher.Layers
                  + "x"
                  + teacher.HiddenDimensions
                  + " / "
                  + FormatParameters(teacher.ParameterCount)
                  + Environment.NewLine
                  + "教师运行计划 · CPU "
                  + teacher.EffectiveCpuThreads
                  + "/"
                  + teacher.EffectiveCpuInteropThreads
                  + " · Batch "
                  + teacher.EffectiveBatchSize
                  + "/"
                  + teacher.EffectiveMicroBatchSize
                  + " · Loader "
                  + teacher.EffectiveDataLoaderWorkers
                  + " · "
                  + teacher.NumericPrecision
                  + " · "
                  + (teacher.WarmStarted
                      ? teacher.TrainingRefreshed
                          ? "热启动增量 "
                          : "复用权重标注 "
                      : "全量初始化 ")
                  + teacher.EpochsExecuted
                  + "/"
                  + teacher.RequestedEpochs
                  + " Epoch"
                  + " · 代次 "
                  + teacher.TeacherGeneration
                  + " · 更新 "
                  + (teacher.TrainingRefreshed
                      ? teacher.UpdateAccepted ? "接受" : "回滚"
                      : "未请求")
                  + " · "
                  + teacher.TrainingFramesPerSecond.ToString("0.0")
                  + " frames/s"
                  + Environment.NewLine
                  + "教师闭环 · 蒸馏使用 "
                  + teacher.DistillationTrainingFrames
                  + "+"
                  + teacher.DistillationValidationFrames
                  + "/"
                  + teacher.AnnotatedFrames
                  + "（"
                  + teacher.DistillationUtilization.ToString("P1")
                  + " | DistillWeight "
                  + teacher.EffectiveDistillationWeight.ToString("0.00")
                  + (teacher.DistillationStudentGuardApplied
                      ? " (student guard: "
                        + teacher.DistillationStudentGuardReason
                        + ")"
                      : "")
                  + "） · 锚点 "
                  + teacher.AnchorValidationFrames
                  + " · 漂移 "
                  + teacher.DatasetDriftScore.ToString("P1")
                  + " · "
                  + teacher.RefreshReason
                  + (teacher.Applied
                      ? ""
                      : Environment.NewLine + teacher.Message);
        }
        var recoveryText = result.Resumable
            ? "检查点已保存，可恢复训练。"
            : string.Equals(
                result.CompletionKind,
                "training-accepted",
                StringComparison.Ordinal)
                ? "底模已通过隔离验收。"
                : "当前结果不可恢复。";
        var checkpointText = result.CheckpointWritesEnqueued <= 0
            ? ""
            : Environment.NewLine
              + "Checkpoint · 线程 "
              + result.EffectiveCheckpointSerializationParallelism
              + (result.CheckpointSerializationAutoScaled
                  ? "（自动扩展）"
                  : "")
              + " · 执行/请求 "
              + result.CheckpointWritesExecuted
              + "/"
              + result.CheckpointWritesEnqueued
              + " · 合并 "
              + result.CheckpointWritesCoalesced
              + " · "
              + result.CheckpointSerializationSeconds.ToString("0.0")
              + "s";
        return "启动模式 · "
               + StartModeText(result)
               + Environment.NewLine
               + validationText
               + teacherText
               + checkpointText
               + Environment.NewLine
               + result.Message
               + Environment.NewLine
               + recoveryText;
    }

    private static string StartModeText(
        ControllerWorkerResultSummary result)
    {
        var requested = result.RequestedStartMode switch
        {
            "resume-required" => "恢复兼容训练",
            "fresh" => "重新开始",
            "champion-seed" => "以上轮 Champion 开新一代",
            "auto-resume" => "旧版自动恢复",
            _ => string.IsNullOrWhiteSpace(result.RequestedStartMode)
                ? "旧版未记录"
                : result.RequestedStartMode
        };
        var effective = result.EffectiveStartMode switch
        {
            "checkpoint" => "活动检查点恢复",
            "historical-working" => "历史 Working Model 恢复",
            "fresh" => "全新初始化",
            "cancelled" => "启动中取消",
            "failed" => "启动失败",
            _ => result.ResumedFromCheckpoint
                ? "恢复训练"
                : result.ResumeRequested
                    ? "未能恢复"
                    : "未记录"
        };
        return requested + " → " + effective;
    }

    private static string FormatLoss(double value)
    {
        return double.IsNaN(value)
               || double.IsInfinity(value)
               || value >= double.MaxValue / 2d
            ? "待计算"
            : value.ToString("0.000000");
    }

    private static string FormatParameters(long value)
    {
        return value >= 1_000_000
            ? (value / 1_000_000d).ToString("0.0") + "M"
            : value >= 1_000
                ? (value / 1_000d).ToString("0.0") + "K"
                : Math.Max(0L, value).ToString();
    }

    private static string FormatBytes(long value)
    {
        var bytes = Math.Max(0L, value);
        return bytes >= 1024L * 1024L * 1024L
            ? (bytes / (1024d * 1024d * 1024d)).ToString("0.00") + " GB"
            : (bytes / (1024d * 1024d)).ToString("0") + " MB";
    }

    private void LoadSettings()
    {
        var modRoot = ResolveArgument("--mod-root")
                      ?? DiscoverModRoot();
        var dataRoot = ResolveArgument("--data-root")
                       ?? DiscoverDataRoot(modRoot);
        var settingsPath = SettingsPath(dataRoot);
        var readPath = File.Exists(settingsPath)
            ? settingsPath
            : LegacySettingsPath(dataRoot);
        try
        {
            settings = File.Exists(readPath)
                ? Deserialize<ControllerSettings>(
                    CombatFoundationCheckpointStorage.ReadAllTextShared(
                        readPath))
                  ?? new ControllerSettings()
                : new ControllerSettings();
        }
        catch
        {
            settings = new ControllerSettings();
        }
        var loadedSchemaVersion = settings.SchemaVersion;
        settings.ModRoot = modRoot;
        settings.DataRoot = dataRoot;
        if (!string.IsNullOrWhiteSpace(settings.LastRunDirectory)
            && !Directory.Exists(settings.LastRunDirectory))
        {
            settings.LastRunDirectory = "";
        }
        settings.Parameters ??= new CombatFoundationTrainingParameters();
        if (loadedSchemaVersion < 3)
        {
            settings.Parameters.RequireCapabilityProbeBaselineGain = true;
        }
        if (loadedSchemaVersion < 4)
        {
            settings.Parameters.AdditionalIterationsOnResume = 3;
            settings.Parameters.MinimumAdvancedReplayShare = 0.40d;
            settings.Parameters.MinimumAdvancedDefeatReplayShare = 0.25d;
        }
        if (loadedSchemaVersion < 5)
        {
            settings.GameSubject = LoadDefaultGameSubject(modRoot);
            settings.LastRunDirectory = "";
            settings.ContinueGeneration = 0;
        }
        if (loadedSchemaVersion < 6)
        {
            settings.Parameters.CapabilityProbeCampaignsPerDifficulty = 128;
        }
        if (loadedSchemaVersion < 7)
        {
            settings.Parameters.GovernanceProfile =
                CombatFoundationGovernanceProfileNames.Development;
            settings.Parameters.TuningInterval = 2;
            settings.Parameters.CapabilityProbeTeacherCampaignsPerDifficulty = 16;
            settings.Parameters.AutoTuneSampleCampaigns = 16;
        }
        if (loadedSchemaVersion < 8)
        {
            settings.Parameters.AutoTuneObjective =
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput;
            settings.Parameters.InferenceParallelism = 0;
            settings.Parameters.InferenceLaneCount = 0;
            settings.Parameters.InferenceBatchSize = 0;
        }
        if (loadedSchemaVersion < 9)
        {
            settings.Parameters.TransformerTeacherBackend =
                CombatTransformerTeacherBackendNames.Auto;
            settings.Parameters.TransformerPythonExecutable = "python";
            settings.Parameters.TransformerTeacherEpochs = 12;
            settings.Parameters.TransformerTeacherBatchSize = 64;
            settings.Parameters.TransformerTeacherStateDimensions = 1024;
            settings.Parameters.TransformerTeacherActionDimensions = 1024;
            settings.Parameters.TransformerTeacherHiddenDimensions = 384;
            settings.Parameters.TransformerTeacherLayers = 6;
            settings.Parameters.TransformerTeacherAttentionHeads = 8;
            settings.Parameters.TransformerTeacherFeedForwardDimensions = 1536;
            settings.Parameters.TransformerTeacherHistoryLength = 12;
            settings.Parameters.TransformerTeacherMinimumFrames = 1024;
            settings.Parameters.TransformerTeacherCpuThreads = 0;
            settings.Parameters.TransformerDistillationWeight = 0.35d;
            settings.Parameters.ModelMaximumUnsafeEndTurnFrameShare = 0.35d;
            settings.Parameters.ModelStateDimensions = 1024;
            settings.Parameters.ModelActionDimensions = 1024;
            settings.Parameters.ModelHiddenDimensions = 512;
        }
        if (loadedSchemaVersion < 10)
        {
            if (string.Equals(
                    settings.Parameters.ParallelismProfile,
                    CombatFoundationExecutionProfileNames.Auto,
                    StringComparison.OrdinalIgnoreCase))
            {
                settings.Parameters.InferenceParallelism = 0;
                settings.Parameters.InferenceLaneCount = 0;
                settings.Parameters.InferenceBatchSize = 0;
                settings.Parameters.ThreadPoolMinimumWorkerThreads = 0;
                settings.Parameters.CheckpointSerializationParallelism = 0;
            }
            settings.Parameters.TransformerPythonExecutable =
                CombatTransformerRuntimeProtocol.AutomaticExecutable;
            settings.Parameters.TransformerTeacherCpuThreads = 0;
            settings.Parameters.TransformerTeacherCpuInteropThreads = 0;
            settings.Parameters.TransformerTeacherMicroBatchSize = 0;
            settings.Parameters.TransformerTeacherDataLoaderWorkers = 0;
            settings.Parameters.TransformerTeacherPrefetchBatches = 2;
            settings.Parameters.TransformerTeacherEnablePinnedMemory = true;
            settings.Parameters.TransformerTeacherEnableMixedPrecision = true;
            settings.Parameters.ModelGradientShardCount = 0;
        }
        if (loadedSchemaVersion < 11)
        {
            settings.Parameters.TransformerTeacherMinimumFrames = 8192;
            settings.Parameters.TransformerTeacherEnableWarmStart = true;
            settings.Parameters.TransformerTeacherCpuRefreshInterval = 2;
            settings.Parameters.TransformerTeacherIncrementalEpochs = 4;
            settings.Parameters.TransformerTeacherFinalEpochs = 12;
            if (string.Equals(
                    settings.Parameters.GovernanceProfile,
                    CombatFoundationGovernanceProfileNames.Development,
                    StringComparison.OrdinalIgnoreCase))
            {
                settings.Parameters.TrainingCampaignsPerIteration = 96;
                settings.Parameters.ArenaCampaignsPerDifficulty = 16;
                settings.Parameters.ArenaConfirmationCampaignsPerDifficulty = 48;
                settings.Parameters.NormalValidationCampaigns = 100;
                settings.Parameters.AdvancedValidationCampaigns = 200;
                settings.Parameters.CapabilityProbeCampaignsPerDifficulty = 64;
            }
        }
        if (loadedSchemaVersion < 12)
        {
            settings.Parameters.ModelMaximumUnsafeEndTurnFrameShare = 0.20d;
            settings.Parameters.ModelUnsafeEndTurnRiskAuxiliaryShare = 0.10d;
            settings.Parameters.MinimumArenaDiscordantPairs = 8;
            settings.Parameters.MaximumOfflineHeadRegression = 0.05d;
            settings.Parameters.MaximumStateFeatureCollisionRate = 0.20d;
            settings.Parameters.MaximumActionFeatureCollisionRate = 0.06d;
            settings.Parameters.TransformerTeacherMaximumFrames = 10000;
            settings.Parameters.TransformerTeacherMinimumFrames = 4096;
            settings.Parameters.TransformerTeacherCpuRefreshInterval = 4;
            settings.Parameters.TransformerTeacherCpuEpochs = 4;
            settings.Parameters.TransformerTeacherCpuIncrementalEpochs = 1;
            settings.Parameters.TransformerTeacherCpuFinalEpochs = 4;
            settings.Parameters.TransformerTeacherEnableAdaptiveRefresh = true;
            settings.Parameters
                .TransformerTeacherAdaptiveRefreshDriftThreshold = 0.15d;
            settings.Parameters
                .TransformerTeacherEnableFixedAnchorValidation = true;
            settings.Parameters.TransformerTeacherMaximumHeadRegression =
                0.05d;
        }
        if (loadedSchemaVersion < 13)
        {
            settings.Parameters.ModelStateDimensions = 1024;
            settings.Parameters.ModelActionDimensions = 1024;
            settings.Parameters.ModelHiddenDimensions = 512;
            settings.Parameters.TransformerTeacherStateDimensions = 1024;
            settings.Parameters.TransformerTeacherActionDimensions = 1024;
            settings.Parameters.TransformerTeacherHiddenDimensions = 384;
            settings.Parameters.TransformerTeacherMinimumFrames = 4096;
            settings.Parameters.TransformerTeacherMaximumFrames = 10000;
            settings.Parameters.MinimumAdvancedReplayShare = Math.Max(
                0.40d,
                settings.Parameters.MinimumAdvancedReplayShare);
            settings.Parameters.MinimumAdvancedDefeatReplayShare = Math.Max(
                0.25d,
                settings.Parameters.MinimumAdvancedDefeatReplayShare);
            settings.Parameters.MinimumArenaDiscordantPairs = 8;
            settings.Parameters.MaximumStateFeatureCollisionRate = 0.20d;
            settings.Parameters.MaximumActionFeatureCollisionRate = 0.06d;
        }
        if (loadedSchemaVersion < 14)
        {
            if (string.Equals(
                    settings.Parameters.ParallelismProfile,
                    CombatFoundationExecutionProfileNames.Auto,
                    StringComparison.OrdinalIgnoreCase))
            {
                settings.Parameters.InferenceExecutionMode =
                    CombatFoundationExecutionProfileNames.DirectInference;
                settings.Parameters.InferenceParallelism = 0;
                settings.Parameters.InferenceLaneCount = 0;
                settings.Parameters.InferenceBatchSize = 0;
                settings.Parameters.ThreadPoolMinimumWorkerThreads = 0;
                settings.Parameters.CheckpointSerializationParallelism = 0;
            }
            settings.Parameters.ReuseAutoTuneCache = true;
            settings.Parameters.AutoTuneSampleCampaigns = Math.Max(
                16,
                settings.Parameters.AutoTuneSampleCampaigns);
            settings.Parameters.AutoTuneObjective =
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput;
            settings.Parameters.EnableSequentialArenaStop = true;
            settings.Parameters.ArenaEvaluationBatchSize = Math.Max(
                16,
                settings.Parameters.ArenaEvaluationBatchSize);
        }
        if (loadedSchemaVersion < 15)
        {
            settings.Parameters.TransformerTeacherMinimumFrames = 1024;
        }
        settings.GameSubject ??= LoadDefaultGameSubject(modRoot);
        settings.GameSubject.Normalize();
        gameSubjectCatalog = LoadGameSubjectCatalog(modRoot);
        gameSubjectCatalog.ResolveReferences(settings.GameSubject);
        settings.SchemaVersion = 15;
        settings.Parameters.Normalized();
        if (File.Exists(readPath)
            && !string.Equals(
                Path.GetFullPath(readPath),
                Path.GetFullPath(settingsPath),
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                WriteAtomic(settingsPath, Serialize(settings));
            }
            catch
            {
                // Migration is best-effort; the legacy settings remain readable.
            }
        }
    }

    private static CombatGameSubjectPreset LoadDefaultGameSubject(
        string modRoot)
    {
        try
        {
            var campaign = Deserialize<CombatCampaignDefinition>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(
                    CampaignPath(modRoot)));
            if (campaign != null)
            {
                return CombatGameSubjectPreset.FromCampaign(campaign);
            }
        }
        catch
        {
        }
        return new CombatGameSubjectPreset().Normalize();
    }

    private static CombatGameSubjectCatalog LoadGameSubjectCatalog(
        string modRoot)
    {
        try
        {
            var path = Path.Combine(
                modRoot,
                "Config",
                "combat-simulation",
                "witch-game-subjects-v1.catalog.json");
            var catalog = Deserialize<CombatGameSubjectCatalog>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(path));
            return (catalog ?? new CombatGameSubjectCatalog()).Normalize();
        }
        catch
        {
            return new CombatGameSubjectCatalog();
        }
    }

    private static string CampaignPath(string modRoot)
    {
        return Path.Combine(
            modRoot,
            "Config",
            "combat-simulation",
            "witch-world-simulation-v2.campaign.json");
    }

    private void SaveSettings()
    {
        try
        {
            var path = SettingsPath(settings.DataRoot);
            WriteAtomic(path, Serialize(settings));
        }
        catch (Exception ex)
        {
            AppendLog("保存控制台设置失败：" + ex.Message);
        }
    }

    private void SaveSession()
    {
        if (session == null)
        {
            return;
        }
        WriteAtomic(SessionPath(settings.DataRoot), Serialize(session));
    }

    private void TryAttachLastSession()
    {
        try
        {
            var currentPath = SessionPath(settings.DataRoot);
            var path = File.Exists(currentPath)
                ? currentPath
                : LegacySessionPath(settings.DataRoot);
            if (!File.Exists(path))
            {
                return;
            }
            session = Deserialize<ControllerSession>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(path));
            if (session != null)
            {
                if (!string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(currentPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    WriteAtomic(currentPath, Serialize(session));
                }
                ResetPollingCache();
                settings.LastRunDirectory = session.ResultDirectory;
                completionNotificationArmed = IsWorkerRunning();
                AppendLog("已挂接最近训练任务：" + session.JobId);
            }
        }
        catch (Exception ex)
        {
            AppendLog("无法挂接最近任务：" + ex.Message);
        }
    }

    private void PullSettingsFromUi()
    {
        settings.ModRoot = Path.GetFullPath(modRootInput.Text.Trim());
        settings.DataRoot = Path.GetFullPath(dataRootInput.Text.Trim());
        PullGameSubjectFromUi();
        var p = settings.Parameters;
        p.GovernanceProfile = Convert.ToString(
                                  governanceProfileInput.SelectedItem,
                                  CultureInfo.InvariantCulture)
                              ?? CombatFoundationGovernanceProfileNames.Development;
        p.DecisionProfile = selectedProfile;
        p.Iterations = Int("Iterations");
        p.AdditionalIterationsOnResume =
            Int("AdditionalIterationsOnResume");
        p.TrainingCampaignsPerIteration = Int("TrainingCampaignsPerIteration");
        p.PreflightCampaignsPerDifficulty =
            Int("PreflightCampaignsPerDifficulty");
        p.ArenaCampaignsPerDifficulty = Int("ArenaCampaignsPerDifficulty");
        p.ArenaConfirmationCampaignsPerDifficulty =
            Int("ArenaConfirmationCampaignsPerDifficulty");
        p.NormalValidationCampaigns = Int("NormalValidationCampaigns");
        p.AdvancedValidationCampaigns = Int("AdvancedValidationCampaigns");
        p.ValidationEarlyStopBatchSize = Int("ValidationEarlyStopBatchSize");
        p.CapabilityProbeCampaignsPerDifficulty =
            Int("CapabilityProbeCampaignsPerDifficulty");
        p.CapabilityProbeTeacherCampaignsPerDifficulty =
            Int("CapabilityProbeTeacherCampaignsPerDifficulty");
        p.CapabilityProbeBatchSize = Int("CapabilityProbeBatchSize");
        p.RequireCapabilityProbeBaselineGain =
            Toggle("RequireCapabilityProbeBaselineGain");
        p.CapabilityProbeMinimumVictoryGain =
            Int("CapabilityProbeMinimumVictoryGain");
        p.CapabilityProbeMinimumDepthGain =
            Double("CapabilityProbeMinimumDepthGain");
        p.MaximumDegreeOfParallelism = Int("MaximumDegreeOfParallelism");
        p.ParallelismProfile = Convert.ToString(
                                   parallelismProfileInput.SelectedItem,
                                   CultureInfo.InvariantCulture)
                               ?? CombatFoundationExecutionProfileNames.Auto;
        p.InferenceExecutionMode = Convert.ToString(
                                       inferenceModeInput.SelectedItem,
                                       CultureInfo.InvariantCulture)
                                   ?? CombatFoundationExecutionProfileNames
                                       .DirectInference;
        p.InferenceParallelism = Int("InferenceParallelism");
        p.InferenceLaneCount = Int("InferenceLaneCount");
        p.InferenceBatchSize = Int("InferenceBatchSize");
        p.ThreadPoolMinimumWorkerThreads =
            Int("ThreadPoolMinimumWorkerThreads");
        p.CheckpointSerializationParallelism =
            Int("CheckpointSerializationParallelism");
        p.ReuseAutoTuneCache = Toggle("ReuseAutoTuneCache");
        p.AutoTuneObjective = Convert.ToString(
                                  autoTuneObjectiveInput.SelectedItem,
                                  CultureInfo.InvariantCulture)
                              ?? CombatFoundationAutoTuneObjectiveNames
                                  .MaximumThroughput;
        p.AutoTuneSampleCampaigns = Int("AutoTuneSampleCampaigns");
        p.AutoTuneThroughputTolerance =
            Double("AutoTuneThroughputTolerance");
        p.ModelEpochs = Int("ModelEpochs");
        p.ModelMinimumEpochs = Int("ModelMinimumEpochs");
        p.ModelEarlyStoppingPatience = Int("ModelEarlyStoppingPatience");
        p.ModelEarlyStoppingMinimumDelta =
            Double("ModelEarlyStoppingMinimumDelta");
        p.ModelBatchSize = Int("ModelBatchSize");
        p.ModelGradientShardCount = Convert.ToInt32(
            gradientShardInput.SelectedItem ?? 12,
            CultureInfo.InvariantCulture);
        p.MinimumEpisodes = Int("MinimumEpisodes");
        p.EnableFrameStratification = Toggle("EnableFrameStratification");
        p.EnableEndTurnSpecialization =
            Toggle("EnableEndTurnSpecialization");
        p.ModelMaximumFrameStratumWeight =
            Double("ModelMaximumFrameStratumWeight");
        p.ModelEndTurnFrameWeight = Double("ModelEndTurnFrameWeight");
        p.ModelMaximumUnsafeEndTurnFrameShare =
            Double("ModelMaximumUnsafeEndTurnFrameShare");
        p.ModelUnsafeEndTurnRiskAuxiliaryShare =
            Double("ModelUnsafeEndTurnRiskAuxiliaryShare");
        p.ModelMinimumValidationRunGroups =
            Int("ModelMinimumValidationRunGroups");
        p.ModelMinimumTestRunGroups =
            Int("ModelMinimumTestRunGroups");
        p.ModelPolicyTargetTemperature =
            Double("ModelPolicyTargetTemperature");
        p.ModelMaximumPolicyTargetProbability =
            Double("ModelMaximumPolicyTargetProbability");
        p.ModelMaximumFramesPerEpisode =
            Int("ModelMaximumFramesPerEpisode");
        p.ModelReplayEpisodeLimit = Int("ModelReplayEpisodeLimit");
        p.ModelRetainedCandidates = Int("ModelRetainedCandidates");
        p.ModelLearningRate = Double("ModelLearningRate");
        p.ModelL2 = Double("ModelL2");
        p.ModelStateDimensions = Int("ModelStateDimensions");
        p.ModelActionDimensions = Int("ModelActionDimensions");
        p.ModelHiddenDimensions = Int("ModelHiddenDimensions");
        p.TransformerTeacherBackend = Convert.ToString(
            transformerBackendInput.SelectedItem,
            CultureInfo.InvariantCulture)
            ?? CombatTransformerTeacherBackendNames.Disabled;
        p.TransformerPythonExecutable =
            inputs["TransformerPythonExecutable"].Text.Trim();
        p.TransformerTeacherEpochs = Int("TransformerTeacherEpochs");
        p.TransformerTeacherBatchSize = Int("TransformerTeacherBatchSize");
        p.TransformerTeacherStateDimensions =
            Int("TransformerTeacherStateDimensions");
        p.TransformerTeacherActionDimensions =
            Int("TransformerTeacherActionDimensions");
        p.TransformerTeacherHiddenDimensions =
            Int("TransformerTeacherHiddenDimensions");
        p.TransformerTeacherLayers = Int("TransformerTeacherLayers");
        p.TransformerTeacherAttentionHeads =
            Int("TransformerTeacherAttentionHeads");
        p.TransformerTeacherFeedForwardDimensions =
            Int("TransformerTeacherFeedForwardDimensions");
        p.TransformerTeacherHistoryLength =
            Int("TransformerTeacherHistoryLength");
        p.TransformerTeacherMinimumFrames =
            Int("TransformerTeacherMinimumFrames");
        p.TransformerTeacherMaximumFrames =
            Int("TransformerTeacherMaximumFrames");
        p.TransformerTeacherEnableWarmStart =
            Toggle("TransformerTeacherEnableWarmStart");
        p.TransformerTeacherCpuRefreshInterval =
            Int("TransformerTeacherCpuRefreshInterval");
        p.TransformerTeacherCpuEpochs =
            Int("TransformerTeacherCpuEpochs");
        p.TransformerTeacherCpuIncrementalEpochs =
            Int("TransformerTeacherCpuIncrementalEpochs");
        p.TransformerTeacherCpuFinalEpochs =
            Int("TransformerTeacherCpuFinalEpochs");
        p.TransformerTeacherEnableAdaptiveRefresh =
            Toggle("TransformerTeacherEnableAdaptiveRefresh");
        p.TransformerTeacherAdaptiveRefreshDriftThreshold =
            Double("TransformerTeacherAdaptiveRefreshDriftThreshold");
        p.TransformerTeacherEnableFixedAnchorValidation =
            Toggle("TransformerTeacherEnableFixedAnchorValidation");
        p.TransformerTeacherMaximumHeadRegression =
            Double("TransformerTeacherMaximumHeadRegression");
        p.TransformerTeacherIncrementalEpochs =
            Int("TransformerTeacherIncrementalEpochs");
        p.TransformerTeacherFinalEpochs =
            Int("TransformerTeacherFinalEpochs");
        p.TransformerTeacherCpuThreads =
            Int("TransformerTeacherCpuThreads");
        p.TransformerTeacherCpuInteropThreads =
            Int("TransformerTeacherCpuInteropThreads");
        p.TransformerTeacherMicroBatchSize =
            Int("TransformerTeacherMicroBatchSize");
        p.TransformerTeacherDataLoaderWorkers =
            Int("TransformerTeacherDataLoaderWorkers");
        p.TransformerTeacherPrefetchBatches =
            Int("TransformerTeacherPrefetchBatches");
        p.TransformerTeacherEnablePinnedMemory =
            Toggle("TransformerTeacherEnablePinnedMemory");
        p.TransformerTeacherEnableMixedPrecision =
            Toggle("TransformerTeacherEnableMixedPrecision");
        p.TransformerDistillationWeight =
            Double("TransformerDistillationWeight");
        p.EnableCurriculum = Toggle("EnableCurriculum");
        p.EnableStratifiedReplay = Toggle("EnableStratifiedReplay");
        p.EnablePrioritizedReplay = Toggle("EnablePrioritizedReplay");
        p.EnableHardSeedCurriculum = Toggle("EnableHardSeedCurriculum");
        p.EnableCounterfactualHardEncounters =
            Toggle("EnableCounterfactualHardEncounters");
        p.EnableSuccessCaseArchive = Toggle("EnableSuccessCaseArchive");
        p.EnableArenaRecovery = Toggle("EnableArenaRecovery");
        p.EnableTuningArena = Toggle("EnableTuningArena");
        p.EnableProgressiveTuning = Toggle("EnableProgressiveTuning");
        p.EnableOfflineTuningGate = Toggle("EnableOfflineTuningGate");
        p.EnableSequentialArenaStop = Toggle("EnableSequentialArenaStop");
        p.EnableEarlyValidationStop = Toggle("EnableEarlyValidationStop");
        p.ArenaInvalidRetryCount = Int("ArenaInvalidRetryCount");
        p.ArenaInvalidRateLimit = Double("ArenaInvalidRateLimit");
        p.TuningNormalCampaigns = Int("TuningNormalCampaigns");
        p.TuningAdvancedCampaigns = Int("TuningAdvancedCampaigns");
        p.TuningScreeningNormalCampaigns =
            Int("TuningScreeningNormalCampaigns");
        p.TuningScreeningAdvancedCampaigns =
            Int("TuningScreeningAdvancedCampaigns");
        p.TuningFinalistCount = Int("TuningFinalistCount");
        p.TuningInterval = Int("TuningInterval");
        p.ArenaEvaluationBatchSize = Int("ArenaEvaluationBatchSize");
        p.MaximumConsecutiveRejectedIterations =
            Int("MaximumConsecutiveRejectedIterations");
        p.NormalAcceptanceRate = Double("NormalAcceptanceRate");
        p.AdvancedAcceptanceRate = Double("AdvancedAcceptanceRate");
        p.MinimumArenaDiscordantPairs = Int("MinimumArenaDiscordantPairs");
        p.MaximumOfflineHeadRegression =
            Double("MaximumOfflineHeadRegression");
        p.MaximumStateFeatureCollisionRate =
            Double("MaximumStateFeatureCollisionRate");
        p.MaximumActionFeatureCollisionRate =
            Double("MaximumActionFeatureCollisionRate");
        p.SuccessExpertReplayShare = Double("SuccessExpertReplayShare");
        p.HardSeedReplayShare = Double("HardSeedReplayShare");
        p.MinimumAdvancedReplayShare =
            Double("MinimumAdvancedReplayShare");
        p.MinimumAdvancedDefeatReplayShare =
            Double("MinimumAdvancedDefeatReplayShare");
        p.SelfPlayExplorationProbability =
            Double("SelfPlayExplorationProbability");
        p.SelfPlayExplorationTemperature =
            Double("SelfPlayExplorationTemperature");
        p.RunSeed = Ulong("RunSeed");
        p.TrainingSeedStart = Ulong("TrainingSeedStart");
        p.ArenaSeedStart = Ulong("ArenaSeedStart");
        p.TuningSeedStart = Ulong("TuningSeedStart");
        p.ValidationSeedStart = Ulong("ValidationSeedStart");
        p.Normalized();
        SaveSettings();
        ApplySettingsToUi();
    }

    private void ApplySettingsToUi()
    {
        modRootInput.Text = settings.ModRoot;
        dataRootInput.Text = settings.DataRoot;
        ApplyGameSubjectToUi();
        var p = settings.Parameters;
        governanceProfileInput.SelectedItem =
            CombatFoundationGovernanceProfiles.Normalize(p.GovernanceProfile);
        SelectProfile(p.DecisionProfile);
        Set("Iterations", p.Iterations);
        Set(
            "AdditionalIterationsOnResume",
            p.AdditionalIterationsOnResume);
        Set("TrainingCampaignsPerIteration", p.TrainingCampaignsPerIteration);
        Set(
            "PreflightCampaignsPerDifficulty",
            p.PreflightCampaignsPerDifficulty);
        Set("ArenaCampaignsPerDifficulty", p.ArenaCampaignsPerDifficulty);
        Set(
            "ArenaConfirmationCampaignsPerDifficulty",
            p.ArenaConfirmationCampaignsPerDifficulty);
        Set("NormalValidationCampaigns", p.NormalValidationCampaigns);
        Set("AdvancedValidationCampaigns", p.AdvancedValidationCampaigns);
        Set("ValidationEarlyStopBatchSize", p.ValidationEarlyStopBatchSize);
        Set(
            "CapabilityProbeCampaignsPerDifficulty",
            p.CapabilityProbeCampaignsPerDifficulty);
        Set(
            "CapabilityProbeTeacherCampaignsPerDifficulty",
            p.CapabilityProbeTeacherCampaignsPerDifficulty);
        Set("CapabilityProbeBatchSize", p.CapabilityProbeBatchSize);
        SetToggle(
            "RequireCapabilityProbeBaselineGain",
            p.RequireCapabilityProbeBaselineGain);
        Set(
            "CapabilityProbeMinimumVictoryGain",
            p.CapabilityProbeMinimumVictoryGain);
        Set(
            "CapabilityProbeMinimumDepthGain",
            p.CapabilityProbeMinimumDepthGain);
        Set("MaximumDegreeOfParallelism", p.MaximumDegreeOfParallelism);
        parallelismProfileInput.SelectedItem =
            CombatFoundationExecutionProfiles.NormalizeProfile(
                p.ParallelismProfile);
        inferenceModeInput.SelectedItem =
            CombatFoundationExecutionProfiles.NormalizeInferenceMode(
                p.InferenceExecutionMode);
        Set("InferenceParallelism", p.InferenceParallelism);
        Set("InferenceLaneCount", p.InferenceLaneCount);
        Set("InferenceBatchSize", p.InferenceBatchSize);
        Set(
            "ThreadPoolMinimumWorkerThreads",
            p.ThreadPoolMinimumWorkerThreads);
        Set(
            "CheckpointSerializationParallelism",
            p.CheckpointSerializationParallelism);
        SetToggle("ReuseAutoTuneCache", p.ReuseAutoTuneCache);
        autoTuneObjectiveInput.SelectedItem =
            CombatFoundationAutoTuneObjectiveNames.Normalize(
                p.AutoTuneObjective);
        Set("AutoTuneSampleCampaigns", p.AutoTuneSampleCampaigns);
        Set(
            "AutoTuneThroughputTolerance",
            p.AutoTuneThroughputTolerance);
        Set("ModelEpochs", p.ModelEpochs);
        Set("ModelMinimumEpochs", p.ModelMinimumEpochs);
        Set("ModelEarlyStoppingPatience", p.ModelEarlyStoppingPatience);
        Set("ModelEarlyStoppingMinimumDelta", p.ModelEarlyStoppingMinimumDelta);
        Set("ModelBatchSize", p.ModelBatchSize);
        SetGradientShardCount(p.ModelGradientShardCount);
        Set("MinimumEpisodes", p.MinimumEpisodes);
        Set(
            "ModelMaximumFrameStratumWeight",
            p.ModelMaximumFrameStratumWeight);
        Set("ModelEndTurnFrameWeight", p.ModelEndTurnFrameWeight);
        Set(
            "ModelMaximumUnsafeEndTurnFrameShare",
            p.ModelMaximumUnsafeEndTurnFrameShare);
        Set(
            "ModelUnsafeEndTurnRiskAuxiliaryShare",
            p.ModelUnsafeEndTurnRiskAuxiliaryShare);
        Set(
            "ModelMinimumValidationRunGroups",
            p.ModelMinimumValidationRunGroups);
        Set(
            "ModelMinimumTestRunGroups",
            p.ModelMinimumTestRunGroups);
        Set(
            "ModelPolicyTargetTemperature",
            p.ModelPolicyTargetTemperature);
        Set(
            "ModelMaximumPolicyTargetProbability",
            p.ModelMaximumPolicyTargetProbability);
        Set(
            "ModelMaximumFramesPerEpisode",
            p.ModelMaximumFramesPerEpisode);
        Set("ModelReplayEpisodeLimit", p.ModelReplayEpisodeLimit);
        Set("ModelRetainedCandidates", p.ModelRetainedCandidates);
        Set("ModelLearningRate", p.ModelLearningRate);
        Set("ModelL2", p.ModelL2);
        Set("ModelStateDimensions", p.ModelStateDimensions);
        Set("ModelActionDimensions", p.ModelActionDimensions);
        Set("ModelHiddenDimensions", p.ModelHiddenDimensions);
        transformerBackendInput.SelectedItem =
            CombatTransformerTeacherBackendNames.Normalize(
                p.TransformerTeacherBackend);
        Set("TransformerPythonExecutable", p.TransformerPythonExecutable);
        Set("TransformerTeacherEpochs", p.TransformerTeacherEpochs);
        Set("TransformerTeacherBatchSize", p.TransformerTeacherBatchSize);
        Set(
            "TransformerTeacherStateDimensions",
            p.TransformerTeacherStateDimensions);
        Set(
            "TransformerTeacherActionDimensions",
            p.TransformerTeacherActionDimensions);
        Set(
            "TransformerTeacherHiddenDimensions",
            p.TransformerTeacherHiddenDimensions);
        Set("TransformerTeacherLayers", p.TransformerTeacherLayers);
        Set(
            "TransformerTeacherAttentionHeads",
            p.TransformerTeacherAttentionHeads);
        Set(
            "TransformerTeacherFeedForwardDimensions",
            p.TransformerTeacherFeedForwardDimensions);
        Set(
            "TransformerTeacherHistoryLength",
            p.TransformerTeacherHistoryLength);
        Set(
            "TransformerTeacherMinimumFrames",
            p.TransformerTeacherMinimumFrames);
        Set(
            "TransformerTeacherMaximumFrames",
            p.TransformerTeacherMaximumFrames);
        SetToggle(
            "TransformerTeacherEnableWarmStart",
            p.TransformerTeacherEnableWarmStart);
        Set(
            "TransformerTeacherCpuRefreshInterval",
            p.TransformerTeacherCpuRefreshInterval);
        Set("TransformerTeacherCpuEpochs", p.TransformerTeacherCpuEpochs);
        Set(
            "TransformerTeacherCpuIncrementalEpochs",
            p.TransformerTeacherCpuIncrementalEpochs);
        Set(
            "TransformerTeacherCpuFinalEpochs",
            p.TransformerTeacherCpuFinalEpochs);
        SetToggle(
            "TransformerTeacherEnableAdaptiveRefresh",
            p.TransformerTeacherEnableAdaptiveRefresh);
        Set(
            "TransformerTeacherAdaptiveRefreshDriftThreshold",
            p.TransformerTeacherAdaptiveRefreshDriftThreshold);
        SetToggle(
            "TransformerTeacherEnableFixedAnchorValidation",
            p.TransformerTeacherEnableFixedAnchorValidation);
        Set(
            "TransformerTeacherMaximumHeadRegression",
            p.TransformerTeacherMaximumHeadRegression);
        Set(
            "TransformerTeacherIncrementalEpochs",
            p.TransformerTeacherIncrementalEpochs);
        Set(
            "TransformerTeacherFinalEpochs",
            p.TransformerTeacherFinalEpochs);
        Set(
            "TransformerTeacherCpuThreads",
            p.TransformerTeacherCpuThreads);
        Set(
            "TransformerTeacherCpuInteropThreads",
            p.TransformerTeacherCpuInteropThreads);
        Set(
            "TransformerTeacherMicroBatchSize",
            p.TransformerTeacherMicroBatchSize);
        Set(
            "TransformerTeacherDataLoaderWorkers",
            p.TransformerTeacherDataLoaderWorkers);
        Set(
            "TransformerTeacherPrefetchBatches",
            p.TransformerTeacherPrefetchBatches);
        SetToggle(
            "TransformerTeacherEnablePinnedMemory",
            p.TransformerTeacherEnablePinnedMemory);
        SetToggle(
            "TransformerTeacherEnableMixedPrecision",
            p.TransformerTeacherEnableMixedPrecision);
        Set(
            "TransformerDistillationWeight",
            p.TransformerDistillationWeight);
        Set("NormalAcceptanceRate", p.NormalAcceptanceRate);
        Set("AdvancedAcceptanceRate", p.AdvancedAcceptanceRate);
        Set("MinimumArenaDiscordantPairs", p.MinimumArenaDiscordantPairs);
        Set(
            "MaximumOfflineHeadRegression",
            p.MaximumOfflineHeadRegression);
        Set(
            "MaximumStateFeatureCollisionRate",
            p.MaximumStateFeatureCollisionRate);
        Set(
            "MaximumActionFeatureCollisionRate",
            p.MaximumActionFeatureCollisionRate);
        Set("SuccessExpertReplayShare", p.SuccessExpertReplayShare);
        Set("HardSeedReplayShare", p.HardSeedReplayShare);
        Set(
            "MinimumAdvancedReplayShare",
            p.MinimumAdvancedReplayShare);
        Set(
            "MinimumAdvancedDefeatReplayShare",
            p.MinimumAdvancedDefeatReplayShare);
        Set("SelfPlayExplorationProbability", p.SelfPlayExplorationProbability);
        Set("SelfPlayExplorationTemperature", p.SelfPlayExplorationTemperature);
        Set("ArenaInvalidRetryCount", p.ArenaInvalidRetryCount);
        Set("ArenaInvalidRateLimit", p.ArenaInvalidRateLimit);
        Set("TuningNormalCampaigns", p.TuningNormalCampaigns);
        Set("TuningAdvancedCampaigns", p.TuningAdvancedCampaigns);
        Set(
            "TuningScreeningNormalCampaigns",
            p.TuningScreeningNormalCampaigns);
        Set(
            "TuningScreeningAdvancedCampaigns",
            p.TuningScreeningAdvancedCampaigns);
        Set("TuningFinalistCount", p.TuningFinalistCount);
        Set("TuningInterval", p.TuningInterval);
        Set("ArenaEvaluationBatchSize", p.ArenaEvaluationBatchSize);
        Set(
            "MaximumConsecutiveRejectedIterations",
            p.MaximumConsecutiveRejectedIterations);
        Set("RunSeed", p.RunSeed);
        Set("TrainingSeedStart", p.TrainingSeedStart);
        Set("ArenaSeedStart", p.ArenaSeedStart);
        Set("TuningSeedStart", p.TuningSeedStart);
        Set("ValidationSeedStart", p.ValidationSeedStart);
        SetToggle("EnableCurriculum", p.EnableCurriculum);
        SetToggle("EnableStratifiedReplay", p.EnableStratifiedReplay);
        SetToggle("EnablePrioritizedReplay", p.EnablePrioritizedReplay);
        SetToggle("EnableHardSeedCurriculum", p.EnableHardSeedCurriculum);
        SetToggle(
            "EnableCounterfactualHardEncounters",
            p.EnableCounterfactualHardEncounters);
        SetToggle("EnableSuccessCaseArchive", p.EnableSuccessCaseArchive);
        SetToggle("EnableArenaRecovery", p.EnableArenaRecovery);
        SetToggle("EnableTuningArena", p.EnableTuningArena);
        SetToggle(
            "EnableProgressiveTuning",
            p.EnableProgressiveTuning);
        SetToggle("EnableOfflineTuningGate", p.EnableOfflineTuningGate);
        SetToggle("EnableSequentialArenaStop", p.EnableSequentialArenaStop);
        SetToggle("EnableEarlyValidationStop", p.EnableEarlyValidationStop);
        SetToggle("EnableFrameStratification", p.EnableFrameStratification);
        SetToggle(
            "EnableEndTurnSpecialization",
            p.EnableEndTurnSpecialization);
    }

    private void PullGameSubjectFromUi()
    {
        var subject = settings.GameSubject?.Clone()
                      ?? new CombatGameSubjectPreset();
        subject.Id = gamePresetIdInput.Text;
        subject.DisplayName = gamePresetNameInput.Text;
        subject.RoleId = Convert.ToString(
                             roleInput.SelectedValue,
                             CultureInfo.InvariantCulture)
                         ?? subject.RoleId;
        subject.PartnerId = Convert.ToString(
                                familiarInput.SelectedValue,
                                CultureInfo.InvariantCulture)
                            ?? subject.PartnerId;
        subject.PreferredDeckSizeMinimum = ParseBoundedInt(
            preferredDeckMinimumInput.Text,
            "牌组倾向下限",
            1,
            80);
        subject.PreferredDeckSizeMaximum = ParseBoundedInt(
            preferredDeckMaximumInput.Text,
            "牌组倾向上限",
            1,
            80);
        if (subject.PreferredDeckSizeMaximum
            < subject.PreferredDeckSizeMinimum)
        {
            throw new FormatException("牌组倾向上限不能小于下限");
        }
        subject.EnabledRewardCardPackIds = cardPackToggles
            .Where(item => item.Value.IsChecked == true)
            .Select(item => item.Key)
            .ToList();
        gameSubjectCatalog.ResolveReferences(subject);
        settings.GameSubject = subject.Normalize();
    }

    private void ApplyGameSubjectToUi()
    {
        var subject = settings.GameSubject ?? new CombatGameSubjectPreset();
        subject.Normalize();
        gamePresetIdInput.Text = subject.Id;
        gamePresetNameInput.Text = subject.DisplayName;
        preferredDeckMinimumInput.Text =
            subject.PreferredDeckSizeMinimum.ToString(
                CultureInfo.InvariantCulture);
        preferredDeckMaximumInput.Text =
            subject.PreferredDeckSizeMaximum.ToString(
                CultureInfo.InvariantCulture);

        roleInput.ItemsSource = Choices(
            gameSubjectCatalog.Roles.Select(item =>
                new GameSubjectChoice(
                    item.Id,
                    item.DisplayName + "  [" + item.Id + "]")),
            subject.RoleId);
        roleInput.SelectedValue = subject.RoleId;
        familiarInput.ItemsSource = Choices(
            gameSubjectCatalog.Familiars.Select(item =>
                new GameSubjectChoice(
                    item.Id,
                    item.DisplayName + "  [" + item.Id + "]")),
            subject.PartnerId);
        familiarInput.SelectedValue = subject.PartnerId;

        cardPackPanel.Children.Clear();
        cardPackToggles.Clear();
        var packs = gameSubjectCatalog.CardPacks.ToList();
        foreach (var selectedId in subject.EnabledRewardCardPackIds)
        {
            if (packs.All(item => !string.Equals(
                    item.Id,
                    selectedId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                packs.Add(new CombatGameSubjectCardPack
                {
                    Id = selectedId,
                    DisplayName = selectedId
                }.Normalize());
            }
        }
        foreach (var pack in packs)
        {
            var enabled = pack.Required
                          || subject.EnabledRewardCardPackIds.Contains(
                              pack.Id,
                              StringComparer.OrdinalIgnoreCase);
            var toggle = new CheckBox
            {
                Content = pack.DisplayName + "  [" + pack.Id + "]",
                IsChecked = enabled,
                IsEnabled = !pack.Required,
                Width = 285,
                Margin = new Thickness(0, 2, 10, 2),
                Foreground = pack.Required
                    ? TrainerTheme.Muted
                    : TrainerTheme.Text
            };
            cardPackToggles[pack.Id] = toggle;
            cardPackPanel.Children.Add(toggle);
        }
        gameSubjectStatus.Text = gameSubjectCatalog.Roles.Count > 0
            ? "目录已加载：角色 "
              + gameSubjectCatalog.Roles.Count
              + " · 使魔 "
              + gameSubjectCatalog.Familiars.Count
              + " · 奖励卡包 "
              + gameSubjectCatalog.CardPacks.Count
              + "；当前预设将保存到控制台设置。"
            : "游戏主体目录缺失；当前只能保留已有预设标识。";
        gameSubjectStatus.Foreground =
            gameSubjectCatalog.Roles.Count > 0
                ? TrainerTheme.Success
                : TrainerTheme.Warning;
    }

    private static List<GameSubjectChoice> Choices(
        IEnumerable<GameSubjectChoice> source,
        string selectedId)
    {
        var result = source
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (!string.IsNullOrWhiteSpace(selectedId)
            && result.All(item => !string.Equals(
                item.Id,
                selectedId,
                StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new GameSubjectChoice(selectedId, selectedId));
        }
        return result;
    }

    private static int ParseBoundedInt(
        string value,
        string label,
        int minimum,
        int maximum)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new FormatException(
                label + " 必须在 " + minimum + "–" + maximum + " 之间");
        }
        return parsed;
    }

    private bool ValidateEnvironment(bool throwOnFailure = false)
    {
        var errors = new List<string>();
        var modRoot = modRootInput.Text.Trim();
        var dataRoot = dataRootInput.Text.Trim();
        var required = new[]
        {
            Path.Combine(
                modRoot,
                "TrainingWorker",
                "AuraFoundationTrainer.Worker.exe"),
            Path.Combine(
                modRoot,
                "Config",
                "combat-simulation",
                "witch-world-simulation-v2.campaign.json"),
            Path.Combine(
                modRoot,
                "Config",
                "combat-simulation",
                "witch-base-evaluation-v2.ruleset.json"),
            Path.Combine(
                modRoot,
                "Config",
                "combat-simulation",
                "witch-game-subjects-v1.catalog.json")
        };
        errors.AddRange(required.Where(path => !File.Exists(path)));
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            errors.Add("ModsData 目录为空");
        }
        var teacherBackend = Convert.ToString(
                                 transformerBackendInput.SelectedItem,
                                 CultureInfo.InvariantCulture)
                             ?? CombatTransformerTeacherBackendNames.Disabled;
        if (throwOnFailure
            && !string.Equals(
                teacherBackend,
                CombatTransformerTeacherBackendNames.Disabled,
                StringComparison.OrdinalIgnoreCase))
        {
            transformerRuntimeProbe = CombatTransformerRuntimeResolver.Resolve(
                inputs["TransformerPythonExecutable"].Text,
                teacherBackend,
                new[]
                {
                    modRoot,
                    Path.Combine(modRoot, "TrainingWorker")
                });
            if (!transformerRuntimeProbe.Success)
            {
                errors.Add("Transformer 运行时：" + transformerRuntimeProbe.Message);
            }
        }
        var ok = errors.Count == 0;
        environmentStatus.Text = ok
            ? "环境就绪。Worker、固定战役、游戏主体目录和冻结规则集均可用。"
            : "环境未就绪：" + string.Join("；", errors.Take(3));
        environmentStatus.Foreground =
            ok ? TrainerTheme.Success : TrainerTheme.Warning;
        if (!ok && throwOnFailure)
        {
            throw new InvalidOperationException(environmentStatus.Text);
        }
        return ok;
    }

    private CombatFoundationWorkerJob? ReadCurrentJob()
    {
        try
        {
            if (session == null
                || !TryGetFileIdentity(
                    session.JobPath,
                    out var length,
                    out var lastWriteUtc))
            {
                return null;
            }
            if (cachedJob != null
                && string.Equals(
                    cachedJobPath,
                    session.JobPath,
                    StringComparison.OrdinalIgnoreCase)
                && cachedJobLength == length
                && cachedJobLastWriteUtc == lastWriteUtc)
            {
                return cachedJob;
            }

            cachedJob = DeserializeFileStreaming<CombatFoundationWorkerJob>(
                session.JobPath);
            cachedJobPath = session.JobPath;
            cachedJobLength = length;
            cachedJobLastWriteUtc = lastWriteUtc;
            return cachedJob;
        }
        catch
        {
            return null;
        }
    }

    private void ResetPollingCache()
    {
        cachedJobPath = "";
        cachedJobLength = -1;
        cachedJobLastWriteUtc = DateTime.MinValue;
        cachedJob = null;
        presentedResultPath = "";
        presentedResultLength = -1;
        presentedResultLastWriteUtc = DateTime.MinValue;
        presentedResult = null;
        diagnostics.Reset();
    }

    private static bool TryGetFileIdentity(
        string path,
        out long length,
        out DateTime lastWriteUtc)
    {
        length = 0;
        lastWriteUtc = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var info = new FileInfo(path);
        length = info.Length;
        lastWriteUtc = info.LastWriteTimeUtc;
        return true;
    }

    private static ControllerWorkerResultSummary? ReadResultSummaryStreaming(
        string path)
    {
        using var reader = CreateJsonReader(path);
        var serializer = JsonSerializer.CreateDefault();
        var summary = new ControllerWorkerResultSummary();
        if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
        {
            return null;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.EndObject)
            {
                return summary;
            }
            if (reader.TokenType != JsonToken.PropertyName)
            {
                continue;
            }

            var propertyName = Convert.ToString(
                                   reader.Value,
                                   CultureInfo.InvariantCulture)
                               ?? "";
            if (!reader.Read())
            {
                return null;
            }
            switch (propertyName)
            {
                case nameof(ControllerWorkerResultSummary.SchemaVersion):
                    summary.SchemaVersion = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.JobId):
                    summary.JobId = Convert.ToString(
                                        reader.Value,
                                        CultureInfo.InvariantCulture)
                                    ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.Success):
                    summary.Success = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.WorkerCompleted):
                    summary.WorkerCompleted = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.TrainingSucceeded):
                    summary.TrainingSucceeded = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.ModelAccepted):
                    summary.ModelAccepted = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.EpochsExecuted):
                    summary.EpochsExecuted = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.SelectedEpoch):
                    summary.SelectedEpoch = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.BestValidationEpoch):
                    summary.BestValidationEpoch = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.DeploymentSelectedEpoch):
                    summary.DeploymentSelectedEpoch = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.PersistedReplayEpisodes):
                    summary.PersistedReplayEpisodes = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointBytes):
                    summary.CheckpointBytes = Convert.ToInt64(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.Cancelled):
                    summary.Cancelled = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CompletionKind):
                    summary.CompletionKind = Convert.ToString(
                                                 reader.Value,
                                                 CultureInfo.InvariantCulture)
                                             ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.Message):
                    summary.Message = Convert.ToString(
                                          reader.Value,
                                          CultureInfo.InvariantCulture)
                                      ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.Runtime):
                    summary.Runtime = Convert.ToString(
                                          reader.Value,
                                          CultureInfo.InvariantCulture)
                                      ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.RulesetHash):
                    summary.RulesetHash = Convert.ToString(
                                              reader.Value,
                                              CultureInfo.InvariantCulture)
                                          ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.EpisodesPath):
                    summary.EpisodesPath = Convert.ToString(
                                              reader.Value,
                                              CultureInfo.InvariantCulture)
                                          ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointPath):
                    summary.CheckpointPath = Convert.ToString(
                                                reader.Value,
                                                CultureInfo.InvariantCulture)
                                            ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.ModelPackagePath):
                    summary.ModelPackagePath = Convert.ToString(
                                                  reader.Value,
                                                  CultureInfo.InvariantCulture)
                                              ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.TrainingMetricsPath):
                    summary.TrainingMetricsPath = Convert.ToString(
                                                      reader.Value,
                                                      CultureInfo.InvariantCulture)
                                                  ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.TrainingAnalysisPath):
                    summary.TrainingAnalysisPath = Convert.ToString(
                                                       reader.Value,
                                                       CultureInfo.InvariantCulture)
                                                   ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.TrainingMetricWriteFailures):
                    summary.TrainingMetricWriteFailures = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.TrainingMetricWarning):
                    summary.TrainingMetricWarning = Convert.ToString(
                                                        reader.Value,
                                                        CultureInfo.InvariantCulture)
                                                    ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.RoleStrategyMetrics):
                    summary.RoleStrategyMetrics =
                        serializer.Deserialize<Dictionary<string, double>>(
                            reader)
                        ?? new Dictionary<string, double>(
                            StringComparer.OrdinalIgnoreCase);
                    break;
                case nameof(ControllerWorkerResultSummary.RoleStrategyGatePassed):
                    summary.RoleStrategyGatePassed = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.RoleStrategyGateFailureReason):
                    summary.RoleStrategyGateFailureReason = Convert.ToString(
                                                                reader.Value,
                                                                CultureInfo.InvariantCulture)
                                                            ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.ResumeRequested):
                    summary.ResumeRequested = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.ResumedFromCheckpoint):
                    summary.ResumedFromCheckpoint = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.ResumeDiagnostic):
                    summary.ResumeDiagnostic = Convert.ToString(
                                                   reader.Value,
                                                   CultureInfo.InvariantCulture)
                                               ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.RequestedStartMode):
                    summary.RequestedStartMode = Convert.ToString(
                                                     reader.Value,
                                                     CultureInfo.InvariantCulture)
                                                 ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.EffectiveStartMode):
                    summary.EffectiveStartMode = Convert.ToString(
                                                     reader.Value,
                                                     CultureInfo.InvariantCulture)
                                                 ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.Resumable):
                    summary.Resumable = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointWriteFailures):
                    summary.CheckpointWriteFailures = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointWarning):
                    summary.CheckpointWarning = Convert.ToString(
                                                   reader.Value,
                                                   CultureInfo.InvariantCulture)
                                               ?? "";
                    break;
                case nameof(ControllerWorkerResultSummary.EffectiveCheckpointSerializationParallelism):
                    summary.EffectiveCheckpointSerializationParallelism =
                        Convert.ToInt32(
                            reader.Value,
                            CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointSerializationAutoScaled):
                    summary.CheckpointSerializationAutoScaled =
                        Convert.ToBoolean(
                            reader.Value,
                            CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointSerializationSeconds):
                    summary.CheckpointSerializationSeconds =
                        Convert.ToDouble(
                            reader.Value,
                            CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointWritesEnqueued):
                    summary.CheckpointWritesEnqueued = Convert.ToInt64(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointWritesExecuted):
                    summary.CheckpointWritesExecuted = Convert.ToInt64(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.CheckpointWritesCoalesced):
                    summary.CheckpointWritesCoalesced = Convert.ToInt64(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerWorkerResultSummary.Training):
                    summary.Training = ReadTrainingSummary(
                        reader,
                        serializer);
                    return summary;
                default:
                    reader.Skip();
                    break;
            }
        }
        return summary;
    }

    private static ControllerTrainingResultSummary? ReadTrainingSummary(
        JsonTextReader reader,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }
        if (reader.TokenType != JsonToken.StartObject)
        {
            reader.Skip();
            return null;
        }

        var summary = new ControllerTrainingResultSummary();
        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.EndObject)
            {
                return summary;
            }
            if (reader.TokenType != JsonToken.PropertyName)
            {
                continue;
            }

            var propertyName = Convert.ToString(
                                   reader.Value,
                                   CultureInfo.InvariantCulture)
                               ?? "";
            if (!reader.Read())
            {
                return summary;
            }
            switch (propertyName)
            {
                case nameof(ControllerTrainingResultSummary.Success):
                    summary.Success = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.AcceptancePassed):
                    summary.AcceptancePassed = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.Message):
                    summary.Message = Convert.ToString(
                                          reader.Value,
                                          CultureInfo.InvariantCulture)
                                      ?? "";
                    break;
                case nameof(ControllerTrainingResultSummary.GeneratedReplayEpisodes):
                    summary.GeneratedReplayEpisodes = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.PersistedReplayEpisodes):
                    summary.PersistedReplayEpisodes = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.SemanticGatePassed):
                    summary.SemanticGatePassed = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.SemanticRejectedCampaigns):
                    summary.SemanticRejectedCampaigns = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.DiscardedSemanticEpisodes):
                    summary.DiscardedSemanticEpisodes = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.SemanticGateFailureReason):
                    summary.SemanticGateFailureReason = Convert.ToString(
                                                           reader.Value,
                                                           CultureInfo.InvariantCulture)
                                                       ?? "";
                    break;
                case nameof(ControllerTrainingResultSummary.LoadedExpertReplayEpisodes):
                    summary.LoadedExpertReplayEpisodes = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ExpertReplaySelection):
                    summary.ExpertReplaySelection =
                        serializer.Deserialize<CombatFoundationExpertReplaySelection>(
                            reader)
                        ?? new CombatFoundationExpertReplaySelection();
                    break;
                case nameof(ControllerTrainingResultSummary.RewardResidualTraining):
                    summary.RewardResidualTraining =
                        serializer
                            .Deserialize<CombatFoundationRewardResidualTrainingResult>(
                                reader)
                        ?? new CombatFoundationRewardResidualTrainingResult();
                    break;
                case nameof(ControllerTrainingResultSummary.Iterations):
                    summary.Iterations =
                        serializer
                            .Deserialize<List<CombatCampaignFoundationIteration>>(
                                reader)
                        ?? new List<CombatCampaignFoundationIteration>();
                    break;
                case nameof(ControllerTrainingResultSummary.Validation):
                    summary.Validation =
                        serializer.Deserialize<CombatCampaignFoundationValidation>(
                            reader)
                        ?? new CombatCampaignFoundationValidation();
                    break;
                case nameof(ControllerTrainingResultSummary.Preflight):
                    summary.Preflight =
                        serializer
                            .Deserialize<CombatCampaignFoundationIntegrityReport>(
                                reader)
                        ?? new CombatCampaignFoundationIntegrityReport();
                    break;
                case nameof(ControllerTrainingResultSummary.CapabilityProbe):
                    summary.CapabilityProbe =
                        serializer.Deserialize<CombatFoundationCapabilityProbe>(
                            reader)
                        ?? new CombatFoundationCapabilityProbe();
                    break;
                case nameof(ControllerTrainingResultSummary.InvalidTrainingCampaigns):
                    summary.InvalidTrainingCampaigns = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.TerminalConsistencyViolations):
                    summary.TerminalConsistencyViolations = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.FeatureLeakageViolations):
                    summary.FeatureLeakageViolations = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.TrainingFailureCounts):
                    summary.TrainingFailureCounts =
                        serializer.Deserialize<Dictionary<string, int>>(reader)
                        ?? new Dictionary<string, int>(
                            StringComparer.OrdinalIgnoreCase);
                    break;
                case nameof(ControllerTrainingResultSummary.TrainingFailures):
                    summary.TrainingFailures =
                        serializer
                            .Deserialize<
                                List<CombatCampaignFoundationIntegrityFailure>>(
                                reader)
                        ?? new List<CombatCampaignFoundationIntegrityFailure>();
                    break;
                case nameof(ControllerTrainingResultSummary.AuthoritativeSelectedActionsAudited):
                    summary.AuthoritativeSelectedActionsAudited =
                        Convert.ToInt64(
                            reader.Value,
                            CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.AuthoritativeSelectedSemanticMismatches):
                    summary.AuthoritativeSelectedSemanticMismatches =
                        Convert.ToInt64(
                            reader.Value,
                            CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.AuthoritativeTeacherOverrides):
                    summary.AuthoritativeTeacherOverrides = Convert.ToInt64(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.RootMaximumVisitShareMean):
                    summary.RootMaximumVisitShareMean = Convert.ToDouble(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ModelCompletedEpochs):
                    summary.ModelCompletedEpochs = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ModelConfiguredEpochs):
                    summary.ModelConfiguredEpochs = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ModelBestEpoch):
                    summary.ModelBestEpoch = Convert.ToInt32(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ModelEarlyStopped):
                    summary.ModelEarlyStopped = Convert.ToBoolean(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ModelTrainingLoss):
                    summary.ModelTrainingLoss = Convert.ToDouble(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ModelValidationLoss):
                    summary.ModelValidationLoss = Convert.ToDouble(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ModelBestValidationLoss):
                    summary.ModelBestValidationLoss = Convert.ToDouble(
                        reader.Value,
                        CultureInfo.InvariantCulture);
                    break;
                case nameof(ControllerTrainingResultSummary.ModelEpochHistory):
                    summary.ModelEpochHistory =
                        serializer
                            .Deserialize<List<CombatPolicyValueEpochMetrics>>(
                                reader)
                        ?? new List<CombatPolicyValueEpochMetrics>();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return summary;
    }

    private static T? DeserializeFileStreaming<T>(string path)
    {
        using var reader = CreateJsonReader(path);
        return JsonSerializer.CreateDefault().Deserialize<T>(reader);
    }

    private static JsonTextReader CreateJsonReader(string path)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.SequentialScan);
        var textReader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        return new JsonTextReader(textReader)
        {
            CloseInput = true
        };
    }

    private bool IsWorkerRunning()
    {
        if (workerProcess != null)
        {
            try
            {
                return !workerProcess.HasExited;
            }
            catch
            {
            }
        }
        if (session?.ProcessId > 0)
        {
            try
            {
                var process = Process.GetProcessById(session.ProcessId);
                return !process.HasExited
                       && string.Equals(
                           process.ProcessName,
                           "AuraFoundationTrainer.Worker",
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
            }
        }
        return false;
    }

    private void OpenRunDirectory()
    {
        var path = session?.ResultDirectory ?? settings.LastRunDirectory;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = TrainingResultsRoot(settings.DataRoot);
        }
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void BrowseModRoot()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 AuraToolsExp MOD 目录",
            InitialDirectory = Directory.Exists(modRootInput.Text)
                ? modRootInput.Text
                : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) == true)
        {
            modRootInput.Text = dialog.FolderName;
            gameSubjectCatalog = LoadGameSubjectCatalog(dialog.FolderName);
            gameSubjectCatalog.ResolveReferences(settings.GameSubject);
            ApplyGameSubjectToUi();
            ValidateEnvironment();
        }
    }

    private void BrowseDataRoot()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 ModsData 目录",
            InitialDirectory = Directory.Exists(dataRootInput.Text)
                ? dataRootInput.Text
                : Environment.CurrentDirectory
        };
        if (dialog.ShowDialog(this) == true)
        {
            dataRootInput.Text = dialog.FolderName;
            ValidateEnvironment();
        }
    }

    private string? ResolveArgument(string name)
    {
        for (var i = 0; i < launchArguments.Length - 1; i++)
        {
            if (string.Equals(
                    launchArguments[i],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(launchArguments[i + 1].Trim('"'));
            }
        }
        return null;
    }

    private static string DiscoverModRoot()
    {
        var executableDirectory = ExecutableDirectory();
        for (DirectoryInfo? current = new DirectoryInfo(executableDirectory);
             current != null;
             current = current.Parent)
        {
            if (IsModRoot(current.FullName))
            {
                return current.FullName;
            }
            var child = Path.Combine(current.FullName, "AuraToolsExp");
            if (IsModRoot(child))
            {
                return Path.GetFullPath(child);
            }
        }

        return Directory.GetParent(executableDirectory)?.FullName
               ?? executableDirectory;
    }

    private static string ExecutableDirectory()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            return Path.GetDirectoryName(Path.GetFullPath(executablePath))
                   ?? Path.GetFullPath(AppContext.BaseDirectory);
        }
        return Path.GetFullPath(AppContext.BaseDirectory);
    }

    private static bool IsModRoot(string path)
    {
        return File.Exists(Path.Combine(
            path,
            "Config",
            "combat-simulation",
            "witch-world-simulation-v2.campaign.json"));
    }

    private static string DiscoverDataRoot(string modRoot)
    {
        var parent = Directory.GetParent(modRoot);
        if (parent != null
            && string.Equals(
                parent.Name,
                "Mods",
                StringComparison.OrdinalIgnoreCase)
            && parent.Parent != null)
        {
            return Path.GetFullPath(
                Path.Combine(parent.Parent.FullName, "ModsData"));
        }
        return Path.GetFullPath(Path.Combine(
            Directory.GetParent(modRoot)?.FullName ?? modRoot,
            "ModsData"));
    }

    private static CombatDecisionProfile BuildProfile(string profileId)
    {
        var profile = new CombatDecisionProfile
        {
            Id = profileId,
            SearchBudgetMode = "dynamic",
            SearchQuality = "deep",
            SearchMinimumSimulations = 64,
            SearchStabilityWindow = 32,
            SearchStableChecks = 2
        };
        if (profileId == "aggressive")
        {
            profile.Weights.Lethal = 2.1d;
            profile.Weights.Tempo = 1.25d;
            profile.Weights.Survival = 0.85d;
            profile.ThreatRiskTolerance = 0.35d;
            profile.DeathRiskLimit = 0.12d;
            profile.TailRiskPenalty = 22d;
        }
        else if (profileId == "defensive")
        {
            profile.Weights.Survival = 1.9d;
            profile.Weights.Risk = -1.6d;
            profile.Weights.Lethal = 1.15d;
            profile.ThreatRiskTolerance = 0.9d;
            profile.SurplusDefendRetention = 0.1d;
            profile.DeathRiskLimit = 0.02d;
            profile.TailRiskPenalty = 55d;
        }
        return profile;
    }

    private static ulong GenerateRunSeed()
    {
        var bytes = new byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt64(bytes, 0);
        return value == 0UL ? 1UL : value;
    }

    private static string SettingsPath(string dataRoot)
    {
        return Path.Combine(
            AuraSharedRoot(dataRoot),
            "Config",
            "Owners",
            "AuraToolsExp",
            "FoundationTrainer",
            "controller-settings.json");
    }

    private static string SessionPath(string dataRoot)
    {
        return Path.Combine(
            AuraSharedRoot(dataRoot),
            "Logs",
            "AuraToolsExp",
            "FoundationTrainer",
            "controller-session.json");
    }

    private static string TrainingResultsRoot(string dataRoot)
    {
        return Path.Combine(
            AuraSharedRoot(dataRoot),
            "Logs",
            "AuraToolsExp",
            "FoundationTrainer",
            "combat-simulation-results");
    }

    private static string AuraSharedRoot(string dataRoot)
    {
        return Path.Combine(dataRoot, "AuraShared");
    }

    private static string LegacySettingsPath(string dataRoot)
    {
        return Path.Combine(
            dataRoot,
            "Config",
            "Owners",
            "AuraToolsExp",
            "FoundationTrainer",
            "controller-settings.json");
    }

    private static string LegacySessionPath(string dataRoot)
    {
        return Path.Combine(
            dataRoot,
            "Logs",
            "AuraToolsExp",
            "foundation-controller-session.json");
    }

    private void SetIdleButtons()
    {
        startButton.IsEnabled = true;
        freshStartButton.IsEnabled = true;
        continueButton.IsEnabled = !string.IsNullOrWhiteSpace(
            settings.LastRunDirectory);
        selectedCheckpointButton.IsEnabled = checkpointInput.Items.Count > 0;
        cancelButton.IsEnabled = false;
        openButton.IsEnabled = !string.IsNullOrWhiteSpace(
            settings.LastRunDirectory);
    }

    private void AppendLog(string message)
    {
        if (logBox == null)
        {
            return;
        }
        logBox.AppendText(
            (logBox.Text.Length == 0 ? "" : Environment.NewLine)
            + DateTime.Now.ToString("HH:mm:ss")
            + " "
            + message);
        logBox.ScrollToEnd();
    }

    private static string FriendlyStage(string value)
    {
        if ((value ?? "").StartsWith(
                "transformer-teacher",
                StringComparison.Ordinal))
        {
            return "Transformer 教师";
        }
        return value switch
        {
            "preflight" => "权威快检",
            "training" => "课程自博弈",
            "model-training" => "模型拟合",
            "transformer-teacher" => "Transformer 教师",
            "arena" => "竞技场",
            "validation" => "隔离验证",
            _ => string.IsNullOrWhiteSpace(value) ? "准备中" : value
        };
    }

    private static string FriendlyTeacherStage(string value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "exporting" => "导出数据",
            "launching" => "启动运行时",
            "configuring" => "配置运行时",
            "loading" => "读取数据",
            "indexing" => "建立序列索引",
            "preparing" => "张量化",
            "merging" => "合并累计语料",
            "merged" => "累计语料就绪",
            "calibrating" => "自动调优",
            "training" => "模型训练",
            "evaluating" => "离线评估",
            "annotating" => "生成标注",
            "saving" => "保存模型",
            "completed" => "已完成",
            _ => "准备中"
        };
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0d || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "--";
        }
        return TimeSpan.FromSeconds(seconds).ToString(
            seconds >= 3600d ? @"hh\:mm\:ss" : @"mm\:ss");
    }

    private TextBox AddPathRow(
        Panel panel,
        string label,
        Action browse)
    {
        var row = NewRow();
        row.Children.Add(Label(label, 170));
        var input = Input(500);
        row.Children.Add(input);
        var button = ActionButton("选择", browse);
        row.Children.Add(button);
        panel.Children.Add(row);
        return input;
    }

    private void AddProfileSelect(Panel panel)
    {
        var row = NewRow();
        row.Children.Add(Label("决策风格", 240));
        foreach (var choice in new[]
                 {
                     new ProfileChoice("balanced", "均衡"),
                     new ProfileChoice("aggressive", "进攻"),
                     new ProfileChoice("defensive", "防守")
                 })
        {
            var profileId = choice.Id;
            var button = ActionButton(
                choice.Label,
                () => SelectProfile(profileId));
            button.MinWidth = 72;
            profileButtons[profileId] = button;
            row.Children.Add(button);
        }
        panel.Children.Add(row);
    }

    private void AddGovernanceProfileSelect(Panel panel)
    {
        var row = NewRow();
        row.Children.Add(Label("训练治理档位", 240));
        governanceProfileInput = new ComboBox
        {
            Width = 180,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = new[]
            {
                CombatFoundationGovernanceProfileNames.Development,
                CombatFoundationGovernanceProfileNames.Release,
                CombatFoundationGovernanceProfileNames.Custom
            },
            SelectedItem = CombatFoundationGovernanceProfileNames.Development,
            ToolTip = "development 减少调优和教师诊断；release 保留完整治理预算"
        };
        row.Children.Add(governanceProfileInput);
        panel.Children.Add(row);
    }

    private void SelectProfile(string? profileId)
    {
        selectedProfile = profileButtons.ContainsKey(profileId ?? "")
            ? profileId!
            : "balanced";
        foreach (var pair in profileButtons)
        {
            pair.Value.Style = TrainerTheme.ButtonStyle(
                pair.Key == selectedProfile
                    ? TrainerButtonTone.Primary
                    : TrainerButtonTone.Secondary);
        }
    }

    private void AddNumber(
        Panel panel,
        string key,
        string label,
        int minimum,
        int maximum)
    {
        var row = NewRow();
        row.Children.Add(Label(label, 240));
        var input = Input(180);
        input.ToolTip = $"范围 {minimum}–{maximum}";
        inputs[key] = input;
        row.Children.Add(input);
        panel.Children.Add(row);
    }

    private void AddDouble(Panel panel, string key, string label)
    {
        AddNumber(panel, key, label, 0, 0);
    }

    private void AddText(Panel panel, string key, string label)
    {
        var row = NewRow();
        row.Children.Add(Label(label, 240));
        var input = Input(360);
        inputs[key] = input;
        row.Children.Add(input);
        panel.Children.Add(row);
    }

    private void AddTransformerPythonRow(Panel panel)
    {
        var row = NewRow();
        row.Children.Add(Label("Python 可执行程序", 240));
        var input = Input(360);
        input.ToolTip = "auto 自动探测 AuraTF、环境变量和 PATH；明确路径视为人工锁定";
        inputs["TransformerPythonExecutable"] = input;
        row.Children.Add(input);
        var browse = ActionButton("选择", BrowseTransformerPython);
        browse.MinWidth = 72;
        row.Children.Add(browse);
        var probe = ActionButton(
            "检测",
            () => _ = RefreshTransformerRuntimeStatusAsync());
        probe.MinWidth = 72;
        row.Children.Add(probe);
        panel.Children.Add(row);
    }

    private void AddTransformerBackendSelect(Panel panel)
    {
        var row = NewRow();
        row.Children.Add(Label("教师计算后端", 240));
        transformerBackendInput = new ComboBox
        {
            Width = 180,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = new[]
            {
                CombatTransformerTeacherBackendNames.Disabled,
                CombatTransformerTeacherBackendNames.Auto,
                CombatTransformerTeacherBackendNames.Cpu,
                CombatTransformerTeacherBackendNames.Cuda
            },
            SelectedItem = CombatTransformerTeacherBackendNames.Auto,
            ToolTip = "auto 在安装 CUDA 版 PyTorch 时使用 GPU，否则使用 CPU"
        };
        transformerBackendInput.SelectionChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                _ = RefreshTransformerRuntimeStatusAsync();
            }
        };
        row.Children.Add(transformerBackendInput);
        panel.Children.Add(row);
    }

    private void BrowseTransformerPython()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Python executable|python.exe;python|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        var configured = inputs["TransformerPythonExecutable"].Text.Trim();
        if (File.Exists(configured))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(configured);
            dialog.FileName = Path.GetFileName(configured);
        }
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        inputs["TransformerPythonExecutable"].Text = dialog.FileName;
        _ = RefreshTransformerRuntimeStatusAsync();
    }

    private async Task RefreshTransformerRuntimeStatusAsync()
    {
        if (transformerRuntimeStatus == null
            || !inputs.TryGetValue(
                "TransformerPythonExecutable",
                out var pythonInput))
        {
            return;
        }
        var configured = pythonInput.Text.Trim();
        var backend = Convert.ToString(
                          transformerBackendInput.SelectedItem,
                          CultureInfo.InvariantCulture)
                      ?? CombatTransformerTeacherBackendNames.Disabled;
        if (string.Equals(
                backend,
                CombatTransformerTeacherBackendNames.Disabled,
                StringComparison.OrdinalIgnoreCase))
        {
            transformerRuntimeProbe = null;
            transformerRuntimeStatus.Text = "Transformer 教师已禁用。";
            transformerRuntimeStatus.Foreground = TrainerTheme.Muted;
            return;
        }
        transformerRuntimeStatus.Text = "正在检测 Python、PyTorch 与计算设备……";
        transformerRuntimeStatus.Foreground = TrainerTheme.Muted;
        var modRoot = modRootInput?.Text?.Trim() ?? "";
        var roots = new[]
        {
            modRoot,
            string.IsNullOrWhiteSpace(modRoot)
                ? ""
                : Path.Combine(modRoot, "TrainingWorker")
        };
        var probe = await Task.Run(() =>
            CombatTransformerRuntimeResolver.Resolve(
                configured,
                backend,
                roots));
        transformerRuntimeProbe = probe;
        transformerRuntimeStatus.Text =
            CombatTransformerRuntimeResolver.DisplayText(probe);
        transformerRuntimeStatus.Foreground = probe.Success
            ? TrainerTheme.Success
            : TrainerTheme.Warning;
    }

    private void AddGradientShardSelect(Panel panel)
    {
        var row = NewRow();
        row.Children.Add(Label("梯度并行分片（0 自动）", 240));
        gradientShardInput = new ComboBox
        {
            Width = 180,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = GradientShardPresets,
            SelectedItem = 0,
            ToolTip = "0 根据有效 CPU 并行度与 Batch 自动选择"
        };
        row.Children.Add(gradientShardInput);
        panel.Children.Add(row);
    }

    private void SetGradientShardCount(int value)
    {
        gradientShardInput.SelectedItem = GradientShardPresets.Contains(value)
            ? value
            : 0;
    }

    private void AddExecutionProfileSelect(Panel panel)
    {
        var row = NewRow();
        row.Children.Add(Label("CPU 执行档位", 240));
        parallelismProfileInput = new ComboBox
        {
            Width = 180,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = new[]
            {
                CombatFoundationExecutionProfileNames.Auto,
                CombatFoundationExecutionProfileNames.Cpu16,
                CombatFoundationExecutionProfileNames.Cpu32,
                CombatFoundationExecutionProfileNames.Custom
            },
            SelectedItem = CombatFoundationExecutionProfileNames.Auto,
            ToolTip = "auto 根据逻辑处理器数选择 CPU-16 或 CPU-32"
        };
        parallelismProfileInput.SelectionChanged += (_, _) =>
        {
            ResetAutomaticExecutionOverride("InferenceParallelism");
            ResetAutomaticExecutionOverride(
                "ThreadPoolMinimumWorkerThreads");
            ResetAutomaticExecutionOverride(
                "CheckpointSerializationParallelism");
        };
        row.Children.Add(parallelismProfileInput);
        panel.Children.Add(row);
    }

    private void ResetAutomaticExecutionOverride(string key)
    {
        if (inputs.TryGetValue(key, out var input))
        {
            input.Text = "0";
        }
    }

    private void AddInferenceModeSelect(Panel panel)
    {
        var row = NewRow();
        row.Children.Add(Label("推理执行模式", 240));
        inferenceModeInput = new ComboBox
        {
            Width = 180,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = new[]
            {
                CombatFoundationExecutionProfileNames.DirectInference,
                CombatFoundationExecutionProfileNames.ShardedBatchInference
            },
            SelectedItem =
                CombatFoundationExecutionProfileNames.ShardedBatchInference,
            ToolTip = "sharded-batch 降低并发推理争用；direct 适合低并发诊断"
        };
        row.Children.Add(inferenceModeInput);
        panel.Children.Add(row);
    }

    private void AddAutoTuneObjectiveSelect(Panel panel)
    {
        var row = NewRow();
        row.Children.Add(Label("Auto-Tune 目标", 240));
        autoTuneObjectiveInput = new ComboBox
        {
            Width = 180,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            ItemsSource = new[]
            {
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput,
                CombatFoundationAutoTuneObjectiveNames.BalancedEfficiency
            },
            SelectedItem =
                CombatFoundationAutoTuneObjectiveNames.MaximumThroughput,
            ToolTip = "maximum-throughput 优先缩短训练墙钟时间"
        };
        row.Children.Add(autoTuneObjectiveInput);
        panel.Children.Add(row);
    }

    private void AddUlong(Panel panel, string key, string label)
    {
        AddNumber(panel, key, label, 0, 0);
    }

    private void AddToggle(Panel panel, string key, string label)
    {
        var check = new CheckBox
        {
            Content = label,
            Margin = new Thickness(0, 4, 0, 4),
            Foreground = TrainerTheme.Text
        };
        toggles[key] = check;
        panel.Children.Add(check);
    }

    private static StackPanel NewRow()
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private static TextBlock Section(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrainerTheme.Accent,
            Margin = new Thickness(0, 18, 0, 8)
        };
    }

    private static TextBlock Label(string text, double width)
    {
        return new TextBlock
        {
            Text = text,
            Width = width,
            Foreground = TrainerTheme.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static TextBox Input(double width)
    {
        return new TextBox
        {
            Width = width,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private static ComboBox ChoiceInput(double width)
    {
        return new ComboBox
        {
            Width = width,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            DisplayMemberPath = nameof(GameSubjectChoice.Label),
            SelectedValuePath = nameof(GameSubjectChoice.Id)
        };
    }

    private static Button ActionButton(
        string text,
        Action action,
        TrainerButtonTone tone = TrainerButtonTone.Secondary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 94,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Style = TrainerTheme.ButtonStyle(tone)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock Hint(Panel panel, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = TrainerTheme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };
        panel.Children.Add(block);
        return block;
    }

    private static TextBlock ProgressText()
    {
        return new TextBlock
        {
            Foreground = TrainerTheme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private sealed record ProfileChoice(string Id, string Label);

    private sealed record GameSubjectChoice(string Id, string Label);

    private int Int(string key)
    {
        return int.TryParse(inputs[key].Text, out var value)
            ? value
            : throw new FormatException(inputs[key].Text + " 不是有效整数");
    }

    private ulong Ulong(string key)
    {
        return ulong.TryParse(inputs[key].Text, out var value)
            ? value
            : throw new FormatException(inputs[key].Text + " 不是有效无符号整数");
    }

    private double Double(string key)
    {
        return double.TryParse(
            inputs[key].Text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : throw new FormatException(inputs[key].Text + " 不是有效小数");
    }

    private bool Toggle(string key)
    {
        return toggles[key].IsChecked == true;
    }

    private void Set(string key, object value)
    {
        if (inputs.TryGetValue(key, out var input))
        {
            input.Text = Convert.ToString(
                             value,
                             CultureInfo.InvariantCulture)
                         ?? "";
        }
    }

    private void SetToggle(string key, bool value)
    {
        if (toggles.TryGetValue(key, out var toggle))
        {
            toggle.IsChecked = value;
        }
    }

    private static T? Deserialize<T>(string json)
    {
        return JsonConvert.DeserializeObject<T>(json);
    }

    private static string Serialize(object value)
    {
        return JsonConvert.SerializeObject(
            value,
            Formatting.Indented,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
    }

    private static void WriteAtomic(string path, string contents)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("输出目录不存在"));
        var temporary = fullPath + ".tmp-" + Environment.ProcessId;
        File.WriteAllText(temporary, contents, new UTF8Encoding(false));
        File.Move(temporary, fullPath, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }
}
