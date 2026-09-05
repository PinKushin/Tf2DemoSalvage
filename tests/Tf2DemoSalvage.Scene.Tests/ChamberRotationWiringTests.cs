using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The chamber rotation reaches a bone, and only for a launcher that is turning (B348).
/// </summary>
/// <remarks>
/// **`ChamberRotationConformanceTests` proves the spline and says nothing about whether anything
/// runs it.** It calls `Spline`, `Degrees` and `Angle` directly with values it chooses; nothing in
/// it touches the path that decides which entity is a grenade launcher, which bone is the chamber,
/// or whether an angle reaches the skeleton.
///
/// **The distinguishing input is the pair of tube numbers.** Equal tubes mean a settled chamber,
/// which still gets its bone written — at the base angle of the tube it sits on, because the
/// override is outside the `m_iGoalTube != m_iCurrentTube` test. Different tubes mean an animation
/// is running. A wiring that ignored the pair would either freeze every chamber at zero or spin
/// every one for ever.
/// </remarks>
public sealed class ChamberRotationWiringTests
{
    /// <summary>Entity slot the weapon occupies.</summary>
    private const int Weapon = 9;

    [Test]
    public void Instances_ForATurningChamber_WritesItsBone()
    {
        EntityModelSet models = Posed(Launcher(current: 0, goal: 1, startedAt: 0d), at: 0.1d);

        models.TurnedChambers.ShouldBeGreaterThan(0, "the chamber bone was written");

        models.FurthestChamberAngle.ShouldBeGreaterThan(
            0f, "and it TURNED, which a count alone cannot say");
    }

    /// <remarks>
    /// **A settled chamber on tube 3 sits at 180 degrees, not at zero.** The base angle is
    /// `60.0f * m_iCurrentTube` (<c>tf_weapon_grenadelauncher.cpp:679</c>) and the override writes
    /// the bone regardless of whether an animation is running — so a wiring that only acted while
    /// turning would snap the chamber back to its first tube the instant each rotation finished.
    ///
    /// **Sampled INSIDE the 0.2666-second window, and that is the whole condition.** Written first
    /// at one second — past the animation's end — it could not fail: `Degrees` short-circuits on
    /// `fraction >= 1` and returns zero whatever `turning` says, so forcing the guard true changed
    /// nothing observable. At 0.1s a settled chamber and a turning one predict different angles,
    /// which is what makes this an experiment rather than a restatement.
    /// </remarks>
    [Test]
    public void Instances_ForASettledChamber_StillWritesItsTubesBaseAngle()
    {
        EntityModelSet models = Posed(Launcher(current: 3, goal: 3, startedAt: 0d), at: 0.1d);

        models.TurnedChambers.ShouldBeGreaterThan(0);

        models.FurthestChamberAngle.ShouldBe(
            180f * System.MathF.PI / 180f,
            1e-4f,
            "three tubes at sixty degrees each, and NO partial rotation — a settled chamber does "
            + "not run the spline even while a window would still be open");
    }

    /// <remarks>
    /// **The control that says the BONE NAME is read.** Every weapon reaches this code; only a model
    /// carrying `procedural_chamber` may be touched, which is what `LookupBone` returns -1 for
    /// otherwise (<c>tf_weapon_grenadelauncher.cpp:602</c>).
    /// </remarks>
    [Test]
    public void Instances_ForAModelWithNoChamberBone_WritesNothing()
    {
        EntityModelSet models = Posed(
            Launcher(current: 0, goal: 1, startedAt: 0d), at: 0.1d, chamber: false);

        models.TurnedChambers.ShouldBe(0);
    }

    /// <remarks>
    /// A prop that is not a grenade launcher sends no tubes, so the step returns before it looks
    /// for a bone — asserted against a model that DOES have the bone, which separates "no tubes"
    /// from "no bone".
    /// </remarks>
    [Test]
    public void Instances_ForAPropWithNoTubes_WritesNothing()
    {
        EntityModelSet models = Posed(Launcher(tubes: null), at: 0.1d);

        models.TurnedChambers.ShouldBe(0);
    }

    /// <remarks>
    /// **Past the animation's end the tube has advanced**, which the engine does locally
    /// (<c>:687</c>). At tube 0 heading for tube 1, a second later the chamber must read 60
    /// degrees — the goal's base angle — rather than holding 0 until the wire restates it.
    /// </remarks>
    [Test]
    public void Instances_AfterTheRotationEnds_ReadsTheGoalTubesAngle()
    {
        EntityModelSet models = Posed(Launcher(current: 0, goal: 1, startedAt: 0d), at: 1d);

        models.FurthestChamberAngle.ShouldBe(
            60f * System.MathF.PI / 180f,
            1e-4f,
            "the rotation is over, so the chamber has arrived at its goal");
    }

    /// <summary>Poses the prop once, at the given demo time.</summary>
    private static EntityModelSet Posed(SceneProp prop, double at, bool chamber = true)
    {
        PropModels.ModelFrames model = Frames(chamber);
        EntityModelSet models = new() { Geometry = _ => model };

        List<SceneProp> drawn = [prop];

        models.Add(drawn, _ => model);
        models.Instances(drawn, [], seconds: at);

        return models;
    }

    /// <summary>A launcher whose chamber is where and when it says.</summary>
    private static SceneProp Launcher(int current, int goal, double startedAt) =>
        Launcher((current, goal, startedAt));

    /// <summary>A launcher, or a prop that never says it is one.</summary>
    private static SceneProp Launcher((int Current, int Goal, double StartedSeconds)? tubes) =>
        new(
            Weapon,
            "models/weapons/c_models/c_grenadelauncher/c_grenadelauncher.mdl",
            SceneModelKind.Studio,
            new ScenePose { Sequence = 0, Cycle = 0f, Chamber = tubes },
            null);

    private static PropModels.ModelFrames Frames(bool chamber)
    {
        PropModels.SkinnedModel model = chamber
            ? SyntheticSkinnedModel.WithBones("weapon_bone", "procedural_chamber")
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
