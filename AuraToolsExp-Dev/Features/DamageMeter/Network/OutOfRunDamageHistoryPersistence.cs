using System;
using System.Text;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class OutOfRunDamageHistoryPersistence
{
    private const string SystemName = "AuraTools";
    private const string FileName = "DamageHistory.auraenc.json";
    private const string EnvelopeFormat = "AuraToolsDamageHistoryEncrypted";
    private const int DefaultMaxEnvelopeBytes = 1048576;

    public static void LoadInto(OutOfRunDamageHistoryStore store, int maxEnvelopeBytes = DefaultMaxEnvelopeBytes)
    {
        if (store == null)
        {
            return;
        }

        try
        {
            var snapshot = AuraSharedConfigStore.ReadShared<AuraSharedEncryptedEnvelope>(
                AuraToolsIds.ModId,
                SystemName,
                FileName,
                new AuraSharedEncryptedEnvelope());
            if (!snapshot.Found || string.IsNullOrWhiteSpace(snapshot.Value?.Ciphertext))
            {
                store.ApplyFile(new OutOfRunDamageHistoryFile());
                return;
            }

            var maxBytes = NormalizeMaxEnvelopeBytes(maxEnvelopeBytes);
            var envelopeJson = AuraSharedJson.Serialize(snapshot.Value);
            if (Utf8ByteCount(envelopeJson) > maxBytes)
            {
                AuraToolsLog.Warn("[DamageMeter] out-of-run history load skipped: encrypted envelope too large. maxBytes="
                                  + maxBytes + ".");
                store.ApplyFile(new OutOfRunDamageHistoryFile());
                return;
            }

            var plainJson = AuraSharedSecureEnvelope.DecryptJson(
                envelopeJson,
                EnvelopeFormat,
                DamageMeterHistoryCryptoKeys.EncryptionPrivateKeyXml,
                DamageMeterHistoryCryptoKeys.SignaturePublicKeyXml);
            if (Utf8ByteCount(plainJson) > maxBytes * 2)
            {
                AuraToolsLog.Warn("[DamageMeter] out-of-run history load skipped: decrypted payload too large. maxBytes="
                                  + (maxBytes * 2) + ".");
                store.ApplyFile(new OutOfRunDamageHistoryFile());
                return;
            }

            store.ApplyFile(AuraSharedJson.Deserialize<OutOfRunDamageHistoryFile>(plainJson));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] out-of-run history load failed: " + ex.Message);
            store.ApplyFile(new OutOfRunDamageHistoryFile());
        }
    }

    public static void Save(OutOfRunDamageHistoryStore store, int maxEnvelopeBytes = DefaultMaxEnvelopeBytes)
    {
        if (store == null)
        {
            return;
        }

        try
        {
            var envelopeJson = AuraSharedSecureEnvelope.EncryptJson(
                EnvelopeFormat,
                DamageMeterHistoryCryptoKeys.KeyId,
                AuraSharedJson.Serialize(store.CreateFile()),
                DamageMeterHistoryCryptoKeys.EncryptionPublicKeyXml,
                DamageMeterHistoryCryptoKeys.SignaturePrivateKeyXml);
            var maxBytes = NormalizeMaxEnvelopeBytes(maxEnvelopeBytes);
            if (Utf8ByteCount(envelopeJson) > maxBytes)
            {
                AuraToolsLog.Warn("[DamageMeter] out-of-run history save skipped: encrypted envelope too large. bytes="
                                  + Utf8ByteCount(envelopeJson) + ", maxBytes=" + maxBytes + ".");
                return;
            }

            var envelope = AuraSharedJson.Deserialize<AuraSharedEncryptedEnvelope>(envelopeJson)
                           ?? new AuraSharedEncryptedEnvelope();
            var result = AuraSharedConfigStore.WriteShared(
                AuraToolsIds.ModId,
                SystemName,
                FileName,
                envelope,
                schemaVersion: 1);
            if (!result.Success)
            {
                AuraToolsLog.Warn("[DamageMeter] out-of-run history save failed: " + result.Message);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] out-of-run history save failed: " + ex.Message);
        }
    }

    public static void Clear(OutOfRunDamageHistoryStore store, int maxEnvelopeBytes = DefaultMaxEnvelopeBytes)
    {
        store?.Clear();
        if (store != null)
        {
            Save(store, maxEnvelopeBytes);
        }
    }

    private static int NormalizeMaxEnvelopeBytes(int value)
    {
        return Math.Max(65536, Math.Min(8388608, value <= 0 ? DefaultMaxEnvelopeBytes : value));
    }

    private static int Utf8ByteCount(string value)
    {
        return string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
    }
}
