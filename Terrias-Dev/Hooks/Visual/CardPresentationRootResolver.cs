using System.Collections.Generic;
using UnityEngine;

namespace SunExp.Dll.Hooks.Visual;

public static class CardPresentationRootResolver
{
    public static bool IsCompactDisplayRoot(Transform? root)
    {
        return root != null
            && root.Find("Mask/CardIcon")?.GetComponent<UnityEngine.UI.Image>() != null;
    }

    public static Transform? FindCardVisualRoot(Transform? root)
    {
        if (root == null)
        {
            return null;
        }

        if (HasCardVisualNodes(root))
        {
            return root;
        }

        foreach (var path in new[] { "CardItem", "cardItem", "Card", "card", "ShowCard", "DisplayCard", "Item", "Root" })
        {
            var child = root.Find(path);
            if (HasCardVisualNodes(child))
            {
                return child;
            }
        }

        var queue = new Queue<Transform>();
        queue.Enqueue(root);
        var visited = 0;
        while (queue.Count > 0 && visited++ < 96)
        {
            var current = queue.Dequeue();
            if (!ReferenceEquals(current, root) && HasCardVisualNodes(current))
            {
                return current;
            }

            for (var i = 0; i < current.childCount; i++)
            {
                queue.Enqueue(current.GetChild(i));
            }
        }

        return null;
    }

    private static bool HasCardVisualNodes(Transform? root)
    {
        return root != null
            && (root.Find("Front/background") != null
                || root.Find("Front/icon") != null
                || root.Find("Front/FrontBack") != null);
    }
}
