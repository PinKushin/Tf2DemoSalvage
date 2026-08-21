using System;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

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
