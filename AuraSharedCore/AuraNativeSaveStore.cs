using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Data.Save;
using Newtonsoft.Json;

namespace AuraShared.Core;

// Native SaveInfo serialization/encryption, committed through Core's atomic
// file writer. Role data and its commit/version markers share one native save.
public static class AuraNativeSaveStore
{
    public static void Commit(SaveInfo save)
    {
        if (!AuraNetworkIdentityRuntime.IsAuthority || !ReferenceEquals(save, GameSaveManager.GetNowSave()))
            throw new InvalidOperationException("Only the current authoritative save can be committed.");
        if (string.IsNullOrWhiteSpace(save.Name) || Path.GetFileName(save.Name) != save.Name
            || save.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Invalid native save name.");
        var root = Path.GetFullPath(SaveInfo.SavePath);
        var path = Path.Combine(root, save.Name + ".sav");
        var json = JsonConvert.SerializeObject(save, Formatting.Indented);
        var bytes = Encode(json, GameRuntimeData.isEncrypted,
            GameRuntimeData.isEncrypted ? GameRuntimeData.key : "",
            GameRuntimeData.isEncrypted ? GameRuntimeData.iv : "");
        using var store = new AuraSharedStorageCoordinator(root);
        try { store.WriteBytesAtomic(path, bytes, createBackup: true); }
        catch
        {
            // A filesystem error after replacement must not report a durable
            // commit as rejected and roll back its in-memory receipt.
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes)) throw;
        }
    }

    public static byte[] Encode(string json, bool encrypted, string key, string iv)
    {
        if (!encrypted) return new UTF8Encoding(false).GetBytes(json);
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key); aes.IV = Encoding.UTF8.GetBytes(iv);
        using var buffer = new MemoryStream();
        using (var crypto = new CryptoStream(buffer, aes.CreateEncryptor(), CryptoStreamMode.Write))
        using (var writer = new StreamWriter(crypto)) writer.Write(json);
        return buffer.ToArray();
    }
}
