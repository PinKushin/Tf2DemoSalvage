using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A player's sequence is CHOSEN, and something has to do the choosing.
/// </summary>
/// <remarks>
/// **B279, and it is a wiring defect of the exact shape this project keeps meeting.**
/// `EntityModelSet.UpdateClientSideAnimations` existed, `PlayerAnimation.For` behind it was tested
/// on its own, `MomentScene`'s own remarks described the order — *"ours is the same:
/// `UpdateClientSideAnimations`, then `Instances`"* — and **nothing called it**. The call was lost
/// when the work moved out of `MainForm.ShowMoment` (B188); the method's doc still says it lives
/// there.
///
/// **The cost was every player's animation.** TF2 runs `CBasePlayerAnimState` on the client and
/// picks a sequence from an activity, so a player's `m_nSequence` off the wire is not the driving
/// value — it decodes to 0. Without the call every player held one pose while their position
/// interpolated, which the owner reported as *"they just kinda slide in the run pose"*.
///
/// **Every unit test still passed**, because they all called `PlayerAnimation.For` directly. This
/// one asserts on the drawn set after the pipeline has run, which is the only level the missing
/// call is visible at — `docs/memory/output-level-assertion-or-it-is-not-done.md`.
/// </remarks>
public sealed class ClientSideAnimationWiringTests
{
    [Test]
    public void UpdateClientSideAnimations_ForAMovingPlayer_ReplacesTheWireSequence()
    {
        EntityModelSet models = new();

        // A model with a run activity to find, which is what the state machine asks for.
        models.Add([Player(speed: 320f)], _ => Frames());

        List<SceneProp> drawn = [Player(speed: 320f)];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldNotBe(
            0,
            "a moving player's sequence is chosen from an activity, not taken from the wire, " +
            "where it decodes to zero and leaves them sliding in one pose");
    }

    /// <remarks>
    /// **The control, and it is what stops this from being a change-detector.** An entity with no
    /// speed is not a player being animated by `CBasePlayerAnimState` — a prop, a building, a
    /// door — and its sequence must be left exactly as the demo stated it. The engine's list is
    /// `g_ClientSideAnimationList`, which such an entity never joins.
    /// </remarks>
    [Test]
    public void UpdateClientSideAnimations_ForAnEntityWithNoSpeed_LeavesItsSequenceAlone()
    {
        EntityModelSet models = new();

        SceneProp prop = new(
            2,
            "models/props_gameplay/door_slide_door.mdl",
            ScenePropTrack.Classify("models/props_gameplay/door_slide_door.mdl"),
            new ScenePose { Sequence = 3 },
            null);

        models.Add([prop], _ => Frames());

        List<SceneProp> drawn = [prop];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldBe(
            3, "a server-animated entity takes its sequence off the wire and keeps it");
    }

    /// <remarks>
    /// **The one that would have caught B279, and the two above would not have.** They call
    /// `UpdateClientSideAnimations` themselves, so they pass whether or not anything in production
    /// does — which is exactly the state the repository was in: the method worked, its unit tests
    /// were green, and `MomentScene.Pose` never invoked it.
    ///
    /// This drives the scene the way the viewer does and reads the sequence off the drawn set
    /// afterwards. It fails the moment the call goes missing again.
    /// </remarks>
    [Test]
    public void Pose_ForAMovingPlayer_HasChosenASequenceByActivity()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp moving = Player(speed: 320f);

        scene.Build([], [moving], default);
        scene.Pose(default);

        scene.Drawn.ShouldContain(
            prop => prop.EntityIndex == moving.EntityIndex && prop.Pose.Sequence != 0,
            "MomentScene must run the client-side animation selection before posing; without it " +
            "every player holds the wire's sequence of zero and slides");
    }

    /// <summary>A player-shaped prop moving at a running speed.</summary>
    private static SceneProp Player(float speed) =>
        new(
            1,
            "models/player/scout.mdl",
            ScenePropTrack.Classify("models/player/scout.mdl"),
            new ScenePose
            {
                Sequence = 0,
                Speed = speed,
                Flags = PlayerActivityState.OnGround,
                Slot = "PRIMARY",
            },
            null);

    /// <summary>A skinned model carrying run and idle activities to choose between.</summary>
    private static PropModels.ModelFrames Frames() =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: SyntheticSkinnedModel.With(
                "a_reference_pose", "ACT_MP_RUN_PRIMARY", "ACT_MP_STAND_PRIMARY"));
}
