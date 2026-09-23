using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// `GetClientInterpAmount()` (`cdll_bounded_cvars.cpp:126`) over the viewer's own config, bounded by the recording's
/// server — the formula <see cref="InterpolationConVarConformanceTests"/> writes down.
/// </summary>
public sealed class ClientInterpTests
{
    [Test]
    public void Amount_AtTf2sDefaults_IsATenthOfASecond()
    {
        // max( 0.1 raised to 1/20, clamp( 2, 1, 5 ) / 20 ) = 0.1 — what the viewer assumed until now.
        new ClientInterp().Amount(new ServerConVars()).ShouldBe(0.1d, 1e-6d);
    }

    [Test]
    public void Amount_AtACompetitiveConfig_IsOneUpdate()
    {
        // The owner's: cl_interp 0, cl_interp_ratio 1, cl_updaterate 66 — max( max( 0, 1/66 ), 1/66 ) = 1/66.
        new ClientInterp(0f, 1f, 66f).Amount(new ServerConVars()).ShouldBe(1d / 66d, 1e-6d);
    }

    [Test]
    public void Amount_ARatioBelowTheServersMinimum_IsRaisedToIt()
    {
        // cl_interp_ratio 0 is clamped to sv_client_min_interp_ratio 1, and cl_interp 0 is raised to 1/66 as well.
        new ClientInterp(0f, 0f, 66f).Amount(new ServerConVars()).ShouldBe(1d / 66d, 1e-6d);
    }

    [Test]
    public void Amount_ARatioAboveTheServersMaximum_IsLoweredToIt()
    {
        new ClientInterp(0f, 9f, 66f).Amount(new ServerConVars()).ShouldBe(5d / 66d, 1e-6d);
    }

    [Test]
    public void Amount_AnUpdateRateAboveTheServersMaximum_IsLoweredTo66()
    {
        // cl_updaterate 128 is clamped to sv_maxupdaterate 66 before the division.
        new ClientInterp(0f, 1f, 128f).Amount(new ServerConVars()).ShouldBe(1d / 66d, 1e-6d);
    }
}
