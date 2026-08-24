using System;
using System.Collections.Generic;
using System.Linq;
using AuraGameData.Shared;
using AuraGameData.Shared.GameApi;
using Witch.Core;

namespace Terrias.Dll.Mechanics;

internal static class ProjectionDeckCapabilityInspector
{
    public static ProjectionDeckCardCapability Inspect(ProjectionDeckCardRecipe recipe)
    {
        if (recipe == null || string.IsNullOrWhiteSpace(recipe.CardId))
        {
            return ProjectionDeckCardCapability.Reject("card recipe is empty");
        }

        var definition = Resolve(recipe.DefinitionType, recipe.CardId);
        if (definition == null)
        {
            return ProjectionDeckCardCapability.Reject(
                "definition is not registered: " + recipe.DefinitionType + "/" + recipe.CardId);
        }

        var scripts = new List<string> { ScriptSurface(definition.Fields) };
        var capability = ProjectionCardExecutionPolicy.Resolve(
            definition.Fields,
            null,
            recipe.CardId,
            scripts[0]);
        if (capability.Mode == ProjectionCardExecutionMode.Unsupported)
        {
            return ProjectionDeckCardCapability.Reject(
                "wrapped behavior has no actor-safe declaration");
        }

        if (!string.IsNullOrWhiteSpace(recipe.AttachmentId))
        {
            var attachment = Resolve(recipe.AttachmentType, recipe.AttachmentId);
            if (attachment == null)
            {
                return ProjectionDeckCardCapability.Reject(
                    "attachment definition is not registered: "
                    + recipe.AttachmentType
                    + "/"
                    + recipe.AttachmentId);
            }

            var attachmentScript = ScriptSurface(attachment.Fields);
            scripts.Add(attachmentScript);
            var attachmentCapability = ProjectionCardExecutionPolicy.Resolve(
                attachment.Fields,
                null,
                recipe.AttachmentId,
                attachmentScript);
            if (attachmentCapability.Mode == ProjectionCardExecutionMode.Unsupported)
            {
                return ProjectionDeckCardCapability.Reject(
                    "attachment has no actor-safe declaration: " + recipe.AttachmentId);
            }
        }

        return ProjectionCardExecutionPolicy.IsHeadlessScriptSurfaceSafe(
            string.Join("\n", scripts),
            out var reason)
            ? ProjectionDeckCardCapability.Safe()
            : ProjectionDeckCardCapability.Reject(reason);
    }

    private static AuraGameDataSnapshot? Resolve(string typeName, string id)
    {
        if (!Enum.TryParse(typeName, true, out DataType type))
        {
            type = DataType.Card;
        }

        return AuraGameDataHostApi.Resolve(type, id)
               ?? (type == DataType.Card
                   ? null
                   : AuraGameDataHostApi.Resolve(DataType.Card, id));
    }

    private static string ScriptSurface(IReadOnlyDictionary<string, string> fields)
    {
        return string.Join("\n", fields.Values);
    }
}
