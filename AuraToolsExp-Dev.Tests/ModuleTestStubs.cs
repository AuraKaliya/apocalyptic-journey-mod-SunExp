namespace UnityEngine
{
    public class Transform
    {
    }
}

namespace Witch.Mod
{
    public class ModConfig
    {
    }
}

namespace AuraShared.Core
{
    using System.Collections.Generic;

    public sealed class AuraSharedConfigSnapshot<T>
    {
        public bool Found { get; set; }
        public long Revision { get; set; }
        public int SchemaVersion { get; set; }
        public T Value { get; set; } = default!;
    }

    public sealed class AuraSharedConfigWriteResult
    {
        public bool Success { get; set; }
        public long Revision { get; set; }
        public string Message { get; set; } = "";
    }

    public static class AuraSharedConfigStore
    {
        private static readonly Dictionary<string, (object Value, long Revision, int SchemaVersion)>
            Values = new();

        public static AuraSharedConfigSnapshot<T> ReadOwner<T>(
            string ownerModId,
            string system,
            string fileName,
            T fallback)
        {
            var key = ownerModId + "|" + system + "|" + fileName;
            if (!Values.TryGetValue(key, out var stored)
                || stored.Value is not T value)
            {
                return new AuraSharedConfigSnapshot<T>
                {
                    Found = false,
                    Value = fallback
                };
            }
            return new AuraSharedConfigSnapshot<T>
            {
                Found = true,
                Revision = stored.Revision,
                SchemaVersion = stored.SchemaVersion,
                Value = value
            };
        }

        public static AuraSharedConfigWriteResult WriteOwner<T>(
            string ownerModId,
            string system,
            string fileName,
            T value,
            long expectedRevision = -1,
            int schemaVersion = 1)
        {
            var key = ownerModId + "|" + system + "|" + fileName;
            var revision = Values.TryGetValue(key, out var stored)
                ? stored.Revision + 1
                : 1;
            Values[key] = (value!, revision, schemaVersion);
            return new AuraSharedConfigWriteResult
            {
                Success = true,
                Revision = revision
            };
        }

        public static void ResetForTests() => Values.Clear();

        public static void SetForTests<T>(
            string ownerModId,
            string system,
            string fileName,
            T value,
            long revision,
            int schemaVersion)
        {
            var key = ownerModId + "|" + system + "|" + fileName;
            Values[key] = (value!, revision, schemaVersion);
        }
    }
}

namespace AuraToolsExp.Dll.Infrastructure
{
    public static class AuraToolsLog
    {
        public static void Warn(string message)
        {
        }

        public static void Error(string message, System.Exception? exception = null)
        {
        }
    }
}
