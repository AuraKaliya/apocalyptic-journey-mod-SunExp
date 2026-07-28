using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
    private readonly DispatcherTimer timer;
    private readonly string[] launchArguments;
    private ControllerSettings settings = new();
    private ControllerSession? session;
    private Process? workerProcess;
    private TextBox modRootInput = null!;
    private TextBox dataRootInput = null!;
    private TextBlock environmentStatus = null!;
    private TextBlock runStatus = null!;
    private TextBlock progressPrimary = null!;
    private TextBlock progressSecondary = null!;
    private ProgressBar progressBar = null!;
    private TextBox logBox = null!;
    private Button startButton = null!;
    private Button cancelButton = null!;
    private Button continueButton = null!;
    private Button openButton = null!;

    public MainWindow(string[] args)
    {
        launchArguments = args ?? Array.Empty<string>();
        Title = "Aura 底模训练控制台";
        Width = 1120;
        Height = 820;
        MinWidth = 920;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(25, 28, 35));
        Foreground = Brushes.WhiteSmoke;
        Content = BuildUi();
        LoadSettings();
        ApplySettingsToUi();
        ValidateEnvironment();
        TryAttachLastSession();
        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (_, _) => RefreshRunState();
        timer.Start();
        Closing += (_, _) =>
        {
            PullSettingsFromUi();
            SaveSettings();
        };
    }

    private UIElement BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var title = new TextBlock
        {
            Text = "Aura Foundation Trainer",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem
        {
            Header = "训练参数",
            Content = BuildParametersTab()
        });
        tabs.Items.Add(new TabItem
        {
            Header = "进度与结果",
            Content = BuildProgressTab()
        });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildParametersTab()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        scroll.Content = panel;

        panel.Children.Add(Section("运行环境"));
        modRootInput = AddPathRow(panel, "MOD 目录", BrowseModRoot);
        dataRootInput = AddPathRow(panel, "ModsData 目录", BrowseDataRoot);
        environmentStatus = Hint(panel, "");

        panel.Children.Add(Section("工作量与性能"));
        AddNumber(panel, "Iterations", "训练轮数", 1, 20);
        AddNumber(panel, "TrainingCampaignsPerIteration", "每轮训练冒险", 2, 1000);
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
            32);
        AddNumber(panel, "MaximumDegreeOfParallelism", "CPU 并行度", 1, 64);

        panel.Children.Add(Section("模型训练"));
        AddNumber(panel, "ModelEpochs", "最大 Epoch", 5, 200);
        AddNumber(panel, "ModelMinimumEpochs", "最小 Epoch", 1, 200);
        AddNumber(panel, "ModelEarlyStoppingPatience", "早停耐心", 1, 30);
        AddDouble(panel, "ModelEarlyStoppingMinimumDelta", "早停最小增益");
        AddNumber(panel, "ModelBatchSize", "Minibatch", 8, 512);
        AddNumber(panel, "ModelReplayEpisodeLimit", "Replay 上限", 64, 20000);
        AddNumber(panel, "ModelRetainedCandidates", "Top-K 候选", 1, 5);
        AddDouble(panel, "ModelLearningRate", "学习率");
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
        AddDouble(panel, "NormalAcceptanceRate", "普通验收率");
        AddDouble(panel, "AdvancedAcceptanceRate", "高级验收率");
        AddDouble(panel, "SuccessExpertReplayShare", "成功教师回放占比");
        AddDouble(panel, "HardSeedReplayShare", "困难种子占比");
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
        startButton = ActionButton("开始 / 恢复训练", StartTraining);
        continueButton = ActionButton("以上轮 Champion 继续", ContinueTraining);
        cancelButton = ActionButton("安全取消", CancelTraining);
        openButton = ActionButton("打开运行目录", OpenRunDirectory);
        actions.Children.Add(startButton);
        actions.Children.Add(continueButton);
        actions.Children.Add(cancelButton);
        actions.Children.Add(openButton);
        panel.Children.Add(actions);
        return scroll;
    }

    private UIElement BuildProgressTab()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        runStatus = new TextBlock
        {
            Text = "尚未开始训练",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        progressPrimary = Hint(panel, "");
        progressSecondary = Hint(panel, "");
        progressBar = new ProgressBar
        {
            Height = 18,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 12, 0, 12)
        };
        panel.Children.Insert(0, runStatus);
        panel.Children.Add(progressBar);
        panel.Children.Add(new TextBlock
        {
            Text = "运行信息",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 6)
        });
        logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 420,
            Background = new SolidColorBrush(Color.FromRgb(16, 18, 23)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 224)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(65, 72, 84))
        };
        panel.Children.Add(logBox);
        return panel;
    }

    private void StartTraining()
    {
        try
        {
            PullSettingsFromUi();
            ValidateEnvironment(throwOnFailure: true);
            StartWorker(initialChampion: null, continueGeneration: false);
        }
        catch (Exception ex)
        {
            AppendLog("无法启动训练：" + ex.Message);
            MessageBox.Show(this, ex.Message, "无法启动训练", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
                File.ReadAllText(resultPath));
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
        var campaignPath = Path.Combine(
            settings.ModRoot,
            "Config",
            "combat-simulation",
            "witch-world-simulation-v2.campaign.json");
        var rulesetPath = Path.Combine(
            settings.ModRoot,
            "Config",
            "combat-simulation",
            "witch-base-evaluation-v2.ruleset.json");
        var sourceCampaign = Deserialize<CombatCampaignDefinition>(
                                 File.ReadAllText(campaignPath))
                             ?? throw new InvalidOperationException("无法读取训练战役");
        var trainingCampaign = Deserialize<CombatCampaignDefinition>(
                                   File.ReadAllText(campaignPath))
                               ?? throw new InvalidOperationException("无法克隆训练战役");
        trainingCampaign.TraceLevel = CombatSimulationTraceLevel.Summary;
        trainingCampaign.RequireAuthoritativeRules = true;
        trainingCampaign.RetainBlockBetweenTurns = true;
        var validationCampaign = Deserialize<CombatCampaignDefinition>(
                                     File.ReadAllText(campaignPath))
                                 ?? throw new InvalidOperationException("无法克隆验证战役");
        validationCampaign.TraceLevel = CombatSimulationTraceLevel.Full;
        validationCampaign.FullTraceFinalEncounterOnly = true;
        validationCampaign.RequireAuthoritativeRules = true;
        validationCampaign.RetainBlockBetweenTurns = true;
        var rulesetDocument = Deserialize<CombatRulesetDocument>(
                                  File.ReadAllText(rulesetPath))
                              ?? throw new InvalidOperationException("无法读取规则集");
        var rulesetBuild = CombatSimulationRegistry.BuildRuleset(rulesetDocument);
        if (!rulesetBuild.Success)
        {
            throw new InvalidOperationException(
                "规则集构建失败：" + string.Join("；", rulesetBuild.Errors.Take(5)));
        }
        var packageAudit = AuraToolsNativeProgramPackageAudit.Validate(
            sourceCampaign,
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
        settings.LastRunDirectory = resultDirectory;
        SaveSession();
        SaveSettings();
        AppendLog(
            "训练已启动："
            + jobId
            + "，预计冒险 "
            + parameters.EstimatedCampaigns()
            + "，PID="
            + workerProcess.Id);
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
        startButton.IsEnabled = !running;
        continueButton.IsEnabled = !running;
        cancelButton.IsEnabled = running;
        openButton.IsEnabled = Directory.Exists(session.ResultDirectory);
        if (File.Exists(job.ProgressPath))
        {
            try
            {
                var progress = Deserialize<CombatFoundationWorkerProgress>(
                    File.ReadAllText(job.ProgressPath));
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
        if (!File.Exists(job.ResultPath))
        {
            return;
        }
        try
        {
            var result = Deserialize<CombatFoundationWorkerResult>(
                File.ReadAllText(job.ResultPath));
            if (result == null)
            {
                return;
            }
            PresentResult(result);
        }
        catch (IOException)
        {
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
            + $"验证损失 {telemetry.ModelValidationLoss:0.000000} · "
            + $"最佳 {telemetry.ModelBestValidationLoss:0.000000} · "
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

    private void PresentResult(CombatFoundationWorkerResult result)
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
        progressBar.Value = accepted ? 100 : progressBar.Value;
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
            + $"待验底模包：{result.ModelPackagePath}\r\n"
            + $"结果目录：{session?.ResultDirectory}";
    }

    private void LoadSettings()
    {
        var defaultModRoot = ResolveArgument("--mod-root")
                             ?? DiscoverModRoot();
        var defaultDataRoot = ResolveArgument("--data-root")
                              ?? DiscoverDataRoot(defaultModRoot);
        var settingsPath = SettingsPath(defaultDataRoot);
        try
        {
            settings = File.Exists(settingsPath)
                ? Deserialize<ControllerSettings>(File.ReadAllText(settingsPath))
                  ?? new ControllerSettings()
                : new ControllerSettings();
        }
        catch
        {
            settings = new ControllerSettings();
        }
        settings.ModRoot = string.IsNullOrWhiteSpace(ResolveArgument("--mod-root"))
            ? string.IsNullOrWhiteSpace(settings.ModRoot)
                ? defaultModRoot
                : settings.ModRoot
            : defaultModRoot;
        settings.DataRoot = string.IsNullOrWhiteSpace(ResolveArgument("--data-root"))
            ? string.IsNullOrWhiteSpace(settings.DataRoot)
                ? defaultDataRoot
                : settings.DataRoot
            : defaultDataRoot;
        settings.Parameters ??= new CombatFoundationTrainingParameters();
        settings.Parameters.Normalized();
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
            session = Deserialize<ControllerSession>(File.ReadAllText(path));
            if (session != null)
            {
                settings.LastRunDirectory = session.ResultDirectory;
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
        var p = settings.Parameters;
        p.Iterations = Int("Iterations");
        p.TrainingCampaignsPerIteration = Int("TrainingCampaignsPerIteration");
        p.ArenaCampaignsPerDifficulty = Int("ArenaCampaignsPerDifficulty");
        p.ArenaConfirmationCampaignsPerDifficulty =
            Int("ArenaConfirmationCampaignsPerDifficulty");
        p.NormalValidationCampaigns = Int("NormalValidationCampaigns");
        p.AdvancedValidationCampaigns = Int("AdvancedValidationCampaigns");
        p.CapabilityProbeCampaignsPerDifficulty =
            Int("CapabilityProbeCampaignsPerDifficulty");
        p.MaximumDegreeOfParallelism = Int("MaximumDegreeOfParallelism");
        p.ModelEpochs = Int("ModelEpochs");
        p.ModelMinimumEpochs = Int("ModelMinimumEpochs");
        p.ModelEarlyStoppingPatience = Int("ModelEarlyStoppingPatience");
        p.ModelEarlyStoppingMinimumDelta =
            Double("ModelEarlyStoppingMinimumDelta");
        p.ModelBatchSize = Int("ModelBatchSize");
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
        p.NormalAcceptanceRate = Double("NormalAcceptanceRate");
        p.AdvancedAcceptanceRate = Double("AdvancedAcceptanceRate");
        p.SuccessExpertReplayShare = Double("SuccessExpertReplayShare");
        p.HardSeedReplayShare = Double("HardSeedReplayShare");
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
        var p = settings.Parameters;
        Set("Iterations", p.Iterations);
        Set("TrainingCampaignsPerIteration", p.TrainingCampaignsPerIteration);
        Set("ArenaCampaignsPerDifficulty", p.ArenaCampaignsPerDifficulty);
        Set(
            "ArenaConfirmationCampaignsPerDifficulty",
            p.ArenaConfirmationCampaignsPerDifficulty);
        Set("NormalValidationCampaigns", p.NormalValidationCampaigns);
        Set("AdvancedValidationCampaigns", p.AdvancedValidationCampaigns);
        Set(
            "CapabilityProbeCampaignsPerDifficulty",
            p.CapabilityProbeCampaignsPerDifficulty);
        Set("MaximumDegreeOfParallelism", p.MaximumDegreeOfParallelism);
        Set("ModelEpochs", p.ModelEpochs);
        Set("ModelMinimumEpochs", p.ModelMinimumEpochs);
        Set("ModelEarlyStoppingPatience", p.ModelEarlyStoppingPatience);
        Set("ModelEarlyStoppingMinimumDelta", p.ModelEarlyStoppingMinimumDelta);
        Set("ModelBatchSize", p.ModelBatchSize);
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
        Set("SelfPlayExplorationProbability", p.SelfPlayExplorationProbability);
        Set("SelfPlayExplorationTemperature", p.SelfPlayExplorationTemperature);
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
                "witch-base-evaluation-v2.ruleset.json")
        };
        errors.AddRange(required.Where(path => !File.Exists(path)));
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            errors.Add("ModsData 目录为空");
        }
        var ok = errors.Count == 0;
        environmentStatus.Text = ok
            ? "环境就绪。Worker、固定战役和冻结规则集均可用。"
            : "环境未就绪：" + string.Join("；", errors.Take(3));
        environmentStatus.Foreground = ok ? Brushes.LightGreen : Brushes.Orange;
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
            return session != null && File.Exists(session.JobPath)
                ? Deserialize<CombatFoundationWorkerJob>(
                    File.ReadAllText(session.JobPath))
                : null;
        }
        catch
        {
            return null;
        }
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
        var candidates = new[]
        {
            Directory.GetParent(AppContext.BaseDirectory)?.Parent?.FullName,
            Path.Combine(Environment.CurrentDirectory, "AuraToolsExp"),
            Environment.CurrentDirectory
        };
        return candidates.FirstOrDefault(candidate =>
                   !string.IsNullOrWhiteSpace(candidate)
                   && File.Exists(Path.Combine(
                       candidate!,
                       "Config",
                       "combat-simulation",
                       "witch-world-simulation-v2.campaign.json")))
               ?? Environment.CurrentDirectory;
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
            return Path.Combine(parent.Parent.FullName, "ModsData");
        }
        return Path.Combine(
            Directory.GetParent(modRoot)?.FullName ?? modRoot,
            "ModsData");
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
        row.Children.Add(Label(label, 180));
        var input = Input(600);
        row.Children.Add(input);
        var button = ActionButton("选择", browse);
        row.Children.Add(button);
        panel.Children.Add(row);
        return input;
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
            Margin = new Thickness(0, 5, 0, 5),
            Foreground = Brushes.WhiteSmoke
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
            Foreground = new SolidColorBrush(Color.FromRgb(132, 191, 255)),
            Margin = new Thickness(0, 18, 0, 8)
        };
    }

    private static TextBlock Label(string text, double width)
    {
        return new TextBlock
        {
            Text = text,
            Width = width,
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
            Background = new SolidColorBrush(Color.FromRgb(42, 47, 57)),
            Foreground = Brushes.WhiteSmoke,
            BorderBrush = new SolidColorBrush(Color.FromRgb(75, 84, 99)),
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 94,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(10, 0, 10, 0)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static TextBlock Hint(Panel panel, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(167, 176, 190)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 4)
        };
        panel.Children.Add(block);
        return block;
    }

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
}
