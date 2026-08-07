using AuraCombatAi.Shared;
using AuraCombatSimulation.Shared;
using AuraDecision.Shared;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Security.Cryptography;
using static CombatAiTestFixtures;

internal static class CombatAiTrainingBehaviorTests
{
    public static void Run(CombatAiSimulationTestContext simulationContext)
    {
        var context = CombatAiPolicyValueBehaviorTests.Run(simulationContext);
        CombatAiCampaignBehaviorTests.Run(context);
        CombatAiFoundationTrainingBehaviorTests.Run(context);
        CombatAiProtocolArtifactBehaviorTests.Run(context);
    }
}
