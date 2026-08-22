using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The fog controller's networked property names, ours against the table that declares them.
/// </summary>
/// <remarks>
/// **A wire name is a string, and a wrong one fails silently by finding nothing.** That is the whole
/// hazard: <c>Number("DT_FogController.m_fog.startdist")</c> returns null exactly as it would for a
/// demo with no fog controller, so a typo and an absent entity are indistinguishable at the call
/// site. See <c>docs/memory/wire-names-are-strings.md</c>.
///
/// Valve declares them in <c>game/server/fogcontroller.cpp</c>:
///
/// <code>
/// IMPLEMENT_SERVERCLASS_ST_NOBASE( CFogController, DT_FogController )
///     SendPropInt( SENDINFO_STRUCTELEM( m_fog.enable ), 1, SPROP_UNSIGNED ),
///     SendPropFloat( SENDINFO_STRUCTELEM( m_fog.start ), 0, SPROP_NOSCALE ),
///     ...
/// </code>
///
/// **`SENDINFO_STRUCTELEM` sends under the member expression itself**, dots included, which is why
/// these names have a shape no other table here uses. The previous conformance test asserted that
/// those lines still appear in Valve's file and stopped there; this compares each against the
/// constant <see cref="EntityState"/> actually looks up.
/// </remarks>
public sealed class FogControllerConformanceTests
{
    private const string Controller = "src/game/server/fogcontroller.cpp";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void FogController_TheTableName_IsTheOneValveDeclares()
    {
        string text = Sdk();

        Match declaration = Regex.Match(
            text,
            @"IMPLEMENT_SERVERCLASS_ST_NOBASE\(\s*CFogController\s*,\s*(?<table>DT_\w+)\s*\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        declaration.Success.ShouldBeTrue("CFogController's server class was not found");

        EntityState.FogControllerTable.ShouldBe(declaration.Groups["table"].Value);
    }

    [Test]
    public void FogController_EveryPropertyThisDecoderReads_IsSentByThatTable()
    {
        // Every SENDINFO_STRUCTELEM member in the file, extracted rather than restated.
        HashSet<string> sent = new(StringComparer.Ordinal);

        foreach (Match member in Regex.Matches(
            Sdk(),
            @"SENDINFO_STRUCTELEM\(\s*(?<name>m_fog\.\w+)\s*\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5)))
        {
            sent.Add(member.Groups["name"].Value);
        }

        // The control: a pattern that matched nothing would make every assertion below vacuous, and
        // a table this small is worth stating a floor for.
        sent.Count.ShouldBeGreaterThan(
            5, "DT_FogController sends start, end, maxdensity, both colours, enable and the lerps");

        foreach (string ours in new[]
        {
            EntityState.FogEnableProperty,
            EntityState.FogStartProperty,
            EntityState.FogEndProperty,
            EntityState.FogColourProperty,
            EntityState.FogMaxDensityProperty,
        })
        {
            sent.ShouldContain(
                ours,
                $"this decoder looks up {ours}, and a name the table does not send returns null "
                + "exactly as an absent controller does — the failure is silent either way");
        }

        // **And the near miss, as a control.** `m_fog.colorSecondary` exists and is one character
        // class away from what we read; asserting the set does NOT make our colour name ambiguous
        // is what shows the match above was on the right member.
        sent.ShouldContain("m_fog.colorSecondary", "the near miss is present in the table");

        EntityState.FogColourProperty.ShouldNotBe("m_fog.colorSecondary");
    }

    /// <summary>Reads the fog controller source, or fails loudly.</summary>
    private static string Sdk() =>
        SourceSdk.Text(Controller)
        ?? throw new InvalidOperationException($"{Controller} is missing from the SDK");
}
