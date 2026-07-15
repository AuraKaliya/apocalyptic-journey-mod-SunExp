using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using AuraShared.Core;
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
        RefreshCurrentPartner();
    }

    public static FamiliarRosterDocument Roster() => FamiliarGrowthService.Snapshot();

    public static FamiliarInstance? Active() => FamiliarGrowthService.Active();

    public static FamiliarInstance? Body(string partnerId) => FamiliarGrowthService.Body(partnerId);

    public static string CurrentPartnerId() => FamiliarGrowthService.CurrentPartnerId();

    public static FamiliarInstance? RefreshCurrentPartner()
    {
        return FamiliarGrowthService.RefreshCurrentPartner(PartnerApi.CurrentPartnerId());
    }

    public static FamiliarInstance? BeginRunFromCurrentPartner()
    {
        return FamiliarGrowthService.BeginRun(PartnerApi.CurrentPartnerId());
    }

    public static bool Rename(string partnerId, string name) => FamiliarGrowthService.Rename(partnerId, name);

    public static FamiliarExperienceResult? GrantExperience(string partnerId, int amount)
    {
        return FamiliarGrowthService.GrantExperience(partnerId, amount);
    }

    public static FamiliarExperienceResult? GrantActiveExperience(int amount)
    {
        return FamiliarGrowthService.GrantActiveExperience(amount);
    }

    public static bool ChooseBlessing(string partnerId, string choiceId, string blessingId)
    {
        return FamiliarGrowthService.ChooseBlessing(partnerId, choiceId, blessingId);
    }

    public static bool CanRebirth(string partnerId) => FamiliarGrowthService.CanRebirth(partnerId);

    public static FamiliarRebirthResult? Rebirth(string partnerId) => FamiliarGrowthService.Rebirth(partnerId);

    public static bool ActiveHasBlessing(string blessingId) => FamiliarGrowthService.ActiveHasBlessing(blessingId);

    public static bool ActiveHasTag(string tag) => FamiliarGrowthService.ActiveHasTag(tag);

    public static bool ActiveHasEffect(string effectKind) => FamiliarGrowthService.ActiveHasEffect(effectKind);

    public static void OpenPanel() => FamiliarGrowthRuntime.OpenPanel();

    private sealed class FamiliarSidecarProfileStore : IFamiliarProfileStore
    {
        private readonly string legacyModDirectory;

        public FamiliarSidecarProfileStore(ModConfig modConfig)
        {
            legacyModDirectory = modConfig.DirectoryName;
        }

        public FamiliarRosterDocument Load()
        {
            var current = ProfilePath();
            var path = File.Exists(current) ? current : LegacyProfilePath();
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
                var recovered = TryRecoverLegacyProfile(path);
                if (recovered != null)
                {
                    SunExpLog.Warn("[FamiliarGrowth] repaired legacy profile " + path + ": " + ex.Message);
                    return recovered;
                }

                SunExpLog.Warn("[FamiliarGrowth] ignored invalid profile " + path + ": " + ex.Message);
                return new FamiliarRosterDocument();
            }
        }

        public void Save(FamiliarRosterDocument document)
        {
            var path = ProfilePath();
            try
            {
                WriteAtomic(path, JsonConvert.SerializeObject(document, Formatting.Indented));
            }
            catch (Exception ex)
            {
                SunExpLog.Warn("[FamiliarGrowth] failed to save profile " + path + ": " + ex.Message);
            }
        }

        private static FamiliarRosterDocument? TryRecoverLegacyProfile(string path)
        {
            try
            {
                var original = File.ReadAllText(path);
                var repaired = Regex.Replace(
                    original,
                    "(?m)^(\\s*\"Name\"\\s*:\\s*\"[^\"]*)(,\\s*)$",
                    "$1\"$2");
                if (string.Equals(original, repaired, StringComparison.Ordinal))
                {
                    return null;
                }

                var document = JsonConvert.DeserializeObject<FamiliarRosterDocument>(repaired);
                if (document == null)
                {
                    return null;
                }

                var backup = path + ".invalid.bak";
                if (!File.Exists(backup))
                {
                    File.Copy(path, backup, overwrite: false);
                }

                return document;
            }
            catch (Exception recoveryError)
            {
                SunExpLog.Warn("[FamiliarGrowth] legacy profile recovery failed " + path + ": " + recoveryError.Message);
                return null;
            }
        }

        private string ProfilePath()
        {
            var modsDirectory = Path.GetDirectoryName(legacyModDirectory) ?? legacyModDirectory;
            var gameDirectory = Path.GetDirectoryName(modsDirectory) ?? modsDirectory;
            var dataRoot = string.IsNullOrWhiteSpace(AuraSharedPaths.ModsDataDirectory)
                ? Path.Combine(gameDirectory, AuraSharedPaths.DefaultDataRootDirectoryName)
                : AuraSharedPaths.ModsDataDirectory;
            return Path.Combine(dataRoot, SunExpIds.ModId, SunExpIds.FamiliarProfileDirectory, ProfileKey() + ".json");
        }

        private string LegacyProfilePath()
        {
            return Path.Combine(legacyModDirectory, SunExpIds.FamiliarProfileDirectory, ProfileKey() + ".json");
        }

        private static void WriteAtomic(string path, string contents)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = path + ".tmp";
            var backup = path + ".bak";
            File.WriteAllText(temporary, contents);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporary, path);
            }
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
