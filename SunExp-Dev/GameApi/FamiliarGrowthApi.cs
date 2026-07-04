using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using SunExp.Dll.Hooks;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using Witch.Core;
using Witch.Mod;

namespace SunExp.Dll.GameApi;

public static class FamiliarGrowthApi
{
    public static void Initialize(ModConfig modConfig)
    {
        FamiliarBlessingRegistry.Load(modConfig);
        FamiliarGrowthService.Configure(new FamiliarSidecarProfileStore(modConfig));
    }

    public static FamiliarRosterDocument Roster()
    {
        return FamiliarGrowthService.Snapshot();
    }

    public static FamiliarInstance? Selected()
    {
        return FamiliarGrowthService.Selected();
    }

    public static FamiliarInstance? Create(string speciesId)
    {
        return FamiliarGrowthService.Create(speciesId);
    }

    public static bool Delete(string instanceId)
    {
        return FamiliarGrowthService.Delete(instanceId);
    }

    public static bool Rename(string instanceId, string name)
    {
        return FamiliarGrowthService.Rename(instanceId, name);
    }

    public static bool Select(string instanceId)
    {
        return FamiliarGrowthService.Select(instanceId);
    }

    public static FamiliarExperienceResult? GrantExperience(string instanceId, int amount)
    {
        return FamiliarGrowthService.GrantExperience(instanceId, amount);
    }

    public static FamiliarExperienceResult? GrantSelectedExperience(int amount)
    {
        return FamiliarGrowthService.GrantSelectedExperience(amount);
    }

    public static bool ChooseBlessing(string instanceId, string choiceId, string blessingId)
    {
        return FamiliarGrowthService.ChooseBlessing(instanceId, choiceId, blessingId);
    }

    public static bool SelectedHasBlessing(string blessingId)
    {
        return FamiliarGrowthService.SelectedHasBlessing(blessingId);
    }

    public static bool SelectedHasTag(string tag)
    {
        return FamiliarGrowthService.SelectedHasTag(tag);
    }

    public static bool SelectedHasEffect(string effectKind)
    {
        return FamiliarGrowthService.SelectedHasEffect(effectKind);
    }

    public static bool SelectedCanManifest()
    {
        return SelectedHasEffect("ManifestEnable");
    }

    public static void OpenPanel()
    {
        FamiliarGrowthRuntime.OpenPanel();
    }

    private sealed class FamiliarSidecarProfileStore : IFamiliarProfileStore
    {
        private readonly string modDirectory;

        public FamiliarSidecarProfileStore(ModConfig modConfig)
        {
            modDirectory = modConfig.DirectoryName;
        }

        public FamiliarRosterDocument Load()
        {
            var path = ProfilePath();
            if (!File.Exists(path))
            {
                return new FamiliarRosterDocument();
            }

            try
            {
                return JsonConvert.DeserializeObject<FamiliarRosterDocument>(File.ReadAllText(path))
                       ?? new FamiliarRosterDocument();
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("[FamiliarGrowth] ignored invalid profile " + path + ": " + ex.Message);
                return new FamiliarRosterDocument();
            }
        }

        public void Save(FamiliarRosterDocument document)
        {
            var path = ProfilePath();
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, JsonConvert.SerializeObject(document, Formatting.Indented));
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("[FamiliarGrowth] failed to save profile " + path + ": " + ex.Message);
            }
        }

        private string ProfilePath()
        {
            return Path.Combine(
                modDirectory,
                SunExpIds.FamiliarProfileDirectory,
                ProfileKey() + ".json");
        }

        private static string ProfileKey()
        {
            var playerId = RuntimeMember(Singleton<GameRuntimeData>.Instance, "PlayerId");
            if (!string.IsNullOrWhiteSpace(playerId))
            {
                return FamiliarId.Sanitize(playerId);
            }

            var savePath = RuntimeMember(Singleton<GameRuntimeData>.Instance, "savePath");
            if (!string.IsNullOrWhiteSpace(savePath))
            {
                return FamiliarId.Sanitize(Path.GetFileNameWithoutExtension(savePath));
            }

            return "local";
        }

        private static string RuntimeMember(object? target, string name)
        {
            if (target == null)
            {
                return "";
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();
            var value = type.GetProperty(name, flags)?.GetValue(target)
                        ?? type.GetField(name, flags)?.GetValue(target);
            return Convert.ToString(value) ?? "";
        }
    }
}
