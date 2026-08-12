using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AuraCombatAi.Shared;
using AuraShared.Core;

namespace Terrias.Dll.Mechanics;

[Serializable]
public sealed class ProjectionPrivateStateEnvelope
{
    public int ProtocolVersion { get; set; } = CompanionAuthorityService.ProjectionProtocolVersion;
    public int BattleEpoch { get; set; }
    public string Token { get; set; } = "";
    public ProjectionOwnerCombatSnapshot OwnerCombat { get; set; } = new();
    public CombatActorCardStateSnapshot CardState { get; set; } = new();
}

[Serializable]
public sealed class ProjectionOwnerCombatSnapshot
{
    public int MaxHp { get; set; }
    public int CurrentHp { get; set; }
    public int Defend { get; set; }
    public int Attack { get; set; }
    public Dictionary<string, float> DynamicVariables { get; set; } = new(StringComparer.Ordinal);
    public List<ProjectionBuffSnapshot> Buffs { get; set; } = new();

    public static ProjectionOwnerCombatSnapshot Capture(IStatusManager? owner)
    {
        var result = new ProjectionOwnerCombatSnapshot
        {
            MaxHp = Math.Max(1, owner?.MaxHp ?? 1),
            CurrentHp = Math.Max(1, owner?.CurHp ?? 1),
            Defend = Math.Max(0, owner?.Defend ?? 0),
            Attack = Math.Max(0, (owner?.fatherObject as OtherObj)?.Attack ?? 0)
        };
        foreach (var pair in owner?.dynamicVariables
                     ?? new Dictionary<string, float>())
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && IsFinite(pair.Value))
            {
                result.DynamicVariables[pair.Key] = pair.Value;
            }
        }
        foreach (var buff in owner?.GetBuffs() ?? Array.Empty<IBuffItem>())
        {
            var id = buff?.buffConfig?.BuffId ?? "";
            var level = buff?.buffConfig?.Level ?? 0;
            if (!string.IsNullOrWhiteSpace(id) && level > 0)
            {
                result.Buffs.Add(new ProjectionBuffSnapshot { BuffId = id, Level = level });
            }
        }
        return result;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

[Serializable]
public sealed class ProjectionBuffSnapshot
{
    public string BuffId { get; set; } = "";
    public int Level { get; set; }
}

public static class ProjectionCardStateTransport
{
    public const int ChunkBytes = 24 * 1024;
    public const int MaxCompressedBytes = 512 * 1024;
    public const int MaxUncompressedBytes = 2 * 1024 * 1024;
    public const int MaxChunks = 32;
    private const int MaxTransferredCombatValue = 1000000;

