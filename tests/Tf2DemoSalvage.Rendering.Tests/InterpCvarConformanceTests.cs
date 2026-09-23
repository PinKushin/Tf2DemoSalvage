namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>That `cl_interp`, `cl_interp_ratio` and `cl_updaterate` are Valve's and reachable from a config.</summary>
/// <remarks>
/// The owner, on the viewer hardcoding 0.1 s: *"thats a changable thing and most comp configs go low on it"*. A
/// competitive config's 0 / 1 / 66 draws entities and fires effects one update behind; the default draws them seven
/// ticks behind, and nothing looks wrong on a machine that never set it.
/// </remarks>
public sealed class InterpCvarConformanceTests
{
    [Test]
    public void Parse_WithNothingSaid_KeepsValvesDefaults()
    {
        ViewerSettings.Parse(string.Empty).Interp.ShouldBe(new Core.Net.ClientInterp(0.1f, 2f, 20f));
    }

    [Test]
    public void Parse_ACompetitiveConfig_CarriesAllThree()
    {
        ViewerSettings.Parse("cl_interp 0\ncl_interp_ratio 1\ncl_updaterate 66\n")
            .Interp.ShouldBe(new Core.Net.ClientInterp(0f, 1f, 66f));
    }

    [Test]
    public void Write_ThenParse_KeepsTheSettings()
    {
        ViewerSettings written = ViewerSettings.Parse("cl_interp 0.033\ncl_interp_ratio 1\ncl_updaterate 66\n");

        ViewerSettings.Parse(written.Write()).Interp.ShouldBe(written.Interp);
    }
}
