using System;
using System.Linq;
using Terrias.Dll.Contracts;
using Terrias.Dll.GameApi;
using Terrias.Dll.Mechanics;

namespace Terrias.Dll.Application;

public static class RuntimeHandAttachmentApplication
{
    public static void ConfigureBroadcast(Action<RuntimeHandAttachmentSpec> publish) => RuntimeCardAttachmentService.PublishHandAttachment = publish;
    public static bool Resolve(RuntimeHandAttachmentSpec? spec, bool senderOwnsStatus)
    {
        if (spec == null || !senderOwnsStatus || !Guid.TryParseExact(spec.Token, "N", out _)) return false;
        var owner = StatusApi.FindById(spec.OwnerStatusId);
        if (!PolymorphStateStore.IsEffectiveCombatRoleFor(owner, "wuna")) return false;
        var attachment = RuntimeCardAttachmentService.WunaWhiteSunPrayerHandAttachment();
        spec.NativeTags = attachment.NativeTags.ToArray(); spec.SpecialTags = attachment.SpecialTags.ToArray();
        spec.Markers = attachment.Markers.ToArray(); spec.TemporaryWhiteRadiance = attachment.TemporaryWhiteRadiance;
        spec.Source = "Wuna.WhiteSunPrayer";
        return true;
    }
    public static void Apply(RuntimeHandAttachmentSpec spec) => RuntimeCardAttachmentService.ApplyNetworkHandAttachment(spec, "RpcRuntimeHandAttachment");
}
