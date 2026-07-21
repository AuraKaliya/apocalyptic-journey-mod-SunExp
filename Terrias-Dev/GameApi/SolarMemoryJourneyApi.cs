using System;
using System.Collections.Generic;
using AuraJourney.Shared;
using AuraShared.Core;
using Terrias.Dll.Infrastructure;
using Witch.Mod;

namespace Terrias.Dll.GameApi;

public static class SolarMemoryJourneyApi
{
    public const string JourneyId = "Terrias:Terrias.SolarMemory";

    public static void Initialize(ModConfig modConfig)
    {
        AuraJourneyRuntime.Initialize(modConfig, TerriasIds.ModId);
        RegisterMapAliases();
        var result = AuraJourneyRuntime.RegisterJourney(TerriasIds.ModId, CreateDefinition());
        if (!result.Success)
        {
            TerriasLog.Warn("Solar Memory journey registration failed: " + result.Message);
        }
    }

    private static void RegisterMapAliases()
    {
        AuraJourneyMapIdAliasRegistry.RegisterPrefixAlias(
            "Terrias.MapFullToShort",
            "Terrias_terrias_",
            "");
    }

    private static AuraJourneyDefinition CreateDefinition()
    {
        return new AuraJourneyDefinition
        {
            JourneyId = JourneyId,
            OwnerModId = TerriasIds.ModId,
            DisplayName = "Solar Memory",
            Description = "Shared route-state contract for Terrias Solar Memory mode.",
            EntryNodeId = "preparation",
            Tags = new List<string> { "solar-memory", "role-pack", "multiplayer-authority" },
            RouteGraph = CreateRouteGraph(),
            Nodes = new List<AuraJourneyNodeDefinition>
            {
                new()
                {
                    NodeId = "preparation",
                    Kind = "Setup",
                    Conditions = new List<AuraJourneyCondition>
                    {
                        new() { Kind = AuraJourneyConditionKinds.Always }
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "SolarMemorySetupFlowRuntime"
                    }
                },
                new()
                {
                    NodeId = "route",
                    Kind = "RouteGraph",
                    Conditions = new List<AuraJourneyCondition>
                    {
                        new() { Kind = AuraJourneyConditionKinds.Flag, Key = "solar_memory_enabled" }
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "SolarMemoryModeRuntime"
                    }
                },
                new()
                {
                    NodeId = "boss",
                    Kind = "Boss",
                    Conditions = new List<AuraJourneyCondition>
                    {
                        new() { Kind = AuraJourneyConditionKinds.MinCounter, Key = "solar_memory_depth", Number = 1 }
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "SolarMemoryBossRedesign"
                    }
                }
            }
        };
    }

    private static AuraJourneyRouteGraph CreateRouteGraph()
    {
        var graph = new AuraJourneyRouteGraph
        {
            GraphId = "Terrias.SolarMemory.RouteGraph"
        };

        for (var layer = 0; layer < TerriasIds.SolarMemoryMaxLayer; layer++)
        {
            graph.Layers.Add(new AuraJourneyRouteLayer
            {
                LayerIndex = layer,
                LayerId = "solar_memory_layer_" + layer,
                LevelStart = layer * 6,
                DefaultSegmentSize = 6,
                SelectSegmentSize = 8,
                DefaultSlots = DefaultSlotRules(layer),
                SelectSlots = SelectSlotRules(layer)
            });
        }

        return graph;
    }

    private static List<AuraJourneySlotRule> DefaultSlotRules(int layer)
    {
        var rules = new List<AuraJourneySlotRule>
        {
            EventSlot(0, layer, 0)
        };

        if (layer == 1)
        {
            rules.Add(BossSlot(5, TerriasIds.SolarBossOrbitMirrorMapId, TerriasIds.SolarBossOrbitMirrorLevelId));
        }
        else if (layer == 2)
        {
            rules.Add(BossSlot(4, TerriasIds.SolarBossSecondSunMapId, TerriasIds.SolarBossSecondSunLevelId));
            rules.Add(BossSlot(5, TerriasIds.SolarBossSaintWunaMapId, TerriasIds.SolarBossSaintWunaLevelId));
        }

        return rules;
    }

    private static List<AuraJourneySlotRule> SelectSlotRules(int layer)
    {
        var rules = new List<AuraJourneySlotRule>();
        if (layer > 0)
        {
            rules.Add(EventSlot(3, layer, 3, AuraJourneyReplacementPolicies.PreserveBreak));
        }

        return rules;
    }

    private static AuraJourneySlotRule EventSlot(
        int slotIndex,
        int layer,
        int mapSlotIndex,
        string replacementPolicy = AuraJourneyReplacementPolicies.Replace)
    {
        var eventIndex = Math.Max(0, Math.Min(TerriasIds.SolarMemoryFullEventIds.Length - 1, layer * 2 + (mapSlotIndex >= 3 ? 1 : 0)));
        return new AuraJourneySlotRule
        {
            SlotIndex = slotIndex,
            MapSlotIndex = mapSlotIndex,
            ReplacementPolicy = replacementPolicy,
            MapNode = new AuraJourneyMapNodeSpec
            {
                NodeKey = "solar_memory_event_" + eventIndex,
                MapId = TerriasIds.SolarMemoryMapIds[eventIndex],
                FallbackMapId = TerriasIds.SolarMemoryShortMapIds[eventIndex],
                NodeId = TerriasIds.SolarMemoryFullEventIds[eventIndex],
                Type = AuraJourneyNodeKinds.Event,
                Note = "普通事件",
                Level = "-1",
                DicePolicy = AuraJourneyDicePolicies.Default,
                FixedNode = true,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "SolarMemoryModeRuntime.FixedNodeSpecs"
                }
            }
        };
    }

    private static AuraJourneySlotRule BossSlot(int slotIndex, string mapId, string levelId)
    {
        return new AuraJourneySlotRule
        {
            SlotIndex = slotIndex,
            MapSlotIndex = slotIndex,
            ReplacementPolicy = AuraJourneyReplacementPolicies.Replace,
            MapNode = new AuraJourneyMapNodeSpec
            {
                NodeKey = mapId,
                MapId = mapId,
                NodeId = levelId,
                Type = AuraJourneyNodeKinds.Fight,
                Note = "首领",
                Level = "-1",
                DicePolicy = AuraJourneyDicePolicies.TreeDice,
                FixedNode = true,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "SolarMemoryMapNodePoolFactory"
                }
            }
        };
    }
}
