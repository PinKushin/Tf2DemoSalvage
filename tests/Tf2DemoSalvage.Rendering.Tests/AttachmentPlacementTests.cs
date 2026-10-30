using System;


namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Placing an item at a named point on its wearer.
/// </summary>
/// <remarks>
/// **Two matrix conventions meet here and neither is negotiable.** Valve's <c>matrix3x4_t</c> — what
/// a bone and an <c>mstudioattachment_t.local</c> both are — is row major with the translation in
/// COLUMN three, and transforms a column vector. This renderer's model matrix is row major with the
/// translation in ROW three, and transforms a row vector, because that is what the shader's
/// <c>row_major</c> declaration wants.
///
/// So composing them is a transpose plus a move, and getting it wrong produces a plausible
/// placement rather than an error: an item somewhere on the wearer, turned the wrong way. These
/// tests predict exact world positions for that reason.
///
/// The engine's own composition is <c>SetupBones_AttachmentHelper</c>:
/// <c>ConcatTransforms( GetBone( iBone ), pattachment.local, world )</c>.
/// </remarks>
public sealed class AttachmentPlacementTests
{
    [Test]
    public void Matrix_AnIdentityAttachmentOnAWearer_LandsWhereTheWearerIs()
    {
        // The simplest prediction there is: nothing offsets anything, so the item is at the
        // wearer's own origin. If this fails, everything below is measuring noise.
        float[] placed = AttachmentPlacement.Matrix(Identity(), Identity(), Wearer(10f, 20f, 30f));

        Position(placed).ShouldBe((10f, 20f, 30f));
    }

    [Test]
    public void Matrix_AnAttachmentOffsetUpTheBone_LiftsTheItem()
    {
        // A halo's case in miniature: the attachment sits above the bone it hangs from, and the
        // item has to end up there rather than at the wearer's feet — which is the whole of B82.
        float[] local = Identity();
        local[11] = 64f;

        float[] placed = AttachmentPlacement.Matrix(Identity(), local, Wearer(0f, 0f, 0f));

        Position(placed).ShouldBe((0f, 0f, 64f));
    }

    [Test]
    public void Matrix_TheBonesOwnOffset_IsAppliedBeforeTheAttachments()
    {
        // **Order matters and the wrong one is not visibly wrong.** ConcatTransforms puts the bone
        // first: the attachment is expressed in the bone's space, so a bone 10 up and an attachment
        // 5 further up give 15 — while the reverse order gives the same answer here and a different
        // one the moment either carries a rotation.
        float[] bone = Identity();
        bone[11] = 10f;

        float[] local = Identity();
        local[3] = 5f;

        float[] placed = AttachmentPlacement.Matrix(bone, local, Wearer(0f, 0f, 0f));

        Position(placed).ShouldBe((5f, 0f, 10f));
    }

