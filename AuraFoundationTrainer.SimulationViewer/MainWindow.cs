using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace AuraFoundationTrainer.SimulationViewer;

internal sealed class MainWindow : Window
{
    private readonly ObservableCollection<CampaignRow> campaigns = new();
    private readonly ObservableCollection<BattleRow> battles = new();
    private readonly ObservableCollection<TurnRow> turns = new();
    private readonly ObservableCollection<RewardRow> rewards = new();
    private readonly ObservableCollection<DecisionDifferenceRow> differences = new();
    private readonly ObservableCollection<ModelNodeRow> modelNodes = new();
    private readonly DataGrid campaignGrid;
    private readonly DataGrid battleGrid;
    private readonly DataGrid turnGrid;
    private readonly DataGrid rewardGrid;
    private readonly DataGrid differenceGrid;
    private readonly DataGrid modelNodeGrid;
    private readonly TextBlock pathText;
    private readonly TextBlock statusText;
    private readonly TextBlock[] metrics;
    private readonly ComboBox difficultyFilter;
    private readonly ComboBox resultFilter;
    private readonly TextBox seedFilter;
    private string databasePath = "";

    public MainWindow(string initialPath)
    {
        Title = "Aura 模拟过程查看器";
        Width = 1460;
        Height = 900;
        MinWidth = 1080;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(244, 247, 248));
        FontFamily = new FontFamily("Microsoft YaHei UI");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(29, 40, 48)),
            LastChildFill = true,
            Margin = new Thickness(0)
        };
        var openButton = Button("打开数据库", OpenDatabase);
        var refreshButton = Button("刷新", (_, _) => Reload());
        openButton.Margin = new Thickness(14, 10, 6, 10);
        refreshButton.Margin = new Thickness(0, 10, 12, 10);
        DockPanel.SetDock(openButton, Dock.Left);
        DockPanel.SetDock(refreshButton, Dock.Left);
        toolbar.Children.Add(openButton);
        toolbar.Children.Add(refreshButton);
        pathText = new TextBlock
        {
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 16, 0),
            Text = "未打开数据库"
        };
        toolbar.Children.Add(pathText);
        root.Children.Add(toolbar);

        var summary = new UniformGrid
        {
            Rows = 1,
            Columns = 6,
            Margin = new Thickness(12, 12, 12, 8)
        };
        metrics = new[]
        {
            Metric(summary, "模型", "-"),
            Metric(summary, "部署资格", "-"),
            Metric(summary, "模拟战役", "0"),
            Metric(summary, "胜率", "0%"),
            Metric(summary, "奖励决策", "0"),
            Metric(summary, "固定 Seed", "0")
        };
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);

        var content = new Grid { Margin = new Thickness(12, 0, 12, 8) };
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(43, GridUnitType.Star),
            MinWidth = 420
        });
        content.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(57, GridUnitType.Star),
            MinWidth = 540
        });

        var campaignPanel = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        campaignPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        campaignPanel.RowDefinitions.Add(new RowDefinition());
        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 7)
        };
        difficultyFilter = Combo("全部难度", "normal", "advanced");
        resultFilter = Combo("全部结果", "胜利", "失败", "无效");
        seedFilter = new TextBox
        {
            Width = 175,
            Height = 31,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "按 Seed 包含匹配"
        };
        var applyButton = Button("筛选", (_, _) => LoadCampaigns());
        filters.Children.Add(difficultyFilter);
        filters.Children.Add(resultFilter);
        filters.Children.Add(seedFilter);
        filters.Children.Add(applyButton);
        campaignPanel.Children.Add(filters);
        campaignGrid = GridFor(campaigns);
        campaignGrid.Columns.Add(TextColumn("难度", "Difficulty", 72));
        campaignGrid.Columns.Add(TextColumn("Seed", "WorldSeed", 155));
        campaignGrid.Columns.Add(TextColumn("结果", "Result", 70));
        campaignGrid.Columns.Add(TextColumn("进度", "Progress", 75));
        campaignGrid.Columns.Add(TextColumn("生命", "Hp", 75));
        campaignGrid.Columns.Add(TextColumn("奖励", "RewardCount", 55));
        campaignGrid.SelectionChanged += CampaignSelectionChanged;
        Grid.SetRow(campaignGrid, 1);
        campaignPanel.Children.Add(campaignGrid);
        content.Children.Add(campaignPanel);

        var tabs = new TabControl
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(201, 210, 216))
        };
        battleGrid = GridFor(battles);
        battleGrid.Columns.Add(TextColumn("#", "BattleIndex", 44));
        battleGrid.Columns.Add(TextColumn("场景", "ScenarioId", 180));
        battleGrid.Columns.Add(TextColumn("结果", "Outcome", 85));
        battleGrid.Columns.Add(TextColumn("回合", "Turns", 55));
        battleGrid.Columns.Add(TextColumn("生命", "FinalHp", 55));
        battleGrid.Columns.Add(TextColumn("搜索模拟", "SearchSimulations", 90));
        battleGrid.Columns.Add(TextColumn("搜索节点", "SearchNodes", 90));
        battleGrid.SelectionChanged += BattleSelectionChanged;
        tabs.Items.Add(Tab("对局进程", battleGrid));

        rewardGrid = GridFor(rewards);
        rewardGrid.Columns.Add(TextColumn("遭遇", "EncounterIndex", 55));
        rewardGrid.Columns.Add(TextColumn("类型", "Kind", 70));
        rewardGrid.Columns.Add(TextColumn("轮次", "Round", 55));
        rewardGrid.Columns.Add(TextColumn("选择", "SelectedId", 160));
        rewardGrid.Columns.Add(TextColumn("跳过", "Skipped", 55));
        rewardGrid.Columns.Add(TextColumn("候选评分", "Candidates", 310));
        tabs.Items.Add(Tab("奖励选取", rewardGrid));

        turnGrid = GridFor(turns);
        turnGrid.Columns.Add(TextColumn("回合", "Turn", 55));
        turnGrid.Columns.Add(TextColumn("玩家生命", "PlayerHp", 110));
        turnGrid.Columns.Add(TextColumn("敌方生命", "EnemyHp", 110));
        turnGrid.Columns.Add(TextColumn("动作数", "Actions", 70));
        tabs.Items.Add(Tab("回合轨迹", turnGrid));

        differenceGrid = GridFor(differences);
        differenceGrid.Columns.Add(TextColumn("难度", "Difficulty", 70));
        differenceGrid.Columns.Add(TextColumn("Seed", "WorldSeed", 145));
        differenceGrid.Columns.Add(TextColumn("战斗", "BattleIndex", 55));
        differenceGrid.Columns.Add(TextColumn("分类", "Category", 145));
        differenceGrid.Columns.Add(TextColumn("置信度", "Confidence", 70));
        differenceGrid.Columns.Add(TextColumn("偏好动作", "PreferredCandidate", 190));
        tabs.Items.Add(Tab("分歧决策", differenceGrid));

        modelNodeGrid = GridFor(modelNodes);
        modelNodeGrid.Columns.Add(TextColumn("节点", "Iteration", 55));
        modelNodeGrid.Columns.Add(TextColumn("模型", "ModelId", 180));
        modelNodeGrid.Columns.Add(TextColumn("普通", "NormalRate", 65));
        modelNodeGrid.Columns.Add(TextColumn("高级", "AdvancedRate", 65));
        modelNodeGrid.Columns.Add(TextColumn("不劣", "NonInferior", 55));
        modelNodeGrid.Columns.Add(TextColumn("绝对线", "Absolute", 60));
        modelNodeGrid.Columns.Add(TextColumn("结果", "Promotion", 145));
        tabs.Items.Add(Tab("模型节点", modelNodeGrid));

        Grid.SetColumn(tabs, 1);
        content.Children.Add(tabs);
        Grid.SetRow(content, 2);
        root.Children.Add(content);

        statusText = new TextBlock
        {
            Margin = new Thickness(14, 0, 14, 10),
            Foreground = new SolidColorBrush(Color.FromRgb(75, 89, 98)),
            Text = "打开训练产物中的 simulation-process-v1.sqlite"
        };
        Grid.SetRow(statusText, 3);
        root.Children.Add(statusText);
        Content = root;

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            Loaded += (_, _) => TryOpen(initialPath);
        }
    }

    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text,
            Height = 31,
            MinWidth = 70,
            Padding = new Thickness(10, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(244, 247, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(178, 189, 197)),
            BorderThickness = new Thickness(1)
        };
        button.Click += handler;
        return button;
    }

    private static ComboBox Combo(params string[] items)
    {
        var combo = new ComboBox
        {
            Width = 112,
            Height = 31,
            Margin = new Thickness(0, 0, 6, 0),
            ItemsSource = items,
            SelectedIndex = 0
        };
        return combo;
    }

    private static TextBlock Metric(Panel owner, string label, string value)
    {
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(211, 219, 224)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(4),
            Padding = new Thickness(12, 9, 12, 10)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(91, 103, 111)),
            FontSize = 12
        });
        var text = new TextBlock
        {
            Text = value,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        stack.Children.Add(text);
        border.Child = stack;
        owner.Children.Add(border);
        return text;
    }

    private static DataGrid GridFor<T>(IEnumerable<T> source)
    {
        return new DataGrid
        {
            ItemsSource = source,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeaderWidth = 0,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(247, 249, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(201, 210, 216)),
            BorderThickness = new Thickness(1)
        };
    }

    private static DataGridTextColumn TextColumn(
        string header,
        string path,
        double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width
        };
    }

    private static TabItem Tab(string header, UIElement content)
    {
        return new TabItem { Header = header, Content = content };
    }

    private void OpenDatabase(object sender, RoutedEventArgs args)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开模拟过程数据库",
            Filter = "SQLite 数据库 (*.sqlite;*.db)|*.sqlite;*.db|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            FileName = "simulation-process-v1.sqlite"
        };
        if (dialog.ShowDialog(this) == true)
        {
            TryOpen(dialog.FileName);
        }
    }

    private void TryOpen(string path)
    {
        try
        {
            var resolved = ResolveDatabasePath(path);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException("未找到模拟过程数据库", resolved);
            }
            databasePath = resolved;
            pathText.Text = databasePath;
            Reload();
        }
        catch (Exception ex)
        {
            statusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "无法打开", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ResolveDatabasePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (Directory.Exists(full))
        {
            var direct = Path.Combine(full, "simulation-process-v1.sqlite");
            if (File.Exists(direct)) return direct;
            var bundled = Path.Combine(full, "training-artifacts-v1", "simulation-process-v1.sqlite");
            if (File.Exists(bundled)) return bundled;
        }
        return full;
    }

    private void Reload()
    {
        if (string.IsNullOrWhiteSpace(databasePath)) return;
        try
        {
            LoadSummary();
            LoadCampaigns();
            LoadDifferences();
            LoadModelNodes();
            statusText.Text = $"已加载 {campaigns.Count} 个模拟冒险；选择左侧记录查看奖励和对局。";
        }
        catch (Exception ex)
        {
            statusText.Text = ex.Message;
        }
    }

    private SqliteConnection OpenReadOnly()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString());
        connection.Open();
        return connection;
    }

    private void LoadSummary()
    {
        using var connection = OpenReadOnly();
        metrics[0].Text = Scalar(connection, "SELECT value FROM metadata WHERE key='model_id'") ?? "-";
        metrics[1].Text = string.Equals(
            Scalar(connection, "SELECT value FROM metadata WHERE key='deployment_eligible'"),
            "True",
            StringComparison.OrdinalIgnoreCase) ? "通过" : "未通过";
        metrics[2].Text = Scalar(connection, "SELECT COUNT(*) FROM campaigns") ?? "0";
        var rate = Convert.ToDouble(
            Scalar(connection, "SELECT COALESCE(AVG(victory),0) FROM campaigns"),
            CultureInfo.InvariantCulture);
        metrics[3].Text = rate.ToString("P1", CultureInfo.CurrentCulture);
        metrics[4].Text = Scalar(connection, "SELECT COUNT(*) FROM reward_decisions") ?? "0";
        metrics[5].Text = Scalar(connection, "SELECT COUNT(*) FROM seed_tags") ?? "0";
    }

    private void LoadCampaigns()
    {
        campaigns.Clear();
        battles.Clear();
        rewards.Clear();
        turns.Clear();
        if (string.IsNullOrWhiteSpace(databasePath)) return;
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        var where = new List<string>();
        var difficulty = Convert.ToString(difficultyFilter.SelectedItem) ?? "";
        if (difficulty is "normal" or "advanced")
        {
            where.Add("difficulty=$difficulty");
            command.Parameters.AddWithValue("$difficulty", difficulty);
        }
        var result = Convert.ToString(resultFilter.SelectedItem) ?? "";
        if (result == "胜利") where.Add("victory=1 AND invalid=0");
        if (result == "失败") where.Add("victory=0 AND invalid=0");
        if (result == "无效") where.Add("invalid=1");
        if (!string.IsNullOrWhiteSpace(seedFilter.Text))
        {
            where.Add("world_seed LIKE $seed");
            command.Parameters.AddWithValue("$seed", "%" + seedFilter.Text.Trim() + "%");
        }
        command.CommandText = """
            SELECT c.id,c.difficulty,c.world_seed,c.victory,c.invalid,
              c.completed_battles,c.total_battles,c.final_hp,c.max_hp,
              (SELECT COUNT(*) FROM reward_decisions r WHERE r.campaign_id=c.id)
            FROM campaigns c
            """ + (where.Count == 0 ? "" : " WHERE " + string.Join(" AND ", where))
            + " ORDER BY c.difficulty,c.id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            campaigns.Add(new CampaignRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(4) != 0 ? "无效" : reader.GetInt64(3) != 0 ? "胜利" : "失败",
                $"{reader.GetInt32(5)}/{reader.GetInt32(6)}",
                $"{reader.GetInt32(7)}/{reader.GetInt32(8)}",
                reader.GetInt32(9)));
        }
        if (campaigns.Count > 0) campaignGrid.SelectedIndex = 0;
    }

    private void CampaignSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (campaignGrid.SelectedItem is not CampaignRow campaign) return;
        LoadBattles(campaign.Id);
        LoadRewards(campaign.Id);
    }

    private void LoadBattles(long campaignId)
    {
        battles.Clear();
        turns.Clear();
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,battle_index,scenario_id,outcome,turns,final_hp,
              search_simulations,search_nodes
            FROM battles WHERE campaign_id=$campaign ORDER BY battle_index
            """;
        command.Parameters.AddWithValue("$campaign", campaignId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            battles.Add(new BattleRow(
                reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5),
                reader.GetInt64(6), reader.GetInt64(7)));
        }
        if (battles.Count > 0) battleGrid.SelectedIndex = 0;
    }

    private void BattleSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (battleGrid.SelectedItem is not BattleRow battle) return;
        turns.Clear();
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT turn_index,player_hp_start,player_hp_end,enemy_hp_start,
              enemy_hp_end,actions FROM turns WHERE battle_id=$battle ORDER BY turn_index
            """;
        command.Parameters.AddWithValue("$battle", battle.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            turns.Add(new TurnRow(
                reader.GetInt32(0), $"{reader.GetInt32(1)} -> {reader.GetInt32(2)}",
                $"{reader.GetInt32(3)} -> {reader.GetInt32(4)}", reader.GetInt32(5)));
        }
    }

    private void LoadRewards(long campaignId)
    {
        rewards.Clear();
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.encounter_index,r.kind,r.round_number,r.selected_id,r.skipped,
              COALESCE((SELECT GROUP_CONCAT(x.reward_id || '=' || printf('%.2f',x.total_score), '; ')
                FROM reward_candidates x WHERE x.reward_decision_id=r.id),'')
            FROM reward_decisions r WHERE r.campaign_id=$campaign
            ORDER BY r.encounter_index,r.id
            """;
        command.Parameters.AddWithValue("$campaign", campaignId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rewards.Add(new RewardRow(
                reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetInt64(4) != 0 ? "是" : "否",
                reader.GetString(5)));
        }
    }

    private void LoadDifferences()
    {
        differences.Clear();
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT difficulty,world_seed,battle_index,failure_category,
              confidence,preferred_candidate_id FROM decision_differences ORDER BY confidence DESC,id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            differences.Add(new DecisionDifferenceRow(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetDouble(4).ToString("0.00"), reader.GetString(5)));
        }
    }

    private void LoadModelNodes()
    {
        modelNodes.Clear();
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT iteration,model_id,normal_win_rate,advanced_win_rate,
              noninferiority_passed,absolute_passed,promotion_kind FROM model_nodes ORDER BY iteration
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            modelNodes.Add(new ModelNodeRow(
                reader.GetInt32(0), reader.GetString(1), reader.GetDouble(2).ToString("P1"),
                reader.GetDouble(3).ToString("P1"), reader.GetInt64(4) != 0 ? "是" : "否",
                reader.GetInt64(5) != 0 ? "是" : "否", reader.GetString(6)));
        }
    }

    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private sealed record CampaignRow(
        long Id, string Difficulty, string WorldSeed, string Result,
        string Progress, string Hp, int RewardCount);

    private sealed record BattleRow(
        long Id, int BattleIndex, string ScenarioId, string Outcome,
        int Turns, int FinalHp, long SearchSimulations, long SearchNodes);

    private sealed record TurnRow(int Turn, string PlayerHp, string EnemyHp, int Actions);

    private sealed record RewardRow(
        int EncounterIndex, string Kind, int Round, string SelectedId,
        string Skipped, string Candidates);

    private sealed record DecisionDifferenceRow(
        string Difficulty, string WorldSeed, int BattleIndex, string Category,
        string Confidence, string PreferredCandidate);

    private sealed record ModelNodeRow(
        int Iteration, string ModelId, string NormalRate, string AdvancedRate,
        string NonInferior, string Absolute, string Promotion);
}
