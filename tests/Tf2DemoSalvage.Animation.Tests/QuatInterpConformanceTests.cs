using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// <c>STUDIO_PROC_QUATINTERP</c> — a helper bone driven by another bone's rotation (B317).
/// </summary>
/// <remarks>
/// **The rule, `DoQuatInterpBone`, `bone_setup.cpp:4700-4770`.** Read-from-source. The control
/// bone's transform RELATIVE TO ITS PARENT is turned into a quaternion, each authored trigger is
/// weighed by how close it is to that, and the triggers' poses are blended by those weights:
///
/// <code>
/// MatrixInvert( bonetoworld.GetBone( pbones[pProc->control].parent ), tmpmatrix );
/// ConcatTransforms( tmpmatrix, bonetoworld.GetBone( pProc->control ), controlmatrix );
/// MatrixAngles( controlmatrix, src, pos );
///
/// for (i = 0; i &lt; pProc->numtriggers; i++)
/// {
///     float dot = fabs( QuaternionDotProduct( pProc->pTrigger( i )->trigger, src ) );
///     dot = clamp( dot, -1.f, 1.f );
///     weight[i] = 1 - (2 * acos( dot ) * pProc->pTrigger( i )->inv_tolerance );
///     weight[i] = max( 0.f, weight[i] );
///     scale += weight[i];
/// }
/// </code>
///
/// **Why it matters, measured rather than assumed.** TF2 puts this rule on `hlp_forearm_L` and
/// `hlp_forearm_R` of every class model, and the `bone-flags` probe reports all four found on
/// `serveme-627619-stv-2026-08-07` as **SKINNED** — vertices are weighted to them. An unimplemented
/// helper bone that nothing is skinned to would be bookkeeping; these are a forearm that does not
/// twist with the wrist.
/// </remarks>
public sealed class QuatInterpConformanceTests
{
    /// <remarks>
    /// **Trigger ONE, not trigger zero, and that is the whole point of the case.** The `scale &lt;=
    /// 0.001` fallback also produces trigger zero's pose, so a test that drove the control to
    /// trigger zero could not tell "matched the trigger" from "matched nothing and fell back" — the
    /// two predict the same observation. Driving it to the second trigger separates them.
    /// </remarks>
    [Test]
    public void Build_WithTheControlAtOneTriggersAngle_TakesThatTriggersPose()
    {
        float[] destination = new float[12];

        QuatInterpBones.Build(
            new StudioQuatInterp(Control: 1, [Identity(1f, 0f, 0f), Quarter(0f, 2f, 0f)]),
            controlWorld: Rotated(QuarterTurn),
            controlParentWorld: Rotated(0f),
            parentWorld: Rotated(0f),
            destination);

        // The parent is the identity, so the bone's world transform IS the trigger's pose.
        destination[3].ShouldBe(0f, 1e-4d);
        destination[7].ShouldBe(2f, 1e-4d);
        destination[11].ShouldBe(0f, 1e-4d);

        // A quarter turn about Z sends the X axis onto +Y.
        destination[0].ShouldBe(0f, 1e-4d);
        destination[4].ShouldBe(1f, 1e-4d);
    }

    /// <remarks>
    /// **The `scale &lt;= 0.001` branch, which is a real branch and not an error path.** Every weight
    /// is `1 - 2·acos(dot)·inv_tolerance` clamped at zero, so a control far from every authored
    /// angle weighs nothing at all — and the engine then uses trigger ZERO outright rather than
    /// dividing by zero. A half turn is far from both triggers here.
    /// </remarks>
    [Test]
    public void Build_WithTheControlFarFromEveryTrigger_FallsBackToTheFirst()
    {
        float[] destination = new float[12];

        QuatInterpBones.Build(
            new StudioQuatInterp(Control: 1, [Identity(1f, 0f, 0f), Quarter(0f, 2f, 0f)]),
            controlWorld: Rotated(HalfTurn),
            controlParentWorld: Rotated(0f),
            parentWorld: Rotated(0f),
            destination);

        destination[3].ShouldBe(1f, 1e-4d);
        destination[7].ShouldBe(0f, 1e-4d);

        // Trigger zero's rotation is the identity, so X stays on X.
        destination[0].ShouldBe(1f, 1e-4d);
    }

    /// <remarks>
    /// **Two triggers weighing equally, which is the only case that tests the BLEND.** Both cases
    /// above are satisfied by an implementation that picks the best trigger and ignores the rest;
    /// this one is not. The control sits exactly between two authored angles, so each weight is
    /// identical, each `s` is a half, and the position lands on the midpoint — a prediction the
    /// test can state exactly because it chose both ends.
    /// </remarks>
    [Test]
    public void Build_WithTheControlBetweenTwoTriggers_BlendsTheirPositions()
    {
        float[] destination = new float[12];

        QuatInterpBones.Build(
            new StudioQuatInterp(
                Control: 1,
                [
                    Turned(EighthTurn, 0f, 0f, 4f),
                    Turned(-EighthTurn, 0f, 0f, 8f),
                ]),
            controlWorld: Rotated(0f),
            controlParentWorld: Rotated(0f),
            parentWorld: Rotated(0f),
            destination);

        destination[11].ShouldBe(6f, 1e-3d, "the midpoint of 4 and 8, each weighing a half");

        // And the blended rotation is the identity, because the two are equal and opposite.
        destination[0].ShouldBe(1f, 1e-3d);
        destination[5].ShouldBe(1f, 1e-3d);
    }

