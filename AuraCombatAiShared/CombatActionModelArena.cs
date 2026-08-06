using System.Collections.Generic;

namespace AuraCombatAi.Shared;

internal sealed class CombatActionModelArena
{
    private readonly List<CombatActionModel> models = new();
    private readonly List<CombatActionOutcome> outcomes = new();
    private readonly List<CombatEffectOperation> effects = new();
    private int modelCursor;
    private int outcomeCursor;
    private int effectCursor;

    public int ModelCapacity => models.Count;

    public int OutcomeCapacity => outcomes.Count;

    public int EffectCapacity => effects.Count;

    public long EstimatedRetainedBytes =>
        models.Count * 128L + outcomes.Count * 96L + effects.Count * 96L;

    public void BeginSearch()
    {
        modelCursor = 0;
        outcomeCursor = 0;
        effectCursor = 0;
    }

    public CombatActionModel RentModel()
    {
        CombatActionModel model;
        if (modelCursor < models.Count)
        {
            model = models[modelCursor];
        }
        else
        {
            model = new CombatActionModel();
            models.Add(model);
        }
        modelCursor++;
        model.ModelId = "semantic-default";
        model.Confidence = 1d;
        model.Outcomes.Clear();
        return model;
    }

    public CombatActionOutcome RentOutcome()
    {
        CombatActionOutcome outcome;
        if (outcomeCursor < outcomes.Count)
        {
            outcome = outcomes[outcomeCursor];
        }
        else
        {
            outcome = new CombatActionOutcome();
            outcomes.Add(outcome);
        }
        outcomeCursor++;
        outcome.OutcomeId = "";
        outcome.Probability = 1d;
        outcome.Effects.Clear();
        return outcome;
    }

    public CombatEffectOperation RentEffect()
    {
        CombatEffectOperation effect;
        if (effectCursor < effects.Count)
        {
            effect = effects[effectCursor];
        }
        else
        {
            effect = new CombatEffectOperation();
            effects.Add(effect);
        }
        effectCursor++;
        effect.Kind = default;
        effect.TargetRuntimeId = 0;
        effect.Magnitude = 0d;
        effect.SecondaryMagnitude = 0d;
        effect.SemanticId = "";
        effect.SourceCardZone = default;
        effect.DestinationCardZone = default;
        effect.SelectionRank = 0;
        return effect;
    }

    public long Trim()
    {
        var retained = EstimatedRetainedBytes;
        models.Clear();
        outcomes.Clear();
        effects.Clear();
        models.TrimExcess();
        outcomes.TrimExcess();
        effects.TrimExcess();
        modelCursor = 0;
        outcomeCursor = 0;
        effectCursor = 0;
        return retained;
    }
}
