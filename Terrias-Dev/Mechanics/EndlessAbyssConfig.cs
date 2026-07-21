using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.Mechanics;

public sealed class EndlessAbyssConfigDocument
{
    public int SchemaVersion { get; set; } = 2;

    public EndlessAbyssGazeConfig Gaze { get; set; } = new();

    public EndlessAbyssEnemyScalingConfig EnemyScaling { get; set; } = new();

    public EndlessAbyssShockConfig Shock { get; set; } = new();

    public EndlessAbyssMilestoneConfig Milestones { get; set; } = new();

    public EndlessAbyssRewardPoolConfig[] RewardPools { get; set; } =
    {
        EndlessAbyssRewardPoolConfig.DefaultOtherDimensionCardPool()
    };

    public EndlessAbyssRewardConfig Rewards { get; set; } = new();
}

public sealed class EndlessAbyssGazeConfig
{
    public int InitialLevel { get; set; } = 1;

    public int ChoiceStep { get; set; } = 3;

    public int MaxRequiredChoices { get; set; } = 3;

    public int EndlessMinLevel { get; set; } = 5;

    public int EndlessPassiveIncreasePerShock { get; set; } = 1;
}

public sealed class EndlessAbyssShockConfig
{
    public int StealthMinFloor { get; set; } = 1;

    public int AnnihilationCardCount { get; set; } = 3;

    public int CrackCardCount { get; set; } = 2;

    public int CrackGold { get; set; } = 300;

    public int SacrificeCardRewardCount { get; set; } = 2;

    public int GazeMaxHpReward { get; set; } = 20;

    public int GazeOriginReward { get; set; } = 2;

    public int CrackThreshold { get; set; } = 3;
}

public sealed class EndlessAbyssMilestoneConfig
{
    public int MinFloor { get; set; } = 2;
}

public sealed class EndlessAbyssRewardConfig
{
    public string OtherDimensionCardPoolId { get; set; } = TerriasIds.EndlessAbyssOtherDimensionCardPoolId;

    public string[] OtherDimensionCardIds { get; set; } =
    {
        TerriasIds.PolymorphCardShortId,
        TerriasIds.ProjectionCardShortId,
        TerriasIds.HeartChangeCardShortId
    };
}

public sealed class EndlessAbyssRewardPoolConfig
{
    public string Id { get; set; } = "";

    public string Kind { get; set; } = "card";

    public EndlessAbyssRewardPoolSourceConfig[] Sources { get; set; } = Array.Empty<EndlessAbyssRewardPoolSourceConfig>();

    public string[] IncludeCardIds { get; set; } = Array.Empty<string>();

    public string[] ExcludeCardIds { get; set; } = Array.Empty<string>();

    public bool RespectEnabledCardPacks { get; set; }

    public static EndlessAbyssRewardPoolConfig DefaultOtherDimensionCardPool()
    {
        return new EndlessAbyssRewardPoolConfig
        {
            Id = TerriasIds.EndlessAbyssOtherDimensionCardPoolId,
            Kind = "card",
            Sources = new[]
            {
                new EndlessAbyssRewardPoolSourceConfig
                {
                    Type = "cardPack",
                    Id = TerriasIds.MoreDimensionsCardPackId
                }
            },
            IncludeCardIds = Array.Empty<string>(),
            ExcludeCardIds = Array.Empty<string>(),
            RespectEnabledCardPacks = false
        };
    }
}

public sealed class EndlessAbyssRewardPoolSourceConfig
{
    public string Type { get; set; } = "";

    public string Id { get; set; } = "";
}

public static class EndlessAbyssConfigStore
{
    private static readonly object SyncRoot = new();
    private static EndlessAbyssConfigDocument current = Normalize(new EndlessAbyssConfigDocument());

    public static EndlessAbyssConfigDocument Current
    {
        get
        {
            lock (SyncRoot)
            {
                return current;
            }
        }
    }

