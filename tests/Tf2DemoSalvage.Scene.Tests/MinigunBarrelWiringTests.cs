using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The barrel spin reaches a bone, and only for a minigun that is wound up (B347).
/// </summary>
/// <remarks>
/// **`MinigunBarrelConformanceTests` proves the arithmetic and says nothing about whether anything
/// runs it.** It calls `Approach`, `Advance` and `Rotation` directly with values it chooses; nothing
/// in it touches the path that decides which entity is a minigun, which bone is the barrel, or
/// whether a rotation reaches the skeleton. That gap is where this project has shipped three no-ops
/// with a green suite, and B346 hit it one field earlier in the same week.
///
/// **The distinguishing inputs are the STATE and the bone NAME.** A minigun at `AC_STATE_IDLE` must
/// not spin, and a model with no bone called `barrel` must not be touched at all — otherwise a
/// wiring that spun everything unconditionally would satisfy a test of the spinning case alone.
///
/// **Time has to pass for an angle to exist.** `m_flBarrelAngle += velocity * frametime`, so a
/// single frame at the same instant produces a velocity and no rotation — which is correct and is
/// exactly what a test asserting on one frame would mistake for a broken wiring.
/// </remarks>
public sealed class MinigunBarrelWiringTests
{
    /// <summary>Entity slot the weapon occupies.</summary>
    private const int Weapon = 9;

    /// <summary><c>AC_STATE_SPINNING</c>.</summary>
    private const int Spinning = 3;

    /// <summary><c>AC_STATE_IDLE</c>.</summary>
    private const int Idle = 0;

    [Test]
    public void Instances_ForASpinningMinigun_TurnsItsBarrelBone()
    {
        EntityModelSet models = Posed(Minigun(Spinning), frames: 8);

        models.SpunBarrels.ShouldBeGreaterThan(0, "the barrel bone was written at all");

        models.FurthestBarrelAngle.ShouldBeGreaterThan(
            0f, "and it TURNED, which a count alone cannot say");
    }

    /// <remarks>
    /// **The control that says the state is read.** `WindDown` sets the target velocity to zero and
    /// `m_iWeaponState > AC_STATE_IDLE` is the engine's own test for "wound up"
    /// (<c>tf_weapon_minigun.cpp:806</c>) — so an idle minigun's barrel must sit still. A wiring
    /// that ignored the state would spin every minigun on the map for ever, including the ones
    /// lying on the ground.
    /// </remarks>
    [Test]
    public void Instances_ForAnIdleMinigun_LeavesItsBarrelStill()
    {
        EntityModelSet models = Posed(Minigun(Idle), frames: 8);

        models.FurthestBarrelAngle.ShouldBe(
            0f, "idle is not wound up, so nothing accumulates");
    }

    /// <remarks>
    /// **The control that says the BONE NAME is read.** Every weapon in the game reaches this code;
    /// only the ones whose model carries a bone called `barrel` may be touched, because that is what
    /// `LookupBone( "barrel" )` returns -1 for otherwise (<c>tf_weapon_minigun.cpp:1048</c>) and the
    /// override is wrapped in `if (m_iBarrelBone != -1)`.
    /// </remarks>
    [Test]
    public void Instances_ForAModelWithNoBarrelBone_WritesNothing()
    {
        EntityModelSet models = Posed(Minigun(Spinning), frames: 8, barrel: false);

        models.SpunBarrels.ShouldBe(
            0, "no bone is called 'barrel', so the engine's guard rejects the model");
    }

    /// <remarks>
    /// **A prop that is not a minigun at all**, which is nearly everything drawn. It sends no
    /// weapon state, so the step returns before it looks for a bone — asserted because the model
    /// here DOES have a barrel bone, which separates "no state" from "no bone".
    /// </remarks>
    [Test]
    public void Instances_ForAPropWithNoWeaponState_WritesNothing()
    {
        EntityModelSet models = Posed(Minigun(state: null), frames: 8);

        models.SpunBarrels.ShouldBe(0, "nothing said this entity was a minigun");
    }

    /// <summary>Poses the prop across several frames, so time passes.</summary>
    private static EntityModelSet Posed(SceneProp prop, int frames, bool barrel = true)
    {
        PropModels.ModelFrames model = Frames(barrel);
        EntityModelSet models = new() { Geometry = _ => model };

        List<SceneProp> drawn = [prop];

        models.Add(drawn, _ => model);

        // **Several frames a tenth of a second apart.** The velocity approaches its target by 0.1
        // per FRAME, so one frame reaches 0.1 and needs a second frame before any angle exists —
        // the engine's own per-frame acceleration, reproduced rather than smoothed.
        for (int frame = 0; frame < frames; frame++)
        {
            models.Instances(drawn, [], seconds: frame * 0.1d);
        }

        return models;
    }

    /// <summary>A minigun in the given state, or one that never says.</summary>
    private static SceneProp Minigun(int? state) =>
        new(
            Weapon,
            "models/weapons/c_models/c_minigun/c_minigun.mdl",
            SceneModelKind.Studio,
            new ScenePose { Sequence = 0, Cycle = 0f, MinigunState = state },
            null);

    private static PropModels.ModelFrames Frames(bool barrel)
    {
        PropModels.SkinnedModel model = barrel
            ? SyntheticSkinnedModel.WithBones("weapon_bone", "barrel")
            : SyntheticSkinnedModel.WithBones("weapon_bone", "grip");

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
