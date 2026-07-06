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

        ReportSaveResult(SaveFile(store.CreateFile(), maxEnvelopeBytes));
    }

    public static void SaveDeferred(OutOfRunDamageHistoryStore store, int maxEnvelopeBytes = DefaultMaxEnvelopeBytes)
    {
        if (store == null)
        {
            return;
        }

        var file = store.CreateFile();
        AuraSharedFrameScheduler.RunBackground(
            "DamageMeter.HistorySave",
            () => SaveFile(file, maxEnvelopeBytes),
            ReportSaveResult,
            ex => AuraToolsLog.Warn("[DamageMeter] out-of-run history deferred save failed: " + ex.Message));
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

    private static SaveResult SaveFile(OutOfRunDamageHistoryFile file, int maxEnvelopeBytes)
    {
        try
        {
            var envelopeJson = AuraSharedSecureEnvelope.EncryptJson(
                EnvelopeFormat,
                DamageMeterHistoryCryptoKeys.KeyId,
                AuraSharedJson.Serialize(file),
                DamageMeterHistoryCryptoKeys.EncryptionPublicKeyXml,
                DamageMeterHistoryCryptoKeys.SignaturePrivateKeyXml);
            var maxBytes = NormalizeMaxEnvelopeBytes(maxEnvelopeBytes);
            var envelopeBytes = Utf8ByteCount(envelopeJson);
            if (envelopeBytes > maxBytes)
            {
                return SaveResult.SkippedResult("encrypted envelope too large. bytes="
                                                + envelopeBytes + ", maxBytes=" + maxBytes + ".");
            }

            var envelope = AuraSharedJson.Deserialize<AuraSharedEncryptedEnvelope>(envelopeJson)
                           ?? new AuraSharedEncryptedEnvelope();
            var result = AuraSharedConfigStore.WriteShared(
                AuraToolsIds.ModId,
                SystemName,
                FileName,
                envelope,
                schemaVersion: 1);
            return result.Success
                ? SaveResult.Ok(envelopeBytes)
                : SaveResult.Failed(result.Message);
        }
        catch (Exception ex)
        {
            return SaveResult.Failed(ex.Message);
        }
    }

    private static void ReportSaveResult(SaveResult result)
    {
        if (result.Success)
        {
            return;
        }

        var prefix = result.Skipped
            ? "[DamageMeter] out-of-run history save skipped: "
            : "[DamageMeter] out-of-run history save failed: ";
        AuraToolsLog.Warn(prefix + result.Message);
    }

    private sealed class SaveResult
    {
        private SaveResult(bool success, bool skipped, string message, int bytes)
        {
            Success = success;
            Skipped = skipped;
            Message = message ?? "";
            Bytes = bytes;
        }

        public bool Success { get; }

        public bool Skipped { get; }

        public string Message { get; }

        public int Bytes { get; }

        public static SaveResult Ok(int bytes)
        {
            return new SaveResult(true, false, "", bytes);
        }

        public static SaveResult SkippedResult(string message)
        {
            return new SaveResult(false, true, message, 0);
        }

        public static SaveResult Failed(string message)
        {
            return new SaveResult(false, false, message, 0);
        }
    }
}
