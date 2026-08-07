using AuraSkin.Shared.Services;

namespace Witch.Mod
{
    public sealed class ModConfig
    {
    }
}

namespace AuraShared.Core
{
    public static class AuraSharedSystems
    {
        public const string Skin = "Skin";
    }

    public static class AuraSharedRuntime
    {
        public static void Initialize(Witch.Mod.ModConfig? config, string ownerModId)
        {
        }
    }

    public static class AuraSharedPaths
    {
        public static string SkinDirectory { get; set; } = "";

        public static string RegistriesRootDirectory { get; set; } = "";

        public static string ConfigDirectory { get; set; } = "";

        public static string SharedSystemConfigDirectory(string systemId) => ConfigDirectory;

        public static bool IsInsideDirectory(string path, string directory)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
                   || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                   || fullPath.StartsWith(fullDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class AuraSharedResourceProtocol
    {
        public static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase);

        public static string ResolvePath(string authorityId, string canonicalRelativePath)
        {
            return Paths.TryGetValue(canonicalRelativePath, out var path) ? path : "";
        }
    }

    public sealed class TestInstalledResource
    {
        public string LogicalId { get; set; } = "";
        public string ContentHash { get; set; } = "";
        public List<TestInstalledSource> Sources { get; set; } = new();
    }

    public sealed class TestInstalledSource
    {
        public string OwnerModId { get; set; } = "";
        public string PackageId { get; set; } = "";
        public long PackageVersion { get; set; }
    }

    public static class AuraSharedPackageEngine
    {
        public static IReadOnlyList<TestInstalledResource> GetResources(string authorityId, string systemId)
        {
            return Array.Empty<TestInstalledResource>();
        }
    }

    public sealed class AuraSharedConfigSnapshot<T>
    {
        public T Value { get; set; } = default!;
        public long Revision { get; set; }
    }

    public sealed class AuraSharedConfigWriteResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public long Revision { get; set; }
    }

    public static class AuraSharedConfigStore
    {
        private static object? storedValue;
        private static long revision;

        public static AuraSharedConfigSnapshot<T> ReadShared<T>(
            string authorityId,
            string systemId,
            string fileName,
            T fallback)
        {
            return new AuraSharedConfigSnapshot<T>
            {
                Value = storedValue is T value ? value : fallback,
                Revision = revision
            };
        }

        public static AuraSharedConfigWriteResult WriteShared<T>(
            string authorityId,
            string systemId,
            string fileName,
            T value,
            long expectedRevision,
            int schemaVersion)
        {
            storedValue = value;
            revision++;
            return new AuraSharedConfigWriteResult { Success = true, Revision = revision };
        }

        public static void Reset()
        {
            storedValue = null;
            revision = 0;
        }
    }
}

namespace AuraSkin.Shared.GameApi
{
    public static class CareerConfigApi
    {
        public static string NormalizeId(string? careerId)
        {
            return AuraShared.Core.AuraSharedIdentity.NormalizeCareerId(careerId);
        }
    }
}

namespace AuraSkin.Shared.Infrastructure
{
    public static class SkinLog
    {
        public static readonly List<string> Warnings = new();

        public static void Info(string message)
        {
        }

        public static void Warn(string message)
        {
            Warnings.Add(message);
        }

        public static void Error(string message, Exception? exception = null)
        {
            Warnings.Add(message);
        }
    }
}

namespace AuraSkin.Shared.Services
{
    public static class SkinPackageInstaller
    {
        public static List<RegisteredSkinResource> ActiveResources { get; } = new();

        public static IReadOnlyList<RegisteredSkinResource> GetActiveResources()
        {
            return ActiveResources.ToArray();
        }

        public sealed class RegisteredSkinResource
        {
            public string OwnerModId { get; set; } = "";
            public string PackageId { get; set; } = "";
            public int PackageVersion { get; set; }
            public int Priority { get; set; }
            public string TargetCareerId { get; set; } = "";
            public string SkinId { get; set; } = "";
            public string CanonicalRelativePath { get; set; } = "";
        }
    }
}
