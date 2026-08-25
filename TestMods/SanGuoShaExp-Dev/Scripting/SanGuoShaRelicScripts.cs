using System;
using SanGuoShaExp.Dll.GameApi;
using SanGuoShaExp.Dll.Hooks;
using SanGuoShaExp.Dll.Infrastructure;

namespace SanGuoShaExp.Dll.Scripting;

public static class SanGuoShaRelicScripts
{
    public static void Fight(ScriptExecutor self, string id)
    {
        try
        {
            switch (id)
            {
                case "bagua_array":
                    RegisterBaguaArray(self);
                    break;
                case "zhuge_crossbow":
                    RegisterZhugeCrossbow(self);
                    break;
                case "renwang_shield":
                    RegisterRenwangShield(self);
                    break;
                case "baiyin_shizi":
                    RegisterSilverLion(self);
                    break;
                case "qinglong_yanyue_dao":
                case "fangtian_huaji":
                case "zhuque_yushan":
                    // These modify Sha-family cards directly in SanGuoShaCardScripts.
                    break;
            }
        }
        catch (Exception ex)
        {
            SanGuoShaExpLog.Error("SanGuoSha relic fight failed: " + id, ex);
        }
    }

    private static void RegisterBaguaArray(ScriptExecutor self)
    {
        self.AddEvent("StartRound", new Action(() =>
        {
            if (SanGuoShaCombatRuntime.IsCombatActive)
            {
                ExecutorApi.SetVar(self, "SanGuoShaBaguaUsed", "0");
            }
        }));
        self.AddEvent<HurtData>("Hurt", new Action<HurtData>(_ =>
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive)
            {
                return;
            }

            if (ExecutorApi.GetVar(self, "SanGuoShaBaguaUsed", "0") == "1" || !Check(self, 50))
            {
                return;
            }

            ExecutorApi.SetVar(self, "SanGuoShaBaguaUsed", "1");
            self.SetStatus("Self");
            self.ChangeDefence("18");
            self.DrawCount("1");
        }));
    }

    private static void RegisterZhugeCrossbow(ScriptExecutor self)
    {
        self.AddEvent("StartRound", new Action(() =>
        {
            if (SanGuoShaCombatRuntime.IsCombatActive)
            {
                ExecutorApi.SetVar(self, "SanGuoShaCrossbowUsed", "0");
            }
        }));
        self.AddEvent<ActionData>("ActionAfter", new Action<ActionData>(data =>
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive)
            {
                return;
            }

            if (!IsShaData(data) || ExecutorApi.GetVar(self, "SanGuoShaCrossbowUsed", "0") == "1")
            {
                return;
            }

            ExecutorApi.SetVar(self, "SanGuoShaCrossbowUsed", "1");
            self.SetStatus("Self");
            self.ChangePower("1");
            if (self.Self?.GetBuff("buff_revelation") != null)
            {
                self.DrawCount("1");
            }
        }));
    }

    private static void RegisterRenwangShield(ScriptExecutor self)
    {
        self.AddEvent("StartRound", new Action(() =>
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive)
            {
                return;
            }

            self.SetStatus("Self");
            self.ChangeDefence("12");
        }));
        self.AddEvent<HurtData>("Hurt", new Action<HurtData>(data =>
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive)
            {
                return;
            }

            if (!IsShaId(data.fromDataId))
            {
                return;
            }

            self.SetStatus("Self");
            self.AddBuff(SanGuoShaExpIds.Resilient, "6");
        }));
    }

    private static void RegisterSilverLion(ScriptExecutor self)
    {
        self.AddEvent("StartRound", new Action(() =>
        {
            if (SanGuoShaCombatRuntime.IsCombatActive)
            {
                ExecutorApi.SetVar(self, "SanGuoShaSilverLionUsed", "0");
            }
        }));
        self.AddEvent<HurtData>("Hurt", new Action<HurtData>(data =>
        {
            if (!SanGuoShaCombatRuntime.IsCombatActive)
            {
                return;
            }

            if (ExecutorApi.GetVar(self, "SanGuoShaSilverLionUsed", "0") == "1"
                || DictionaryUtil.ParseInt(data.val) <= 20)
            {
                return;
            }

            ExecutorApi.SetVar(self, "SanGuoShaSilverLionUsed", "1");
            self.SetStatus("Self");
            self.ChangeHp("8");
            self.AddBuff(SanGuoShaExpIds.Resilient, "4");
        }));
    }

    private static bool IsShaData(ActionData data)
    {
        return IsShaId(data.dataId) || IsShaId(data.data?.data?.GetValueOrDefault("Id", ""));
    }

    private static bool IsShaId(string? id)
    {
        var text = id ?? "";
        if (text.Length == 0)
        {
            return false;
        }

        return text.EndsWith("_sha", StringComparison.Ordinal)
            || text.EndsWith("_huosha", StringComparison.Ordinal)
            || text.EndsWith("_leisha", StringComparison.Ordinal)
            || text == "sha"
            || text == "huosha"
            || text == "leisha";
    }

    private static bool Check(ScriptExecutor self, int threshold)
    {
        try
        {
            return self.CheckDice.Roll().Value > threshold;
        }
        catch
        {
            return false;
        }
    }
}
