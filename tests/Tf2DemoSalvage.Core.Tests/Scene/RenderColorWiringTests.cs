using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// An entity's <c>m_clrRender</c> survives the hop from the wire to a drawn pose (B391).
/// </summary>
/// <remarks>
/// **The hop the decode and completeness tests cannot see.** `RenderStateDecodeTests` proves the three
/// bytes are read off the field, and `PoseCompletenessTests` that a rebuilt pose keeps them; neither
/// says whether `DemoTimeline` ever copies them from the entity into the pose. That copy is one line,
/// and B391 exists because the field it copies had been decoded and read by nothing on the sprite path —
/// the shape `MinigunStateWiringTests` was written for, and that suite's two paths are this one's.
///
/// **The fixture's color is `0xFF3366CC`**: alpha 255 so every other test on `SyntheticProp` keeps an
/// opaque prop, and three different color bytes, none of them 255, so a field dropped anywhere on the way
/// reads back as white rather than as a plausible color.
/// </remarks>
public sealed class RenderColorWiringTests
{
    /// <remarks>
    /// **The rebuild path**: at the later keyframe's own tick, `At` falls through into the
    /// field-by-field rebuild, which is where a discrete value gets dropped.
    ///
    /// Red is the LOW byte of the packed `color32`, so `0xFF3366CC` is red `0xCC`, green `0x66` and
    /// blue `0x33`.
    /// </remarks>
    [Test]
    public void PropsAt_AtTheLaterKeyframe_CarriesTheRenderColorThroughTheRebuild()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 0, Parity: 0),
            (Tick: 660, Sequence: 0, Parity: 0)));

        SyntheticProp.PoseAt(timeline, 660).RenderColor.ShouldBe(
            ((byte)0xCC, (byte)0x66, (byte)0x33),
            "the fixture sends 0xFF3366CC, and the pose is what the sprite pass reads");
    }

    /// <remarks>
    /// **The other path**: a tick before the later keyframe arrives returns the earlier keyframe's pose
    /// untouched, so a field missing from the raw pose — rather than from the rebuild — shows here.
    /// </remarks>
    [Test]
    public void PropsAt_BeforeTheLaterKeyframeArrives_CarriesTheRenderColorFromTheEarlierPose()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 0, Parity: 0),
            (Tick: 660, Sequence: 1, Parity: 1)));

        SyntheticProp.PoseAt(timeline, 330).RenderColor.ShouldBe(
            ((byte)0xCC, (byte)0x66, (byte)0x33),
            "the earlier keyframe is what a client would be holding, and it carries the color too");
    }
}