    public static void Load(ModConfig modConfig)
    {
        lock (SyncRoot)
        {
            var fallback = Normalize(new EndlessAbyssConfigDocument());
            var path = Path.Combine(modConfig.DirectoryName, TerriasIds.EndlessAbyssConfigFile);
            if (!File.Exists(path))
            {
                current = fallback;
                EndlessAbyssRewardPoolService.Initialize(current.RewardPools);
                TerriasLog.Warn("[EndlessAbyss] missing config; using built-in defaults.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<EndlessAbyssConfigDocument>(File.ReadAllText(path))
                             ?? new EndlessAbyssConfigDocument();
                current = Normalize(loaded);
                EndlessAbyssRewardPoolService.Initialize(current.RewardPools);
                TerriasLog.Info("[EndlessAbyss] loaded config from " + path);
            }
            catch (Exception ex)
            {
                current = fallback;
                EndlessAbyssRewardPoolService.Initialize(current.RewardPools);
                TerriasLog.Warn("[EndlessAbyss] failed to load config; using built-in defaults: " + ex.Message);
            }
        }
    }

    private static EndlessAbyssConfigDocument Normalize(EndlessAbyssConfigDocument document)
    {
        document ??= new EndlessAbyssConfigDocument();
        document.Gaze ??= new EndlessAbyssGazeConfig();
        document.EnemyScaling ??= new EndlessAbyssEnemyScalingConfig();
        document.Shock ??= new EndlessAbyssShockConfig();
        document.Milestones ??= new EndlessAbyssMilestoneConfig();
        document.Rewards ??= new EndlessAbyssRewardConfig();
        document.RewardPools ??= Array.Empty<EndlessAbyssRewardPoolConfig>();

        document.SchemaVersion = Math.Max(1, document.SchemaVersion);
        document.Gaze.InitialLevel = Math.Max(1, document.Gaze.InitialLevel);
        document.Gaze.ChoiceStep = Math.Max(1, document.Gaze.ChoiceStep);
        document.Gaze.MaxRequiredChoices = Math.Max(1, Math.Min(3, document.Gaze.MaxRequiredChoices));
        document.Gaze.EndlessMinLevel = Math.Max(document.Gaze.InitialLevel, document.Gaze.EndlessMinLevel);
        document.Gaze.EndlessPassiveIncreasePerShock = Math.Max(0, document.Gaze.EndlessPassiveIncreasePerShock);
        NormalizeEnemyScaling(document.EnemyScaling);
        document.Shock.StealthMinFloor = Math.Max(1, document.Shock.StealthMinFloor);
        document.Shock.AnnihilationCardCount = Math.Max(1, document.Shock.AnnihilationCardCount);
        document.Shock.CrackCardCount = Math.Max(1, document.Shock.CrackCardCount);
        document.Shock.CrackGold = Math.Max(0, document.Shock.CrackGold);
        document.Shock.SacrificeCardRewardCount = Math.Max(1, document.Shock.SacrificeCardRewardCount);
        document.Shock.GazeMaxHpReward = Math.Max(0, document.Shock.GazeMaxHpReward);
        document.Shock.GazeOriginReward = Math.Max(0, document.Shock.GazeOriginReward);
        document.Shock.CrackThreshold = Math.Max(1, document.Shock.CrackThreshold);
        document.Milestones.MinFloor = Math.Max(1, document.Milestones.MinFloor);
        document.Rewards.OtherDimensionCardPoolId = string.IsNullOrWhiteSpace(document.Rewards.OtherDimensionCardPoolId)
            ? TerriasIds.EndlessAbyssOtherDimensionCardPoolId
            : document.Rewards.OtherDimensionCardPoolId.Trim();
        document.Rewards.OtherDimensionCardIds ??= Array.Empty<string>();
        document.RewardPools = NormalizeRewardPools(document.RewardPools);
        return document;
    }

    private static void NormalizeEnemyScaling(EndlessAbyssEnemyScalingConfig config)
    {
        config.HpLinearPerFloor = Clamp(config.HpLinearPerFloor, 0.0, 5.0);
        config.HpQuadraticPerFloor = Clamp(config.HpQuadraticPerFloor, 0.0, 1.0);
        config.AttackLinearPerFloor = Clamp(config.AttackLinearPerFloor, 0.0, 1.0);
        config.AttackQuadraticPerFloor = Clamp(config.AttackQuadraticPerFloor, 0.0, 0.5);
        config.EndlessStartFloor = Math.Max(1, config.EndlessStartFloor);
        config.EndlessHpMultiplier = Clamp(config.EndlessHpMultiplier, 1.0, 10.0);
        config.EndlessAttackMultiplier = Clamp(config.EndlessAttackMultiplier, 1.0, 10.0);
        config.CycleFloorCount = Math.Max(1, config.CycleFloorCount);
        config.CycleHpGrowth = Clamp(config.CycleHpGrowth, 0.0, 1.0);
        config.CycleAttackGrowth = Clamp(config.CycleAttackGrowth, 0.0, 1.0);
        config.HpSoftCap = Math.Max(1.0, config.HpSoftCap);
        config.HpSoftCapOverflowRatio = Clamp(config.HpSoftCapOverflowRatio, 0.0, 1.0);
        config.AttackSoftCap = Math.Max(1.0, config.AttackSoftCap);
        config.AttackSoftCapOverflowRatio = Clamp(config.AttackSoftCapOverflowRatio, 0.0, 1.0);
        config.GazeHpGrowth = Clamp(config.GazeHpGrowth, 0.0, 1.0);
        config.GazeHpGrowthCap = Clamp(config.GazeHpGrowthCap, 0.0, 10.0);
        config.GazeAttackGrowth = Clamp(config.GazeAttackGrowth, 0.0, 1.0);
        config.GazeAttackGrowthCap = Clamp(config.GazeAttackGrowthCap, 0.0, 10.0);
        config.EliteHpMultiplier = Clamp(config.EliteHpMultiplier, 1.0, 10.0);
        config.EliteAttackMultiplier = Clamp(config.EliteAttackMultiplier, 1.0, 10.0);
        config.BossHpMultiplier = Clamp(config.BossHpMultiplier, 1.0, 10.0);
        config.BossAttackMultiplier = Clamp(config.BossAttackMultiplier, 1.0, 10.0);
        config.EndlessBossHpMultiplier = Clamp(config.EndlessBossHpMultiplier, 1.0, 10.0);
        config.EndlessBossAttackMultiplier = Clamp(config.EndlessBossAttackMultiplier, 1.0, 10.0);
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static EndlessAbyssRewardPoolConfig[] NormalizeRewardPools(EndlessAbyssRewardPoolConfig[] pools)
    {
        var result = new System.Collections.Generic.List<EndlessAbyssRewardPoolConfig>();
        foreach (var pool in pools ?? Array.Empty<EndlessAbyssRewardPoolConfig>())
        {
            if (pool == null || string.IsNullOrWhiteSpace(pool.Id))
            {
                continue;
            }

            pool.Id = pool.Id.Trim();
            pool.Kind = string.IsNullOrWhiteSpace(pool.Kind) ? "card" : pool.Kind.Trim();
            pool.Sources ??= Array.Empty<EndlessAbyssRewardPoolSourceConfig>();
            foreach (var source in pool.Sources)
            {
                if (source == null)
                {
                    continue;
                }

                source.Type = string.IsNullOrWhiteSpace(source.Type) ? "" : source.Type.Trim();
                source.Id = string.IsNullOrWhiteSpace(source.Id) ? "" : source.Id.Trim();
            }

            pool.IncludeCardIds ??= Array.Empty<string>();
            pool.ExcludeCardIds ??= Array.Empty<string>();
            result.Add(pool);
        }

        if (result.All(pool => !string.Equals(pool.Id, TerriasIds.EndlessAbyssOtherDimensionCardPoolId, StringComparison.Ordinal)))
        {
            result.Add(EndlessAbyssRewardPoolConfig.DefaultOtherDimensionCardPool());
        }

        return result.ToArray();
    }
}
