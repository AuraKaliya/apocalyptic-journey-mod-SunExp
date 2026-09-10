using System;
using AuraShared.Core;
using Terrias.Dll.Application;
using Terrias.Dll.Contracts;
namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcRuntimeHandAttachment : AuraBattleRpcCommand, ITerriasServerBoundRpcCommand
{
    private TerriasRpcSender sender = TerriasRpcSender.Unbound;
    public bool Accepted { get; set; }
    public RuntimeHandAttachmentSpec Spec { get; set; } = new();
    public RpcRuntimeHandAttachment() { }
    public RpcRuntimeHandAttachment(RuntimeHandAttachmentSpec spec) { Spec = spec; }
    public void BindServerSender(TerriasRpcSender value) => sender = value;
    public override void CmdExecute()
    {
        var owns = Spec != null && sender.IsAvailable && sender.IsLobbyMember
            && TerriasStatusOwnershipPolicy.SenderOwnsStatus(sender.PlayerId, Spec.OwnerStatusId, out _);
        Accepted = RuntimeHandAttachmentApplication.Resolve(Spec, owns);
    }
    public override void RpcExecute()
    {
        if (Accepted) RuntimeHandAttachmentApplication.Apply(Spec);
    }
}
