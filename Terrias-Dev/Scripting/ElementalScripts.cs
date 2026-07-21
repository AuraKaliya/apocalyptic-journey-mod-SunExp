using System;
using SunExp.Dll.GameApi;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;

namespace SunExp.Dll.Scripting;

public static class ElementalScripts
{
    public static void Hit(ScriptExecutor self, string element, string damage)
    {
        try
        {
            if (!TryElement(element, out var parsed))
            {
                return;
            }

            var target = TargetApi.PrimaryTarget(self);
            ElementalReactionService.Hit(
                self,
                target,
                parsed,
                Math.Max(0, DictionaryUtil.ParseInt(damage)),
                "ElementalScripts.Hit");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Elemental hit failed: element=" + element + ", damage=" + damage, ex);
        }
    }

    public static void Apply(ScriptExecutor self, string element)
    {
        try
        {
            if (!TryElement(element, out var parsed))
            {
                return;
            }

            ElementalReactionService.Apply(
                self,
                TargetApi.PrimaryTargetIncludingSelf(self),
                parsed,
                "ElementalScripts.Apply");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Elemental application failed: element=" + element, ex);
        }
    }

    public static void ApplySelf(ScriptExecutor self, string element)
    {
        try
        {
            if (!TryElement(element, out var parsed))
            {
                return;
            }

            ElementalReactionService.Apply(self, self?.Self, parsed, "ElementalScripts.ApplySelf");
        }
        catch (Exception ex)
        {
            SunExpLog.Error("Self elemental application failed: element=" + element, ex);
        }
    }

    public static string Magic(ScriptExecutor self)
    {
        return ElementalMagicService.Read(self?.Self).ToString();
    }

    private static bool TryElement(string value, out ElementalType element)
    {
        if (ElementalTypeParser.TryParse(value, out element))
        {
            return true;
        }

        SunExpLog.Warn("Unknown elemental type: " + (value ?? "<null>"));
        return false;
    }
}
