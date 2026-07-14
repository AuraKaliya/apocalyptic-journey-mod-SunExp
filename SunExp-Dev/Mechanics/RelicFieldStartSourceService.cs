using System.Collections.Generic;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;

namespace SunExp.Dll.Mechanics;

public static class RelicFieldStartSourceService
{
    private static readonly IReadOnlyList<RelicFieldDefinition> Definitions =
        new[]
        {
            new RelicFieldDefinition(
                "blazing_crown_heart",
                SunExpFieldId.ScorchingCanopy,
                2,
                0)
        };

    public static IReadOnlyList<FieldStartGrant> OpeningFieldGrants()
    {
        var grants = new List<FieldStartGrant>();
        foreach (var definition in Definitions)
        {
            if (!RelicApi.HasRelic(definition.RelicId))
            {
                continue;
            }

            grants.Add(new FieldStartGrant(
                "relic." + definition.RelicId,
                definition.Field,
                definition.Stacks,
                definition.Order));
        }

        return grants;
    }

    private sealed class RelicFieldDefinition
    {
        public RelicFieldDefinition(string relicId, SunExpFieldId field, int stacks, int order)
        {
            RelicId = relicId;
            Field = field;
            Stacks = stacks;
            Order = order;
        }

        public string RelicId { get; }

        public SunExpFieldId Field { get; }

        public int Stacks { get; }

        public int Order { get; }
    }
}
