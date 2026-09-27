using System.Collections.Generic;

using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHud`'s render groups (hud.cpp:1016-1131) and `CHudElement::ShouldDraw`'s check of them (:288).</summary>
public sealed class HudRenderGroupConformanceTests
{
    private static readonly HudState Playing = new(true, true, 0, 100, true);

    [Test]
    public void ShouldDraw_AHigherPriorityLockerInItsGroup_HidesALowerElement()
    {
        HudViewport viewport = new();
        Element low = new(viewport, "Low", priority: 10, "mid");
        Element high = new(viewport, "High", priority: 50, "mid");

        viewport.LockRenderGroup("mid", high);

        (((IHudElement)low).ShouldDraw(Playing), ((IHudElement)high).ShouldDraw(Playing)).ShouldBe((false, true));
    }

    [Test]
    public void ShouldDraw_ALockerOfEqualPriority_HidesNothing()
    {
        // `pLocker->GetRenderGroupPriority() > pHudElement->GetRenderGroupPriority()` (:1131) — strictly greater.
        HudViewport viewport = new();
        Element chat = new(viewport, "Chat", priority: 35, "mid");
        Element secondary = new(viewport, "Secondary", priority: 35, "mid");

        viewport.LockRenderGroup("mid", secondary);

        ((IHudElement)chat).ShouldDraw(Playing).ShouldBeTrue();
    }

    [Test]
    public void ShouldDraw_AfterUnlock_ShowsAgain()
    {
        HudViewport viewport = new();
        Element low = new(viewport, "Low", priority: 10, "mid");
        Element high = new(viewport, "High", priority: 50, "mid");

        viewport.LockRenderGroup("mid", high);
        viewport.UnlockRenderGroup("mid", high);

        ((IHudElement)low).ShouldDraw(Playing).ShouldBeTrue();
    }

    [Test]
    public void ShouldDraw_TwoLockers_TheHighestPriorityOneDecides()
    {
        // `ElementAtHead()` of a priority queue ordered by `GetRenderGroupPriority` (hudelement.h:133).
        HudViewport viewport = new();
        Element middle = new(viewport, "Middle", priority: 40, "mid");
        Element low = new(viewport, "Low", priority: 30, "mid");
        Element high = new(viewport, "High", priority: 50, "mid");

        viewport.LockRenderGroup("mid", low);
        viewport.LockRenderGroup("mid", high);

        ((IHudElement)middle).ShouldDraw(Playing).ShouldBeFalse();
    }

    [Test]
    public void ShouldDraw_ALockOnAGroupItIsNotIn_LeavesItAlone()
    {
        HudViewport viewport = new();
        Element other = new(viewport, "Other", priority: 0, "global");
        Element high = new(viewport, "High", priority: 50, "mid");

        viewport.LockRenderGroup("mid", high);

        ((IHudElement)other).ShouldDraw(Playing).ShouldBeTrue();
    }

    private sealed class Element(VguiPanel viewport, string name, int priority, params string[] groups) : VguiPanel(viewport, name), IHudElement
    {
        public int HiddenBits => 0;

        public IReadOnlyList<string> RenderGroups => groups;

        public int RenderGroupPriority => priority;
    }
}
