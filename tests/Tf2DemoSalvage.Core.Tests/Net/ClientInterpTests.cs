using System.Collections.Generic;

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

    /// <summary>
    /// A SourceTV recording has no interpolation rule of its own (B539): `cl_updaterate` is clamped to the replicated
    /// `sv_minupdaterate` and `sv_maxupdaterate` and nothing else — `CBoundedCvar_UpdateRate::GetFloat`, read in
    /// `engine.dll` at `0x180164230`, three loads and two compares — and neither `GetClientInterpAmount`
    /// (`cdll_bounded_cvars.cpp:127-144`) nor `C_BaseEntity::GetInterpolationAmount` (`c_baseentity.cpp:5916-5961`)
    /// asks `IsHLTV()` or reads `tv_snapshotrate`.
    /// </summary>
    [Test]
    public void Amount_ASourceTvRecordingReplicatingAMinimumUpdateRate_RaisesTheRateAndAsksNothingElse()
    {
        // What cp_process_f12's SourceTV demo replicates: sv_minupdaterate 66, tv_transmitall 1, no interp ratio.
        ServerConVars recorded = new();

        recorded.Apply(new SetConVarMessage(
        [
            new KeyValuePair<string, string>("sv_minupdaterate", "66"),
            new KeyValuePair<string, string>("tv_transmitall", "1"),
        ]));

        // TF2's defaults there: cl_updaterate 20 becomes 66, so the ratio's 2/66 loses to cl_interp's tenth.
        new ClientInterp().Amount(recorded).ShouldBe(0.1d, 1e-6d);

        // The owner's: one update, which is one tick and the engine's one more — two.
        double owners = new ClientInterp(0f, 1f, 66f).Amount(recorded);

        owners.ShouldBe(1d / 66d, 1e-6d);
        Tf2DemoSalvage.Core.Scene.ScenePropTrack.DelayTicksFor(0.015d, owners).ShouldBe(2);
    }

    [Test]
    public void Amount_AnUpdateRateAboveTheServersMaximum_IsLoweredTo66()
    {
        // cl_updaterate 128 is clamped to sv_maxupdaterate 66 before the division.
        new ClientInterp(0f, 1f, 128f).Amount(new ServerConVars()).ShouldBe(1d / 66d, 1e-6d);
    }
}
