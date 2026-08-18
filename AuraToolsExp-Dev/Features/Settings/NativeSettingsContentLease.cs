using System;
using System.Collections.Generic;
using System.Linq;
using AuraToolsExp.Dll.Infrastructure;
using UnityEngine;

namespace AuraToolsExp.Dll.Features.Settings;

internal sealed class NativeSettingsContentLease
{
    private readonly NativeContentVisibilityLease<GameObject> visibilityLease = new();
    private Transform? leasedHost;

    internal bool IsActive => visibilityLease.IsActive;

    internal void Acquire(
        Transform host,
        GameObject ownedPanel,
        IEnumerable<Transform?> protectedTransforms)
    {
        if (host == null || ownedPanel == null)
        {
            return;
        }

        if (IsActive && leasedHost == host)
        {
            return;
        }

        Release("host changed");
        var protectedBranches = ResolveProtectedBranches(
            host,
            protectedTransforms.Append(ownedPanel.transform));
        var targets = new List<GameObject>();
        foreach (Transform child in host)
        {
            if (child == null
                || child.gameObject == ownedPanel
                || protectedBranches.Contains(child))
            {
                continue;
            }

            targets.Add(child.gameObject);
        }

        leasedHost = host;
        visibilityLease.Acquire(
            targets,
            target => target != null && target.activeSelf,
            (target, visible) =>
            {
                if (target != null)
                {
                    target.SetActive(visible);
                }
            },
            ex => AuraToolsLog.Warn(
                "[Settings] native content visibility lease degraded: " + ex.Message));
        AuraToolsLog.Debug(
            "[Settings] native content leased: host=" + Describe(host)
            + ", roots=" + visibilityLease.Count + ".");
    }

    internal void Release(string source)
    {
        if (!visibilityLease.Release())
        {
            leasedHost = null;
            return;
        }

        AuraToolsLog.Debug(
            "[Settings] native content lease released: source=" + source
            + ", host=" + Describe(leasedHost) + ".");
        leasedHost = null;
    }

    private static HashSet<Transform> ResolveProtectedBranches(
        Transform host,
        IEnumerable<Transform?> protectedTransforms)
    {
        var branches = new HashSet<Transform>();
        foreach (var protectedTransform in protectedTransforms)
        {
            var branch = DirectChildBranch(host, protectedTransform);
            if (branch != null)
            {
                branches.Add(branch);
            }
        }

        return branches;
    }

    private static Transform? DirectChildBranch(Transform host, Transform? item)
    {
        if (item == null || item == host)
        {
            return null;
        }

        var current = item;
        while (current.parent != null && current.parent != host)
        {
            current = current.parent;
        }

        return current.parent == host ? current : null;
    }

    private static string Describe(Transform? transform)
    {
        return transform == null
            ? "none"
            : transform.name + "#" + transform.GetInstanceID();
    }
}
