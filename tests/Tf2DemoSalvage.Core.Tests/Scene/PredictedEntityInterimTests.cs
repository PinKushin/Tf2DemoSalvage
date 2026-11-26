using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What the point-of-view recorder's predicted entities are drawn at until B862 — an interim, not a conformance value.
/// </summary>
/// <remarks>
/// **Not a conformance suite on purpose** (B860 review 1, Fable). `GetInterpolationAmount` gives a predictable entity
/// one tick whatever the amount (`c_baseentity.cpp:5925-5928`), and these hold that the viewer takes that tick. But the
/// engine also moves such an entity's target to the predicted time and fills its history from prediction, not the
/// network (`BaseInterpolatePart1`, `:2853-2862`; `PostDataUpdate`, `:2593-2598`), which the viewer does not run: the
/// 590 below is no number an engine frame draws. B862 replaces these with values predicted from the engine. The controls
/// that ARE engine values (a weapon or player nobody predicts) are in <see cref="InterpolationRiseConformanceTests"/>.
/// </remarks>
public sealed class PredictedEntityInterimTests
{
    /// <summary>A soldier, entity 1, updated every tick at ten units a tick under the rise at 50, drawn at 60.</summary>
    /// <param name="recorder">The recording's local player, or null for none.</param>
    /// <returns>The soldier as `PlayersAt` draws him with no viewpoint.</returns>
    internal static ScenePlayer SoldierAt60(int? recorder)
    {
        ScenePropTrack soldier = new(entityIndex: 1, "models/player/soldier.mdl")
        {
            Interpolation = InterpolationRiseConformanceTests.RiseAtFifty(),
        };

        for (int tick = 0; tick <= 99; tick++)
        {
            soldier.Add(tick, new ScenePose { X = tick * 10f });
        }

        DemoTimeline timeline = DemoTimeline.ForPlayerTracks(
            [soldier], [new ScenePlayer(EntityIndex: 1, X: 0f, Y: 0f, Z: 0f, Team: 2, Health: 100, PlayerClass: 3)], recorder);
        List<ScenePlayer> players = [];

        timeline.PlayersAt(60d, players);

        return players.ShouldHaveSingleItem();
    }

    /// <summary>
    /// The recorder's own launcher (`CTFWeaponBase::ShouldPredict`, `tf_weaponbase.cpp:3433-3441`; `sv_client_predict` at its
    /// default -1 leaves `cl_predict` at the client's 1) aims one tick back, at 59: 590, across the rise that puts anyone
    /// else's at 360.
    /// </summary>
    [Test]
    public void PropsAt_TheRecordersOwnWeapon_IsOneTickBehindWhateverTheDelay()
    {
        DemoTimeline timeline = DemoTimeline.ForTracks([InterpolationRiseConformanceTests.Launcher(owner: 1)], recorder: 1);
        List<SceneProp> props = [];

        timeline.PropsAt(60, props);

        props.ShouldHaveSingleItem().Pose.X.ShouldBe(590f, 0.01f);
    }

    /// <summary>
    /// The recorder with no viewpoint for the frame (`C_BasePlayer::ShouldPredict` is `IsLocalPlayer()`,
    /// `c_baseplayer.cpp:2324-2333`) aims one tick back: 590.
    /// </summary>
    [Test]
    public void PlayersAt_TheRecorderWithNoViewpoint_IsOneTickBehind()
    {
        SoldierAt60(recorder: 1).X.ShouldBe(590f, 0.01f);
    }

    /// <summary>
    /// **Whoever asks where an entity is gets where it is drawn** (B860 review 13, Sonnet): the holiday lights and a sound's
    /// origin ask `GetAbsOrigin()`, the predicted one for the recorder and his weapons. The recorder at 60 is at 590, not
    /// the delayed 360; his launcher likewise; anyone else's launcher 360.
    /// </summary>
    [Test]
    public void DrawnPose_TheRecorderAndHisWeapon_AreAtTheirPredictedTick()
    {
        ScenePropTrack soldier = new(entityIndex: 1, "models/player/soldier.mdl")
        {
            Interpolation = InterpolationRiseConformanceTests.RiseAtFifty(),
        };

        for (int tick = 0; tick <= 99; tick++)
        {
            soldier.Add(tick, new ScenePose { X = tick * 10f });
        }

        ScenePropTrack mine = InterpolationRiseConformanceTests.Launcher(owner: 1);
        ScenePropTrack theirs = InterpolationRiseConformanceTests.Launcher(owner: 2);
        DemoTimeline timeline = DemoTimeline.ForTracks([soldier, mine, theirs], recorder: 1);

        new[]
        {
            timeline.DrawnPose(1, soldier, 60)!.Value.X,
            timeline.DrawnPose(mine.EntityIndex, mine, 60)!.Value.X,
            timeline.DrawnPose(theirs.EntityIndex, theirs, 60)!.Value.X,
        }.ShouldBe([590f, 590f, 360f], 0.01f);
    }
}
