using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What the scene layer keeps from a decoded entity, and what it silently drops.
/// </summary>
/// <remarks>
/// **`NetworkedProperties` is a whitelist, so anything missing from it is discarded before any
/// consumer could ask for it.** That makes it the first place to look when a field "is decoded but
/// never arrives" — the decode is fine and the filter is the loss.
///
/// **This class exists because `m_nSkin` was missing from it for a month.** The renderer reads
/// <c>prop.Pose.Skin</c>, <c>ScenePropTrack</c> copies <c>Skin</c> through its clone, and a comment
/// beside that copy explains at length why losing a skin draws every entity in family zero. All of
/// it downstream of a value that was never retained, so <c>Skin</c> was structurally always 0.
///
/// **Third instance of one shape, and the third happened inside the fix for the second.** A record
/// built field by field, one field forgotten, and a default that is also a legitimate value so
/// nothing can report it: `ScenePlayer.Yaw`, then `ScenePose.Body`, now `ScenePose.Skin`. The fix
/// for `Body` added `Skin` to the CLONE and not to the CONSTRUCTION.
///
/// So the assertion is on the list rather than on one field. A test naming only `m_nSkin` would have
/// been satisfied by the same partial fix that caused this.
/// </remarks>
public sealed class RetainedPropertyTests
{
    /// <summary>The table every drawable animating entity sends its appearance on.</summary>
    private const string AnimatingTable = "DT_BaseAnimating";

    [Test]
    public void TheAnimatingTableKeepsEverythingThePoseIsBuiltFrom()
    {
        // Each name here is read by ScenePose construction in DemoTimeline. The list is asserted
        // whole so that adding a field to the pose without retaining it — or retaining one and
        // forgetting to read it — shows up as a difference rather than as a silent zero.
        //
        // m_nSkin is the reason this test exists. It is sent by DT_BaseAnimating
        // (c_baseanimating.cpp:176, RecvPropInt(RECVINFO(m_nSkin))) and was not retained, so TF2's
        // team colouring — which is two skin families of one model, not a tint — could never reach
        // the renderer.
        IReadOnlyDictionary<string, IReadOnlyList<string>> retained =
            EntityState.NetworkedProperties;

        retained.ShouldContainKey(AnimatingTable);

        retained[AnimatingTable].ShouldBe(
            [
                "m_nSequence", "m_nBody", "m_flCycle", "m_flPlaybackRate",
                "m_flModelScale", "m_nSkin",
            ],
            ignoreOrder: true);
    }
}
