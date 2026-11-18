using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The duck-jump correction reaches the skeleton, and only for a player it applies to (B314, B768).
/// </summary>
/// <remarks>
/// **`DuckJumpConformanceTests` proves the ramp and says nothing about whether anything runs it.**
/// It calls `DuckJump.Update` directly with the inputs it chooses; nothing in it touches the path that
/// decides which entity has no ground, which is ducking, or whether the result reaches a bone. That
/// gap is where this project has shipped three no-ops with a green suite.
///
/// **The distinguishing inputs are the DUCK flag and the GROUND ENTITY** (B768): the engine's block is
/// `if ( GetGroundEntity() == NULL )` (c_tf_player.cpp:8764), the handle the client holds, not `FL_ONGROUND`. A remote
/// player's handle is what the wire left — a stale baseline worldspawn in a point-of-view recording, measured on
/// `tf2-live-pov-cp_process_final-bots-2026-11-05` — so the two disagree, and each case below sets them apart.
/// </remarks>
public sealed class DuckJumpWiringTests
{
    private const int Worldspawn = 0;

    [Test]
    public void Instances_ForACrouchingPlayerWithNoGroundEntity_LowersTheSkeleton()
    {
        EntityModelSet models = Posed(Cabinet(PlayerActivityState.Ducking, groundEntity: null));

        // Twenty units is the whole hull difference, which is what the first frame of a duck gets:
        // the ramp is 1 - 0/0.15 at the instant it begins.
        models.DuckJumpOffsetOf(9).ShouldNotBeNull()
            .ShouldBe(20f, 1e-3d, "the full hull difference at the moment of ducking with no ground entity");
    }

    /// <remarks>
    /// **The ground ENTITY, not the ground flag** (B768): a remote player in a point-of-view recording holds his
    /// handle as the wire last left it, which may be none while `FL_ONGROUND` (networked to everyone) says standing — and
    /// TF2 then dips him for 0.15 s on the crouch. A gate on the flag would leave him alone.
    /// </remarks>
    [Test]
    public void Instances_ForACrouchingPlayerFlaggedOnTheGroundButHoldingNoGroundEntity_LowersTheSkeleton()
    {
        EntityModelSet models = Posed(
            Cabinet(PlayerActivityState.Ducking | PlayerActivityState.OnGround, groundEntity: null));

        models.DuckJumpOffsetOf(9).ShouldNotBeNull().ShouldBe(20f, 1e-3d);
    }

    /// <remarks>
    /// **And the converse**: a remote player whose held handle is the baseline's worldspawn, crouching in the air, is
    /// left alone — `GetGroundEntity()` is not NULL — where a gate on the flag would lower him.
    /// </remarks>
    [Test]
    public void Instances_ForACrouchingPlayerInTheAirHoldingAGroundEntity_LeavesItAlone()
    {
        EntityModelSet models = Posed(Cabinet(PlayerActivityState.Ducking, groundEntity: Worldspawn));

        models.DuckJumpOffsetOf(9).ShouldNotBeNull().ShouldBe(0f);
    }

    /// <remarks>
    /// **The control: a crouch on the ground is an ordinary animation.** Flag and handle agree, and a wiring that
    /// ignored the ground would sink every crouching player twenty units into the floor.
    /// </remarks>
    [Test]
    public void Instances_ForACrouchingPlayerOnTheGround_LeavesItAlone()
    {
        EntityModelSet models = Posed(
            Cabinet(PlayerActivityState.Ducking | PlayerActivityState.OnGround, groundEntity: Worldspawn));

        models.DuckJumpOffsetOf(9).ShouldNotBeNull().ShouldBe(0f);
    }

    /// <remarks>
    /// **The other control: no ground entity is not enough.** A rocket-jumping player who never crouches is
    /// off the ground for a long time, and correcting them would drag every jump twenty units down.
    /// </remarks>
    [Test]
    public void Instances_ForAPlayerWithNoGroundEntityNotCrouching_LeavesItAlone()
    {
        EntityModelSet models = Posed(Cabinet(0, groundEntity: null));

        models.DuckJumpOffsetOf(9).ShouldNotBeNull().ShouldBe(0f);
    }

    /// <summary>Builds a scene with the one prop and poses it.</summary>
    private static EntityModelSet Posed(SceneProp prop)
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [prop];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 0d);

        return models;
    }

    /// <summary>A prop carrying the given activity flags and held ground entity.</summary>
    internal static SceneProp Cabinet(int flags, int? groundEntity = null) =>
        new(
            9,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = 0, Cycle = 0f, Flags = flags, GroundEntity = groundEntity },
            null);

    internal static PropModels.ModelFrames Frames()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("root");

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, 1, 0f),
            },
            [0],
            [true],
            Skinned: model);
    }
}
