using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Terrias.Dll.Mechanics;

public static class SpiritCollectionDocumentCodec
{
    private static readonly string[] LegacyInstanceFields =
    {
        "SpiritUid", "SpeciesId", "ProfileId", "ElementId", "ElementSource",
        "ElementAssignmentRevision", "Snapshot", "Presentation", "Level", "Experience",
        "Aptitude", "Speed", "GuiyuanValue", "GuiyuanAllocations", "TrainingPlanVersion",
        "InherentAbilityPlanVersion", "ResolvedInherentIntentIds", "ResolvedInherentPassiveId",
        "LearnedIntentIds", "EquippedIntentIds", "LearnedPassiveIds", "EquippedPassiveId",
        "UnlockPlan", "NewAbilityIds", "LoadoutRevision", "LoadoutHash", "ArtifactLoadout",
        "Favorite", "Locked", "CapturedAt"
    };

    public static SpiritCollectionDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new SpiritCollectionDocument();
        var root = JObject.Parse(json);
        var sourceVersion = root.Value<int?>("Version") ?? 0;
        if (sourceVersion > SpiritSystemContract.CollectionVersion)
            throw new InvalidOperationException("Spirit collection version " + sourceVersion
                                                + " is newer than supported version "
                                                + SpiritSystemContract.CollectionVersion + ".");
        if (root["Instances"] is JArray instances)
        {
            foreach (var token in instances)
            {
                if (token is not JObject instance) continue;
                if (sourceVersion < SpiritSystemContract.CollectionVersion) MigrateInstance(instance);
                else if (!HasCurrentComponents(instance))
                    throw new InvalidOperationException("Spirit collection V12 instance is missing a required component.");
            }
        }
        var document = root.ToObject<SpiritCollectionDocument>() ?? new SpiritCollectionDocument();
        document.Version = sourceVersion;
        return document;
    }

    public static string Serialize(SpiritCollectionDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        return JsonConvert.SerializeObject(document, Formatting.None);
    }

    private static void MigrateInstance(JObject instance)
    {
        instance["Identity"] ??= Component(
            SpiritComponentVersions.Identity,
            ("SpiritUid", Copy(instance, "SpiritUid", "")),
            ("SpeciesId", Copy(instance, "SpeciesId", "")),
            ("ProfileId", Copy(instance, "ProfileId", "")));
        instance["Source"] ??= Component(
            SpiritComponentVersions.Source,
            ("Capture", Copy(instance, "Snapshot", new JObject())),
            ("Presentation", Copy(instance, "Presentation", new JObject())),
            ("CapturedAt", Copy(instance, "CapturedAt", "")));
        instance["Growth"] ??= Component(
            SpiritComponentVersions.Growth,
            ("Level", Copy(instance, "Level", 1)),
            ("Experience", Copy(instance, "Experience", 0)),
            ("Aptitude", Copy(instance, "Aptitude", 60)),
            ("Speed", Copy(instance, "Speed", 0)));
        instance["Element"] ??= Component(
            SpiritComponentVersions.Element,
            ("ElementId", Copy(instance, "ElementId", "")),
            ("Source", Copy(instance, "ElementSource", "")),
            ("AssignmentRevision", Copy(instance, "ElementAssignmentRevision", 0)));
        instance["Ascension"] ??= Component(
            SpiritComponentVersions.Ascension,
            ("GuiyuanValue", Copy(instance, "GuiyuanValue", 0)),
            ("Allocations", Copy(instance, "GuiyuanAllocations", new JObject())));
        instance["Training"] ??= Component(
            SpiritComponentVersions.Training,
            ("TrainingPlanVersion", Copy(instance, "TrainingPlanVersion", 0)),
            ("InherentAbilityPlanVersion", Copy(instance, "InherentAbilityPlanVersion", 0)),
            ("ResolvedInherentIntentIds", Copy(instance, "ResolvedInherentIntentIds", new JArray())),
            ("ResolvedInherentPassiveId", Copy(instance, "ResolvedInherentPassiveId", "")),
            ("LearnedIntentIds", Copy(instance, "LearnedIntentIds", new JArray())),
            ("EquippedIntentIds", Copy(instance, "EquippedIntentIds", new JArray())),
            ("LearnedPassiveIds", Copy(instance, "LearnedPassiveIds", new JArray())),
            ("EquippedPassiveId", Copy(instance, "EquippedPassiveId", "")),
            ("UnlockPlan", Copy(instance, "UnlockPlan", new JArray())),
            ("NewAbilityIds", Copy(instance, "NewAbilityIds", new JArray())),
            ("LoadoutRevision", Copy(instance, "LoadoutRevision", 0)),
            ("LoadoutHash", Copy(instance, "LoadoutHash", "")));
        instance["Equipment"] ??= Component(
            SpiritComponentVersions.Equipment,
            ("ArtifactLoadout", Copy(instance, "ArtifactLoadout", new JObject())));
        instance["Metadata"] ??= Component(
            SpiritComponentVersions.Metadata,
            ("Favorite", Copy(instance, "Favorite", false)),
            ("Locked", Copy(instance, "Locked", false)));

        foreach (var field in LegacyInstanceFields) instance.Remove(field);
    }

    private static bool HasCurrentComponents(JObject instance)
        => instance["Identity"] is JObject
           && instance["Source"] is JObject
           && instance["Growth"] is JObject
           && instance["Element"] is JObject
           && instance["Ascension"] is JObject
           && instance["Training"] is JObject
           && instance["Equipment"] is JObject
           && instance["Metadata"] is JObject;

    private static JObject Component(int version, params (string Name, JToken Value)[] values)
    {
        var result = new JObject { ["Version"] = version };
        foreach (var value in values) result[value.Name] = value.Value;
        return result;
    }

    private static JToken Copy(JObject source, string name, object fallback)
    {
        if (source.TryGetValue(name, StringComparison.Ordinal, out var value) && value.Type != JTokenType.Null)
            return value.DeepClone();
        return fallback is JToken token ? token.DeepClone() : JToken.FromObject(fallback);
    }
}
