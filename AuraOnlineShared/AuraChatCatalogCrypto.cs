using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace AuraOnline.Shared;

public static class AuraChatCatalogCrypto
{
    public const string EnvelopeFormat = "AuraChatCatalogEncrypted";
    public const string EncryptionAlgorithm = "RSA-OAEP-SHA1+A256CBC-HS256";
    public const string SignatureAlgorithm = "RSA-SHA256";

    public static AuraChatCatalog LoadEncryptedCatalog(
        string filePath,
        string signPublicKeyXml,
        string decryptPrivateKeyXml,
        out string catalogHash)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Encrypted chat catalog not found.", filePath);
        }

        var envelope = JsonConvert.DeserializeObject<AuraChatEncryptedCatalogEnvelope>(File.ReadAllText(filePath, Encoding.UTF8))
            ?? throw new InvalidOperationException("Encrypted chat catalog envelope is empty.");

        ValidateEnvelope(envelope);
        VerifySignature(envelope, signPublicKeyXml);

        var encryptedKey = Convert.FromBase64String(envelope.EncryptedKey);
        var iv = Convert.FromBase64String(envelope.Iv);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var expectedHmac = Convert.FromBase64String(envelope.Hmac);
        var keyMaterial = DecryptKeyMaterial(encryptedKey, decryptPrivateKeyXml);
        if (keyMaterial.Length != 64)
        {
            throw new InvalidOperationException("Encrypted chat catalog key material length is invalid.");
        }

        var aesKey = new byte[32];
        var hmacKey = new byte[32];
        Buffer.BlockCopy(keyMaterial, 0, aesKey, 0, aesKey.Length);
        Buffer.BlockCopy(keyMaterial, 32, hmacKey, 0, hmacKey.Length);

        VerifyHmac(hmacKey, iv, ciphertext, expectedHmac);
        var plainBytes = DecryptPayload(aesKey, iv, ciphertext);
        catalogHash = Sha256Hex(plainBytes);
        if (!string.Equals(catalogHash, envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Encrypted chat catalog payload hash mismatch.");
        }

        return JsonConvert.DeserializeObject<AuraChatCatalog>(Encoding.UTF8.GetString(plainBytes))
            ?? throw new InvalidOperationException("Encrypted chat catalog payload is empty.");
    }

    public static string CanonicalizeForSignature(AuraChatEncryptedCatalogEnvelope envelope)
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

    private static void ValidateEnvelope(AuraChatEncryptedCatalogEnvelope envelope)
    {
        if (!string.Equals(envelope.Format, EnvelopeFormat, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Encrypted chat catalog format is unsupported.");
        }

        if (envelope.Version != 1)
        {
            throw new InvalidOperationException("Encrypted chat catalog version is unsupported.");
        }

        if (!string.Equals(envelope.EncAlg, EncryptionAlgorithm, StringComparison.Ordinal)
            || !string.Equals(envelope.SigAlg, SignatureAlgorithm, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Encrypted chat catalog crypto algorithm is unsupported.");
        }

        if (string.IsNullOrWhiteSpace(envelope.EncryptedKey)
            || string.IsNullOrWhiteSpace(envelope.Iv)
            || string.IsNullOrWhiteSpace(envelope.Ciphertext)
            || string.IsNullOrWhiteSpace(envelope.Hmac)
            || string.IsNullOrWhiteSpace(envelope.PayloadSha256)
            || string.IsNullOrWhiteSpace(envelope.Signature))
        {
            throw new InvalidOperationException("Encrypted chat catalog envelope is incomplete.");
        }
    }

    private static void VerifySignature(AuraChatEncryptedCatalogEnvelope envelope, string signPublicKeyXml)
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        rsa.FromXmlString(signPublicKeyXml);
        var data = Encoding.UTF8.GetBytes(CanonicalizeForSignature(envelope));
        var signature = Convert.FromBase64String(envelope.Signature);
        if (!rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256"), signature))
        {
            throw new InvalidOperationException("Encrypted chat catalog signature is invalid.");
        }
    }

    private static byte[] DecryptKeyMaterial(byte[] encryptedKey, string decryptPrivateKeyXml)
    {
        using var rsa = new RSACryptoServiceProvider(2048);
        rsa.FromXmlString(decryptPrivateKeyXml);
        return rsa.Decrypt(encryptedKey, true);
    }

    private static void VerifyHmac(byte[] hmacKey, byte[] iv, byte[] ciphertext, byte[] expected)
    {
        using var hmac = new HMACSHA256(hmacKey);
        var actual = hmac.ComputeHash(Combine(iv, ciphertext));
        if (!FixedTimeEquals(actual, expected))
        {
            throw new InvalidOperationException("Encrypted chat catalog HMAC is invalid.");
        }
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

    private static byte[] Combine(byte[] first, byte[] second)
    {
        var result = new byte[(first?.Length ?? 0) + (second?.Length ?? 0)];
        if (first != null)
        {
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
        }

        if (second != null)
        {
            Buffer.BlockCopy(second, 0, result, first?.Length ?? 0, second.Length);
        }

        return result;
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
        {
            return false;
        }

        var diff = 0;
        for (var index = 0; index < left.Length; index++)
        {
            diff |= left[index] ^ right[index];
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
}
