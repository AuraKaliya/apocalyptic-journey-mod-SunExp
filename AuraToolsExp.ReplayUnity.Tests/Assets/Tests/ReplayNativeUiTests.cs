using System;
using System.Collections;
using AuraToolsExp.Dll.Features.MatchRecords.ReplayV17.Playback;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Witch.UI.Window;
using Object = UnityEngine.Object;

public sealed class ReplayNativeUiTests
{
    private GameObject sourceOwner;
    private GameObject destination;
    private GameObject template;

    [SetUp]
    public void SetUp()
    {
        sourceOwner = new GameObject("SourceAssetQuarantine");
        sourceOwner.SetActive(false);
        template = Child(sourceOwner, "FightUI");
        destination = new GameObject("ReplayWorld");
        TutorialSpotlightUI.LifecycleCalls = 0;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        Object.Destroy(sourceOwner);
        Object.Destroy(destination);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RemovesTutorialGraphicsBeforeFirstFrameWithoutChangingSource()
    {
        var names = new[] { "TutorialSpotlightUI", "TutorialSpotlightUI2",
            "TutorialSpotlightUI2 (1)", "RenamedNativeGuide" };
        foreach (var name in names) Tutorial(template, name);
        var ordinary = Child(template, "TutorialNamedDecoration");
        ordinary.AddComponent<Image>();
        var hand = Child(template, "container");
        var image = hand.AddComponent<Image>();
        image.color = Color.cyan;
        image.raycastTarget = true;
        var mask = Child(template, "BattleMask").AddComponent<RectMask2D>();

        var clone = ReplayNativePrefabInstanceV17.Clone(template, destination.transform, "ReplayFightUI");

        Assert.That(clone.activeInHierarchy, Is.True);
        Assert.That(clone.GetComponentsInChildren<TutorialSpotlightUI>(true), Is.Empty);
        foreach (var name in names)
        {
            Assert.That(clone.transform.Find(name), Is.Null, "No authored tutorial graphics may survive.");
            Assert.That(template.transform.Find(name).gameObject.activeSelf, Is.True);
        }
        Assert.That(template.GetComponentsInChildren<TutorialSpotlightUI>(true).Length, Is.EqualTo(4));
        Assert.That(clone.transform.Find("TutorialNamedDecoration"), Is.Not.Null,
            "Names alone must not classify content as a tutorial.");
        var clonedImage = clone.transform.Find("container").GetComponent<Image>();
        Assert.That(clonedImage.color, Is.EqualTo(Color.cyan));
        Assert.That(clonedImage.raycastTarget, Is.False);
        Assert.That(image.raycastTarget, Is.True, "The original asset is untouched.");
        Assert.That(clone.transform.Find("BattleMask").GetComponent<RectMask2D>().enabled, Is.EqualTo(mask.enabled));

        yield return null;
        Assert.That(TutorialSpotlightUI.LifecycleCalls, Is.Zero);
        Assert.That(clone.GetComponentsInChildren<Image>(true).Length, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator RemovesNestedAndInactiveOwnersWithoutRemovingOrdinaryParents()
    {
        var hidden = Child(template, "HiddenContainer");
        hidden.SetActive(false);
        Tutorial(hidden, "UntriggeredGuide");
        var parentGuide = Tutorial(template, "OuterGuide");
        Tutorial(parentGuide, "InnerGuide");

        var clone = ReplayNativePrefabInstanceV17.Clone(template, destination.transform, "ReplayFightUI");
        Assert.That(clone.transform.Find("OuterGuide"), Is.Null);
        Assert.That(clone.transform.Find("HiddenContainer"), Is.Not.Null);
        Assert.That(clone.transform.Find("HiddenContainer").gameObject.activeSelf, Is.False);
        Assert.That(clone.transform.Find("HiddenContainer/UntriggeredGuide"), Is.Null);
        yield return null;
        Assert.That(TutorialSpotlightUI.LifecycleCalls, Is.Zero);
    }

    [UnityTest]
    public IEnumerator ReopenDoesNotResurrectTutorialsOrModifyTemplate()
    {
        Tutorial(template, "Guide");
        Child(template, "container").AddComponent<Image>();
        var first = ReplayNativePrefabInstanceV17.Clone(template, destination.transform, "First");
        Object.Destroy(first);
        yield return null;

        var second = ReplayNativePrefabInstanceV17.Clone(template, destination.transform, "Second");
        Assert.That(second.transform.Find("Guide"), Is.Null);
        Assert.That(second.transform.Find("container"), Is.Not.Null);
        Assert.That(template.transform.Find("Guide"), Is.Not.Null);
        yield return null;
        Assert.That(TutorialSpotlightUI.LifecycleCalls, Is.Zero);
        Assert.That(destination.transform.childCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator TutorialCannotBeUsedAsThePresentationRoot()
    {
        template.AddComponent<TutorialSpotlightUI>();
        Child(template, "Background").AddComponent<Image>();
        Assert.Throws<InvalidOperationException>(() =>
            ReplayNativePrefabInstanceV17.Clone(template, destination.transform, "InvalidRoot"));
        yield return null;
        Assert.That(destination.transform.childCount, Is.Zero);
        Assert.That(template.GetComponent<TutorialSpotlightUI>(), Is.Not.Null);
        Assert.That(TutorialSpotlightUI.LifecycleCalls, Is.Zero);
    }

    private static GameObject Tutorial(GameObject parent, string name)
    {
        var owner = Child(parent, name);
        owner.AddComponent<TutorialSpotlightUI>();
        Child(owner, "Background").AddComponent<Image>();
        Child(owner, "DialogueBox").AddComponent<Image>();
        return owner;
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent.transform, false);
        return child;
    }
}
