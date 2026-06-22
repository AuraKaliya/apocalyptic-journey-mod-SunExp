using System;
using System.Collections.Generic;

namespace AuraOnline.Shared;

[Serializable]
public sealed class AuraChatCatalog
{
    public int SchemaVersion { get; set; }

    public string CatalogId { get; set; } = "";

    public string CatalogVersion { get; set; } = "";

    public List<AuraChatCatalogMessage> Messages { get; set; } = new();

    public List<AuraChatCatalogSticker> Stickers { get; set; } = new();
}

[Serializable]
public sealed class AuraChatCatalogMessage
{
    public string Id { get; set; } = "";

    public string Text { get; set; } = "";

    public int Order { get; set; }
}

[Serializable]
public sealed class AuraChatCatalogSticker
{
    public string Id { get; set; } = "";

    public string PackId { get; set; } = "";

    public string StickerId { get; set; } = "";

    public string ResourcePath { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public int Order { get; set; }
}

[Serializable]
public sealed class AuraChatEncryptedCatalogEnvelope
{
    public string Format { get; set; } = "";

    public int Version { get; set; }

    public string KeyId { get; set; } = "";

    public string EncAlg { get; set; } = "";

    public string SigAlg { get; set; } = "";

    public string EncryptedKey { get; set; } = "";

    public string Iv { get; set; } = "";

    public string Ciphertext { get; set; } = "";

    public string Hmac { get; set; } = "";

    public string PayloadSha256 { get; set; } = "";

    public string Signature { get; set; } = "";
}
