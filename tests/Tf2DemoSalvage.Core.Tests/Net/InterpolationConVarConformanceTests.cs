using System;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Net;

/// <summary>
/// How the engine decides how far behind live it renders — written down before anything uses it.
/// </summary>
/// <remarks>
/// **Nothing implements this yet, and that is deliberate.** `docs/CONFORMANCE.md` requires the
/// parity test to be written from the source *before* the code exists, so the answer cannot be a
/// description of whatever got built. The owner has also flagged interpolation as bigger than it
/// looks and wants to scope it himself — so this establishes what the engine does and stops there.
///
/// **The finding, and it is why `cl_interp` alone is the wrong question.**
/// `GetClientInterpAmount()` (`src/game/client/cdll_bounded_cvars.cpp:126`) is:
///
/// <code>
/// MAX( cl_interp-&gt;GetFloat(), cl_interp_ratio-&gt;GetFloat() / cl_updaterate-&gt;GetFloat() )
/// </code>
///
/// and neither operand is the value the player typed. Both are `ConVar_ServerBounded`, whose
/// `GetFloat` consults the server:
///
/// - `cl_interp_ratio` → `clamp( base, sv_client_min_interp_ratio, sv_client_max_interp_ratio )`,
///   skipped entirely when the minimum is −1;
/// - `cl_interp` → `MAX( base, sv_client_min_interp_ratio / cl_updaterate )`.
///
/// So reproducing what the recorder saw needs **five** values from two places: `cl_interp`,
/// `cl_interp_ratio` and `cl_updaterate` out of their `userinfo`, and the server's two ratio bounds
/// out of `net_setconvar`. `docs/CVAR-COVERAGE.md` measured a real competitive server sending
/// `sv_client_max_interp_ratio`, `sv_mincmdrate` and `sv_minupdaterate` — so the clamps are not
/// hypothetical.
///
/// **What this project does today is seven ticks, flat** (`ScenePropTrack`), whose own comment calls
/// the tick-rather-than-seconds form a known simplification. Left alone here on purpose: changing
/// it changes what every track samples, and on a 33-tick server it halves the delay.
/// </remarks>
public sealed class InterpolationConVarConformanceTests
{
    [Test]
    public void Declarations_ForTheInterpolationSet_MatchValvesDefaults()
    {
        EngineConVars.ByName("cl_interp").Default.ShouldBe("0.1");
        EngineConVars.ByName("cl_interp_ratio").Default.ShouldBe("2.0");
        EngineConVars.ByName("cl_updaterate").Default.ShouldBe("20");
        EngineConVars.ByName("sv_client_min_interp_ratio").Default.ShouldBe("1");
        EngineConVars.ByName("sv_client_max_interp_ratio").Default.ShouldBe("5");
    }

    /// <summary>That the three client halves are userinfo, so a demo carries the RECORDER's.</summary>
    /// <remarks>
    /// **This is what makes them answerable at all.** No server sends them, so without
    /// `FCVAR_USERINFO` there would be no way to know what the person recording had set — and
    /// `userinfo` is a string table this project already reads for the roster.
    /// </remarks>
    [Test]
    public void Declarations_ForTheClientHalves_AreUserInfoRatherThanReplicated()
    {
        foreach (string name in new[] { "cl_interp", "cl_interp_ratio", "cl_updaterate" })
        {
            EngineConVars.ByName(name).UserInfo.ShouldBeTrue($"{name} travels in userinfo");
            EngineConVars.ByName(name).Replicated.ShouldBeFalse($"no server sends {name}");
        }

        foreach (string name in new[] { "sv_client_min_interp_ratio", "sv_client_max_interp_ratio" })
        {
            EngineConVars.ByName(name).Replicated.ShouldBeTrue($"{name} is the server's clamp");
        }
    }

    [Test]
    public void Sdk_ForTheTwoBoundedClientConVars_DeclaresTheSameDefaults()
    {
        string source = Skip.Unless(
            SourceSdk.Text("src/game/client/cdll_bounded_cvars.cpp"), SourceSdk.Missing);

        Declared(source, "cl_interp").ShouldBe("0.1");
        Declared(source, "cl_interp_ratio").ShouldBe("2.0");
    }

    /// <summary>That the engine's own formula still reads the way this suite says it does.</summary>
    /// <remarks>
    /// **A text assertion, and it is the honest instrument here.** There is nothing of ours to
    /// measure yet, so the only thing that can go stale is this file's account of the SDK — and an
    /// SDK update that reshapes `GetClientInterpAmount` would leave the prose above quietly wrong.
    /// Whitespace is normalised because the file is tab-indented and the expression spans a line.
    /// </remarks>
    [Test]
    public void Sdk_GetClientInterpAmount_IsTheMaximumOfInterpAndRatioOverUpdateRate()
    {
        string source = Skip.Unless(
            SourceSdk.Text("src/game/client/cdll_bounded_cvars.cpp"), SourceSdk.Missing);

        string flattened = Regex.Replace(source, @"\s+", " ", RegexOptions.None, Limit);

        // GetClientInterpAmount takes the LARGER of the two, not cl_interp alone.
        flattened.ShouldContain("return MAX( cl_interp->GetFloat(), cl_interp_ratio->GetFloat() /");
    }

    /// <summary>That `cl_interp` is clamped up by the server, not merely reported.</summary>
    /// <remarks>
    /// The half most likely to be missed: a competitive server setting
    /// `sv_client_min_interp_ratio 1` raises a recorder's `cl_interp 0` to `1 / cl_updaterate`,
    /// so the popular "cl_interp 0" config does not mean zero delay. A viewer that read the
    /// userinfo value literally would draw the wrong instant.
    /// </remarks>
    [Test]
    public void Sdk_ClInterpGetFloat_IsRaisedToTheServersMinimumRatioOverUpdateRate()
    {
        string source = Skip.Unless(
            SourceSdk.Text("src/game/client/cdll_bounded_cvars.cpp"), SourceSdk.Missing);

        string flattened = Regex.Replace(source, @"\s+", " ", RegexOptions.None, Limit);

        // The server's minimum ratio RAISES cl_interp rather than replacing it.
        flattened.ShouldContain("return MAX( GetBaseFloatValue(), pMin->GetFloat() /");
    }

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static string Declared(string source, string name)
    {
        Match found = Regex.Match(
            source,
            @"ConVar_ServerBounded\(\s*""" + Regex.Escape(name) + @"""\s*,\s*""([^""]*)""",
            RegexOptions.None,
            Limit);

        found.Success.ShouldBeTrue($"{name} is declared in cdll_bounded_cvars.cpp");

        return found.Groups[1].Value;
    }
}
