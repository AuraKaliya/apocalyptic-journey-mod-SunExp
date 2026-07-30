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
    private Button startButton = null!;
    private Button cancelButton = null!;
    private Button continueButton = null!;
    private Button openButton = null!;
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
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            () => parametersScroll.ScrollToTop(),
            DispatcherPriority.ContextIdle);
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
        AddNumber(
            panel,
            "CapabilityProbeCampaignsPerDifficulty",
            "能力探针/难度",
            0,
            64);
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
            "能力探针最少深度增益");
        AddNumber(panel, "MaximumDegreeOfParallelism", "CPU 并行度", 1, 64);

        panel.Children.Add(Section("模型训练"));
        AddNumber(panel, "ModelEpochs", "最大 Epoch", 5, 200);
        AddNumber(panel, "ModelMinimumEpochs", "最小 Epoch", 1, 200);
        AddNumber(panel, "ModelEarlyStoppingPatience", "早停耐心", 1, 30);
        AddDouble(panel, "ModelEarlyStoppingMinimumDelta", "早停最小增益");
        AddNumber(panel, "ModelBatchSize", "Minibatch", 8, 512);
        AddNumber(panel, "MinimumEpisodes", "最少训练 Episodes", 2, 1000);
        AddNumber(panel, "ModelReplayEpisodeLimit", "Replay 上限", 64, 20000);
        AddNumber(panel, "ModelRetainedCandidates", "Top-K 候选", 1, 5);
        AddToggle(panel, "EnableFrameStratification", "启用帧分层再平衡");
        AddDouble(panel, "ModelMaximumFrameStratumWeight", "帧分层最大权重");
        AddDouble(panel, "ModelLearningRate", "学习率");
        AddNumber(panel, "ModelMaximumFramesPerEpisode", "Frames per episode", 8, 512);
        AddDouble(panel, "ModelL2", "L2");
        AddNumber(panel, "ModelStateDimensions", "状态维度", 16, 512);
        AddNumber(panel, "ModelActionDimensions", "动作维度", 16, 512);
        AddNumber(panel, "ModelHiddenDimensions", "隐藏维度", 8, 256);

        panel.Children.Add(Section("课程、探索与验收"));
        AddToggle(panel, "EnableCurriculum", "启用课程难度");
        AddToggle(panel, "EnableStratifiedReplay", "启用分层回放");
        AddToggle(panel, "EnableHardSeedCurriculum", "启用困难种子课程");
        AddToggle(
            panel,
            "EnableCounterfactualHardEncounters",
            "启用困难遭遇反事实教师");
        AddToggle(panel, "EnableSuccessCaseArchive", "启用成功案例库");
        AddToggle(panel, "EnableArenaRecovery", "启用竞技场恢复");
        AddToggle(panel, "EnableTuningArena", "启用 Top-K 调优竞技场");
        AddToggle(panel, "EnableEarlyValidationStop", "启用验证提前停止");
        AddNumber(panel, "ArenaInvalidRetryCount", "无效竞技场重试", 0, 3);
        AddDouble(panel, "ArenaInvalidRateLimit", "无效竞技场率上限");
        AddNumber(panel, "TuningNormalCampaigns", "普通调优冒险", 0, 64);
        AddNumber(panel, "TuningAdvancedCampaigns", "高级调优冒险", 0, 64);
        AddNumber(
            panel,
            "MaximumConsecutiveRejectedIterations",
            "连续拒绝停止阈值",
            0,
            8);
        AddDouble(panel, "NormalAcceptanceRate", "普通验收率");
        AddDouble(panel, "AdvancedAcceptanceRate", "高级验收率");
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

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 16, 0, 20)
        };
        startButton = ActionButton(
            "开始 / 恢复训练",
            StartTraining,
            TrainerButtonTone.Primary);
        continueButton = ActionButton("以上轮 Champion 继续", ContinueTraining);
        cancelButton = ActionButton(
            "安全取消",
            CancelTraining,
            TrainerButtonTone.Danger);
        openButton = ActionButton("打开运行目录", OpenRunDirectory);
        actions.Children.Add(startButton);
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
            StartWorker(initialChampion: null, continueGeneration: false);
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

        var message =
            "检测到上一轮可恢复检查点与当前训练参数不同。"
            + Environment.NewLine
            + "为避免把不同目标混入同一权重，本轮将从新模型开始，成功案例库仍会复用。"
            + Environment.NewLine
            + Environment.NewLine
            + string.Join(Environment.NewLine, differences.Take(8))
            + Environment.NewLine
            + Environment.NewLine
            + "是否按当前参数开始全新训练？";
        return MessageBox.Show(
                   this,
                   message,
                   "训练参数已变化",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning)
               == MessageBoxResult.Yes;
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
            var champion = result?.Training?.Champion;
            if (champion == null
                || !string.Equals(
                    result!.CompletionKind,
                    "training-accepted",
                    StringComparison.Ordinal))
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
            StartWorker(champion, continueGeneration: true);
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
        bool continueGeneration)
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
        var resultsRoot = Path.Combine(
            settings.DataRoot,
            "Logs",
            "AuraToolsExp",
            "combat-simulation-results");
        var resultDirectory = Path.Combine(resultsRoot, jobId);
        Directory.CreateDirectory(resultDirectory);
        var checkpointRoot = Path.Combine(
            resultsRoot,
            "foundation-controller-checkpoint");
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
        var jobPath = Path.Combine(resultDirectory, "foundation-worker-job.json");
        WriteAtomic(jobPath, Serialize(job));
        TryDelete(job.CancellationPath);

        workerProcess = Process.Start(new ProcessStartInfo
        {
            FileName = workerPath,
            Arguments = "--job \"" + jobPath.Replace("\"", "\\\"") + "\"",
            WorkingDirectory = Path.GetDirectoryName(workerPath)!,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Worker 进程未能启动");
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
            "训练已启动："
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
        continueButton.IsEnabled = !running;
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
                    PresentTelemetry(progress.Telemetry, running);
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
        bool running)
    {
        runStatus.Text = (running ? "运行中 · " : "")
                         + FriendlyStage(telemetry.Stage)
                         + " · 第 "
                         + telemetry.Iteration
                         + "/"
                         + telemetry.TotalIterations
                         + " 轮";
        runStatus.Foreground =
            running ? TrainerTheme.Accent : TrainerTheme.Text;
        var total = Math.Max(1, telemetry.RequestedCampaigns);
        progressBar.Value = Math.Max(
            0,
            Math.Min(100, telemetry.CompletedCampaigns * 100d / total));
        progressPrimary.Text =
            $"冒险 {telemetry.CompletedCampaigns}/{telemetry.RequestedCampaigns} · "
            + $"战斗 {telemetry.CompletedBattles} · 深度 "
            + $"{telemetry.MaximumActiveBattleDepth}/{telemetry.MaximumCompletedBattleDepth}/37";
        progressSecondary.Text =
            $"Epoch {telemetry.ModelEpoch}/{telemetry.ModelTotalEpochs} · "
            + $"验证损失 {FormatLoss(telemetry.ModelValidationLoss)} · "
            + $"最佳 {FormatLoss(telemetry.ModelBestValidationLoss)} · "
            + $"并行 {telemetry.ActiveCampaigns}/{telemetry.EffectiveParallelism} · "
            + $"{telemetry.CampaignsPerSecond:0.00} 冒险/秒 · "
            + $"ETA {FormatDuration(telemetry.EstimatedRemainingSeconds)}";
        logBox.Text =
            $"阶段：{telemetry.Stage} / {telemetry.Phase}\r\n"
            + $"搜索：{telemetry.SearchSimulations:N0} 次，"
            + $"{telemetry.SearchSimulationsPerSecond:N0}/秒，"
            + $"提前停止 {telemetry.SearchEarlyStops}\r\n"
            + $"线程：active={telemetry.ActiveCampaigns}，"
            + $"peak={telemetry.PeakConcurrentCampaigns}，"
            + $"observed={telemetry.ObservedWorkerThreads}\r\n"
            + $"GC：{telemetry.Gen0Collections}/"
            + $"{telemetry.Gen1Collections}/{telemetry.Gen2Collections}\r\n"
            + $"更新时间：{DateTime.Now:HH:mm:ss}";
    }

    private void PresentResult(ControllerWorkerResultSummary result)
    {
        var accepted = string.Equals(
            result.CompletionKind,
            "training-accepted",
            StringComparison.Ordinal);
        runStatus.Text = accepted
            ? "训练完成 · 底模已通过隔离验收"
            : result.Cancelled
                ? "训练已取消"
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
                + $"{result.Training.Validation.NormalCampaigns} · "
                + $"高级 {result.Training.Validation.AdvancedVictories}/"
                + $"{result.Training.Validation.AdvancedCampaigns} · "
                + $"无效 {result.Training.Validation.InvalidCampaigns}";
        }
        logBox.Text =
            $"完成类型：{result.CompletionKind}\r\n"
            + $"运行时：{result.Runtime}\r\n"
            + $"规则集：{result.RulesetHash}\r\n"
            + $"可恢复：{result.Resumable}\r\n"
            + $"检查点：{result.CheckpointPath}\r\n"
            + $"检查点写入失败：{result.CheckpointWriteFailures}\r\n"
            + (string.IsNullOrWhiteSpace(result.CheckpointWarning)
                ? ""
                : $"检查点提示：{result.CheckpointWarning}\r\n")
            + $"待验底模包：{result.ModelPackagePath}\r\n"
            + $"结果目录：{session?.ResultDirectory}";
        recentResultStatus.Text = runStatus.Text;
        recentResultStatus.Foreground = runStatus.Foreground;
        recentResultDetails.Text = ResultSummary(result);
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
        tabs.SelectedIndex = ProgressTabIndex;
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
              + $"（计划 {validation.NormalPlannedCampaigns}） · "
              + $"高级 {validation.AdvancedVictories}/{validation.AdvancedCampaigns}"
              + $"（计划 {validation.AdvancedPlannedCampaigns}） · "
              + $"无效 {validation.InvalidCampaigns}";
        var recoveryText = result.Resumable
            ? "检查点已保存，可恢复训练。"
            : string.Equals(
                result.CompletionKind,
                "training-accepted",
                StringComparison.Ordinal)
                ? "底模已通过隔离验收。"
                : "当前结果不可恢复。";
        return validationText
               + Environment.NewLine
               + result.Message
               + Environment.NewLine
               + recoveryText;
    }

    private static string FormatLoss(double value)
    {
        return double.IsNaN(value)
               || double.IsInfinity(value)
               || value >= double.MaxValue / 2d
            ? "待计算"
            : value.ToString("0.000000");
    }

    private void LoadSettings()
    {
        var modRoot = ResolveArgument("--mod-root")
                      ?? DiscoverModRoot();
        var dataRoot = ResolveArgument("--data-root")
                       ?? DiscoverDataRoot(modRoot);
        var settingsPath = SettingsPath(dataRoot);
        try
        {
            settings = File.Exists(settingsPath)
                ? Deserialize<ControllerSettings>(
                    CombatFoundationCheckpointStorage.ReadAllTextShared(
                        settingsPath))
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
        settings.GameSubject ??= LoadDefaultGameSubject(modRoot);
        settings.GameSubject.Normalize();
        gameSubjectCatalog = LoadGameSubjectCatalog(modRoot);
        gameSubjectCatalog.ResolveReferences(settings.GameSubject);
        settings.SchemaVersion = 5;
        settings.Parameters.Normalized();
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
            var path = SessionPath(settings.DataRoot);
            if (!File.Exists(path))
            {
                return;
            }
            session = Deserialize<ControllerSession>(
                CombatFoundationCheckpointStorage.ReadAllTextShared(path));
            if (session != null)
            {
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
        p.CapabilityProbeCampaignsPerDifficulty =
            Int("CapabilityProbeCampaignsPerDifficulty");
        p.RequireCapabilityProbeBaselineGain =
            Toggle("RequireCapabilityProbeBaselineGain");
        p.CapabilityProbeMinimumVictoryGain =
            Int("CapabilityProbeMinimumVictoryGain");
        p.CapabilityProbeMinimumDepthGain =
            Double("CapabilityProbeMinimumDepthGain");
        p.MaximumDegreeOfParallelism = Int("MaximumDegreeOfParallelism");
        p.ModelEpochs = Int("ModelEpochs");
        p.ModelMinimumEpochs = Int("ModelMinimumEpochs");
        p.ModelEarlyStoppingPatience = Int("ModelEarlyStoppingPatience");
        p.ModelEarlyStoppingMinimumDelta =
            Double("ModelEarlyStoppingMinimumDelta");
        p.ModelBatchSize = Int("ModelBatchSize");
        p.MinimumEpisodes = Int("MinimumEpisodes");
        p.EnableFrameStratification = Toggle("EnableFrameStratification");
        p.ModelMaximumFrameStratumWeight =
            Double("ModelMaximumFrameStratumWeight");
        p.ModelMaximumFramesPerEpisode =
            Int("ModelMaximumFramesPerEpisode");
        p.ModelReplayEpisodeLimit = Int("ModelReplayEpisodeLimit");
        p.ModelRetainedCandidates = Int("ModelRetainedCandidates");
        p.ModelLearningRate = Double("ModelLearningRate");
        p.ModelL2 = Double("ModelL2");
        p.ModelStateDimensions = Int("ModelStateDimensions");
        p.ModelActionDimensions = Int("ModelActionDimensions");
        p.ModelHiddenDimensions = Int("ModelHiddenDimensions");
        p.EnableCurriculum = Toggle("EnableCurriculum");
        p.EnableStratifiedReplay = Toggle("EnableStratifiedReplay");
        p.EnableHardSeedCurriculum = Toggle("EnableHardSeedCurriculum");
        p.EnableCounterfactualHardEncounters =
            Toggle("EnableCounterfactualHardEncounters");
        p.EnableSuccessCaseArchive = Toggle("EnableSuccessCaseArchive");
        p.EnableArenaRecovery = Toggle("EnableArenaRecovery");
        p.EnableTuningArena = Toggle("EnableTuningArena");
        p.EnableEarlyValidationStop = Toggle("EnableEarlyValidationStop");
        p.ArenaInvalidRetryCount = Int("ArenaInvalidRetryCount");
        p.ArenaInvalidRateLimit = Double("ArenaInvalidRateLimit");
        p.TuningNormalCampaigns = Int("TuningNormalCampaigns");
        p.TuningAdvancedCampaigns = Int("TuningAdvancedCampaigns");
        p.MaximumConsecutiveRejectedIterations =
            Int("MaximumConsecutiveRejectedIterations");
        p.NormalAcceptanceRate = Double("NormalAcceptanceRate");
        p.AdvancedAcceptanceRate = Double("AdvancedAcceptanceRate");
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
        Set(
            "CapabilityProbeCampaignsPerDifficulty",
            p.CapabilityProbeCampaignsPerDifficulty);
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
        Set("ModelEpochs", p.ModelEpochs);
        Set("ModelMinimumEpochs", p.ModelMinimumEpochs);
        Set("ModelEarlyStoppingPatience", p.ModelEarlyStoppingPatience);
        Set("ModelEarlyStoppingMinimumDelta", p.ModelEarlyStoppingMinimumDelta);
        Set("ModelBatchSize", p.ModelBatchSize);
        Set("MinimumEpisodes", p.MinimumEpisodes);
        Set(
            "ModelMaximumFrameStratumWeight",
            p.ModelMaximumFrameStratumWeight);
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
        Set("NormalAcceptanceRate", p.NormalAcceptanceRate);
        Set("AdvancedAcceptanceRate", p.AdvancedAcceptanceRate);
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
            "MaximumConsecutiveRejectedIterations",
            p.MaximumConsecutiveRejectedIterations);
        Set("RunSeed", p.RunSeed);
        Set("TrainingSeedStart", p.TrainingSeedStart);
        Set("ArenaSeedStart", p.ArenaSeedStart);
        Set("TuningSeedStart", p.TuningSeedStart);
        Set("ValidationSeedStart", p.ValidationSeedStart);
        SetToggle("EnableCurriculum", p.EnableCurriculum);
        SetToggle("EnableStratifiedReplay", p.EnableStratifiedReplay);
        SetToggle("EnableHardSeedCurriculum", p.EnableHardSeedCurriculum);
        SetToggle(
            "EnableCounterfactualHardEncounters",
            p.EnableCounterfactualHardEncounters);
        SetToggle("EnableSuccessCaseArchive", p.EnableSuccessCaseArchive);
        SetToggle("EnableArenaRecovery", p.EnableArenaRecovery);
        SetToggle("EnableTuningArena", p.EnableTuningArena);
        SetToggle("EnableEarlyValidationStop", p.EnableEarlyValidationStop);
        SetToggle("EnableFrameStratification", p.EnableFrameStratification);
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
            if (string.Equals(
                    propertyName,
                    nameof(ControllerTrainingResultSummary.Validation),
                    StringComparison.Ordinal))
            {
                summary.Validation =
                    serializer.Deserialize<CombatCampaignFoundationValidation>(
                        reader)
                    ?? new CombatCampaignFoundationValidation();
                return summary;
            }
            reader.Skip();
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
            path = Path.Combine(
                settings.DataRoot,
                "Logs",
                "AuraToolsExp",
                "combat-simulation-results");
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
            dataRoot,
            "Config",
            "Owners",
            "AuraToolsExp",
            "FoundationTrainer",
            "controller-settings.json");
    }

    private static string SessionPath(string dataRoot)
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
        continueButton.IsEnabled = !string.IsNullOrWhiteSpace(
            settings.LastRunDirectory);
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
        return value switch
        {
            "preflight" => "权威快检",
            "training" => "课程自博弈",
            "model-training" => "模型拟合",
            "arena" => "竞技场",
            "validation" => "隔离验证",
            _ => string.IsNullOrWhiteSpace(value) ? "准备中" : value
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
