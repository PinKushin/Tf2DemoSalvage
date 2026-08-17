using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Text;

namespace Tf2DemoSalvage.Core.Tests.Text;

/// <summary>
/// That the trace seeds entities from <c>instancebaseline</c> before applying a delta.
/// </summary>
/// <remarks>
/// **A wiring test, not a component test, and that is the whole point.** `BaselineBuilder` works
/// and has its own unit tests; `DemoTimeline` calls it. The trace writer did not — it handled
/// string tables only to resolve sound names — so the text dump decoded every entity against a
/// zero baseline. Nothing in the component tests could see that, because the component was never
/// the thing at fault.
///
/// **How the gap was found, since it is the argument for this test existing.** An entity's
/// properties split cleanly by where they are set. Traced over cp_process with entities on, the
/// gameplay properties of `CTFObjectiveResource` appear in their hundreds — `m_iTeamInZone` 531
/// times, `m_iCappingTeam` 84, `m_iOwner` 26 — while every property the map sets once at init is
/// absent from the same 782 MB of text: `m_iNumControlPoints`, `m_vCPPositions`, `m_bCPIsVisible`,
/// `m_iCPGroup`, `m_iTeamIcons`, `m_bCPLocked`, `m_pszCapLayoutInHUD`. A split that exact is not a
/// demo that lacks control points; it is the baseline half of the state never being applied.
///
/// The gameplay properties are the control here. Had they been missing too, this would have been
/// a decoder fault rather than a wiring one, and the fix would be somewhere else entirely.
/// </remarks>
public sealed class CorpusTraceBaselineTests
{
    /// <summary>The class that made the split visible, and the one asserted on.</summary>
    private const string ObjectiveResource = "CTFObjectiveResource";

    /// <summary>
    /// Set once by <c>team_control_point_master</c> at map init, so it reaches a client only
    /// through the instance baseline — never as a delta, because it never changes.
    /// </summary>
    private const string MapInitProperty = "m_iNumControlPoints";

    /// <summary>
    /// Arrives in the entity's own snapshot rather than through the baseline, so it is present
    /// with or without this fix. The control: it proves the entity is being decoded at all.
    /// </summary>
    /// <remarks>
    /// **Not `m_iTeamInZone`, which was the first choice and was wrong.** That one only moves when
    /// a player stands in a capture zone, and the committed era specimens are solo recordings —
    /// so it is absent for a reason that has nothing to do with baselines, and the control failed
    /// while the code under test was innocent. The per-team cap requirements are sent for every
    /// point on any map that has them.
    /// </remarks>
    private const string GameplayProperty = "m_iTeamReqCappers";

    private static string TraceWithEntities(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        DemoHeader header = DemoHeader.Parse(bytes);

        // Enough commands to carry the signon, which is where instancebaseline arrives.
        List<DemoCommand> commands =
        [
            .. DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)).Take(400),
        ];

        StringWriter writer = new() { NewLine = "\n" };
        DemoTraceWriter.Write(
            writer,
            Path.GetFileName(path),
            header,
            commands,
            null,
            new DemoTraceOptions { IncludeEntities = true });

        return writer.ToString();
    }

    [Test]
    public void TheTrace_SeedsEntitiesFromTheirInstanceBaseline()
    {
        string path = Corpus.Demo("stv-cp_foundry");

        string trace = TraceWithEntities(path);

        // Control first, and deliberately so: a bare assertion on the map-init property would
        // fail identically if the class never appeared, if the schema failed to parse, or if the
        // demo simply had no control points. Establishing that the entity decodes, and that its
        // delta-borne properties arrive, narrows the next failure to exactly one cause.
        trace.ShouldContain(ObjectiveResource);
        trace.ShouldContain(GameplayProperty);

        // The claim. Absent before the fix, in every demo ever traced.
        trace.ShouldContain(MapInitProperty);
    }
}
