using System;
using AuraShared.Core;
using AuraToolsExp.Dll.Features.DamageMeter.Model;
using AuraToolsExp.Dll.Infrastructure;

namespace AuraToolsExp.Dll.Features.DamageMeter.Network;

internal static class LegacyOutOfRunDamageHistoryPersistence
{
    private const string SystemName = "AuraTools";
    private const string FileName = "DamageHistory.auraenc.json";
    private const string EnvelopeFormat = "AuraToolsDamageHistoryEncrypted";

    public static OutOfRunDamageHistoryFile LoadLegacyFile()
    {
        try
        {
            var snapshot = AuraSharedConfigStore.ReadShared<AuraSharedEncryptedEnvelope>(
                AuraToolsIds.ModId,
                SystemName,
                FileName,
                new AuraSharedEncryptedEnvelope());
            if (!snapshot.Found || string.IsNullOrWhiteSpace(snapshot.Value?.Ciphertext))
            {
                return new OutOfRunDamageHistoryFile();
            }

            var envelopeJson = AuraSharedJson.Serialize(snapshot.Value);
            var plainJson = AuraSharedSecureEnvelope.DecryptJson(
                envelopeJson,
                EnvelopeFormat,
                LegacyDamageMeterHistoryCryptoKeys.EncryptionPrivateKeyXml,
                LegacyDamageMeterHistoryCryptoKeys.SignaturePublicKeyXml);
            return AuraSharedJson.Deserialize<OutOfRunDamageHistoryFile>(plainJson)
                   ?? new OutOfRunDamageHistoryFile();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[DamageMeter] legacy out-of-run history load failed: " + ex.Message);
            return new OutOfRunDamageHistoryFile();
        }
    }
}
