using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests;

/// <summary>
/// A point-of-view recording's player carries its 3D sky fog once <c>dem_stringtables</c> is read (B452).
/// </summary>
/// <remarks>
/// **The output-level half of B452.** The block's reader and its routing are unit-tested
/// synthetically; only a real recording proves the player baseline it carries is what was missing.
/// The 2013 badlands POV sends <c>m_skybox3d.fog.enable 1</c> only through that baseline — before
/// the fix the timeline had no sky fog at all.
/// </remarks>
public sealed class PovSkyFogCorpusTests
{
    [Test]
    public void SkyFogAt_APointOfViewRecordingStartedMidMatch_IsTheSkyboxsFog()
    {
        DemoTimeline timeline = TimelineCache.For(
            Corpus.Demo("tf2-2013-build1729296-pov-cp_badlands"));

        // The last state the recording reached: the baseline arrives once, before the first packet.
        timeline.SkyFogAt(int.MaxValue).ShouldNotBeNull(
            "the player's m_skybox3d rides the CTFPlayer instance baseline only dem_stringtables carries");
    }
}
