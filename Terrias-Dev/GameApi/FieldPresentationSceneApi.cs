using UnityEngine;
using Witch.UI;
using Witch.UI.Window;

namespace Terrias.Dll.GameApi;

public static class FieldPresentationSceneApi
{
    public static bool TryGet(FieldPresentationScene? cached, out FieldPresentationScene? scene)
    {
        scene = null;
        if (!BattleLifecycleApi.AcceptsCompanionContinuation) return false;
        var fightUi = UIManager.Instance?.GetUI<FightUI>("FightUI");
        var background = GameApp.Instance?.NowBackground;
        var camera = Camera.main;
        if (fightUi == null || !fightUi.gameObject.activeInHierarchy || background == null
            || !background.activeInHierarchy || camera == null) return false;
        if (cached != null && cached.FightUi == fightUi.transform && cached.Background == background
            && cached.Camera == camera && cached.IsAlive)
        {
            scene = cached;
            return true;
        }
        var ground = background.transform.Find("com/groundPos");
        var info = background.transform.Find("com")?.GetComponent<SceneInfo>();
        if (ground == null || info == null || !(fightUi.transform is RectTransform rect)) return false;
        scene = new FieldPresentationScene(rect, background, camera, ground,
            rect.Find("container") as RectTransform, rect.Find("Left") as RectTransform,
            rect.Find("ClockBoard") as RectTransform, () => info.ground_y);
        return true;
    }
}
