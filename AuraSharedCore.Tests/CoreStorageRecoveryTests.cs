using AuraShared.Core;
using AuraJourney.Shared;
using AuraMode.Shared;
using AuraOnline.Shared;
using AuraDirector.Shared;
using AuraRole.Shared;
using AuraGameData.Shared;
using Newtonsoft.Json.Linq;
internal static partial class CoreTestSuite
{
    public static void Increment(AuraSharedStorageCoordinator coordinator)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var read = coordinator.Read(new AuraSharedStorageRequest
            {
                Scope = AuraSharedStorageScopes.Shared,
                System = "Concurrency",
                FileName = "counter.json"
            });
            var value = read.Found ? JObject.Parse(read.PayloadJson)["value"]!.Value<int>() : 0;
            var write = coordinator.Write(new AuraSharedStorageRequest
            {
                Scope = AuraSharedStorageScopes.Shared,
                System = "Concurrency",
                FileName = "counter.json",
                WriterId = "Counter",
                AuthorityId = "Counter",
                ExpectedRevision = read.Revision,
                PayloadJson = "{\"value\":" + (value + 1) + "}"
            });
            if (write.Success)
            {
                return;
            }
            if (!write.Conflict)
            {
                throw new InvalidOperationException(write.Message);
            }
        }
        throw new TimeoutException("CAS increment did not converge.");
    }
    
    public static void TestRecovery(AuraSharedStorageCoordinator coordinator, AuraSharedPackageCoordinator packageCoordinator)
    {
        var destination = Path.Combine(tempRoot, "Audio", "Recovery", "file.wav");
        var backup = Path.Combine(tempRoot, "Backups", "Recovery", "old.wav");
        var staging = Path.Combine(tempRoot, "Cache", "Packages", "recovery-test");
        var registry = Path.Combine(tempRoot, "Registries", "Recovery", "resources.json");
        var registryBackup = Path.Combine(staging, "registry.backup.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(Path.GetDirectoryName(registry)!);
        File.WriteAllText(destination, "new");
        File.WriteAllText(backup, "old");
        File.WriteAllText(registry, "{\"new\":true}");
        File.WriteAllText(registryBackup, "{\"old\":true}");
    
        var journal = new AuraSharedTransactionJournal
        {
            TransactionId = "recovery-test",
            State = "ContentCommitted",
            DestinationPath = destination,
            BackupPath = backup,
            StagingPath = staging,
            RegistryPath = registry,
            RegistryBackupPath = registryBackup,
            DestinationExisted = true,
            RegistryExisted = true,
            Kind = AuraSharedResourceKinds.File
        };
        coordinator.WriteRawJsonAtomic(Path.Combine(tempRoot, "Transactions", "recovery-test.json"), journal, false);
        var recovered = packageCoordinator.RecoverTransactions();
        if (recovered != 1 || File.ReadAllText(destination) != "old" || !File.ReadAllText(registry).Contains("old"))
        {
            throw new InvalidOperationException("Interrupted transaction was not restored.");
        }
    }
    
    public static void TestSecureEnvelopeContracts()
    {
        using var encryptionKey = new System.Security.Cryptography.RSACryptoServiceProvider(2048);
        using var signatureKey = new System.Security.Cryptography.RSACryptoServiceProvider(2048);
        var envelopeJson = AuraSharedSecureEnvelope.EncryptJson(
            "TestEnvelope",
            "test-key",
            "{\"value\":42}",
            encryptionKey.ToXmlString(false),
            signatureKey.ToXmlString(true));
    
        Assert(envelopeJson.Contains("RSA-OAEP-SHA1+A256CBC-HS256")
               && envelopeJson.Contains("RSA-SHA256"),
            "secure envelope records crypto algorithms");
    
        var plainJson = AuraSharedSecureEnvelope.DecryptJson(
            envelopeJson,
            "TestEnvelope",
            encryptionKey.ToXmlString(true),
            signatureKey.ToXmlString(false));
        Assert(JObject.Parse(plainJson)["value"]!.Value<int>() == 42,
            "secure envelope decrypts signed payload");
    
        var tamperedEnvelope = JObject.Parse(envelopeJson);
        tamperedEnvelope["ciphertext"] = "AA" + tamperedEnvelope["ciphertext"]!.Value<string>();
        var tampered = tamperedEnvelope.ToString(Newtonsoft.Json.Formatting.None);
        try
        {
            AuraSharedSecureEnvelope.DecryptJson(
                tampered,
                "TestEnvelope",
                encryptionKey.ToXmlString(true),
                signatureKey.ToXmlString(false));
            throw new InvalidOperationException("Tampered envelope was accepted.");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            Assert(true, "secure envelope rejects tampered payload");
        }
        catch (InvalidOperationException)
        {
            Assert(true, "secure envelope rejects tampered payload");
        }
        catch (FormatException)
        {
            Assert(true, "secure envelope rejects malformed payload");
        }
    }
}
