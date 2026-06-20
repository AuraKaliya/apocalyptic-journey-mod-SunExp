using System;
using SanGuoShaExp.Dll.Infrastructure;

namespace SanGuoShaExp.Dll.Scripting;

public static class SanGuoShaEnchTagScripts
{
    public static void Use(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "linkage":
                    SanGuoShaCardScripts.ResolveLinkage(self);
                    break;
                case "snatch":
                    SanGuoShaCardScripts.ResolveSnatch(self);
                    break;
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("SanGuoSha enchant tag failed: " + id, ex);
        }
    }
}
