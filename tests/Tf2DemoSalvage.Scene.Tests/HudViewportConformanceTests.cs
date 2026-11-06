using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHud::IsHidden` (hud.cpp:951), `CHudElement::ShouldDraw` (:288), `CHud::Think` (hud_redraw.cpp:48), `CBaseViewport`.</summary>
/// <remarks>
/// Hidden outright out of game, without a local player, or with `HIDEHUD_ALL`; `HIDEHUD_PLAYERDEAD` hides when health is
/// at most 0 and the player is not alive; otherwise hidden when the element's bits meet the player's `m_iHideHUD`.
/// `CHud::Think` shows or hides each element's panel by it. The viewport is `CBaseViewport`, proportional, and applies
/// `scripts/HudLayout.res` to its elements by name.
/// </remarks>
public sealed class HudViewportConformanceTests
{
    private static readonly HudState Playing = new(InGame: true, HasLocalPlayer: true, HideHud: 0, Health: 125, Alive: true);

    [Test]
    public void IsHidden_NotInGameOrNoPlayer_HidesEverything()
    {
        HudVisibility.IsHidden(Playing with { InGame = false }, 0).ShouldBeTrue();
        HudVisibility.IsHidden(Playing with { HasLocalPlayer = false }, 0).ShouldBeTrue();
        HudVisibility.IsHidden(Playing, 0).ShouldBeFalse();
    }

    [Test]
    public void IsHidden_HideHudAll_HidesAnElementWithNoBits() =>
        HudVisibility.IsHidden(Playing with { HideHud = HudVisibility.HideAll }, 0).ShouldBeTrue();

    [Test]
    public void IsHidden_PlayerDead_NeedsNoHealthAndNotAlive()
    {
        HudVisibility.IsHidden(Playing with { Health = 0, Alive = false }, HudVisibility.HidePlayerDead).ShouldBeTrue();
        HudVisibility.IsHidden(Playing with { Health = 0, Alive = true }, HudVisibility.HidePlayerDead).ShouldBeFalse();
        HudVisibility.IsHidden(Playing with { Health = 5, Alive = false }, HudVisibility.HidePlayerDead).ShouldBeFalse();
        HudVisibility.IsHidden(Playing with { Health = 0, Alive = false }, HudVisibility.HideHealth).ShouldBeFalse("dead is not a HIDEHUD bit");
    }

    [Test]
    public void IsHidden_TheElementsBits_MeetThePlayersHideHud()
    {
        HudVisibility.IsHidden(Playing with { HideHud = HudVisibility.HideHealth }, HudVisibility.HideHealth | HudVisibility.HidePlayerDead).ShouldBeTrue();
        HudVisibility.IsHidden(Playing with { HideHud = HudVisibility.HideHealth }, HudVisibility.HideMiscStatus).ShouldBeFalse();
    }

    [Test]
    public void Think_AnElementThatShouldNotDraw_IsHiddenAndShownAgain()
    {
        HudViewport viewport = new();
        TestElement element = new(viewport) { HiddenBits = HudVisibility.HideHealth };

        viewport.Think(Playing with { HideHud = HudVisibility.HideHealth });
        element.Visible.ShouldBeFalse();

        viewport.Think(Playing);
        element.Visible.ShouldBeTrue();
    }

    [Test]
    public void LoadControlSettings_HudLayout_PlacesAnElementByNameAgainstTheScreen()
    {
        HudViewport viewport = new();
        TestElement element = new(viewport);
        VguiContext context = Context();

        viewport.LoadControlSettings(
            KeyValuesTree.Load(Encoding.UTF8.GetBytes("""Resource { "Test" { "xpos" "10" "ypos" "r20" "wide" "30" "tall" "40" } }"""), "scripts/hudlayout.res", _ => null),
            context);

        (element.X, element.Y, element.Wide, element.Tall).ShouldBe((20, 960 - 40, 60, 80), "proportional at 960 tall doubles each");
    }

    [Test]
    public void Viewport_IsTheProportionalCBaseViewport()
    {
        HudViewport viewport = new();

        viewport.Name.ShouldBe("CBaseViewport");
        viewport.Proportional.ShouldBeTrue();
    }

    /// <remarks>`InvalidateLayout( false, true )` (Panel.cpp:3924): the scheme applied at once under a viewport with a scheme.</remarks>
    [Test]
    public void InvalidateLayout_AReloadUnderTheViewport_AppliesTheSchemeAtOnce()
    {
        HudViewport viewport = new() { Context = Context() };
        SchemeCounter panel = new(viewport);

        panel.InvalidateLayout(reloadScheme: true);

        panel.Applied.ShouldBe(1);
    }

    [Test]
    public void InvalidateLayout_AReloadWithNoScheme_WaitsForTheTraversal()
    {
        SchemeCounter panel = new(new VguiPanel(null, "Root"));

        panel.InvalidateLayout(reloadScheme: true);

        panel.Applied.ShouldBe(0);
    }

    /// <remarks>
    /// `EditablePanel::OnSizeChanged` (EditablePanel.cpp:179): a top-right pinned child keeps its distance from the right
    /// edge, a right-resizing one stretches with it, a default one returns to its pin offset.
    /// </remarks>
    // Its right edge at 30 of 100 is 70 short of the parent's, at 200 too: x 110, or a 120 stretch from x 10. The child is
    // first moved to 77, so each case shows where the resize puts it.
    [TestCase("\"PinCorner\" \"1\"", 110, 20)]
    [TestCase("\"AutoResize\" \"1\"", 10, 120)]
    [TestCase("", 10, 20)]
    public void OnSizeChanged_TheParentWidening_PlacesTheChildByItsPin(string keys, int x, int wide)
    {
        (VguiEditablePanel parent, VguiPanel child) = Pinned(keys, skip: false);
        child.X = 77;

        parent.Wide = 200;

        (child.X, child.Wide).ShouldBe((x, wide));
    }

    // Its bottom edge at 15 of 50 is 35 short of the parent's, at 100 too: y 55, or a 60 stretch from y 5 to 65.
    [TestCase("\"PinCorner\" \"2\"", 55, 10)]
    [TestCase("\"PinCorner\" \"3\"", 55, 10)]
    [TestCase("\"AutoResize\" \"2\"", 5, 60)]
    [TestCase("\"AutoResize\" \"3\"", 5, 60)]
    public void OnSizeChanged_TheParentGrowingTaller_PlacesTheChildByItsPin(string keys, int y, int tall)
    {
        (VguiEditablePanel parent, VguiPanel child) = Pinned(keys, skip: false);
        child.Y = 44;

        parent.Tall = 100;

        (child.Y, child.Tall).ShouldBe((y, tall));
    }

    /// <remarks>:240-247: a stretch past its start is clamped to nothing, never negative.</remarks>
    [Test]
    public void OnSizeChanged_AStretchNarrowerThanItsStart_ClampsToZeroWide()
    {
        (VguiEditablePanel parent, VguiPanel child) = Pinned("\"AutoResize\" \"1\"", skip: false);

        parent.Wide = 50;

        (child.X, child.Wide).ShouldBe((10, 0));
    }

    /// <remarks>`SetSize` (Panel.cpp:932) sets both at once, so `VPanel::SetSize` calls `OnSizeChanged` once; a same size, none.</remarks>
    [Test]
    public void SetSize_BothAtOnce_CallsOnSizeChangedOnce()
    {
        SizeCounter panel = new();

        panel.SetSize(100, 50);
        panel.SetSize(100, 50);

        (panel.Calls, panel.Wide, panel.Tall).ShouldBe((1, 100, 50));
    }

    private sealed class SizeCounter() : VguiPanel(null, "Sized")
    {
        public int Calls { get; private set; }

        protected override void OnSizeChanged(int newWide, int newTall)
        {
            base.OnSizeChanged(newWide, newTall);
            Calls++;
        }
    }

    [Test]
    public void OnSizeChanged_WithSkipAutoResize_LeavesTheChild()
    {
        (VguiEditablePanel parent, VguiPanel child) = Pinned("\"PinCorner\" \"1\"", skip: true);
        child.X = 33;

        parent.Wide = 200;

        child.X.ShouldBe(33);
    }

    /// <summary>A 100-wide parent with one child at x 10, 20 wide, set by its `.res` block with the given keys.</summary>
    private static (VguiEditablePanel Parent, VguiPanel Child) Pinned(string keys, bool skip)
    {
        VguiContext context = Context();
        VguiEditablePanel parent = new(null, "Parent") { Wide = 100, Tall = 50 };
        VguiPanel child = new(parent, "Child");
        KeyValuesTree block = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes($"Parent {{ \"wide\" \"100\" \"tall\" \"50\" \"skip_autoresize\" \"{(skip ? 1 : 0)}\" Child {{ \"xpos\" \"10\" \"ypos\" \"5\" \"wide\" \"20\" \"tall\" \"10\" {keys} }} }}"),
            "test.res",
            _ => null);

        parent.ApplySettings(block, context);
        return (parent, child);
    }

    private sealed class SchemeCounter(VguiPanel parent) : VguiPanel(parent, "Counter")
    {
        public int Applied { get; private set; }

        public override void ApplySchemeSettings(VguiContext context)
        {
            base.ApplySchemeSettings(context);
            Applied++;
        }
    }

    private static VguiContext Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { Borders { } Fonts { } }"), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, 960), scheme.Find("Fonts")!, 1280, 960, "english");
    }

    private sealed class TestElement(VguiPanel parent) : VguiEditablePanel(parent, "Test"), IHudElement
    {
        public int HiddenBits { get; init; }
    }
}
