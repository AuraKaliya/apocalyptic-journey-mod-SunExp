using System;
using System.IO;

namespace AuraShared.Core;

public static class AuraSharedConfigStore
{
    public static AuraSharedConfigSnapshot<T> ReadShared<T>(
        string callerId,
        string system,
        string fileName,
        T fallback)
    {
        return Read(callerId, new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Shared,
            System = system,
            FileName = fileName
        }, fallback);
    }

    public static AuraSharedConfigWriteResult WriteShared<T>(
        string authorityId,
        string system,
        string fileName,
        T value,
        long expectedRevision = -1,
        int schemaVersion = 1)
    {
        return Write(authorityId, new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Shared,
            System = system,
            FileName = fileName,
            WriterId = authorityId,
            AuthorityId = authorityId,
            ExpectedRevision = expectedRevision,
            SchemaVersion = schemaVersion,
            PayloadJson = AuraSharedJson.Serialize(value),
            CreateBackup = true
        });
    }

    public static AuraSharedConfigSnapshot<T> ReadOwner<T>(
        string ownerModId,
        string system,
        string fileName,
        T fallback)
    {
        return Read(ownerModId, new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Owner,
            System = system,
            OwnerModId = ownerModId,
            FileName = fileName
        }, fallback);
    }

    public static AuraSharedConfigWriteResult WriteOwner<T>(
        string ownerModId,
        string system,
        string fileName,
        T value,
        long expectedRevision = -1,
        int schemaVersion = 1)
    {
        return Write(ownerModId, new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Owner,
            System = system,
            OwnerModId = ownerModId,
            FileName = fileName,
            WriterId = ownerModId,
            AuthorityId = ownerModId,
            ExpectedRevision = expectedRevision,
            SchemaVersion = schemaVersion,
            PayloadJson = AuraSharedJson.Serialize(value),
            CreateBackup = true
        });
    }

    public static AuraSharedConfigSnapshot<T> ReadRuntime<T>(
        string callerId,
        string system,
        string fileName,
        T fallback)
    {
        return Read(callerId, new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Runtime,
            System = system,
            FileName = fileName
        }, fallback);
    }

    public static AuraSharedConfigWriteResult WriteRuntime<T>(
        string authorityId,
        string system,
        string fileName,
        T value,
        long expectedRevision = -1,
        int schemaVersion = 1)
    {
        return Write(authorityId, new AuraSharedStorageRequest
        {
            Scope = AuraSharedStorageScopes.Runtime,
            System = system,
            FileName = fileName,
            WriterId = authorityId,
            AuthorityId = authorityId,
            ExpectedRevision = expectedRevision,
            SchemaVersion = schemaVersion,
            PayloadJson = AuraSharedJson.Serialize(value),
            CreateBackup = false
        });
    }

    public static string ConfigPath(string ownerModId, string system, string fileName)
    {
        var safeName = Path.GetFileName(AuraSharedPaths.NormalizeRelativePath(fileName));
        return Path.Combine(
            AuraSharedPaths.OwnerSystemConfigDirectory(ownerModId, system),
            string.IsNullOrWhiteSpace(safeName) ? "config.json" : safeName);
    }

    private static AuraSharedConfigSnapshot<T> Read<T>(
        string callerId,
        AuraSharedStorageRequest request,
        T fallback)
    {
        var response = AuraSharedStorage.Read(callerId, request);
        if (!response.Success || !response.Found || string.IsNullOrWhiteSpace(response.PayloadJson))
        {
            return new AuraSharedConfigSnapshot<T>
            {
                Found = false,
                Value = fallback
            };
        }

        try
        {
            return new AuraSharedConfigSnapshot<T>
            {
                Found = true,
                Revision = response.Revision,
                SchemaVersion = response.SchemaVersion,
                AuthorityId = response.AuthorityId,
                Value = AuraSharedJson.Deserialize<T>(response.PayloadJson) ?? fallback
            };
        }
        catch
        {
            return new AuraSharedConfigSnapshot<T>
            {
                Found = false,
                Revision = response.Revision,
                SchemaVersion = response.SchemaVersion,
                AuthorityId = response.AuthorityId,
                Value = fallback
            };
        }
    }

    private static AuraSharedConfigWriteResult Write(string callerId, AuraSharedStorageRequest request)
    {
        var response = AuraSharedStorage.Write(callerId, request);
        return new AuraSharedConfigWriteResult
        {
            Success = response.Success,
            Conflict = response.Conflict,
            Revision = response.Revision,
            Message = response.Message
        };
    }
}
