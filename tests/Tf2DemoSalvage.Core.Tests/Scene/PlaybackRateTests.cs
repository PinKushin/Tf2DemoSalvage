using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The third factor in Valve's cycle advance, which this project decoded and never used.
/// </summary>
/// <remarks>
/// **`c_baseanimating.cpp:5493`:**
///
/// <code>
/// float addcycle = flInterval * cyclerate * m_flPlaybackRate;
/// </code>
///
/// This project implements <c>cyclerate</c> — <c>StudioAnimation.CyclesPerSecond</c>, added because
/// without it every prop reported cycle zero forever — and advances with
/// <c>cycle + seconds * cyclerate</c>. **The playback rate is simply absent from the multiplication**,
/// so anything not playing at rate 1 animates at the wrong speed.
///
/// **Everything needed was already in place**, which is the same shape as the skin defect a few
/// commits ago: <c>m_flPlaybackRate</c> is in the retained-property whitelist,
/// <c>EntityState.PlaybackRate()</c> exists, and it has a unit test. The only consumer was that
/// test — no production code read it, so a property was decoded, kept and discarded.
///
/// Found by auditing the nullable accessors for invented unknowns. This one's null handling is
/// correct; what the audit turned up is that nothing called it at all.
/// </remarks>
public sealed class PlaybackRateTests
{
    [Test]
    public void PlaybackRate_APoseNeverToldARate_PlaysAtNormalSpeed()
    {
        // The engine's default is 1, not 0 — a rate of zero would freeze the animation, which is
        // the wrong reading of "the demo never mentioned it". Valve sets 1 explicitly in several
        // places, e.g. basecombatweapon_shared.cpp:1058.
        ScenePose pose = new();

        pose.PlaybackRate.ShouldBe(1f);
    }

    [Test]
    public void PlaybackRate_TheRate_IsCarriedThroughARebuild()
    {
        // **The completeness hazard this project keeps hitting**, and the reason this assertion
        // exists at all: ScenePose is rebuilt field by field when a pose is interpolated, and Body,
        // Skin and Yaw have each fallen off that list. A rate that silently reverted to 1 between
        // keyframes would be the same defect wearing a new name.
        ScenePropTrack track = new(entityIndex: 1, modelPath: "a.mdl", serialNumber: 1);

        track.Add(0, new ScenePose { PlaybackRate = 2f, Cycle = 0.0f });
        track.Add(10, new ScenePose { PlaybackRate = 2f, Cycle = 0.2f });

        track.At(5).ShouldNotBeNull().PlaybackRate.ShouldBe(2f);
    }
}
