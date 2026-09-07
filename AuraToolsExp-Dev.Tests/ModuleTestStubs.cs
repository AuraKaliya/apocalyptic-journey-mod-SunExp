namespace UnityEngine
{
    public class Transform
    {
    }
}

namespace Witch.Mod
{
    using System;
    using System.Collections.Generic;
    using Witch.Core;

    public class ModConfig
    {
        private readonly Dictionary<string, List<Action<ModHookContext>>> before = new();
        private readonly Dictionary<string, List<Action<ModHookContext>>> after = new();

        public void AddMethodHookBefore(string target, Action<ModHookContext> action)
        {
            if (!before.TryGetValue(target, out var handlers))
            {
                handlers = new List<Action<ModHookContext>>();
                before[target] = handlers;
            }
            handlers.Add(action);
        }

        public void AddMethodHookAfter(string target, Action<ModHookContext> action)
        {
            if (!after.TryGetValue(target, out var handlers))
            {
                handlers = new List<Action<ModHookContext>>();
                after[target] = handlers;
            }
            handlers.Add(action);
        }

        public int BeforeRegistrationCount(string target) =>
            before.TryGetValue(target, out var handlers) ? handlers.Count : 0;

        public int AfterRegistrationCount(string target) =>
            after.TryGetValue(target, out var handlers) ? handlers.Count : 0;

        public void InvokeBefore(string target)
        {
            InvokeBefore(target, new ModHookContext());
        }

        public void InvokeBefore(string target, ModHookContext context)
        {
            if (!before.TryGetValue(target, out var handlers)) return;
            foreach (var handler in handlers) handler(context);
        }

        public void InvokeAfter(string target)
        {
            InvokeAfter(target, new ModHookContext());
        }

        public void InvokeAfter(string target, ModHookContext context)
        {
            if (!after.TryGetValue(target, out var handlers)) return;
            foreach (var handler in handlers) handler(context);
        }
    }
}

namespace Witch.Core
{
    public sealed class ModHookContext
    {
        public object? Target { get; set; }
        public object[]? Arguments { get; set; }
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
        private static readonly Dictionary<string, (string Json, long Revision, int SchemaVersion)>
            Values = new();
        public static bool FailNextWriteForTests { get; set; }

        public static AuraSharedConfigSnapshot<T> ReadOwner<T>(
            string ownerModId,
            string system,
            string fileName,
            T fallback)
        {
            var key = ownerModId + "|" + system + "|" + fileName;
            if (!Values.TryGetValue(key, out var stored))
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
                Value = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(stored.Json)!
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
            var currentRevision = Values.TryGetValue(key, out var stored) ? stored.Revision : 0;
            if (FailNextWriteForTests || expectedRevision >= 0 && expectedRevision != currentRevision)
            {
                FailNextWriteForTests = false;
                return new AuraSharedConfigWriteResult { Success = false, Revision = currentRevision, Message = "injected failure or revision conflict" };
            }
            var revision = currentRevision + 1;
            Values[key] = (Newtonsoft.Json.JsonConvert.SerializeObject(value), revision, schemaVersion);
            return new AuraSharedConfigWriteResult
            {
                Success = true,
                Revision = revision
            };
        }

        public static void ResetForTests() { Values.Clear(); FailNextWriteForTests = false; }

        public static void SetForTests<T>(
            string ownerModId,
            string system,
            string fileName,
            T value,
            long revision,
            int schemaVersion)
        {
            var key = ownerModId + "|" + system + "|" + fileName;
            Values[key] = (Newtonsoft.Json.JsonConvert.SerializeObject(value), revision, schemaVersion);
        }
    }
}

namespace AuraToolsExp.Dll.Infrastructure
{
    public static class AuraToolsLog
    {
        public static void Info(string message)
        {
        }

        public static void Warn(string message)
        {
        }

        public static void Error(string message, System.Exception? exception = null)
        {
        }
    }
}
