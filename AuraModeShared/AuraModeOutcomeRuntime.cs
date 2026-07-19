using System;
using System.Globalization;
using Newtonsoft.Json;

namespace AuraMode.Shared;

public static class AuraModeOutcomeStates
{
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Abandoned = "Abandoned";
}

[Serializable]
public sealed class AuraModeOutcomeSnapshot
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("ownerModId")]
    public string OwnerModId { get; set; } = "";

    [JsonProperty("modeId")]
    public string ModeId { get; set; } = "";

    [JsonProperty("runId")]
    public string RunId { get; set; } = "";

    [JsonProperty("outcomeId")]
    public string OutcomeId { get; set; } = "";

    [JsonProperty("status")]
    public string Status { get; set; } = "";

    [JsonProperty("source")]
    public string Source { get; set; } = "";

    [JsonProperty("sequence")]
    public long Sequence { get; set; }

    [JsonProperty("updatedUtc")]
    public string UpdatedUtc { get; set; } = "";

    [JsonIgnore]
    public bool IsCompleted => string.Equals(Status, AuraModeOutcomeStates.Completed, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Process-local, bounded handoff from a mode owner to generic settlement
/// consumers. Networked modes publish only after their own authoritative
/// resolution has reached each peer. Consumers match both mode and run IDs so
/// a previous adventure cannot classify a later native GameExitUI.
/// </summary>
public static class AuraModeOutcomeRuntime
{
    private static readonly object Gate = new();
    private static AuraModeOutcomeSnapshot? latest;
    private static long sequence;

    public static bool Publish(AuraModeOutcomeSnapshot? outcome)
    {
        if (!TryNormalize(outcome, out var normalized))
        {
            return false;
        }

        lock (Gate)
        {
            normalized.Sequence = ++sequence;
            normalized.UpdatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            latest = normalized;
        }

        return true;
    }

    public static bool TryReadRecent(
        string modeId,
        string runId,
        TimeSpan maximumAge,
        out AuraModeOutcomeSnapshot outcome)
    {
        outcome = new AuraModeOutcomeSnapshot();
        var expectedMode = Clean(modeId);
        var expectedRun = Clean(runId);
        if (expectedMode.Length == 0 || expectedRun.Length == 0 || maximumAge <= TimeSpan.Zero)
        {
            return false;
        }

        AuraModeOutcomeSnapshot? snapshot;
        lock (Gate)
        {
            snapshot = latest == null ? null : Clone(latest);
        }

        if (snapshot == null
            || !string.Equals(snapshot.ModeId, expectedMode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.RunId, expectedRun, StringComparison.Ordinal))
        {
            return false;
        }

        if (!DateTime.TryParse(
                snapshot.UpdatedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var updatedUtc))
        {
            return false;
        }

        var age = DateTime.UtcNow - updatedUtc.ToUniversalTime();
        if (age < TimeSpan.Zero || age > maximumAge)
        {
            return false;
        }

        outcome = snapshot;
        return true;
    }

    public static bool Clear(string ownerModId, string modeId, string runId)
    {
        var expectedOwner = Clean(ownerModId);
        var expectedMode = Clean(modeId);
        var expectedRun = Clean(runId);
        lock (Gate)
        {
            if (latest == null
                || !string.Equals(latest.OwnerModId, expectedOwner, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(latest.ModeId, expectedMode, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(latest.RunId, expectedRun, StringComparison.Ordinal))
            {
                return false;
            }

            latest = null;
            return true;
        }
    }

    private static bool TryNormalize(AuraModeOutcomeSnapshot? source, out AuraModeOutcomeSnapshot normalized)
    {
        normalized = source == null ? new AuraModeOutcomeSnapshot() : Clone(source);
        normalized.SchemaVersion = 1;
        normalized.OwnerModId = Clean(normalized.OwnerModId);
        normalized.ModeId = Clean(normalized.ModeId);
        normalized.RunId = Clean(normalized.RunId);
        normalized.OutcomeId = Clean(normalized.OutcomeId);
        normalized.Status = NormalizeStatus(normalized.Status);
        normalized.Source = Clean(normalized.Source);
        return normalized.OwnerModId.Length > 0
               && normalized.ModeId.Length > 0
               && normalized.RunId.Length > 0
               && normalized.OutcomeId.Length > 0
               && normalized.Status.Length > 0;
    }

    private static string NormalizeStatus(string? value)
    {
        var status = Clean(value);
        if (string.Equals(status, AuraModeOutcomeStates.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return AuraModeOutcomeStates.Completed;
        }

        if (string.Equals(status, AuraModeOutcomeStates.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return AuraModeOutcomeStates.Failed;
        }

        return string.Equals(status, AuraModeOutcomeStates.Abandoned, StringComparison.OrdinalIgnoreCase)
            ? AuraModeOutcomeStates.Abandoned
            : "";
    }

    private static AuraModeOutcomeSnapshot Clone(AuraModeOutcomeSnapshot source)
    {
        return new AuraModeOutcomeSnapshot
        {
            SchemaVersion = source.SchemaVersion,
            OwnerModId = source.OwnerModId,
            ModeId = source.ModeId,
            RunId = source.RunId,
            OutcomeId = source.OutcomeId,
            Status = source.Status,
            Source = source.Source,
            Sequence = source.Sequence,
            UpdatedUtc = source.UpdatedUtc
        };
    }

    private static string Clean(string? value)
    {
        return (value ?? "").Trim();
    }
}
