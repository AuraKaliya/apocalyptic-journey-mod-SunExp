using System.Collections.Generic;
using Terrias.Dll.GameApi;
using Terrias.Dll.Infrastructure;

namespace Terrias.Dll.Mechanics;

public static class RelicFieldStartSourceService
{
    private static readonly IReadOnlyList<RelicFieldDefinition> Definitions =
        new[]
        {
            new RelicFieldDefinition(
                "blazing_crown_heart",
                TerriasFieldId.ScorchingCanopy,
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
        public RelicFieldDefinition(string relicId, TerriasFieldId field, int stacks, int order)
        {
            RelicId = relicId;
            Field = field;
            Stacks = stacks;
            Order = order;
        }

        public string RelicId { get; }

        public TerriasFieldId Field { get; }

        public int Stacks { get; }

        public int Order { get; }
    }
}
