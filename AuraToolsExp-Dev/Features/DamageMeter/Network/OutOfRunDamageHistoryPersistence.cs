using System;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class OutOfRunDamageHistoryPersistence
{
    private const string SystemName = "AuraTools";
    private const string FileName = "DamageHistory.auraenc.json";
    private const string EnvelopeFormat = "AuraToolsDamageHistoryEncrypted";

    public static void LoadInto(OutOfRunDamageHistoryStore store)
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

            var plainJson = AuraSharedSecureEnvelope.DecryptJson(
                AuraSharedJson.Serialize(snapshot.Value),
                EnvelopeFormat,
                DamageMeterHistoryCryptoKeys.EncryptionPrivateKeyXml,
                DamageMeterHistoryCryptoKeys.SignaturePublicKeyXml);
            store.ApplyFile(AuraSharedJson.Deserialize<OutOfRunDamageHistoryFile>(plainJson));
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] out-of-run history load failed: " + ex.Message);
            store.ApplyFile(new OutOfRunDamageHistoryFile());
        }
    }

    public static void Save(OutOfRunDamageHistoryStore store)
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

    public static void Clear(OutOfRunDamageHistoryStore store)
    {
        store?.Clear();
        if (store != null)
        {
            Save(store);
        }
    }
}