    private static readonly HashSet<string> ScriptFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "InitScript", "DrawScript", "UseScript", "DropScript", "PreUseScript"
    };

    public static bool TryEncode(
        ProjectionPrivateStateEnvelope envelope,
        out byte[] compressed,
        out string sha256,
        out int uncompressedBytes,
        out string reason)
    {
        compressed = Array.Empty<byte>();
        sha256 = "";
        uncompressedBytes = 0;
        if (!Validate(envelope, out reason))
        {
            return false;
        }

        try
        {
            var raw = Encoding.UTF8.GetBytes(AuraSharedJson.SerializeCompact(envelope));
            uncompressedBytes = raw.Length;
            if (raw.Length > MaxUncompressedBytes)
            {
                reason = "projection card state exceeds uncompressed transfer limit";
                return false;
            }

            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
            {
                gzip.Write(raw, 0, raw.Length);
            }
            compressed = output.ToArray();
            if (compressed.Length > MaxCompressedBytes
                || ChunkCount(compressed.Length) > MaxChunks)
            {
                reason = "projection card state exceeds compressed transfer limit";
                compressed = Array.Empty<byte>();
                return false;
            }
            sha256 = Hash(compressed);
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = "projection card state encoding failed: " + ex.Message;
            return false;
        }
    }

    public static bool TryDecode(
        byte[] compressed,
        string expectedSha256,
        int expectedUncompressedBytes,
        out ProjectionPrivateStateEnvelope? envelope,
        out string reason)
    {
        envelope = null;
        if (compressed == null
            || compressed.Length == 0
            || compressed.Length > MaxCompressedBytes
            || !string.Equals(Hash(compressed), expectedSha256 ?? "", StringComparison.OrdinalIgnoreCase))
        {
            reason = "projection card state checksum mismatch";
            return false;
        }

        try
        {
            using var input = new MemoryStream(compressed, false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = gzip.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
                output.Write(buffer, 0, read);
                if (output.Length > MaxUncompressedBytes)
                {
                    reason = "projection card state decompressed beyond limit";
                    return false;
                }
            }

            if (expectedUncompressedBytes > 0 && output.Length != expectedUncompressedBytes)
            {
                reason = "projection card state length mismatch";
                return false;
            }
            envelope = AuraSharedJson.Deserialize<ProjectionPrivateStateEnvelope>(
                Encoding.UTF8.GetString(output.ToArray()));
            return Validate(envelope, out reason);
        }
        catch (Exception ex)
        {
            reason = "projection card state decoding failed: " + ex.Message;
            return false;
        }
    }

    public static IEnumerable<ArraySegment<byte>> Chunks(byte[] payload)
    {
        for (var offset = 0; offset < payload.Length; offset += ChunkBytes)
        {
            yield return new ArraySegment<byte>(
                payload,
                offset,
                Math.Min(ChunkBytes, payload.Length - offset));
        }
    }

    public static int ChunkCount(int bytes)
    {
        return Math.Max(1, (Math.Max(0, bytes) + ChunkBytes - 1) / ChunkBytes);
    }

    public static bool Validate(ProjectionPrivateStateEnvelope? envelope, out string reason)
    {
        if (envelope == null
            || envelope.ProtocolVersion != CompanionAuthorityService.ProjectionProtocolVersion
            || envelope.BattleEpoch != CompanionAuthorityService.BattleEpoch
            || string.IsNullOrWhiteSpace(envelope.Token))
        {
            reason = "projection private state identity is invalid";
            return false;
        }
        if (envelope.CardState == null)
        {
            reason = "projection card state is unavailable";
            return false;
        }
        envelope.OwnerCombat ??= new ProjectionOwnerCombatSnapshot();
        envelope.OwnerCombat.DynamicVariables ??= new Dictionary<string, float>(StringComparer.Ordinal);
        envelope.OwnerCombat.Buffs ??= new List<ProjectionBuffSnapshot>();
        envelope.CardState.Cards ??= new List<CombatCardInstanceSnapshot>();
        if (!envelope.CardState.Validate(out reason))
        {
            return false;
        }
        if (envelope.CardState.Cards.Count > 512
            || envelope.OwnerCombat.DynamicVariables.Count > 256
            || envelope.OwnerCombat.Buffs.Count > 256)
        {
            reason = "projection private state exceeds collection limits";
            return false;
        }
        if (envelope.OwnerCombat.MaxHp <= 0
            || envelope.OwnerCombat.MaxHp > MaxTransferredCombatValue
            || envelope.OwnerCombat.CurrentHp <= 0
            || envelope.OwnerCombat.CurrentHp > envelope.OwnerCombat.MaxHp
            || envelope.OwnerCombat.Attack < 0
            || envelope.OwnerCombat.Attack > MaxTransferredCombatValue
            || envelope.OwnerCombat.Defend < 0
            || envelope.OwnerCombat.Defend > MaxTransferredCombatValue
            || envelope.OwnerCombat.DynamicVariables.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key)
                || pair.Key.Length > 256
                || float.IsNaN(pair.Value)
                || float.IsInfinity(pair.Value)
                || Math.Abs(pair.Value) > MaxTransferredCombatValue)
            || envelope.OwnerCombat.Buffs.Any(buff =>
                buff == null
                || string.IsNullOrWhiteSpace(buff.BuffId)
                || buff.BuffId.Length > 256
                || buff.Level <= 0
                || buff.Level > MaxTransferredCombatValue))
        {
            reason = "projection owner combat state is invalid";
            return false;
        }

        foreach (var card in envelope.CardState.Cards)
        {
            card.RuntimeVariables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            card.RuntimeData ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            card.AttachmentStates ??= new List<CombatCardAttachmentSnapshot>();
            card.Tags ??= new List<string>();
            if (!ValidMap(card.RuntimeVariables, 256)
                || !ValidMap(card.RuntimeData, 128)
                || card.RuntimeData.Keys.Any(ScriptFields.Contains)
                || card.AttachmentStates.Count > 16
                || card.Tags.Count > 64)
            {
                reason = "projection card instance contains unsafe runtime state";
                return false;
            }
            foreach (var attachment in card.AttachmentStates)
            {
                if (attachment == null)
                {
                    reason = "projection attachment state is unavailable";
                    return false;
                }
                attachment.RuntimeData ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                attachment.Variables ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!ValidMap(attachment.RuntimeData, 128)
                    || !ValidMap(attachment.Variables, 256)
                    || attachment.RuntimeData.Keys.Any(ScriptFields.Contains))
                {
                    reason = "projection attachment contains unsafe runtime state";
                    return false;
                }
            }
        }
        reason = "";
        return true;
    }

    private static bool ValidMap(IDictionary<string, string>? values, int maximum)
    {
        return values != null
               && values.Count <= maximum
               && values.All(pair => !string.IsNullOrWhiteSpace(pair.Key)
                                     && pair.Key.Length <= 256
                                     && (pair.Value?.Length ?? 0) <= 8192);
    }

    private static string Hash(byte[] value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(value).Select(item => item.ToString("x2")));
    }
}
