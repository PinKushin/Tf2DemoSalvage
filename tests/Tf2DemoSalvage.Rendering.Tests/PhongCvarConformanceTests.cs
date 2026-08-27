namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That <c>mat_phong</c> is Valve's switch, under Valve's name, reachable from a pasted config.
/// </summary>
/// <remarks>
/// **`mat_phong` is a real convar and this viewer had no equivalent.** The game's own shipped list
/// gives it as <c>mat_phong : 1 : :</c> — default 1, no flags, no help text. What existed here was
/// `mat_specular`, which is cubemap reflections and a different thing entirely, so the one
/// manipulation that would settle B170 could not be made.
///
/// **It is a config setting rather than a menu item, and that was the owner's call**, 2026-08-27:
/// *"if its a valve setting then yes it goes in the config and can be imported from a config"*.
/// D69 is that a real TF2 config must work wholesale, and plenty of real configs carry
/// `mat_phong 0` for performance — a viewer that reached the same switch only from its own menu
/// would silently ignore every one of them, which is D69's exact failure mode.
///
/// **The render half is asserted elsewhere.** `PhongRenderTests.PhongRender_WithPhongOff_LeavesNoHighlight`
/// draws a `$phong` material with the switch both ways and requires the highlight to go, with a
/// matte bystander that must not move. This file is the other half: that the switch is named what
/// Valve names it, defaults where Valve defaults it, and survives the journey from a config line.
/// Neither half implies the other — a correctly parsed setting that reaches no shader is the no-op
/// this project has shipped three times.
/// </remarks>
public sealed class PhongCvarConformanceTests
{
    [Test]
    public void PhongCommand_AgainstTheShippedCvarList_IsMatPhongAndDefaultsOn()
    {
        ViewerSettings.PhongCommand.ShouldBe("mat_phong");

        // `mat_phong : 1` — the default is ON, so a config that says nothing about it must leave
        // highlights drawn.
        ViewerSettings.Parse(string.Empty).Phong.ShouldBeTrue();
    }

    [Test]
    public void Parse_WithMatPhongZero_TurnsHighlightsOff()
    {
        // A Source config writes a boolean convar as a number, which is the form a pasted TF2
        // config actually contains.
        ViewerSettings.Parse("mat_phong 0").Phong.ShouldBeFalse();
    }

    [Test]
    public void Parse_WithMatPhongOne_LeavesHighlightsOn()
    {
        // **The bystander for the test above.** Without it, a parser that returned false for every
        // input — or ignored the value and always turned the feature off — would pass, and "the
        // config is read" would be indistinguishable from "the config always means off".
        ViewerSettings.Parse("mat_phong 1").Phong.ShouldBeTrue();
    }
}
