using System;
using System.Collections.Generic;

namespace AuraDecision.Shared;

public enum DecisionComparison
{
    Always,
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed class DecisionCondition
{
    public string Feature { get; set; } = "";

    public DecisionComparison Comparison { get; set; } = DecisionComparison.Always;

    public double Value { get; set; }
}

public sealed class DecisionGraphNode
{
    public string Id { get; set; } = "";

    public DecisionCondition Condition { get; set; } = new();

    public string TrueNodeId { get; set; } = "";

    public string FalseNodeId { get; set; } = "";

    public bool Reject { get; set; }

    public bool Terminal { get; set; }

    public DecisionUtility UtilityDelta { get; set; } = new();
}

public sealed class DecisionGraph
{
    public string RootNodeId { get; set; } = "";

    public List<DecisionGraphNode> Nodes { get; set; } = new();
}

public sealed class DecisionUtility
{
    public double Survival { get; set; }

    public double Lethal { get; set; }

    public double Tempo { get; set; }

    public double Resource { get; set; }

    public double DeckEconomy { get; set; }

    public double Scaling { get; set; }

    public double Synergy { get; set; }

    public double Continuation { get; set; }

    public double Risk { get; set; }

    public double Uncertainty { get; set; }

    public double Coordination { get; set; }

    public DecisionUtility Clone()
    {
        return (DecisionUtility)MemberwiseClone();
    }

    public void Add(DecisionUtility? other)
    {
        if (other == null)
        {
            return;
        }

        Survival += other.Survival;
        Lethal += other.Lethal;
        Tempo += other.Tempo;
        Resource += other.Resource;
        DeckEconomy += other.DeckEconomy;
        Scaling += other.Scaling;
        Synergy += other.Synergy;
        Continuation += other.Continuation;
        Risk += other.Risk;
        Uncertainty += other.Uncertainty;
        Coordination += other.Coordination;
    }
}

public sealed class DecisionWeights
{
    public double Survival { get; set; } = 1.35;

    public double Lethal { get; set; } = 1.6;

    public double Tempo { get; set; } = 1.0;

    public double Resource { get; set; } = 0.8;

    public double DeckEconomy { get; set; } = 0.55;

    public double Scaling { get; set; } = 0.7;

    public double Synergy { get; set; } = 0.65;

    public double Continuation { get; set; } = 0.9;

    public double Risk { get; set; } = -1.25;

    public double Uncertainty { get; set; } = -0.8;

    public double Coordination { get; set; } = 0.6;

    public double Score(DecisionUtility utility)
    {
        return utility.Survival * Survival
               + utility.Lethal * Lethal
               + utility.Tempo * Tempo
               + utility.Resource * Resource
               + utility.DeckEconomy * DeckEconomy
               + utility.Scaling * Scaling
               + utility.Synergy * Synergy
               + utility.Continuation * Continuation
               + utility.Risk * Risk
               + utility.Uncertainty * Uncertainty
               + utility.Coordination * Coordination;
    }
}

public sealed class DecisionCandidate<TAction>
{
    public string Id { get; set; } = "";

    public TAction Action { get; set; } = default!;

    public bool Legal { get; set; } = true;

    public string RejectionReason { get; set; } = "";

    public DecisionUtility Utility { get; set; } = new();

    public Dictionary<string, double> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DecisionResult<TAction>
{
    public bool HasAction { get; set; }

    public TAction Action { get; set; } = default!;

    public string CandidateId { get; set; } = "";

    public double Score { get; set; }

    public string Reason { get; set; } = "";
}

public interface IDecisionResidualModel
{
    string ModelId { get; }

    int ProtocolVersion { get; }

    double Predict(IReadOnlyDictionary<string, double> features);
}

public sealed class NullDecisionResidualModel : IDecisionResidualModel
{
    public static readonly NullDecisionResidualModel Instance = new();

    public string ModelId => "none";

    public int ProtocolVersion => 1;

    public double Predict(IReadOnlyDictionary<string, double> features)
    {
        return 0d;
    }
}
