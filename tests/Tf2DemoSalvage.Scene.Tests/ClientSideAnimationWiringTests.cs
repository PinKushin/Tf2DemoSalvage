using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A player's sequence is CHOSEN, and something has to do the choosing.
/// </summary>
/// <remarks>
/// **Written during B279 on a diagnosis that turned out to be WRONG, and kept because the guard is
/// right.** The claim was that `EntityModelSet.UpdateClientSideAnimations` had no caller — a grep
/// appeared to show it, a duplicate call was added to `MomentScene.Pose`, and then the grep turned
/// out to have been truncated by `head -6`: the real call is the seventh line, in
/// `MomentScene.Build` at the point its own comment describes, *"Valve's own pass, under Valve's
/// own name"*. The duplicate was reverted and `MomentScene` is unchanged.
///
/// **What these tests actually pin is that the call stays where it is.** TF2 runs
/// `CBasePlayerAnimState` on the client and picks a player's sequence from an activity, so a
/// player's `m_nSequence` off the wire is not the driving value — it decodes to 0 — and if the
/// call were ever lost in a move (it has moved once already, out of `MainForm.ShowMoment` in
/// B188) every player would hold one pose while their position interpolated. Nothing else in the
/// suite would notice: every other test of this path calls `PlayerAnimation.For` directly.
///
/// The stepping the owner saw was B279 itself — the missing inter-frame fraction — not this.
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
    /// **The only one of the three that tests the WIRING.** The two above call
    /// `UpdateClientSideAnimations` themselves, so they pass whether or not anything in production
    /// does. This one drives the scene the way the viewer does — `Build`, then `Pose` — and reads
    /// the sequence off the drawn set afterwards, so it fails the moment the call is lost from
    /// `MomentScene.Build`. It has not been lost; this exists so that a future move cannot lose it
    /// silently, the way B188's move was briefly and wrongly believed to have.
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
