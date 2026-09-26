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
