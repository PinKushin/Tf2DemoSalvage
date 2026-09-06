namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// That <c>cl_detaildist</c> and <c>cl_detailfade</c> are Valve's, and reachable from a config.
/// </summary>
/// <remarks>
/// **These are the two settings the game's own quality configs move furthest.** `tf/cfg/low.cfg`
/// ships `cl_detaildist 0` — which draws no detail props at all — and `tf/cfg/ultra.cfg` ships
/// `cl_detaildist 8592`, seven times the default. So the shipped range of one setting spans zero to
/// 8592, and a viewer that hardcoded 1200 would ignore both files.
///
/// D69 is that a real TF2 config must work wholesale, and this is its exact failure mode: the
/// default and the ignored setting agree everywhere except on the machine that set it, so nothing
/// looks wrong.
///
/// **The arithmetic half is asserted elsewhere.** `DetailFadeConformanceTests` covers what the
/// numbers DO — the FOV factor's asymmetry, the `MIN(fade, max − 1)` that makes zero draw nothing,
/// the truncation to a byte. This file is the other half: that the names are Valve's, the defaults
/// are Valve's, and a config line survives the journey. Neither implies the other — a correctly
/// parsed setting that reaches no renderer is the no-op this project has shipped three times.
/// </remarks>
public sealed class DetailDistanceCvarConformanceTests
{
    [Test]
    public void DetailCommands_AreValvesNames()
    {
        ViewerSettings.DetailDistanceCommand.ShouldBe("cl_detaildist");
        ViewerSettings.DetailFadeCommand.ShouldBe("cl_detailfade");
    }

    /// <remarks>
    /// <c>ConVar cl_detaildist( "cl_detaildist", "1200", ... )</c> and
    /// <c>cl_detailfade( "cl_detailfade", "400", ... )</c> — `detailobjectsystem.cpp:52-53`.
    /// </remarks>
    [Test]
    public void Parse_WithNothingSaid_KeepsValvesDefaults()
    {
        ViewerSettings settings = ViewerSettings.Parse(string.Empty);

        settings.DetailDistance.ShouldBe(1200f);
        settings.DetailFade.ShouldBe(400f);
    }

    /// <remarks>
    /// **`low.cfg` ships exactly this, and zero is a VALUE rather than a disabled state.** It must
    /// be accepted and carried, not treated as "unset" and replaced by the default — which is what
    /// a guard of the form `> 0` would do, and it would silently give a low-spec config the full
    /// 1200.
    /// </remarks>
    [Test]
    public void Parse_WithTheLowConfigsZero_DrawsNoDetailPropsAtAll()
    {
        ViewerSettings settings = ViewerSettings.Parse(
            "cl_detaildist 0\ncl_detailfade 0\n");

        settings.DetailDistance.ShouldBe(0f);
        settings.DetailFade.ShouldBe(0f);

        // And the arithmetic that follows must agree: nothing is within a maximum of zero.
        DetailFade fade = DetailFade.For(settings.DetailDistance, settings.DetailFade);

        fade.Alpha(0f).ShouldBe((byte)0);
    }

    /// <remarks>
    /// `ultra.cfg` ships `cl_detaildist 8592`, and the control is that the default would have
    /// dropped a sprite this test keeps — otherwise "accepted the number" and "ignored it" look the
    /// same.
    /// </remarks>
    [Test]
    public void Parse_WithTheUltraConfigsDistance_KeepsWhatTheDefaultWouldDrop()
    {
        ViewerSettings settings = ViewerSettings.Parse("cl_detaildist 8592\n");

        settings.DetailDistance.ShouldBe(8592f);

        DetailFade.For(settings.DetailDistance, settings.DetailFade)
            .Alpha(4_000_000f).ShouldBe((byte)255);

        DetailFade.For(ViewerSettings.DefaultDetailDistance, ViewerSettings.DefaultDetailFade)
            .Alpha(4_000_000f).ShouldBe((byte)0);
    }

    /// <remarks>
    /// A negative distance is not a shorter range; it is nonsense that would make the falloff
    /// divide the wrong way. Ignored rather than obeyed, which is the rule `fps_max` already keeps.
    /// </remarks>
    [Test]
    public void Parse_WithANegativeDistance_KeepsTheDefault()
    {
        ViewerSettings.Parse("cl_detaildist -1\n").DetailDistance.ShouldBe(1200f);
    }

    /// <remarks>
    /// **The round trip, because a setting that cannot be written back is lost on the next save.**
    /// The written text must be parseable into the same value.
    /// </remarks>
    [Test]
    public void Write_ThenParse_KeepsTheDistance()
    {
        ViewerSettings written = ViewerSettings.Parse("cl_detaildist 8592\ncl_detailfade 250\n");

        ViewerSettings read = ViewerSettings.Parse(written.Write());

        read.DetailDistance.ShouldBe(8592f);
        read.DetailFade.ShouldBe(250f);
    }
}
