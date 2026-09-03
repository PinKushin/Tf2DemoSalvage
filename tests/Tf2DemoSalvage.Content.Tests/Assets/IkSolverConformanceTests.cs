using System;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Two links reach a point — <c>CIKSolver::solve</c> and <c>Studio_AlignIKMatrix</c>.
/// </summary>
/// <remarks>
/// **<c>bone_setup.cpp:2601</c>**, Ken Perlin's closed form, credited in Valve's own comment. Given
/// a chain from the origin to <c>P</c> with link lengths <c>a</c> and <c>b</c>, find a knee
/// <c>Q</c> with <c>|Q| = a</c> and <c>|P − Q| = b</c>.
///
/// **The predictions here are GEOMETRY, not a transcription of the implementation.** Every
/// assertion below is one of the two defining constraints, or Pythagoras on a case chosen so the
/// answer is exact — a 3/4/5 triangle, a right angle, a fully folded chain. That is what makes them
/// a test of the solver rather than a copy of it.
///
/// **It matters because 705 of the scout's 1012 animations ask for IK** (B296). This is not a
/// corner of the format.
/// </remarks>
public sealed class IkSolverConformanceTests
{
    /// <summary>How close two floats must be to count as equal here.</summary>
    private const double Tolerance = 1e-3;

    [Test]
    public void Solve_ATargetTheChainCanReach_PutsTheKneeOnBothSpheres()
    {
        // **The two defining constraints, asserted directly.** |Q| = a and |P − Q| = b is the whole
        // problem statement; anything satisfying both is a correct knee, whatever route reached it.
        //
        // **The links are UNEQUAL on purpose.** With `a == b` the closed form's `(a² − b²)` term is
        // zero, so swapping the two squares changes nothing and this test is blind to the one line
        // it most needs to check. Found by sabotage: an equal-length version stayed green while the
        // formula was inverted.
        bool solved = StudioIkSolver.Solve(
            first: 8f,
            second: 12f,
            target: new Vector3(14f, 0f, 0f),
            preferred: new Vector3(0f, 5f, 0f),
            out Vector3 knee);

        solved.ShouldBeTrue("a target fourteen units away is inside a twenty-unit chain's reach");

        knee.Length().ShouldBe(8f, Tolerance, "|Q| = a");

        (new Vector3(14f, 0f, 0f) - knee).Length().ShouldBe(12f, Tolerance, "|P - Q| = b");
    }

    [Test]
    public void Solve_A345Triangle_PlacesTheKneeWhereTrigonometrySays()
    {
        // **An exact case, so the prediction is a number rather than a property.** Links of 3 and 4
        // reaching 5: `d = (c + (a² − b²)/c) / 2 = (5 + (9 − 16)/5) / 2 = 1.8`, and
        // `e = sqrt(9 − 3.24) = 2.4`. That is the classic right triangle, with the knee at the foot
        // of the altitude — 1.8 along the reach and 2.4 off it.
        StudioIkSolver.Solve(
            first: 3f,
            second: 4f,
            target: new Vector3(5f, 0f, 0f),
            preferred: new Vector3(0f, 1f, 0f),
            out Vector3 knee).ShouldBeTrue();

        knee.X.ShouldBe(1.8f, Tolerance, "d = (c + (a^2 - b^2)/c) / 2");
        knee.Y.ShouldBe(2.4f, Tolerance, "e = sqrt(a^2 - d^2)");
        knee.Z.ShouldBe(0f, Tolerance, "the knee lies in the plane the preference picks");
    }

    [Test]
    public void Solve_TheSamePointWithTheOppositePreference_BendsTheOtherWay()
    {
        // **The preference is the whole of the choice, and this is the control for it.** Two links
        // reaching one point leave a CIRCLE of valid knees; which one is taken is the difference
        // between a knee that bends forwards and one that bends backwards, and nothing but the
        // preference decides. Both answers below satisfy the constraints, so an assertion on the
        // constraints alone could not tell them apart.
        StudioIkSolver.Solve(
            3f, 4f, new Vector3(5f, 0f, 0f), new Vector3(0f, 1f, 0f), out Vector3 up);

        StudioIkSolver.Solve(
            3f, 4f, new Vector3(5f, 0f, 0f), new Vector3(0f, -1f, 0f), out Vector3 down);

        up.Y.ShouldBeGreaterThan(0f);
        down.Y.ShouldBeLessThan(0f, "the opposite preference bends the knee the opposite way");

        up.X.ShouldBe(down.X, Tolerance, "and both sit the same distance along the reach");
    }

    [Test]
    public void Solve_APreferenceLeaningAlongTheReach_IsOrthogonalisedFirst()
    {
        // **`Y = D − X(D·X)`, and nothing else in this file could see it.** Every other test here
        // gives a preference perpendicular to the target, where the subtracted term is exactly zero
        // — so deleting the orthogonalisation entirely left the whole suite green. Found by
        // sabotage; the fix is an input with a component ALONG the reach, which is also the
        // realistic case, since a knee preference is a rough direction rather than a right angle.
        //
        // The prediction is the constraint, which an unnormalised basis breaks: a `y` still leaning
        // along `x` makes the knee's distance from the root wrong.
        StudioIkSolver.Solve(
            first: 3f,
            second: 4f,
            target: new Vector3(5f, 0f, 0f),
            preferred: new Vector3(4f, 1f, 0f),
            out Vector3 knee).ShouldBeTrue();

        knee.Length().ShouldBe(3f, Tolerance, "|Q| = a even when the preference leans along P");

        (new Vector3(5f, 0f, 0f) - knee).Length().ShouldBe(4f, Tolerance, "|P - Q| = b");

        // And it landed on the same side as the preference's perpendicular part, which is up.
        knee.Y.ShouldBeGreaterThan(0f);
    }

