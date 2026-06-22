using System.IO;
using AuraOnline.Shared;
using ChatExp.Dll.Hooks;
using ChatExp.Dll.Infrastructure;
using Witch.Mod;

namespace ChatExp.Dll;

public static class Entry
{
    [ModInitialize]
    public static void Initialize(ModConfig modConfig)
    {
        AuraChatRuntime.Initialize(ChatExpIds.ModId, ChatExpIds.MaxMessages);
        AuraChatCatalogStore.LoadEncrypted(
            Path.Combine(modConfig.DirectoryName, ChatExpIds.CatalogRelativePath),
            ChatExpCatalogKeys.SignaturePublicKeyXml,
            ChatExpCatalogKeys.EncryptionPrivateKeyXml,
            ChatExpLog.Info,
            ChatExpLog.Warn);
        ChatExpRuntimeHooks.Initialize(modConfig);
        ChatExpLog.Info("Initialized.");
    }
}
