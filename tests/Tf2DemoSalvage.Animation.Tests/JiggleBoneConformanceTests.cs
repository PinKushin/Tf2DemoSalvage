using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A jiggle bone swings on a spring — <c>CJiggleBones::BuildJiggleTransformations</c>.
/// </summary>
/// <remarks>
/// **<c>jigglebones.cpp:60</c>**, reached from <c>BuildTransformations</c> for a bone carrying both
/// <c>BONE_ALWAYS_PROCEDURAL</c> and <c>STUDIO_PROC_JIGGLE</c> (<c>c_baseanimating.cpp:1545</c>).
/// It is the only procedural bone rule TF2 uses: measured across two demos, every bone with a
/// <c>proctype</c> is a jiggle bone and none of the four rules `CalcProceduralBone` implements
/// appears at all.
///
/// **The tests below predict from Valve's arithmetic rather than from the port.** A first step at
/// gravity alone is <c>v = -tipMass * dt</c> and <c>p = tipPos + v * dt</c>, which is one line of
/// algebra and is wrong in a different way than a mis-ported branch would be.
///
/// **The frame counters make a naive test lie, and they are why several cases here step twice.** A
/// new bone starts with `useJiggleBoneCount = 16`, so the first sixteen calls simulate whatever the
/// step size — but the very first call also creates the state at the goal, so nothing has moved yet
/// and a single-call test measures the seed rather than the physics.
/// </remarks>
public sealed class JiggleBoneConformanceTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-4;

    /// <summary>A step inside the framerate cutoff, so the simulation actually runs.</summary>
    private const float Step = 0.01f;

    [Test]
    public void Build_AFlexibleBoneUnderGravity_MovesItsTipDown()
    {
        // **Gravity is the one term in GLOBAL space**: `data->tipAccel.z -= jiggleInfo->tipMass`,
        // before any local decomposition. With every stiffness at zero the tip is in free fall, so
        // after one integrated step of dt the velocity is -tipMass*dt and the position has moved
        // -tipMass*dt*dt.
        JiggleBones bones = new();

        StudioJiggleBone jiggle = Spring() with { TipMass = 100f };

        float[] goal = Goal();
        float[] into = new float[12];

        bones.Build(0, 0f, jiggle, goal, into, flipped: false);
        bones.Build(0, Step, jiggle, goal, into, flipped: false);

        // The bone points along X, so its forward axis has no Z part until the tip falls.
        into[10].ShouldBeLessThan(
            0f, "a tip in free fall drags the bone's forward axis below the horizontal");
    }

    /// <remarks>
    /// **The control, and it is the case that separates gravity from a mis-seeded state.** With
    /// tipMass zero nothing accelerates, so the bone must sit exactly on its goal however many steps
    /// run. Without this, "gravity moved it" and "the state was seeded wrong" look the same.
    /// </remarks>
    [Test]
    public void Build_AFlexibleBoneWithNoMass_StaysOnItsGoal()
    {
        JiggleBones bones = new();

        StudioJiggleBone jiggle = Spring() with { TipMass = 0f };

        float[] goal = Goal();
        float[] into = new float[12];

        bones.Build(0, 0f, jiggle, goal, into, flipped: false);
        bones.Build(0, Step, jiggle, goal, into, flipped: false);
        bones.Build(0, Step * 2f, jiggle, goal, into, flipped: false);

        into[2].ShouldBe(1f, Tolerance, "nothing accelerates it, so it stays pointing along X");
        into[6].ShouldBe(0f, Tolerance);
        into[10].ShouldBe(0f, Tolerance);
    }

    [Test]
    public void Build_ABoneWithNoFlexAtAll_TakesTheGoalMatrixUnchanged()
    {
        // "no flex at all - just use goal matrix" (`jigglebones.cpp:759`). A bone with none of
        // FLEXIBLE, RIGID, BASE_SPRING or BOING is a jiggle bone that does nothing, and the engine
        // says so by copying the goal rather than by skipping the call.
        JiggleBones bones = new();

        StudioJiggleBone jiggle = Spring() with { Flags = 0 };

        float[] goal = Goal();
        float[] into = new float[12];

        bones.Build(0, 0f, jiggle, goal, into, flipped: false);
        bones.Build(0, Step, jiggle, goal, into, flipped: false);

        into.ShouldBe(goal, "an inert jiggle bone is its goal matrix");
    }

    [Test]
    public void Build_BelowTheFramerateCutoff_UsesTheGoalMatrix()
    {
        // **`cl_jiggle_bone_framerate_cutoff` is 20 frames a second**, so a step longer than a
        // twentieth of a second skips the simulation — Euler integration over that long a step
        // makes the spring explode, which the cvar's own help string says.
        //
        // **The first calls are exempt and there are more of them than the obvious sixteen.** A new
        // bone starts at `useJiggleBoneCount = 16`, but the first below-cutoff call takes the `else`
        // that sets it to 32 — so the counter has to run down from THIRTY-TWO before the cutoff can
        // take effect. Twenty calls measured nothing and the test failed against a correct port.
        JiggleBones bones = new();

        StudioJiggleBone jiggle = Spring() with { TipMass = 500f };

        float[] goal = Goal();
        float[] into = new float[12];

        float now = 0f;

        for (int call = 0; call < 40; call++)
        {
            now += 0.2f;
            bones.Build(0, now, jiggle, goal, into, flipped: false);
        }

        into.ShouldBe(
            goal,
            "a fifth of a second is below the twenty-frame cutoff, so the goal matrix is used");
    }

    [Test]
    public void Build_WithALengthConstraint_HoldsTheTipOneLengthOut()
    {
        // `data->tipPos = goalBasePosition + jiggleInfo->length * forward` — the constraint is
        // applied to the POSITION rather than to the spring, so however far gravity drags the tip it
        // ends the step exactly one length from the base. The bone's forward axis is then a unit
        // vector by construction, which is what this measures.
        JiggleBones bones = new();

        StudioJiggleBone jiggle = Spring() with
        {
            TipMass = 400f,
            Flags = StudioJiggleFlags.Flexible | StudioJiggleFlags.LengthConstraint,
        };

        float[] goal = Goal();
        float[] into = new float[12];

        bones.Build(0, 0f, jiggle, goal, into, flipped: false);

        for (int call = 1; call <= 5; call++)
        {
            bones.Build(0, Step * call, jiggle, goal, into, flipped: false);
        }

        // **The TIP, not the matrix, and that correction is the point of this test.** The first
        // version measured the length of the matrix's forward axis — which is normalised one line
        // BEFORE the constraint runs, so it is a unit vector whether the constraint fires or not.
        // Deleting the constraint's own reprojection left the whole suite green. A proxy unfaithful
        // to the variable, caught by sabotage rather than by reading.
        bones.TipOf(0, out System.Numerics.Vector3 tip).ShouldBeTrue();

        tip.Length().ShouldBe(
            jiggle.Length,
            (float)Tolerance,
            "the constraint holds the tip exactly one length from the base");

        into[10].ShouldBeLessThan(0f, "and gravity has swung it below the horizontal");
    }

    [Test]
    public void Build_WithAnAngleConstraint_KeepsTheTipInsideItsLimit()
    {
        // `if (angleBetween > jiggleInfo->angleLimit)` pulls the tip back onto the limit cone. A
        // thirty-degree limit under heavy gravity must leave the forward axis at least cos(30°)
        // along the goal, where the same bone without the limit falls much further.
        StudioJiggleBone limited = Spring() with
        {
            TipMass = 900f,
            AngleLimit = MathF.PI / 6f,
            Flags = StudioJiggleFlags.Flexible |
                StudioJiggleFlags.LengthConstraint |
                StudioJiggleFlags.AngleConstraint,
        };

        StudioJiggleBone free = limited with
        {
            Flags = StudioJiggleFlags.Flexible | StudioJiggleFlags.LengthConstraint,
        };

        // The dot product of the bone's forward axis with its goal's, which for a goal along X is
        // simply the X component. cos(30 degrees) is the floor a thirty-degree cone allows.
        float constrained = ForwardAlongGoal(limited);
        float unconstrained = ForwardAlongGoal(free);

        constrained.ShouldBeGreaterThan(
            unconstrained, "the angle limit stops the tip swinging as far");

        constrained.ShouldBeGreaterThan(
            MathF.Cos(MathF.PI / 6f) - 0.001f, "and holds it within thirty degrees of the goal");
    }

    [Test]
    public void Build_AfterALongGap_ReseedsRatherThanIntegratingIt()
    {
        // "if frames have been skipped since our last update, we were likely disabled and
        // re-enabled, so re-init" — half a second. Without it, a viewer that scrubs forward would
        // integrate the whole gap in one Euler step and fling the bone away.
        JiggleBones bones = new();

        StudioJiggleBone jiggle = Spring() with
        {
            TipMass = 900f,
            Flags = StudioJiggleFlags.Flexible | StudioJiggleFlags.LengthConstraint,
        };

        float[] goal = Goal();
        float[] into = new float[12];

        bones.Build(0, 0f, jiggle, goal, into, flipped: false);

        for (int call = 1; call <= 5; call++)
        {
            bones.Build(0, Step * call, jiggle, goal, into, flipped: false);
        }

        float swung = into[10];

        // A ten-second gap, then one ordinary step.
        bones.Build(0, 10f, jiggle, goal, into, flipped: false);
        bones.Build(0, 10f + Step, jiggle, goal, into, flipped: false);

        into[10].ShouldBeGreaterThan(
            swung, "the gap re-seeded the bone at its goal instead of integrating ten seconds");
    }

    /// <summary>How much of the bone's forward axis still lies along the goal after falling.</summary>
    private static float ForwardAlongGoal(StudioJiggleBone jiggle)
    {
        JiggleBones bones = new();

        float[] goal = Goal();
        float[] into = new float[12];

        bones.Build(0, 0f, jiggle, goal, into, flipped: false);

        // **Twenty steps, because six did not fall far enough for the limit to fire.** At six the
        // unconstrained bone was eleven degrees off its goal, well inside a thirty-degree cone — so
        // both fixtures returned the identical number and the comparison could not fail. Effect size
        // below the resolution of the condition, and the fix is a longer fall rather than a looser
        // assertion.
        for (int call = 1; call <= 20; call++)
        {
            bones.Build(0, Step * call, jiggle, goal, into, flipped: false);
        }

        return into[2];
    }

    /// <summary>A goal matrix at the origin pointing along world X, with world Z as its up.</summary>
    /// <remarks>
    /// **Forward is column two and that is not the obvious column.** `MatrixGetColumn( goalMX, 2,
    /// goalForward )` — Valve's comment on the whole branch says the bone is assumed to lie along
    /// its own Z, so column two of a bone matrix is where it points. A fixture built with forward in
    /// column zero would be measuring the left axis and every prediction below would be about the
    /// wrong vector.
    ///
    /// **The bone points along world X and NOT along world Z, which the first version got wrong.**
    /// Gravity is `tipAccel.z -= tipMass`, so a bone already pointing along Z is pulled exactly
    /// along its own length: the tip gets closer to the base and the direction never changes, so
    /// every test measuring a tilt passed a normalised vector that had not moved. Correct and broken
    /// predict the same observation — the wrong-condition trap, and the fix is the input rather than
    /// the assertion.
    ///
    /// Left is world Y and up is world Z, so left cross up is X and the basis is right-handed.
    /// </remarks>
    private static float[] Goal() =>
    [
        0f, 0f, 1f, 0f,
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
    ];

    /// <summary>A flexible bone with a length, no stiffness, and no constraints.</summary>
    /// <remarks>
    /// **Every stiffness and damping at zero on purpose.** A spring pulling the tip back is a second
    /// force acting at the same time, and separating "gravity moved it" from "the spring returned
    /// it" needs one of the two switched off. Each test below turns on only what it measures.
    /// </remarks>
    private static StudioJiggleBone Spring() =>
        new(
            Flags: StudioJiggleFlags.Flexible,
            Length: 10f,
            TipMass: 0f,
            YawStiffness: 0f,
            YawDamping: 0f,
            PitchStiffness: 0f,
            PitchDamping: 0f,
            AlongStiffness: 0f,
            AlongDamping: 0f,
            AngleLimit: 0f,
            MinYaw: 0f,
            MaxYaw: 0f,
            YawFriction: 0f,
            YawBounce: 0f,
            MinPitch: 0f,
            MaxPitch: 0f,
            PitchFriction: 0f,
            PitchBounce: 0f,
            BaseMass: 0f,
            BaseStiffness: 0f,
            BaseDamping: 0f,
            BaseMinLeft: 0f,
            BaseMaxLeft: 0f,
            BaseLeftFriction: 0f,
            BaseMinUp: 0f,
            BaseMaxUp: 0f,
            BaseUpFriction: 0f,
            BaseMinForward: 0f,
            BaseMaxForward: 0f,
            BaseForwardFriction: 0f,
            BoingImpactSpeed: 0f,
            BoingImpactAngle: 0f,
            BoingDampingRate: 0f,
            BoingFrequency: 0f,
            BoingAmplitude: 0f);
}
