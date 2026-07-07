using System;
using Network.Command;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Network;

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
