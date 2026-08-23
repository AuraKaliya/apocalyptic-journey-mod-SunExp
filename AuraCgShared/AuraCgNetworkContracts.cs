using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AuraShared.Core;
using Network.Command;
using UnityEngine;
using Witch.Core;
using Witch.Mod;

namespace AuraCg.Shared;

[Serializable]
public sealed class SkillCgNetworkEvent
{
    public string ProviderId { get; set; } = "";

    public string OwnerModId { get; set; } = "";

    // Stable local registry id. Media paths and presentation data are resolved locally from this id.
    public string CgId { get; set; } = "";

    public string CardId { get; set; } = "";

    public string TriggerKind { get; set; } = "";

    public string OwnerInstanceId { get; set; } = "";

    public long ActionSequence { get; set; }

    public string EventToken { get; set; } = "";

    public string IssuerPlayerId { get; set; } = "";

    public string SkillCgPlayId { get; set; } = "";
}

internal sealed class SkillCgFightSessionRequest
{
    public SkillCgFightSessionRequest(string ownerModId, string reason, string fightToken = "")
    {
        OwnerModId = ownerModId ?? "";
        Reason = reason ?? "";
        FightToken = fightToken ?? "";
    }

    public string OwnerModId { get; }

    public string Reason { get; }

    public string FightToken { get; }
}

[Serializable]
public sealed class SkillCgPlaybackSnapshot
{
    public string IssuerPlayerId { get; set; } = "";

    public string SkillCgPlayId { get; set; } = "";

    public string OwnerStatusId { get; set; } = "";

    public string CardId { get; set; } = "";

    public long ActionSequence { get; set; }

    public string FightToken { get; set; } = "";

    public List<SkillCgNetworkEvent> Events { get; set; } = new();
}

internal sealed class SkillCgServerPlaybackEnvelope
{
    public SkillCgServerPlaybackEnvelope(SkillCgPlaybackSnapshot playback, AuraCgRpcSender sender)
    {
        Playback = playback ?? new SkillCgPlaybackSnapshot();
        Sender = sender ?? AuraCgRpcSender.Unbound;
    }

    public SkillCgPlaybackSnapshot Playback { get; }

    public AuraCgRpcSender Sender { get; }
}

internal sealed class SkillCgNetworkPlaybackEnvelope
{
    public SkillCgNetworkPlaybackEnvelope(SkillCgPlaybackSnapshot playback, string source)
    {
        Playback = playback ?? new SkillCgPlaybackSnapshot();
        Source = source ?? "";
    }

    public SkillCgPlaybackSnapshot Playback { get; }

    public string Source { get; }
}
