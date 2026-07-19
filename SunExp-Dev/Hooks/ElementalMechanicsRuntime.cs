using SunExp.Dll.Hooks.Ui;
using SunExp.Dll.Infrastructure;
using SunExp.Dll.Mechanics;
using UnityEngine;
using Witch.Mod;

namespace SunExp.Dll.Hooks;

public static class ElementalMechanicsRuntime
{
    private const string RunnerName = "SunExp_ElementalMechanicsRunner";
    private static bool initialized;

    public static void Initialize(ModConfig modConfig)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        ElementalCrystalPresenter.Initialize();
        EnsureRunner();
        SunExpBattleLifecycleRouter.Register("ElementalMechanics", new SunExpBattleLifecycleSubscription
        {
            FightInitializing = _ => BeginBattle(),
            FightEnding = _ => EndBattle("FightEnding"),
            FightEnded = _ => EndBattle("FightEnded")
        });
        SunExpStatusLifecycleRouter.Register("ElementalMechanics", new SunExpStatusLifecycleSubscription
        {
            AfterEnemyInit = context =>
            {
                if (context.Target is Enemy enemy)
                {
                    ElementalMagicService.ObserveEnemy(enemy, "Enemy.Init");
                }
            }
        });
        SunExpLog.Info("Elemental mechanics runtime initialized");
    }

    private static void BeginBattle()
    {
        ElementalMagicService.BeginBattle();
        ElementalCrystalChallengeService.BeginBattle();
        ElementalCrystalPresenter.CloseAll("ElementalMechanicsRuntime.BeginBattle");
    }

    private static void EndBattle(string source)
    {
        ElementalMagicService.EndBattle();
        ElementalCrystalChallengeService.EndBattle(source);
        ElementalCrystalPresenter.CloseAll("ElementalMechanicsRuntime." + source);
    }

    private static void EnsureRunner()
    {
        if (GameObject.Find(RunnerName) != null)
        {
            return;
        }

        var root = new GameObject(RunnerName);
        UnityEngine.Object.DontDestroyOnLoad(root);
        root.AddComponent<ElementalMechanicsRunner>();
    }
}

public sealed class ElementalMechanicsRunner : MonoBehaviour
{
    private void Update()
    {
        ElementalCrystalChallengeService.Tick();
    }
}
