using System;


namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The boundary between Valve's matrices and this renderer's.
/// </summary>
/// <remarks>
/// **This renderer speaks two conventions on purpose, and that is the thing to be tested.** Bones
/// reach the shader in Valve's own 3×4 column-vector layout and are used RAW —
/// <c>dot(boneRows[row], float4(position, 1))</c> is that formula exactly. The model matrix is a
/// <c>row_major float4x4</c> transforming a row vector, which <c>PropTransform.ToMatrix</c> already
/// produces from <c>AngleMatrix</c>.
///
/// So anything that uses a Valve transform AS a model matrix has to cross between them. Doing that
/// in two places with two pieces of code is how the two come to disagree, which is why it lives in
/// one place now — and why these predict exact numbers rather than checking that something changed.
/// </remarks>
public sealed class MatrixConventionTests
{
    /// <remarks>
    /// **`ConcatTransforms` sums `mul0 + (mul1 + mul2)`** (`mathlib_base.cpp:686`, `:695`, `:704`), then adds the
    /// translation (`:710-712`). Float addition does not associate, so the order is visible: with the column
    /// (1, 1e8, −1e8) and a row of ones, Valve's grouping gives 1 and a left-to-right sum gives 0 (1e8 + 1 rounds to 1e8).
    /// </remarks>
    [Test]
    public void Concatenate_ARowWhoseLeftToRightSumLosesAUnit_GroupsTheLastTwoProductsAsValveDoes()
    {
        float[] first = [1f, 1f, 1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];
        float[] second = [1f, 0f, 0f, 0f, 1e8f, 1f, 0f, 0f, -1e8f, 0f, 1f, 0f];
        float[] result = new float[12];

        MatrixConvention.Concatenate(first, second, result);

        result[0].ShouldBe(1f);
    }

    /// <remarks>
    /// **`ConcatTransforms` loads both operands before it stores** (`mathlib_base.cpp:670-676`, `:715-717`), so the
    /// engine's callers may pass the output as an input — `ConcatTransforms( a, b, a )`. Ours wrote rows while still
    /// reading them (B612). The aliased answer must be the separate one, float for float.
    /// </remarks>
    [TestCase(true, TestName = "Concatenate_IntoItsFirstOperand_IsTheSameAsIntoAFreshMatrix")]
    [TestCase(false, TestName = "Concatenate_IntoItsSecondOperand_IsTheSameAsIntoAFreshMatrix")]
    public void Concatenate_IntoAnOperand_IsTheSameAsIntoAFreshMatrix(bool intoFirst)
    {
        float[] first = [0.6f, -0.8f, 0f, 10f, 0.8f, 0.6f, 0f, -4f, 0f, 0f, 1f, 2.5f];
        float[] second = [0f, 0f, 1f, 3f, 0f, 1f, 0f, -7f, -1f, 0f, 0f, 0.5f];
        float[] expected = new float[12];

        MatrixConvention.Concatenate(first, second, expected);

        float[] aliased = intoFirst ? first : second;
        MatrixConvention.Concatenate(first, second, aliased);

        aliased.ShouldBe(expected);
    }

    /// <remarks>
    /// **The masked add puts +0.0 on the rotation lanes** (`mathlib_base.cpp:707-712`: `AndSIMD( rowA, lastMask )` is
    /// zero there), so a row whose three products are all −0.0 comes out +0.0, which an `atan2` reads as +π, not −π.
    /// Row (1, 0, 0) against the column (−0, −1, −1): −0 + (−0 + −0) is −0, plus +0 is +0 (B612 review).
    /// </remarks>
    [Test]
    public void Concatenate_ARowOfNegativeZeroProducts_IsPositiveZeroAsTheMaskedAddMakesIt()
    {
        float[] first = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];
        float[] second = [-0f, 0f, 0f, 0f, -1f, 1f, 0f, 0f, -1f, 0f, 1f, 0f];
        float[] result = new float[12];

        MatrixConvention.Concatenate(first, second, result);