    /// <remarks>
    /// **The control's transform is read RELATIVE TO ITS PARENT, not in world space**, which is what
    /// the `MatrixInvert` + `ConcatTransforms` pair at the top of the engine's function does. Give
    /// the control and its parent the SAME world rotation and the relative transform is the
    /// identity — so the answer must be the identity-matching trigger, not the quarter-turn one. An
    /// implementation reading the control's world matrix directly picks the other and passes every
    /// test above, all of which use an identity parent.
    /// </remarks>
    [Test]
    public void Build_WithTheControlRotatedWithItsParent_ReadsTheRelativeTransform()
    {
        float[] destination = new float[12];

        QuatInterpBones.Build(
            new StudioQuatInterp(Control: 1, [Identity(1f, 0f, 0f), Quarter(0f, 2f, 0f)]),
            controlWorld: Rotated(QuarterTurn),
            controlParentWorld: Rotated(QuarterTurn),
            parentWorld: Rotated(0f),
            destination);

        destination[3].ShouldBe(1f, 1e-4d, "the control is unrotated relative to its parent");
        destination[7].ShouldBe(0f, 1e-4d);
    }

    /// <remarks>
    /// **The only case that tests the `fabs` on the dot product, and it was missing.** A sabotage
    /// that removed `MathF.Abs` left all four tests above GREEN: every dot product they compute is
    /// non-negative, because each fixture's half-angles fall in [-π/2, π/2] and the one half-turn
    /// case gives exactly zero, where the sign does not matter. `MathF.Abs` was a no-op on every
    /// value the suite produced — the "wrong condition" failure, found by a sabotage that reddened
    /// nothing rather than by reading the tests.
    ///
    /// **A quaternion and its negation are the same rotation**, which is why the engine writes
    /// `fabs( QuaternionDotProduct( ... ) )`. This trigger stores the identity rotation in its
    /// negated form, so the raw dot against an unrotated control is −1 and the absolute dot is 1.
    /// With the absolute value the trigger weighs fully and the bone takes its position; without it
    /// the trigger weighs nothing, the total falls under the epsilon, and the bone snaps to trigger
    /// zero instead. Two visibly different answers from one missing call.
    /// </remarks>
    [Test]
    public void Build_WithATriggerStoredAsTheNegatedQuaternion_StillMatchesIt()
    {
        float[] destination = new float[12];

        QuatInterpBones.Build(
            new StudioQuatInterp(
                Control: 1,
                [
                    // Far from the control, so it weighs nothing and cannot mask the result.
                    Turned(HalfTurn, 7f, 0f, 0f),

                    // The identity rotation, written as its own negation.
                    new StudioQuatInterpTrigger(
                        InverseTolerance: 1f,
                        TriggerX: 0f, TriggerY: 0f, TriggerZ: 0f, TriggerW: -1f,
                        PositionX: 0f, PositionY: 0f, PositionZ: 9f,
                        QuatX: 0f, QuatY: 0f, QuatZ: 0f, QuatW: -1f),
                ]),
            controlWorld: Rotated(0f),
            controlParentWorld: Rotated(0f),
            parentWorld: Rotated(0f),
            destination);

        destination[11].ShouldBe(9f, 1e-4d, "the negated trigger is the same rotation and matches");
        destination[3].ShouldBe(0f, 1e-4d, "so trigger zero's position is not what came back");
    }

    /// <summary>A quarter turn about Z, in radians.</summary>
    private const float QuarterTurn = MathF.PI / 2f;

    /// <summary>A half turn about Z.</summary>
    private const float HalfTurn = MathF.PI;

    /// <summary>An eighth turn about Z.</summary>
    private const float EighthTurn = MathF.PI / 4f;

    /// <summary>A trigger that matches an unrotated control, wanting the given position.</summary>
    private static StudioQuatInterpTrigger Identity(float x, float y, float z) =>
        Turned(0f, x, y, z);

    /// <summary>A trigger that matches a quarter-turned control.</summary>
    private static StudioQuatInterpTrigger Quarter(float x, float y, float z) =>
        Turned(QuarterTurn, x, y, z);

    /// <summary>
    /// A trigger matching a rotation of <paramref name="radians"/> about Z, wanting that same
    /// rotation and the given position.
    /// </summary>
    /// <remarks>
    /// **`inv_tolerance` is 1, meaning a one-radian window** — the reciprocal, per Valve's comment
    /// *"1 / radian angle of trigger influence"*. Chosen so the arithmetic in each prediction above
    /// is legible: a control half a radian away weighs zero, and one on the nose weighs one.
    /// </remarks>
    private static StudioQuatInterpTrigger Turned(float radians, float x, float y, float z)
    {
        float half = radians / 2f;
        float sin = MathF.Sin(half);
        float cos = MathF.Cos(half);

        return new StudioQuatInterpTrigger(
            InverseTolerance: 1f,
            TriggerX: 0f, TriggerY: 0f, TriggerZ: sin, TriggerW: cos,
            PositionX: x, PositionY: y, PositionZ: z,
            QuatX: 0f, QuatY: 0f, QuatZ: sin, QuatW: cos);
    }

    /// <summary>A world matrix rotated about Z by the given angle, at the origin.</summary>
    private static float[] Rotated(float radians)
    {
        float half = radians / 2f;

        float[] matrix = new float[12];

        StudioBones.FromQuaternion(
            (0f, 0f, MathF.Sin(half), MathF.Cos(half)), (0f, 0f, 0f), matrix);

        return matrix;
    }
}
