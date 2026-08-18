using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// Players look up and down, which aims the torso rather than tipping the body.
/// </summary>
/// <remarks>
/// **<c>ComputePoseParam_AimPitch</c> is one line** — <c>SetPoseParameter( m_iAimPitch,
/// -flAimPitch )</c>, where <c>flAimPitch</c> is the eye pitch and <c>m_iAimPitch</c> is looked up
/// by the name <c>body_pitch</c> (<c>multiplayer_animstate.cpp:1421</c>). Until now that parameter
/// sat at zero for every player, so nobody in a recording ever looked anywhere but level — which is
/// most visible on a sniper, and on anyone shooting at a rocket-jumping soldier.
///
/// **Kept apart from the pose's own <c>Pitch</c>, which stays zero.** That one rotates the whole
/// model, and a player stands upright however far the eyes are pitched — tipping the body by the
/// view lays them on their side every time they look up.
/// </remarks>
public sealed class CorpusAimPitchTests
{
    private const string MovementDemo = "movement-test-stv-cp_process";

    [Test]
    public void ARecordingCarriesWhereEachPlayerIsLooking()
    {
        string path = Corpus.Demo(MovementDemo);

        List<ScenePlayer> players =
        [
            .. DemoTimeline.Build(File.ReadAllBytes(path))
                .Frames
                .SelectMany(frame => frame.Players)
                .Where(player => player.IsPlaying),
        ];

        List<float> pitches =
        [
            .. players.Where(player => player.EyePitch is not null)
                .Select(player => player.EyePitch!.Value),
        ];

        pitches.ShouldNotBeEmpty("eye angles are sent for every player in the PVS");

        // **Both directions, because a single sign would pass against a reader that dropped one.**
        // The owner rocket-jumps in this recording, which means looking down at the floor, and
        // looks up on the way; a recording with only one would not distinguish a working reader
        // from one clamped at zero.
        pitches.ShouldContain(pitch => pitch > 5f, "somebody looks down");
        pitches.ShouldContain(pitch => pitch < -5f, "and somebody looks up");

        // **Within the range the engine's own parameter spans.** body_pitch runs −45 to 90 in the
        // player models, and a value far outside that would mean the angle was decoded at the wrong
        // scale — the wire sends these quantised, and a wrong bit width reads as a plausible number.
        pitches.Max().ShouldBeLessThanOrEqualTo(90f);
        pitches.Min().ShouldBeGreaterThanOrEqualTo(-90f);
    }

    // **A second test was written here and removed rather than weakened**, and what it was for is
    // still uncovered. It tried to assert that the eye pitch never reaches the pose's own Pitch —
    // the field that rotates the whole model — because a reader that assigned it to the wrong one
    // would satisfy everything above and lay every looking-up player on their back.
    //
    // It could not be written at this level. Players are not props in the timeline: PropsAt returns
    // entity props, and a player becomes a prop only in the viewer, so there is no artefact here
    // carrying both a player and a model rotation. Two attempts failed for two different reasons —
    // the first asserted that only brush entities carry a pitch and was falsified by
    // comp_win_banner_scaled.mdl at 14.9 degrees, which is a prop the mapper genuinely tilted, and
    // the second found no player models at all.
    //
    // The separation is structural for now: ScenePlayer carries EyePitch, and the viewer's pose
    // construction sets Yaw and never Pitch. Covering it properly needs that construction extracted
    // from MainForm, which is a change worth making for its own sake rather than for a test.
}