        BitConverter.SingleToInt32Bits(result[0]).ShouldBe(0);
    }

    /// <remarks>
    /// **A span shorter than a matrix is refused before anything is written**, and a model matrix written over its own
    /// source is refused (B612): a short span threw mid-loop after partial writes, and an overlapping one was silently
    /// wrong.
    /// </remarks>
    [Test]
    public void SpanForms_AShortSpanOrAnOverlappingModelMatrix_AreRefused()
    {
        float[] twelve = new float[12];
        float[] sixteen = new float[16];

        Should.Throw<ArgumentException>(() => MatrixConvention.Concatenate(twelve, twelve, new float[11]));
        Should.Throw<ArgumentException>(() => MatrixConvention.Multiply(sixteen, sixteen, new float[15]));
        Should.Throw<ArgumentException>(() => MatrixConvention.ToModelMatrix(twelve, new float[15]));
        Should.Throw<ArgumentException>(() => MatrixConvention.ToModelMatrix(sixteen.AsSpan(0, 12), sixteen));
    }

    /// <remarks>**`Multiply` into one of its operands is the separate answer** (ours alone, not-parity; it copies when aliased).</remarks>
    [TestCase(true, TestName = "Multiply_IntoItsFirstOperand_IsTheSameAsIntoAFreshMatrix")]
    [TestCase(false, TestName = "Multiply_IntoItsSecondOperand_IsTheSameAsIntoAFreshMatrix")]
    public void Multiply_IntoAnOperand_IsTheSameAsIntoAFreshMatrix(bool intoFirst)
    {
        float[] first = Move(10f, -4f, 2.5f);
        float[] second = Turn90();
        float[] expected = new float[16];

        MatrixConvention.Multiply(first, second, expected);

        float[] aliased = intoFirst ? first : second;
        MatrixConvention.Multiply(first, second, aliased);

        aliased.ShouldBe(expected);
    }

    /// <remarks>
    /// **An entity's placement is written into the bone matrix it owns** (B574): `PlacementOf` built a 16-float model
    /// matrix and a 12-float copy of it for every parented prop every frame, 172 MB per 20 s on f12's corpse worker
    /// (finding 89's measurement file). The engine's `matrix3x4_t` is a local. The written matrix must be the two-step
    /// one's, float for float, and writing it must allocate nothing.
    /// </remarks>
    [Test]
    public void ToBoneMatrix_APlacementIntoASpan_IsTheArrayFormAndAllocatesNothing()
    {
        Content.Bsp.PropTransform placement = new(120f, -48f, 16f, 25f, -110f, 40f, 1.5f);
        float[] expected = MatrixConvention.ToBoneMatrix(placement.ToMatrix());
        float[] written = new float[12];
        MatrixConvention.ToBoneMatrix(placement, written);

        long before = GC.GetAllocatedBytesForCurrentThread();
        MatrixConvention.ToBoneMatrix(placement, written);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);
        written.ShouldBe(expected);
    }

    [Test]
    public void ToModelMatrix_ATranslation_MovesFromColumnThreeToRowThree()
    {
        // The half that is easy to get right and easy to check.
        float[] valve = [1f, 0f, 0f, 5f, 0f, 1f, 0f, 6f, 0f, 0f, 1f, 7f];

        float[] model = MatrixConvention.ToModelMatrix(valve);

        (model[12], model[13], model[14]).ShouldBe((5f, 6f, 7f));
        model[15].ShouldBe(1f);
    }

    [Test]
    public void ToModelMatrix_ARotation_IsTransposed()
    {
        // **The half that is not.** A rotation is the only case where a missing transpose shows —
        // it is invisible on a pure translation, which is exactly how such a bug survives the
        // obvious test and appears the moment something turns.
        //
        // Valve's 90° about Z sends +X to +Y. Under the row-vector convention that means row 0 of
        // the model matrix must BE (0, 1, 0): a point at +X reads row 0 and lands at +Y.
        float[] valve = [0f, -1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f];

        float[] model = MatrixConvention.ToModelMatrix(valve);

        (model[0], model[1], model[2]).ShouldBe((0f, 1f, 0f));
        (model[4], model[5], model[6]).ShouldBe((-1f, 0f, 0f));
    }

    [Test]
    public void Concatenate_ABoneThenAnOffsetWithinIt_AppliesTheBoneFirst()
    {
        // ConcatTransforms' own order. A bone turned 90° with an offset 10 along the bone's +X puts
        // the point at +Y, because the offset is expressed in the bone's space.
        float[] bone = [0f, -1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f];
        float[] local = [1f, 0f, 0f, 10f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

        float[] point = MatrixConvention.Concatenate(bone, local);

        point[3].ShouldBe(0f, 0.001f);
        point[7].ShouldBe(10f, 0.001f);
        point[11].ShouldBe(0f, 0.001f);
    }

    [Test]
    public void Multiply_TwoRowVectorMatrices_AppliesTheFirstFirst()
    {
        // **Order is the other half of the same trap.** With row vectors, p·A·B applies A first;
        // under the column convention the same expression would read the other way round.
        float[] moveThenTurn = MatrixConvention.Multiply(Move(10f, 0f, 0f), Turn90());
        float[] turnThenMove = MatrixConvention.Multiply(Turn90(), Move(10f, 0f, 0f));

        // Moving along +X and then turning puts the translation at +Y.
        moveThenTurn[12].ShouldBe(0f, 0.001f);
        moveThenTurn[13].ShouldBe(10f, 0.001f);

        // Turning first leaves the move along the world's +X.
        turnThenMove[12].ShouldBe(10f, 0.001f);
        turnThenMove[13].ShouldBe(0f, 0.001f);
    }

    [Test]
    public void ToModelMatrix_SomethingThatIsNotAMatrix_IsRefused()
    {
        Should.Throw<ArgumentException>(() => MatrixConvention.ToModelMatrix([1f, 2f, 3f]));
    }

    private static float[] Move(float x, float y, float z) =>
        [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, x, y, z, 1f];

    private static float[] Turn90() =>
        [0f, 1f, 0f, 0f, -1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];
}
