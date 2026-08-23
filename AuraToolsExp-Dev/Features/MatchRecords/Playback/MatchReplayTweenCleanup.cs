using System;
using DG.Tweening;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.MatchRecords.Playback;

/// <summary>
/// Last-resort cleanup for replay-owned objects whose native close animation did
/// not finish inside the lifecycle timeout. Normal UI exits must use UIBase.Close.
/// </summary>
internal static class MatchReplayTweenCleanup
{
    internal static int KillTree(GameObject? root)
    {
        if (root == null)
        {
            return 0;
        }

        var killed = 0;
        try
        {
            killed += DOTween.Kill(root, complete: false);
        }
        catch
        {
        }

        UnityEngine.Component[] components;
        try
        {
            components = root.GetComponentsInChildren<UnityEngine.Component>(includeInactive: true);
        }
        catch
        {
            return killed;
        }

        foreach (var component in components)
        {
            if (component == null)
            {
                continue;
            }

            try
            {
                killed += DOTween.Kill(component, complete: false);
            }
            catch (Exception)
            {
                // A partially destroyed Unity component may throw while DOTween
                // resolves its target. Continue so the remaining tree is cleaned.
            }
        }

        return killed;
    }
}
