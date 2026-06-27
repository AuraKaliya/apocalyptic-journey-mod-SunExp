using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace AuraShared.Core;

public static class AuraSharedSecureEnvelope
{
    public const string EncryptionAlgorithm = "RSA-OAEP-SHA1+A256CBC-HS256";
    public const string SignatureAlgorithm = "RSA-SHA256";

    public static string EncryptJson(
        string format,
        string keyId,
        string payloadJson,
        string encryptionPublicKeyXml,
        string signaturePrivateKeyXml)
    {
        var plainBytes = Encoding.UTF8.GetBytes(payloadJson ?? "");
        var keyMaterial = new byte[64];
        var iv = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(keyMaterial);
            rng.GetBytes(iv);
        }

        var aesKey = new byte[32];
        var hmacKey = new byte[32];
        Buffer.BlockCopy(keyMaterial, 0, aesKey, 0, aesKey.Length);
        Buffer.BlockCopy(keyMaterial, 32, hmacKey, 0, hmacKey.Length);

        var envelope = new AuraSharedEncryptedEnvelope
        {
            Format = string.IsNullOrWhiteSpace(format) ? "AuraSharedEncrypted" : format.Trim(),
            Version = 1,
            KeyId = keyId?.Trim() ?? "",
            EncAlg = EncryptionAlgorithm,
            SigAlg = SignatureAlgorithm,
            EncryptedKey = Convert.ToBase64String(EncryptKeyMaterial(keyMaterial, encryptionPublicKeyXml)),
            Iv = Convert.ToBase64String(iv),
            Ciphertext = Convert.ToBase64String(EncryptPayload(aesKey, iv, plainBytes)),
            PayloadSha256 = Sha256Hex(plainBytes)
        };
        envelope.Hmac = Convert.ToBase64String(ComputeHmac(hmacKey, Convert.FromBase64String(envelope.Iv), Convert.FromBase64String(envelope.Ciphertext)));
        envelope.Signature = Convert.ToBase64String(Sign(envelope, signaturePrivateKeyXml));
        return AuraSharedJson.Serialize(envelope);
    }

    public static string DecryptJson(
        string envelopeJson,
        string expectedFormat,
        string decryptionPrivateKeyXml,
        string signaturePublicKeyXml)
    {
        var envelope = AuraSharedJson.Deserialize<AuraSharedEncryptedEnvelope>(envelopeJson)
                       ?? throw new InvalidOperationException("Encrypted envelope is empty.");
        ValidateEnvelope(envelope, expectedFormat);
        VerifySignature(envelope, signaturePublicKeyXml);

        var keyMaterial = DecryptKeyMaterial(Convert.FromBase64String(envelope.EncryptedKey), decryptionPrivateKeyXml);
        if (keyMaterial.Length != 64)
        {
            throw new InvalidOperationException("Encrypted envelope key material length is invalid.");
        }

        var aesKey = new byte[32];
        var hmacKey = new byte[32];
        Buffer.BlockCopy(keyMaterial, 0, aesKey, 0, aesKey.Length);
        Buffer.BlockCopy(keyMaterial, 32, hmacKey, 0, hmacKey.Length);

        var iv = Convert.FromBase64String(envelope.Iv);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var expectedHmac = Convert.FromBase64String(envelope.Hmac);
        var actualHmac = ComputeHmac(hmacKey, iv, ciphertext);
        if (!FixedTimeEquals(actualHmac, expectedHmac))
        {
            throw new InvalidOperationException("Encrypted envelope HMAC is invalid.");
        }

        var plainBytes = DecryptPayload(aesKey, iv, ciphertext);
        if (!string.Equals(Sha256Hex(plainBytes), envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Encrypted envelope payload hash mismatch.");
        }

        return Encoding.UTF8.GetString(plainBytes);
    }

    public static string CanonicalizeForSignature(AuraSharedEncryptedEnvelope envelope)
    {
        var builder = new StringBuilder(2048);
        builder.Append("Format=").Append(envelope.Format ?? "").Append('\n');
        builder.Append("Version=").Append(envelope.Version).Append('\n');
        builder.Append("KeyId=").Append(envelope.KeyId ?? "").Append('\n');
        builder.Append("EncAlg=").Append(envelope.EncAlg ?? "").Append('\n');
        builder.Append("SigAlg=").Append(envelope.SigAlg ?? "").Append('\n');
        builder.Append("EncryptedKey=").Append(envelope.EncryptedKey ?? "").Append('\n');
        builder.Append("Iv=").Append(envelope.Iv ?? "").Append('\n');
        builder.Append("Ciphertext=").Append(envelope.Ciphertext ?? "").Append('\n');
        builder.Append("Hmac=").Append(envelope.Hmac ?? "").Append('\n');
        builder.Append("PayloadSha256=").Append(envelope.PayloadSha256 ?? "");
        return builder.ToString();
    }

    public static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return ToHex(sha.ComputeHash(bytes ?? Array.Empty<byte>()));
    }

    private static byte[] EncryptKeyMaterial(byte[] keyMaterial, string publicKeyXml)
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        rsa.FromXmlString(publicKeyXml);
        return rsa.Encrypt(keyMaterial, true);
    }

    private static byte[] DecryptKeyMaterial(byte[] encryptedKey, string privateKeyXml)
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        rsa.FromXmlString(privateKeyXml);
        return rsa.Decrypt(encryptedKey, true);
    }

    private static byte[] Sign(AuraSharedEncryptedEnvelope envelope, string privateKeyXml)
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        rsa.FromXmlString(privateKeyXml);
        var data = Encoding.UTF8.GetBytes(CanonicalizeForSignature(envelope));
        return rsa.SignData(data, Sha256Oid());
    }

    private static void VerifySignature(AuraSharedEncryptedEnvelope envelope, string publicKeyXml)
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        rsa.FromXmlString(publicKeyXml);
        var data = Encoding.UTF8.GetBytes(CanonicalizeForSignature(envelope));
        var signature = Convert.FromBase64String(envelope.Signature);
        if (!rsa.VerifyData(data, Sha256Oid(), signature))
        {
            throw new InvalidOperationException("Encrypted envelope signature is invalid.");
        }
    }

    private static byte[] EncryptPayload(byte[] aesKey, byte[] iv, byte[] plainBytes)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = aesKey;
        aes.IV = iv;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
    }

    private static byte[] DecryptPayload(byte[] aesKey, byte[] iv, byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = aesKey;
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    private static byte[] ComputeHmac(byte[] hmacKey, byte[] iv, byte[] ciphertext)
    {
        using var hmac = new HMACSHA256(hmacKey);
        var combined = new byte[iv.Length + ciphertext.Length];
        Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, iv.Length, ciphertext.Length);
        return hmac.ComputeHash(combined);
    }

    private static void ValidateEnvelope(AuraSharedEncryptedEnvelope envelope, string expectedFormat)
    {
        if (!string.IsNullOrWhiteSpace(expectedFormat)
            && !string.Equals(envelope.Format, expectedFormat, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Encrypted envelope format is unsupported.");
        }

        if (envelope.Version != 1
            || !string.Equals(envelope.EncAlg, EncryptionAlgorithm, StringComparison.Ordinal)
            || !string.Equals(envelope.SigAlg, SignatureAlgorithm, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(envelope.EncryptedKey)
            || string.IsNullOrWhiteSpace(envelope.Iv)
            || string.IsNullOrWhiteSpace(envelope.Ciphertext)
            || string.IsNullOrWhiteSpace(envelope.Hmac)
            || string.IsNullOrWhiteSpace(envelope.PayloadSha256)
            || string.IsNullOrWhiteSpace(envelope.Signature))
        {
            throw new InvalidOperationException("Encrypted envelope is incomplete.");
        }
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < left.Length; i++)
        {
            diff |= left[i] ^ right[i];
        }

        return diff == 0;
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private static object Sha256Oid()
    {
        return CryptoConfig.MapNameToOID("SHA256")
               ?? throw new CryptographicException("SHA256 OID is unavailable.");
    }
}

[Serializable]
public sealed class AuraSharedEncryptedEnvelope
{
    [JsonProperty("format")]
    public string Format { get; set; } = "";

    [JsonProperty("version")]
    public int Version { get; set; }

    [JsonProperty("keyId")]
    public string KeyId { get; set; } = "";

    [JsonProperty("encAlg")]
    public string EncAlg { get; set; } = "";

    [JsonProperty("sigAlg")]
    public string SigAlg { get; set; } = "";

    [JsonProperty("encryptedKey")]
    public string EncryptedKey { get; set; } = "";

    [JsonProperty("iv")]
    public string Iv { get; set; } = "";

    [JsonProperty("ciphertext")]
    public string Ciphertext { get; set; } = "";

    [JsonProperty("hmac")]
    public string Hmac { get; set; } = "";

    [JsonProperty("payloadSha256")]
    public string PayloadSha256 { get; set; } = "";

    [JsonProperty("signature")]
    public string Signature { get; set; } = "";
}
