using System;
using AuraShared.Core;
using Network.Command;

namespace AudioArbiter.Shared;

public sealed class RpcAudioEvent : RpcCommandBase
{
    public RpcAudioEvent()
    {
        Event = new SoundPlaybackRequest();
    }

    public RpcAudioEvent(SoundPlaybackRequest request)
    {
        Event = AudioNetworkEventMapper.CreateRemoteCopy(request);
    }

    public SoundPlaybackRequest Event { get; set; }

    public override void RpcExecute()
    {
        Event.IsRemote = true;
        Event.DisableSync = true;
        AudioArbiterRuntime.ReceiveRemote(Event);
    }
}

public interface IAudioArbiterServerBoundRpcCommand
{
    void BindServerSender(AuraRpcSender sender);
}

[Serializable]
public sealed class RpcAudioPresentationRequest : RpcCommandBase, IAudioArbiterServerBoundRpcCommand
{
    private AuraRpcSender serverSender = AuraRpcSender.Unbound;

    public RpcAudioPresentationRequest()
    {
        Event = new SoundPlaybackRequest();
    }

    public RpcAudioPresentationRequest(SoundPlaybackRequest request)
    {
        Event = new RpcAudioEvent(request).Event;
    }

    public SoundPlaybackRequest Event { get; set; }

    public void BindServerSender(AuraRpcSender sender)
    {
        serverSender = sender ?? AuraRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        AudioArbiterRuntime.ApplyServerCardUsePresentation(Event, serverSender);
    }

    public override void RpcExecute()
    {
    }
}

[Serializable]
public sealed class RpcAudioFightSession : RpcCommandBase, IAudioArbiterServerBoundRpcCommand
{
    private AuraRpcSender serverSender = AuraRpcSender.Unbound;

    public RpcAudioFightSession()
    {
    }

    public RpcAudioFightSession(string fightToken)
    {
        FightToken = fightToken ?? "";
    }

    public string FightToken { get; set; } = "";

    public bool Accepted { get; set; }

    public void BindServerSender(AuraRpcSender sender)
    {
        serverSender = sender ?? AuraRpcSender.Unbound;
    }

    public override void CmdExecute()
    {
        Accepted = serverSender.IsAvailable && serverSender.IsLobbyMember && serverSender.IsLobbyHost;
        if (Accepted)
        {
            AudioArbiterRuntime.ApplyFightSession(FightToken, "RpcAudioFightSession.CmdExecute");
        }
    }

    public override void RpcExecute()
    {
        if (Accepted)
        {
            AudioArbiterRuntime.ApplyFightSession(FightToken, "RpcAudioFightSession.RpcExecute");
        }
    }
}
