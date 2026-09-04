using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The duck-jump correction reaches the skeleton, and only for a player it applies to (B314).
/// </summary>
/// <remarks>
/// **`DuckJumpConformanceTests` proves the ramp and says nothing about whether anything runs it.**
/// It calls `DuckJump.Update` directly with flags it chooses; nothing in it touches the path that
/// decides which entity is airborne, which is ducking, or whether the result reaches a bone. That
/// gap is where this project has shipped three no-ops with a green suite.
///
/// **The distinguishing input is the FLAGS**, since the correction exists only for a player who is
/// off the ground AND crouching. A prop with neither, and a player with only one, must come back at
/// zero — otherwise a wiring that applied it unconditionally would pass a test of the airborne case
/// alone.
/// </remarks>
public sealed class DuckJumpWiringTests
{
    [Test]
    public void Instances_ForAnAirborneCrouchingPlayer_LowersTheSkeleton()
    {
        EntityModelSet models = Posed(Cabinet(PlayerActivityState.Ducking));

        // Twenty units is the whole hull difference, which is what the first frame of a duck gets:
        // the ramp is 1 - 0/0.15 at the instant it begins.
        models.DuckJumpOffsetOf(9).ShouldNotBeNull()
            .ShouldBe(20f, 1e-3d, "the full hull difference at the moment of ducking in air");
    }

    /// <remarks>
    /// **The control that says the GROUND flag is read.** The engine's whole block is inside
    /// `if ( GetGroundEntity() == NULL )`, so a crouch on the ground is an ordinary animation — a
    /// wiring that ignored the ground state would sink every crouching player twenty units into the
    /// floor, which is a worse defect than the one being fixed.
    /// </remarks>
    [Test]
    public void Instances_ForACrouchingPlayerOnTheGround_LeavesItAlone()
    {
        EntityModelSet models = Posed(
            Cabinet(PlayerActivityState.Ducking | PlayerActivityState.OnGround));

        models.DuckJumpOffsetOf(9).ShouldNotBeNull().ShouldBe(0f);
    }

    /// <remarks>
    /// **The other control: airborne is not enough.** A rocket-jumping player who never crouches is
    /// off the ground for a long time, and correcting them would drag every jump twenty units down.
    /// </remarks>
    [Test]
    public void Instances_ForAnAirbornePlayerNotCrouching_LeavesItAlone()
    {
        EntityModelSet models = Posed(Cabinet(0));

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

    /// <summary>A prop carrying the given activity flags.</summary>
    private static SceneProp Cabinet(int flags) =>
        new(
            9,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = 0, Cycle = 0f, Flags = flags },
            null);

    private static PropModels.ModelFrames Frames()
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
