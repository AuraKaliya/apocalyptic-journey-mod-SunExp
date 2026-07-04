using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SunExp.Dll.Infrastructure;
using Witch.Mod;

namespace SunExp.Dll.Mechanics;

public sealed class EndlessAbyssConfigDocument
{
    public int SchemaVersion { get; set; } = 1;

    public EndlessAbyssGazeConfig Gaze { get; set; } = new();

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

    public double HpGrowthPerGaze { get; set; } = 0.08;
}

public sealed class EndlessAbyssShockConfig
{
    public int StealthMinFloor { get; set; } = 1;

    public int AnnihilationCardCount { get; set; } = 3;
}

public sealed class EndlessAbyssMilestoneConfig
{
    public int MinFloor { get; set; } = 2;
}

public sealed class EndlessAbyssRewardConfig
{
    public string OtherDimensionCardPoolId { get; set; } = SunExpIds.EndlessAbyssOtherDimensionCardPoolId;

    public string[] OtherDimensionCardIds { get; set; } =
    {
        SunExpIds.PolymorphCardShortId,
        SunExpIds.ProjectionCardShortId,
        SunExpIds.HeartChangeCardShortId
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
            Id = SunExpIds.EndlessAbyssOtherDimensionCardPoolId,
            Kind = "card",
            Sources = new[]
            {
                new EndlessAbyssRewardPoolSourceConfig
                {
                    Type = "cardPack",
                    Id = SunExpIds.MoreDimensionsCardPackId
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
            var path = Path.Combine(modConfig.DirectoryName, SunExpIds.EndlessAbyssConfigFile);
            if (!File.Exists(path))
            {
                current = fallback;
                EndlessAbyssRewardPoolService.Initialize(current.RewardPools);
                SunExpLog.Warn("[EndlessAbyss] missing config; using built-in defaults.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<EndlessAbyssConfigDocument>(File.ReadAllText(path))
                             ?? new EndlessAbyssConfigDocument();
                current = Normalize(loaded);
                EndlessAbyssRewardPoolService.Initialize(current.RewardPools);
                SunExpLog.Info("[EndlessAbyss] loaded config from " + path);
            }
            catch (Exception ex)
            {
                current = fallback;
                EndlessAbyssRewardPoolService.Initialize(current.RewardPools);
                SunExpLog.Warn("[EndlessAbyss] failed to load config; using built-in defaults: " + ex.Message);
            }
        }
    }

    private static EndlessAbyssConfigDocument Normalize(EndlessAbyssConfigDocument document)
    {
        document ??= new EndlessAbyssConfigDocument();
        document.Gaze ??= new EndlessAbyssGazeConfig();
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
        document.Gaze.HpGrowthPerGaze = Math.Max(0.0, Math.Min(1.0, document.Gaze.HpGrowthPerGaze));
        document.Shock.StealthMinFloor = Math.Max(1, document.Shock.StealthMinFloor);
        document.Shock.AnnihilationCardCount = Math.Max(1, document.Shock.AnnihilationCardCount);
        document.Milestones.MinFloor = Math.Max(1, document.Milestones.MinFloor);
        document.Rewards.OtherDimensionCardPoolId = string.IsNullOrWhiteSpace(document.Rewards.OtherDimensionCardPoolId)
            ? SunExpIds.EndlessAbyssOtherDimensionCardPoolId
            : document.Rewards.OtherDimensionCardPoolId.Trim();
        document.Rewards.OtherDimensionCardIds ??= Array.Empty<string>();
        document.RewardPools = NormalizeRewardPools(document.RewardPools);
        return document;
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

        if (result.All(pool => !string.Equals(pool.Id, SunExpIds.EndlessAbyssOtherDimensionCardPoolId, StringComparison.Ordinal)))
        {
            result.Add(EndlessAbyssRewardPoolConfig.DefaultOtherDimensionCardPool());
        }

        return result.ToArray();
    }
}
