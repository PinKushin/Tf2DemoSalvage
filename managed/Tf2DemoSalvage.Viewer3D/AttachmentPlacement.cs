using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Puts an item at a named point on its wearer.
/// </summary>
/// <remarks>
/// **The other way an item rides a wearer.** A hat shares bone names with the player and is
/// bone-merged, taking their matrices outright. A halo, an MvM canteen, a spellbook and a spy's
/// sapper share no bone name at all, and the engine hangs those from a named attachment — without
/// which they fall back to the wearer's transform, which on a player is their feet (RISKS B82).
///
/// The engine's composition, <c>SetupBones_AttachmentHelper</c>:
///
/// <code>
/// ConcatTransforms( GetBone( iBone ), pattachment.local, world );
/// </code>
///
/// **Two matrix conventions meet here.** Valve's <c>matrix3x4_t</c> — a bone, and an attachment's
/// <c>local</c> — is row major with the translation in COLUMN three and transforms a column vector.
/// This renderer's model matrix is row major with the translation in ROW three and transforms a row
/// vector, because that is what the shader's <c>row_major</c> declaration wants. Composing them is
/// therefore a transpose plus a move, and skipping it produces an item somewhere on the wearer
/// facing the wrong way — a plausible placement rather than an error.
/// </remarks>
internal static class AttachmentPlacement
{
    /// <summary>Where an item hanging from an attachment should be drawn.</summary>
    /// <param name="boneToWorld">The wearer's bone, 3×4 in Valve's layout, in the wearer's space.</param>
    /// <param name="local">The attachment's own offset from that bone, 3×4.</param>
    /// <param name="wearer">The wearer's model matrix, sixteen floats in the renderer's layout.</param>
    /// <param name="worldAligned">Whether to keep the position and discard the orientation.</param>
    /// <returns>Sixteen floats, ready to be an instance's matrix.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// <paramref name="worldAligned"/> is <c>ATTACHMENT_FLAG_WORLD_ALIGN</c>
    /// (<c>studio.h:508</c>). The engine takes the local position through the bone and then builds
    /// an identity matrix around it, so such an attachment does not turn with what it hangs from —
    /// a halo stays level while the head beneath it looks around.
    /// </remarks>
    public static float[] Matrix(
        IReadOnlyList<float> boneToWorld,
        IReadOnlyList<float> local,
        IReadOnlyList<float> wearer,
        bool worldAligned = false)
    {
        ArgumentNullException.ThrowIfNull(boneToWorld);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(wearer);

        // The bone first and the attachment second, which is ConcatTransforms' own order: the
        // attachment is expressed in the bone's space. The reverse agrees on a pure translation and
        // differs the moment either carries a rotation, which is why the tests use one.
        float[] point = MatrixConvention.Concatenate(boneToWorld, local);

        if (worldAligned)
        {
            // Position only. MatrixGetColumn/SetColumn around an identity, as the engine does it.
            point = [1f, 0f, 0f, point[3], 0f, 1f, 0f, point[7], 0f, 0f, 1f, point[11]];
        }

        // **The convention change happens in one place**, which is the whole reason
        // MatrixConvention exists: this was the second site to need it and the second to implement
        // it, and two implementations of a boundary is how they come to disagree.
        return MatrixConvention.Multiply(MatrixConvention.ToModelMatrix(point), wearer);
    }
}