    [Test]
    public void Solve_ATargetFurtherThanTheChain_ReportsNoRealBend()
    {
        // `return d > (r - B) && d < A;` — a chain of twenty reaching thirty cannot bend at all, so
        // `d` runs past `A` and the answer is refused. The caller then leaves the bones alone rather
        // than using it, which is why the return value matters as much as the knee.
        StudioIkSolver.Solve(
            first: 10f,
            second: 10f,
            target: new Vector3(30f, 0f, 0f),
            preferred: new Vector3(0f, 1f, 0f),
            out _).ShouldBeFalse("thirty units is beyond a twenty-unit chain");
    }

    [Test]
    public void Solve_ATargetAtTheOrigin_IsRefusedRatherThanDividedBy()
    {
        // `normalize(X)` divides by the target's length, which the engine never checks. A chain
        // asked to reach its own root is degenerate; answering false is a position the caller can
        // act on, where a NaN basis is not.
        StudioIkSolver.Solve(
            10f, 10f, Vector3.Zero, new Vector3(0f, 1f, 0f), out Vector3 knee).ShouldBeFalse();

        float.IsNaN(knee.X).ShouldBeFalse("a degenerate target must not produce a NaN knee");
    }

    [Test]
    public void Align_AVectorAlongX_LeavesAnOrthonormalBasis()
    {
        // `Studio_AlignIKMatrix` points column zero along the vector and rebuilds the other two.
        // Whatever it produces has to remain a rotation, which is three unit columns at right
        // angles — a property no amount of transcription guarantees.
        float[] matrix = Identity();

        StudioIkSolver.Align(matrix, new Vector3(0f, 1f, 0f));

        Column(matrix, 0).Length().ShouldBe(1f, Tolerance);
        Column(matrix, 1).Length().ShouldBe(1f, Tolerance);
        Column(matrix, 2).Length().ShouldBe(1f, Tolerance);

        Vector3.Dot(Column(matrix, 0), Column(matrix, 1)).ShouldBe(0f, Tolerance);
        Vector3.Dot(Column(matrix, 1), Column(matrix, 2)).ShouldBe(0f, Tolerance);

        Column(matrix, 0).X.ShouldBe(0f, Tolerance, "column zero is the vector");
        Column(matrix, 0).Y.ShouldBe(1f, Tolerance);
    }

    [Test]
    public void Align_AnyVector_LeavesThePositionAlone()
    {
        // **The control, and it is the mistake this function invites.** Three of the twelve floats
        // are the bone's POSITION, and a rebuild that wrote whole rows rather than the three
        // rotation columns would move the bone as well as turn it — which looks like a solver bug
        // and is not one.
        float[] matrix = Identity();

        matrix[3] = 11f;
        matrix[7] = 22f;
        matrix[11] = 33f;

        StudioIkSolver.Align(matrix, new Vector3(1f, 2f, 3f));

        matrix[3].ShouldBe(11f);
        matrix[7].ShouldBe(22f);
        matrix[11].ShouldBe(33f);
    }

    [Test]
    public void Align_WithARolledMatrix_TakesItsRollFromTheOldZ()
    {
        // **The ordering claim, and nothing else here tested it.** Column one is
        // `oldZ cross newX`, so the roll the bone ends up with comes from the Z it had BEFORE the
        // rebuild. Reading it after column two is written would take the new Z instead and lose the
        // animation's roll — a wrong picture that is still a valid rotation, which is why only a
        // prediction from the old Z can catch it.
        //
        // Every other case here starts from the identity, where the old and new Z coincide and the
        // distinction cannot appear. This one is rolled ninety degrees about X first, so its Z is
        // −Y rather than +Z.
        float[] matrix =
        [
            1f, 0f, 0f, 0f,
            0f, 0f, -1f, 0f,
            0f, 1f, 0f, 0f,
        ];

        StudioIkSolver.Align(matrix, new Vector3(1f, 0f, 0f));

        // oldZ is (0, -1, 0); newX is (1, 0, 0); their cross is (0, 0, 1).
        Column(matrix, 1).X.ShouldBe(0f, Tolerance);
        Column(matrix, 1).Y.ShouldBe(0f, Tolerance);
        Column(matrix, 1).Z.ShouldBe(1f, Tolerance, "column one is oldZ cross newX");

        // And column two follows from the pair, not from what it held before.
        Column(matrix, 2).Y.ShouldBe(-1f, Tolerance, "newX cross newY");
    }

    [Test]
    public void Align_AVectorAlongTheOldZ_StillLeavesARotation()
    {
        // **Valve's own unfixed note is here**: *"check for X being too near to Z"*. Column one is
        // the cross of the OLD column two with the new column zero, so aligning to the old Z makes
        // that cross vanish and the engine's normalise divides by zero. The roll is arbitrary in
        // that case whatever is done; what must not happen is a NaN reaching a bone matrix.
        float[] matrix = Identity();

        StudioIkSolver.Align(matrix, new Vector3(0f, 0f, 1f));

        for (int cell = 0; cell < 12; cell++)
        {
            float.IsNaN(matrix[cell]).ShouldBeFalse($"cell {cell} must not be NaN");
        }

        Column(matrix, 1).Length().ShouldBe(1f, Tolerance, "and it is still a rotation");
    }

    /// <summary>A row-major 3x4 identity.</summary>
    private static float[] Identity() =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
    ];

    /// <summary>One column of a row-major 3x4.</summary>
    private static Vector3 Column(ReadOnlySpan<float> matrix, int column) =>
        new(matrix[column], matrix[4 + column], matrix[8 + column]);
}
