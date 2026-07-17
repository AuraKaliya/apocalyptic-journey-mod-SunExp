using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using Newtonsoft.Json;

namespace AuraToolsExp.Dll.Config;

public sealed class AuraToolsLoggingSettings
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 4;

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonProperty("performanceDiagnostics")]
    public bool PerformanceDiagnostics { get; set; }

    [JsonProperty("fileNamePattern")]
    public string FileNamePattern { get; set; } = "AuraTools-{date}.log";

    [JsonProperty("minimumLevel")]
    public string MinimumLevel { get; set; } = "Info";

    [JsonProperty("mirrorUnityLog")]
    public bool MirrorUnityLog { get; set; }

    [JsonProperty("mirrorCommandsLog")]
    public bool MirrorCommandsLog { get; set; }

    [JsonProperty("enabledSources")]
    public List<string> EnabledSources { get; set; } = new() { "AuraTools" };

    [JsonProperty("unityLogTypes")]
    public List<string> UnityLogTypes { get; set; } = new() { "Warning", "Error", "Exception", "Assert" };

    [JsonProperty("includedCommandTags")]
    public List<string> IncludedCommandTags { get; set; } = new();

    [JsonProperty("excludedCommandTags")]
    public List<string> ExcludedCommandTags { get; set; } = new();

    [JsonProperty("stackTraceMode")]
    public string StackTraceMode { get; set; } = "ErrorsOnly";

    [JsonProperty("maxQueueLength")]
    public int MaxQueueLength { get; set; } = 1024;

    [JsonProperty("flushIntervalMs")]
    public int FlushIntervalMs { get; set; } = 1000;

    [JsonProperty("maxRetainedLogFiles")]
    public int MaxRetainedLogFiles { get; set; } = 10;

    public void Normalize()
    {
        var loadedSchemaVersion = SchemaVersion;
        var shouldMigrateHighVolumeDefaults = loadedSchemaVersion < 2 && LooksLikeLegacyHighVolumeDefaults();
        var shouldMigrateWarningOnlyDefaults = loadedSchemaVersion < 3 && LooksLikeWarningOnlyDefaults();
        SchemaVersion = Math.Max(4, SchemaVersion);
        if (shouldMigrateHighVolumeDefaults || shouldMigrateWarningOnlyDefaults)
        {
            MinimumLevel = LoggingLevelNames.Info;
            MirrorUnityLog = false;
            MirrorCommandsLog = false;
            EnabledSources = new List<string> { "AuraTools" };
            UnityLogTypes = new List<string> { "Warning", "Error", "Exception", "Assert" };
            StackTraceMode = LoggingStackTraceModes.ErrorsOnly;
            MaxQueueLength = Math.Min(MaxQueueLength <= 0 ? 1024 : MaxQueueLength, 1024);
        }

        FileNamePattern = string.IsNullOrWhiteSpace(FileNamePattern) ? "AuraTools-{date}.log" : FileNamePattern.Trim();
        MinimumLevel = LoggingLevelNames.Normalize(MinimumLevel);
        EnabledSources = NormalizeList(EnabledSources, new[] { "AuraTools" });
        UnityLogTypes = NormalizeList(UnityLogTypes, new[] { "Warning", "Error", "Exception", "Assert" });
        IncludedCommandTags = NormalizeList(IncludedCommandTags, Array.Empty<string>());
        ExcludedCommandTags = NormalizeList(ExcludedCommandTags, Array.Empty<string>());
        StackTraceMode = LoggingStackTraceModes.Normalize(StackTraceMode);
        MaxQueueLength = Math.Max(128, Math.Min(65536, MaxQueueLength));
        FlushIntervalMs = Math.Max(100, Math.Min(10000, FlushIntervalMs));
        MaxRetainedLogFiles = Math.Max(1, Math.Min(50, MaxRetainedLogFiles));
    }

    private bool LooksLikeLegacyHighVolumeDefaults()
    {
        return MirrorUnityLog
               || MirrorCommandsLog
               || ContainsValue(EnabledSources, "Unity")
               || ContainsValue(EnabledSources, "Command")
               || ContainsValue(UnityLogTypes, "Log")
               || string.Equals(StackTraceMode, LoggingStackTraceModes.All, StringComparison.OrdinalIgnoreCase)
               || MaxQueueLength >= 4096;
    }

    private bool LooksLikeWarningOnlyDefaults()
    {
        return string.Equals(MinimumLevel, LoggingLevelNames.Warning, StringComparison.OrdinalIgnoreCase)
               && !MirrorUnityLog
               && !MirrorCommandsLog
               && ContainsOnlyValue(EnabledSources, "AuraTools")
               && !ContainsValue(UnityLogTypes, "Log")
               && string.Equals(StackTraceMode, LoggingStackTraceModes.ErrorsOnly, StringComparison.OrdinalIgnoreCase)
               && MaxQueueLength <= 1024;
    }

    private static bool ContainsValue(IEnumerable<string>? values, string expected)
    {
        return values != null && values.Any(value => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsOnlyValue(IEnumerable<string>? values, string expected)
    {
        if (values == null)
        {
            return false;
        }

        var normalized = values
            .Select(value => value?.Trim() ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count == 1 && string.Equals(normalized[0], expected, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeList(IEnumerable<string>? values, IEnumerable<string> fallback)
    {
        var list = new List<string>();
        foreach (var value in values ?? fallback)
        {
            var text = value?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(text)
                && !list.Any(existing => string.Equals(existing, text, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(text);
            }
        }

        if (list.Count == 0)
        {
            foreach (var value in fallback)
            {
                var text = value?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    list.Add(text);
                }
            }
        }

        return list;
    }
}

public static class LoggingLevelNames
{
    public const string Debug = "Debug";
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";

    public static string Normalize(string? value)
    {
        var text = value?.Trim() ?? "";
        if (string.Equals(text, Debug, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Log", StringComparison.OrdinalIgnoreCase))
        {
            return Debug;
        }

        if (string.Equals(text, Warning, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Warn", StringComparison.OrdinalIgnoreCase))
        {
            return Warning;
        }

        if (string.Equals(text, Error, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Exception", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Assert", StringComparison.OrdinalIgnoreCase))
        {
            return Error;
        }

        return Info;
    }
}

public static class LoggingStackTraceModes
{
    public const string Off = "Off";
    public const string ErrorsOnly = "ErrorsOnly";
    public const string All = "All";

    public static string Normalize(string? value)
    {
        var text = value?.Trim() ?? "";
        if (string.Equals(text, Off, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
        {
            return Off;
        }

        if (string.Equals(text, All, StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "Always", StringComparison.OrdinalIgnoreCase))
        {
            return All;
        }

        return ErrorsOnly;
    }
}
