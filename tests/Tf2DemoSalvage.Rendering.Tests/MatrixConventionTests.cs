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
