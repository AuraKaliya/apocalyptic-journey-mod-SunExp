using System;

namespace AuraShared.Core;

public static class AuraSharedConfigStore
{
    private static object? value;
    private static long revision;

    public static AuraSharedConfigSnapshot<T> ReadShared<T>(string callerId, string system, string fileName, T fallback)
    {
        return value is T stored
            ? new AuraSharedConfigSnapshot<T>
            {
                Found = true,
                Revision = revision,
                SchemaVersion = 4,
                AuthorityId = callerId,
                Value = stored
            }
            : new AuraSharedConfigSnapshot<T> { Value = fallback };
    }

    public static AuraSharedConfigWriteResult WriteShared<T>(
        string authorityId,
        string system,
        string fileName,
        T next,
        long expectedRevision = -1,
        int schemaVersion = 1)
    {
        if (expectedRevision >= 0 && expectedRevision != revision)
        {
            return new AuraSharedConfigWriteResult { Conflict = true, Revision = revision, Message = "Conflict" };
        }

        value = next;
        revision++;
        return new AuraSharedConfigWriteResult { Success = true, Revision = revision, Message = "Applied" };
    }

    public static void ResetGameDataTestStore()
    {
        value = null;
        revision = 0;
    }
}
