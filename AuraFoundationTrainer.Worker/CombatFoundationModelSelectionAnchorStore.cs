using AuraCombatAi.Shared;
using Newtonsoft.Json;

namespace AuraFoundationTrainer.Worker;

internal static class CombatFoundationModelSelectionAnchorStore
{
    public static void Load(CombatFoundationWorkerJob job)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }
        job.Request.ModelSelectionAnchorEpisodes = new List<CombatEpisode>();
        if (CombatFoundationPathRuntime.FileExists(
                job.ModelSelectionAnchorPath))
        {
            foreach (var line in File.ReadLines(
                         CombatFoundationPathRuntime.ForFileSystem(
                             job.ModelSelectionAnchorPath)))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                var episode = JsonConvert.DeserializeObject<CombatEpisode>(line);
                if (episode != null)
                {
                    job.Request.ModelSelectionAnchorEpisodes.Add(episode);
                }
            }
        }
        job.Request.ModelSelectionAnchorCreated = episodes =>
        {
            if (CombatFoundationPathRuntime.FileExists(
                    job.ModelSelectionAnchorPath))
            {
                return;
            }
            CombatFoundationCheckpointStorage.WriteAtomicJsonLines(
                job.ModelSelectionAnchorPath,
                episodes.Select(SerializeCompact));
        };
    }

    private static string SerializeCompact(CombatEpisode episode)
    {
        return JsonConvert.SerializeObject(
            episode,
            Formatting.None,
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                FloatFormatHandling = FloatFormatHandling.DefaultValue,
                ContractResolver =
                    WorkerCompactEpisodeContractResolver.Instance
            });
    }
}
