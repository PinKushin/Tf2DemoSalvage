using System.Collections.Generic;

using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The three per-bone scales, carried by an AUTHORED demo (B312).
/// </summary>
/// <remarks>
/// **The corpus cannot exercise this and never will.** Every recording in it is ordinary
/// competitive or pub play, and all three fields read exactly 1 there — 440 of 440 occurrences on
/// `z1800`. A field that defaults to 1 and multiplies a scale is invisible at its default, so no
/// demo of ordinary play can tell a working implementation from a missing one.
///
/// **So the specimen is authored rather than waited for** (`docs/memory/author-the-specimen-the-corpus-lacks.md`).
/// The writer is a test instrument: a demo carrying `m_flHeadScale` at 2 goes through the real
/// container, the real schema, the real entity decode and the real timeline, and comes out the far
/// end as a `ScenePlayer`. That is the difference between "correct by construction and citation"
/// and "observed on a demo".
///
/// **What this still does not cover, said plainly:** it observes the value reaching the scene, not
/// a head drawn twice the size. The bone arithmetic is pinned separately by
/// `PlayerBoneScaleConformanceTests` and the hop into the renderer by
/// `PlayerBoneScaleWiringTests`; a picture would need a person looking at one, which is the rule
/// for anything visual here.
/// </remarks>
public sealed class SyntheticBoneScaleTests
{
    [Test]
    public void Build_ADemoCarryingBoneScales_ReachesTheScenePlayer()
    {
        // Three DIFFERENT values, none of them 1: equal ones would let a carry into the wrong field
        // pass, and 1 is what every hop defaults to when the value is lost.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Blu),
                ["m_lifeState"] = PropertyValue.FromInt(0),
                ["m_flHeadScale"] = PropertyValue.FromFloat(2f),
                ["m_flTorsoScale"] = PropertyValue.FromFloat(0.5f),
                ["m_flHandScale"] = PropertyValue.FromFloat(1.5f),
            }));

        ScenePlayer player = timeline.PlayersAt(66).ShouldHaveSingleItem();

        player.HeadScale.ShouldBe(2f, 0.01f, "m_flHeadScale off the wire");
        player.TorsoScale.ShouldBe(0.5f, 0.01f, "m_flTorsoScale, and not the head's value");
        player.HandScale.ShouldBe(1.5f, 0.01f, "m_flHandScale, and not either of the others");
    }

    /// <remarks>
    /// **The control, and it is the engine's default rather than a fallback.** `C_TFPlayer`
    /// initialises all three to 1 (`c_tf_player.cpp:577`), so a demo that never sends them is one
    /// where TF2 would also have used 1. A reader defaulting to 0 would collapse the model; one
    /// defaulting to the last player's value would be worse.
    /// </remarks>
    [Test]
    public void Build_ADemoSendingNoBoneScales_LeavesThemAtOne()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.Demo(
            new Dictionary<string, PropertyValue>
            {
                ["m_vecOrigin"] = PropertyValue.FromVectorXY(0f, 0f),
                ["m_vecOrigin[2]"] = PropertyValue.FromFloat(0f),
                ["m_iTeamNum"] = PropertyValue.FromInt(SceneTeams.Blu),
                ["m_lifeState"] = PropertyValue.FromInt(0),
            }));

        ScenePlayer player = timeline.PlayersAt(66).ShouldHaveSingleItem();

        player.HeadScale.ShouldBe(1f);
        player.TorsoScale.ShouldBe(1f);
        player.HandScale.ShouldBe(1f);
    }
}