    [Test]
    public void Matrix_AWearerTurnedNinetyDegrees_TurnsTheOffsetWithThem()
    {
        // **The control that catches a missing transpose.** With the wearer facing along +Y, an
        // attachment 10 units along the wearer's own +X must come out at +Y in the world. A
        // composition that skipped the convention change would leave it at +X — a placement that
        // looks fine until the player turns.
        float[] local = Identity();
        local[3] = 10f;

        float[] placed = AttachmentPlacement.Matrix(Identity(), local, Wearer(0f, 0f, 0f, yaw: 90f));

        (float x, float y, float z) = Position(placed);

        x.ShouldBe(0f, 0.001f);
        y.ShouldBe(10f, 0.001f);
        z.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void Matrix_AWorldAlignedAttachment_KeepsItsPlaceAndDropsTheRotation()
    {
        // ATTACHMENT_FLAG_WORLD_ALIGN: SetupBones_AttachmentHelper takes the position through the
        // bone and then builds an IDENTITY matrix around it. A halo stays level while the head it
        // floats above turns, which is the difference between a halo and a hat.
        float[] bone = Rotated90();
        bone[11] = 64f;

        float[] local = Identity();
        local[3] = 10f;

        float[] placed = AttachmentPlacement.Matrix(
            bone, local, Wearer(0f, 0f, 0f), worldAligned: true);

        // The position still goes through the bone's rotation — only the ORIENTATION is discarded.
        (float x, float y, float z) = Position(placed);

        x.ShouldBe(0f, 0.001f);
        y.ShouldBe(10f, 0.001f);
        z.ShouldBe(64f, 0.001f);

        // Identity rotation: the first row is the world's own X axis.
        placed[0].ShouldBe(1f, 0.001f);
        placed[1].ShouldBe(0f, 0.001f);
        placed[2].ShouldBe(0f, 0.001f);
    }

    /// <remarks>
    /// **The stack path must be the array path bit for bit** (B599): `AttachmentsOf` now composes on the stack, and a
    /// reordered sum or a dropped identity product would move a muzzle by a rounding error nobody sees until two runs
    /// disagree. The oracle is the arithmetic the arrays did, written out: `Concatenate`'s sums in their order, the world
    /// align, the transpose, and `Multiply`'s accumulator from 0f — which turns a −0 into +0, so the wearer is the real
    /// identity `PropTransform` makes and every fourth seed composes a row of −0. Compared by bits, both alignments, 200
    /// seeds. Sabotaged 2026-10-30: dropping the identity product reddened it (the −0 rows); reversing the accumulation
    /// order did NOT, and cannot under an identity wearer (every other term is ±0), which is the only wearer production
    /// passes.
    /// </remarks>
    [Test]
    public void Matrix_OnTheStack_IsTheArrayArithmeticBitForBit()
    {
        uint random = 599;
        float[] wearer = Content.Bsp.PropTransform.Identity.ToMatrix();
        int compared = 0;
        Span<float> placed = stackalloc float[16];

        for (int seed = 0; seed < 200; seed++)
        {
            float[] bone = Matrix34(ref random);
            float[] local = Matrix34(ref random);

            // Every fourth seed, a bone row of negative zeros over a positive offset: that row of the composition is −0
            // throughout, which the identity product turns to +0 — the one place the product changes bits.
            if (seed % 4 == 0)
            {
                bone[0] = bone[1] = bone[2] = bone[3] = -0f;

                for (int index = 0; index < 12; index++)
                {
                    local[index] = Math.Abs(local[index]) + 1f;
                }
            }

            foreach (bool aligned in (bool[])[false, true])
            {
                AttachmentPlacement.Matrix(bone, local, wearer, aligned, placed);

                float[] expected = Oracle(bone, local, wearer, aligned);

                for (int index = 0; index < 16; index++)
                {
                    BitConverter.SingleToInt32Bits(placed[index]).ShouldBe(
                        BitConverter.SingleToInt32Bits(expected[index]), $"seed {seed}, aligned {aligned}, element {index}");
                }

                compared++;
            }
        }

        compared.ShouldBe(400);
    }

    /// <remarks>The decision tested as the decision: composing an attachment on the stack allocates nothing.</remarks>
    [Test]
    public void Matrix_OnTheStack_AllocatesNothing()
    {
        float[] bone = Rotated90();
        float[] local = Identity();
        float[] wearer = Content.Bsp.PropTransform.Identity.ToMatrix();
        Span<float> placed = stackalloc float[16];

        AttachmentPlacement.Matrix(bone, local, wearer, true, placed);

        long before = GC.GetAllocatedBytesForCurrentThread();
        AttachmentPlacement.Matrix(bone, local, wearer, false, placed);
        AttachmentPlacement.Matrix(bone, local, wearer, true, placed);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        bytes.ShouldBe(0L);
        placed[12].ShouldBe(0f);
    }

    /// <summary>A 3×4 of values in ±64 with a negative zero in two places, so a sign change shows in the bits.</summary>
    /// <remarks>A fixed linear congruential sequence (Numerical Recipes' constants) rather than `Random`: the inputs repeat.</remarks>
    private static float[] Matrix34(ref uint state)
    {
        float[] matrix = new float[12];

        for (int index = 0; index < 12; index++)
        {
            matrix[index] = (Next(ref state) / (float)uint.MaxValue * 128f) - 64f;
        }

        matrix[Next(ref state) % 12] = -0f;
        matrix[Next(ref state) % 12] = -0f;
        return matrix;
    }

    private static uint Next(ref uint state) => state = unchecked((state * 1664525u) + 1013904223u);

    /// <summary>The arithmetic the array path did before B599, written out as the oracle.</summary>
    private static float[] Oracle(float[] bone, float[] local, float[] wearer, bool aligned)
    {
        float[] point = new float[12];

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                point[(row * 4) + column] =
                    (bone[(row * 4) + 0] * local[column]) +
                    (bone[(row * 4) + 1] * local[4 + column]) +
                    (bone[(row * 4) + 2] * local[8 + column]);
            }

            point[(row * 4) + 3] =
                (bone[(row * 4) + 0] * local[3]) +
                (bone[(row * 4) + 1] * local[7]) +
                (bone[(row * 4) + 2] * local[11]) +
                bone[(row * 4) + 3];
        }

        if (aligned)
        {
            point = [1f, 0f, 0f, point[3], 0f, 1f, 0f, point[7], 0f, 0f, 1f, point[11]];
        }

        float[] model =
        [
            point[0], point[4], point[8], 0f,
            point[1], point[5], point[9], 0f,
            point[2], point[6], point[10], 0f,
            point[3], point[7], point[11], 1f,
        ];

        float[] product = new float[16];

        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                float total = 0f;

                for (int step = 0; step < 4; step++)
                {
                    total += model[(row * 4) + step] * wearer[(step * 4) + column];
                }

                product[(row * 4) + column] = total;
            }
        }

        return product;
    }

    /// <summary>A 3×4 identity, Valve's layout.</summary>
    private static float[] Identity() =>
        [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    /// <summary>A 3×4 turned ninety degrees about Z, Valve's layout.</summary>
    private static float[] Rotated90() =>
        [0f, -1f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f];

    /// <summary>The wearer's model matrix, in the renderer's row-vector layout.</summary>
    private static float[] Wearer(float x, float y, float z, float yaw = 0f)
    {
        float radians = yaw * (MathF.PI / 180f);
        (float sine, float cosine) = MathF.SinCos(radians);

        return
        [
            cosine, sine, 0f, 0f,
            -sine, cosine, 0f, 0f,
            0f, 0f, 1f, 0f,
            x, y, z, 1f,
        ];
    }

    /// <summary>Where a row-vector matrix puts the origin.</summary>
    private static (float X, float Y, float Z) Position(float[] matrix) =>
        (matrix[12], matrix[13], matrix[14]);
}
