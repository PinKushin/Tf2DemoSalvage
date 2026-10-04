using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A <c>CBeam</c> through <c>DemoTimeline</c>: admitted, carrying its beam, and standing where it was last sent.</summary>
/// <remarks>
/// **The admission gate is the defect these exist for.** `RecordProp` refuses an entity with neither a model nor an item,
/// and a beam's model index is on its own NOBASE table — so until the accessors read it there, 18,942 corpus beams
/// produced no track, and nothing downstream ever saw one. These build the beam on the wire, through the real decoder, and
/// read what the timeline hands the viewer.
/// </remarks>
public sealed class BeamTimelineTests
{
    private static List<SceneProp> PropsAt(DemoTimeline timeline, double tick)
    {
        List<SceneProp> props = [];
        timeline.PropsAt(tick, props);
        return props;
    }

    [Test]
    public void PropsAt_ASpotlightBeamOnTheWire_IsAPropCarryingItsBeam()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticBeam.Demo([(10, -712f, -1f, 1056f), (20, -712f, -1f, 1056f)]));

        SceneProp beam = PropsAt(timeline, 15).Single(prop => prop.EntityIndex == SyntheticBeam.BeamEntityIndex);

        beam.ModelPath.ShouldBe(SyntheticBeam.ModelPath);
        beam.Kind.ShouldBe(SceneModelKind.Sprite, "a beam's model is a sprite; its class decides how it draws");
        beam.ClassName.ShouldBe("CBeam");
        SceneBeam carried = beam.Pose.Beam.ShouldNotBeNull();

        carried.Flags.ShouldBe(SyntheticBeam.SpotlightFlags);
        carried.Width.ShouldBe(100f);
        carried.HaloPath.ShouldBe(SyntheticBeam.HaloPath, "the halo index resolved through the precache");
        beam.Pose.RenderMode.ShouldBe(2, "kRenderTransTexture, from DT_Beam's own m_nRenderMode");
        beam.Pose.RenderAlpha.ShouldBe((byte)64, "from DT_Beam's own m_clrRender");
        beam.Pose.X.ShouldBe(-712f, 0.01f);
        beam.Pose.Z.ShouldBe(1056f, 0.01f);
    }

    /// <remarks>
    /// **A beam stands where it was last SENT, never between two sends.** <c>C_Beam::AddEntity</c> ends with
    /// <c>MoveToLastReceivedPosition()</c> (`beam_shared.cpp:1075`), which writes the network origin back over whatever
    /// interpolation produced, every frame. Sent at x = 0 on tick 10 and x = 100 on tick 20, it is at 100 from tick 20
    /// on — where an interpolated entity would still be most of the way back toward 0, an interpolation window behind.
    /// </remarks>
    [Test]
    public void PropsAt_ABeamResentAtANewOrigin_StandsAtTheLastOneReceived()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticBeam.Demo([(10, 0f, 0f, 500f), (20, 100f, 0f, 500f)]));

        SceneProp beam = PropsAt(timeline, 21).Single(prop => prop.EntityIndex == SyntheticBeam.BeamEntityIndex);

        beam.Pose.X.ShouldBe(100f, 0.01f);
    }
}
