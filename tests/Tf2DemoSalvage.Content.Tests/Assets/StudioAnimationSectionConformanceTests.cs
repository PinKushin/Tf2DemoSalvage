using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <see cref="StudioAnimation.Section"/> against <c>mstudioanimdesc_t::pAnim</c>.
/// </summary>
/// <remarks>
/// **The citation is <c>public/studio.cpp</c>, and every case below is read off it** rather than off
/// our implementation:
///
/// <code>
///   int section = 0;
///   if (sectionframes != 0)
///   {
///       if (numframes > sectionframes &amp;&amp; *piFrame == numframes - 1)
///       {
///           *piFrame = 0;
///           section = (numframes / sectionframes) + 1;
///       }
///       else
///       {
///           section = *piFrame / sectionframes;
///           *piFrame -= section * sectionframes;
///       }
///       block = pSection( section )->animblock;
///       index = pSection( section )->animindex;
///   }
/// </code>
///
/// **Why this exists at all** (B222): a long animation is stored in sections and each restarts its
/// frame numbering. Reading every frame out of section zero does not fail loudly — the run-length
/// walk runs off the end and keeps going, repeating a stale value for most frames and landing on
/// stray bytes for a few. In `c_demo_animations.mdl` that put `vm_weapon_bone_1` 219 units from its
/// rest position, and the sticky launcher merged onto it tore across the view.
/// </remarks>
public sealed class StudioAnimationSectionConformanceTests
{
    [Test]
    public void Section_WithSectionFramesZero_IsSectionZeroAtTheSameFrame()
    {
        // `if (sectionframes != 0)` — an unsectioned animation keeps animindex and the frame as-is.
        StudioAnimation.Section(frames: 150, sectionFrames: 0, frame: 113)
            .ShouldBe((0, 113));
    }

    [Test]
    public void Section_WithinTheFirstSection_IsSectionZeroAtTheSameFrame()
    {
        // section = 29 / 30 = 0; frame -= 0 * 30.
        StudioAnimation.Section(frames: 150, sectionFrames: 30, frame: 29)
            .ShouldBe((0, 29));
    }

    [Test]
    public void Section_AtASectionBoundary_StartsTheNextSectionAtFrameZero()
    {
        // section = 30 / 30 = 1; frame -= 1 * 30. The boundary is where reading section zero for
        // every frame first walks past the end of its data.
        StudioAnimation.Section(frames: 150, sectionFrames: 30, frame: 30)
            .ShouldBe((1, 0));
    }

    [Test]
    public void Section_InALaterSection_SubtractsThePrecedingSections()
    {
        // section = 113 / 30 = 3; 113 - 90 = 23. This is the frame that spiked in the viewer.
        StudioAnimation.Section(frames: 150, sectionFrames: 30, frame: 113)
            .ShouldBe((3, 23));
    }

    [Test]
    public void Section_AtTheLastFrameOfALongAnimation_IsTheSeparateTrailingSection()
    {
        // "last frame on long anims is stored separately": section = (150 / 30) + 1 = 6, frame 0.
        // Not 149 / 30 = 4, which is what the ordinary arithmetic alone would give.
        StudioAnimation.Section(frames: 150, sectionFrames: 30, frame: 149)
            .ShouldBe((6, 0));
    }

    [Test]
    public void Section_AtTheLastFrameOfAShortAnimation_TakesTheOrdinaryPath()
    {
        // **The guard is `numframes > sectionframes`, and this case is why it is there.** An
        // animation no longer than one section has no separately-stored last frame, so the trailing
        // section does not exist and asking for it would read past the section table.
        StudioAnimation.Section(frames: 30, sectionFrames: 30, frame: 29)
            .ShouldBe((0, 29));
    }

    [Test]
    public void Section_AtTheLastFrameWhenSectionsDoNotDivideEvenly_TruncatesLikeIntegerDivision()
    {
        // (100 / 30) + 1 = 3 + 1 = 4. Valve's integer division truncates and the trailing section
        // is indexed off the truncated count, not off a rounded-up one.
        StudioAnimation.Section(frames: 100, sectionFrames: 30, frame: 99)
            .ShouldBe((4, 0));
    }
}
