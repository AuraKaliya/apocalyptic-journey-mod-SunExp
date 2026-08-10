using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;
using static CombatAiTestFixtures;

ResetAssertions();
CombatAgentRuntimeBehaviorTests.Run();
CombatAiDecisionBehaviorTests.Run();
RuntimeDecisionSafetyBehaviorTests.Run();
SemanticCausalityBehaviorTests.Run();
var simulationContext = CombatAiSimulationBehaviorTests.Run();
CombatAiTrainingBehaviorTests.Run(simulationContext);

Console.WriteLine($"AuraCombatAiShared.Tests passed: {Assertions} assertions.");
