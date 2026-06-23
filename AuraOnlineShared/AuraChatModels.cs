using System;
using System.Collections.Generic;

namespace AuraOnline.Shared;

public static class AuraChatAreas
{
    public const string Chat = "Chat";
    public const string ModSyncStatus = "ModSyncStatus";
}

public static class AuraChatKinds
{
    public const string PlayerText = "PlayerText";
    public const string PresetMessage = "PresetMessage";
    public const string Sticker = "Sticker";
    public const string SystemStatus = "SystemStatus";
}

[Serializable]
public sealed class AuraChatMessage
{
    public string MessageId { get; set; } = "";

    public int Sequence { get; set; }

    public string Area { get; set; } = AuraChatAreas.Chat;

    public string Kind { get; set; } = AuraChatKinds.PlayerText;

    public string SenderId { get; set; } = "";

    public string SenderName { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    public string RawText { get; set; } = "";

    public string ContentKind { get; set; } = "";

    public string ContentId { get; set; } = "";

    public string CatalogHash { get; set; } = "";

    public long ServerTimeMs { get; set; }
}

[Serializable]
public sealed class AuraChatRenderSegment
{
    public string Kind { get; set; } = "Text";

    public string Text { get; set; } = "";

    public string PackId { get; set; } = "";

    public string StickerId { get; set; } = "";
}

[Serializable]
public sealed class AuraChatModPlayerSnapshot
{
    public string PlayerId { get; set; } = "";

    public string PlayerName { get; set; } = "";

    public List<AuraChatModSnapshot> Mods { get; set; } = new();
}

[Serializable]
public sealed class AuraChatModSyncState
{
    public string CurrentModId { get; set; } = "";

    public string LocalPlayerId { get; set; } = "";

    public string HostPlayerId { get; set; } = "";

    public List<AuraChatModPlayerSnapshot> Players { get; set; } = new();

    public List<AuraChatModSyncRow> Rows { get; set; } = new();
}

[Serializable]
public sealed class AuraChatModSyncRow
{
    public string ModKey { get; set; } = "";

    public string ModName { get; set; } = "";

    public AuraChatModSnapshot? HostMod { get; set; }

    public AuraChatModSnapshot? LocalMod { get; set; }
}

[Serializable]
public sealed class AuraChatModSnapshot
{
    public string ModId { get; set; } = "";

    public string ModName { get; set; } = "";

    public string ModVersion { get; set; } = "";

    public string ModAuthor { get; set; } = "";

    public string DirectoryName { get; set; } = "";

    public bool IsWorkshopMod { get; set; }

    public ulong PublishedFileId { get; set; }

    public bool Enabled { get; set; }

    public string MatchKey => string.IsNullOrWhiteSpace(ModId) ? ModName : ModId;
}
