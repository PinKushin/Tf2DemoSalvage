using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// Crossing between Source space and the physics engine's — <c>FUN_180002cc0</c> (B58).
/// </summary>
/// <remarks>
/// **Read out of `vphysics.dll`, not inferred**, and it changes three things at once:
///
/// - **Axes.** `Source (x, y, z)` becomes `IVP (x, −z, y)`, applied to a rotation as
///   `M' = P M Pᵀ` with `P = [[1,0,0],[0,0,−1],[0,1,0]]`. Source is Z-up; IVP is Y-up.
/// - **Units.** Translations are multiplied by <b>0.0254</b>, metres per inch, dumped from
///   `18011f000` with `39.37` in the adjacent dword.
/// - **Storage.** `FUN_18000ca70` writes the rows into COLUMNS of a 4×4 and puts the translation in
///   the last ROW.
///
/// The twelve assignments in the binary are the specification, and the sign pattern falls out of the
/// similarity transform: a minus appears exactly where one of the row or column index passes
/// through the negated axis and the other does not.
///
/// **The permutation is cross-checked three ways**, which is why it is a reading rather than a
/// plausible guess: the twelve assignments, the joint axis remap table at `18011f014` holding
/// `00 02 01 03`, and the single negated axis in `InitRagdoll` being Source 2. See
/// `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md`.
///
/// **Every case below uses an input with no symmetry**, because a symmetric one cannot tell a
/// missing transpose from a present one, nor a swapped pair from an unswapped one.
/// </remarks>
public sealed class IvpTransformConformanceTests
{
    private const double Tolerance = 1e-6;

    /// <remarks>
    /// **Metres per inch, and the axes permuted.** `(1, 2, 3)` inches becomes
    /// `(1, −3, 2) × 0.0254`. All three components differ and none is zero, so a dropped sign or an
    /// exchanged pair cannot land on the same answer.
    /// </remarks>
    [Test]
    public void Position_ASourcePoint_IsMetresWithZNegatedIntoY()
    {
        (float X, float Y, float Z) at = IvpTransform.Position(1f, 2f, 3f);

        at.X.ShouldBe(0.0254f, Tolerance);
        at.Y.ShouldBe(-0.0762f, Tolerance);
        at.Z.ShouldBe(0.0508f, Tolerance);
    }

    /// <remarks>
    /// The round trip, which is what says the two directions describe one convention rather than two
    /// readings of it.
    ///
    /// **The tolerance is loose on purpose, and by a measured amount.** Valve stores `0.0254` and
    /// `39.37` as a pair rather than one and its reciprocal, and `0.0254 × 39.37` is `0.999998` —
    /// so a round trip loses two parts in a million by design. At 33 inches that is `6.6e-5`, which
    /// is why the bound is `1e-4` and not `1e-6`.
    /// </remarks>
    [Test]
    public void Position_ThenBack_ReturnsTheSourcePoint()
    {
        (float X, float Y, float Z) there = IvpTransform.Position(11f, -22f, 33f);
        (float X, float Y, float Z) back = IvpTransform.SourcePosition(there.X, there.Y, there.Z);

        back.X.ShouldBe(11f, 1e-4);
        back.Y.ShouldBe(-22f, 1e-4);
        back.Z.ShouldBe(33f, 1e-4);
    }

