using System;
using Network.Command;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Network;

[Serializable]
public sealed class RpcRuntimeHandAttachment : RpcCommandBase
{
    public RuntimeHandAttachmentSpec Spec { get; set; } = new();

    public RpcRuntimeHandAttachment()
    {
    }

    public RpcRuntimeHandAttachment(RuntimeHandAttachmentSpec spec)
    {
        Spec = spec ?? new RuntimeHandAttachmentSpec();
    }

    public override void RpcExecute()
    {
        RuntimeCardAttachmentService.ApplyNetworkHandAttachment(Spec, "RpcRuntimeHandAttachment");
    }
}