    /// <remarks>
    /// **A quarter turn about Source Z becomes a quarter turn the OTHER way about IVP Y**, because
    /// `P` sends Source Z to −IVP Y. Worked by hand from the twelve assignments:
    ///
    /// <code>
    ///   Source  [ 0 -1  0 ]        IVP  [ 0  0 -1 ]
    ///           [ 1  0  0 ]   →         [ 0  1  0 ]
    ///           [ 0  0  1 ]             [ 1  0  0 ]
    /// </code>
    ///
    /// and the IVP result is `R_y(−90°)`, which is the prediction the sign convention makes.
    /// </remarks>
    [Test]
    public void Rotation_AQuarterTurnAboutSourceZ_BecomesTheOppositeTurnAboutIvpY()
    {
        float[] source =
        [
            0f, -1f, 0f, 0f,
            1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f,
        ];

        float[] ivp = IvpTransform.Rotation(source);

        // Row 0.
        ivp[0].ShouldBe(0f, Tolerance);
        ivp[1].ShouldBe(0f, Tolerance);
        ivp[2].ShouldBe(-1f, Tolerance);

        // Row 1.
        ivp[3].ShouldBe(0f, Tolerance);
        ivp[4].ShouldBe(1f, Tolerance);
        ivp[5].ShouldBe(0f, Tolerance);

        // Row 2.
        ivp[6].ShouldBe(1f, Tolerance);
        ivp[7].ShouldBe(0f, Tolerance);
        ivp[8].ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **The identity is the control that catches a transposed or shuffled implementation only when
    /// paired with the case above** — on its own it passes for any permutation, which is exactly why
    /// it is not the only rotation case here.
    /// </remarks>
    [Test]
    public void Rotation_TheIdentity_StaysTheIdentity()
    {
        float[] ivp = IvpTransform.Rotation(
        [
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
        ]);

        ivp[0].ShouldBe(1f, Tolerance);
        ivp[4].ShouldBe(1f, Tolerance);
        ivp[8].ShouldBe(1f, Tolerance);
    }

    /// <remarks>
    /// **This is the case the others could not fail on, and it was found by sabotage.** Every other
    /// rotation here — the identity, the quarter turn about Z, the cyclic permutation first used
    /// for this test — has `m20 = 0`, so flipping the sign on `−source[8]` in the implementation
    /// changed nothing anywhere and all eight tests stayed green. The fix is the input, not the
    /// assertion (`docs/memory/most-of-a-decoder-is-untested.md#a-duplicated-guard-cannot-be-tested` is the same shape).
    ///
    /// **45° about `(1, 1, 1)/√3` has all nine entries non-zero and no two equal in magnitude
    /// pattern by accident**, so flipping any single sign in the transcription changes exactly one
    /// asserted value. Predicted by Rodrigues by hand, then mapped through
    /// `M' = P M Pᵀ`:
    ///
    /// <code>
    ///   Source [  0.80474 -0.31062  0.50588 ]     IVP [  0.80474 -0.50588 -0.31062 ]
    ///          [  0.50588  0.80474 -0.31062 ]  →      [  0.31062  0.80474 -0.50588 ]
    ///          [ -0.31062  0.50588  0.80474 ]         [  0.50588  0.31062  0.80474 ]
    /// </code>
    ///
    /// The determinant is asserted as well: `P` has determinant +1, so a similarity transform
    /// preserves it and a right-handed Source rotation must stay right-handed. That catches a
    /// mirrored corpse, which no single component assertion does on its own.
    /// </remarks>
    [Test]
    public void Rotation_AnObliqueRotation_MapsEveryEntryAndKeepsItsHandedness()
    {
        const float Diagonal = 0.804_737_85f;
        const float Minor = 0.310_617_22f;
        const float Major = 0.505_879_36f;

        float[] ivp = IvpTransform.Rotation(
        [
            Diagonal, -Minor, Major, 0f,
            Major, Diagonal, -Minor, 0f,
            -Minor, Major, Diagonal, 0f,
        ]);

        ivp[0].ShouldBe(Diagonal, Tolerance);
        ivp[1].ShouldBe(-Major, Tolerance);
        ivp[2].ShouldBe(-Minor, Tolerance);

        ivp[3].ShouldBe(Minor, Tolerance);
        ivp[4].ShouldBe(Diagonal, Tolerance);
        ivp[5].ShouldBe(-Major, Tolerance);

        ivp[6].ShouldBe(Major, Tolerance);
        ivp[7].ShouldBe(Minor, Tolerance);
        ivp[8].ShouldBe(Diagonal, Tolerance);

        double determinant =
            (ivp[0] * ((ivp[4] * ivp[8]) - (ivp[5] * ivp[7]))) -
            (ivp[1] * ((ivp[3] * ivp[8]) - (ivp[5] * ivp[6]))) +
            (ivp[2] * ((ivp[3] * ivp[7]) - (ivp[4] * ivp[6])));

        determinant.ShouldBe(1d, 1e-5);
    }

    /// <remarks>
    /// **The stored 4×4 is TRANSPOSED and carries its translation in the last row**, which is
    /// `FUN_18000ca70`. Asserted against the rotation case above so the two claims cannot both be
    /// satisfied by one wrong layout: entry `[2]` of the row-major rotation must appear at index 8
    /// of the stored matrix, not at index 2.
    /// </remarks>
    [Test]
    public void Matrix_ASourceTransform_IsStoredTransposedWithTheTranslationLast()
    {
        float[] stored = IvpTransform.Matrix(
        [
            0f, -1f, 0f, 1f,
            1f, 0f, 0f, 2f,
            0f, 0f, 1f, 3f,
        ]);

        stored.Length.ShouldBe(16);

        // The rotation's row 0 is (0, 0, -1); transposed, its third entry lands at index 8.
        stored[0].ShouldBe(0f, Tolerance);
        stored[4].ShouldBe(0f, Tolerance);
        stored[8].ShouldBe(-1f, Tolerance);

        // The translation occupies the last row rather than the last column.
        stored[12].ShouldBe(0.0254f, Tolerance);
        stored[13].ShouldBe(-0.0762f, Tolerance);
        stored[14].ShouldBe(0.0508f, Tolerance);
        stored[15].ShouldBe(1f, Tolerance);

        // And the column that WOULD hold a translation in Source's layout is zero.
        stored[3].ShouldBe(0f, Tolerance);
        stored[7].ShouldBe(0f, Tolerance);
        stored[11].ShouldBe(0f, Tolerance);
    }

    /// <remarks>
    /// **The joint axis remap is the same permutation, and the binary states it as a table** —
    /// `00 02 01 03` at `18011f014`. Asserted separately from the matrix because the engine reaches
    /// for it separately: a joint's three limits are remapped by index, not by transforming a
    /// matrix.
    /// </remarks>
    [Test]
    public void Axis_TheJointRemap_ExchangesTheSecondAndThird()
    {
        IvpTransform.Axis(0).ShouldBe(0);
        IvpTransform.Axis(1).ShouldBe(2);
        IvpTransform.Axis(2).ShouldBe(1);
        IvpTransform.Axis(3).ShouldBe(3);
    }

    /// <remarks>
    /// `FUN_180002bb0` answers 0 for anything from 4 up rather than reading past its four-byte
    /// table. A `.phy` is a stranger's file (D32), so the guard is carried rather than assumed
    /// unreachable.
    /// </remarks>
    [Test]
    public void Axis_AnIndexPastTheTable_IsZero()
    {
        IvpTransform.Axis(4).ShouldBe(0);
        IvpTransform.Axis(-1).ShouldBe(0);
    }
}
