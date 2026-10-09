namespace Tf2DemoSalvage.Scene;

/// <summary>A left-handed viewmodel — whether to draw one, and the mirror it is drawn through (B515).</summary>
/// <remarks>Citations and the cases: <c>ViewmodelFlipConformanceTests</c>.</remarks>
public static class ViewmodelFlip
{
    /// <summary>`C_BaseViewModel::ShouldFlipViewModel` (`c_baseviewmodel.cpp:215-236`), TF branch.</summary>
    /// <param name="weaponFlips">The weapon's own `m_bFlipViewModel` — its item's `flip_viewmodel`.</param>
    /// <param name="spectatedFlips">
    /// The spectated player's `m_bFlipViewModels` when the local player is an observer, else null.
    /// </param>
    /// <param name="watcherFlips">The watching client's own `cl_flipviewmodels`.</param>
    /// <returns>Whether the viewmodel is mirrored.</returns>
    public static bool ShouldFlip(bool weaponFlips, bool? spectatedFlips, bool watcherFlips) =>
        weaponFlips != (spectatedFlips ?? watcherFlips);

    /// <summary>
    /// `ApplyBoneMatrixTransform`'s whole effect as one matrix: into view space, negate view-space Y, back out
    /// (`c_baseviewmodel.cpp:243-270`).
    /// </summary>
    /// <param name="eye">The player view's origin — `pSetup->origin`.</param>
    /// <param name="right">The player view's right vector; view-space Y is its negation, and the sign cancels.</param>
    /// <returns>A row-major 3x4: the reflection through the plane containing the eye, forward and up.</returns>
    /// <remarks>
    /// `viewInverse · diag(1, -1, 1) · view` is `I - 2 r rᵀ` on the rotation and `2 (r · eye) r` on the translation —
    /// arithmetic, so the per-bone work is one concatenate instead of three.
    /// </remarks>
    public static float[] Reflection((float X, float Y, float Z) eye, (float X, float Y, float Z) right)
    {
        (float x, float y, float z) = right;
        float along = 2f * ((x * eye.X) + (y * eye.Y) + (z * eye.Z));

        return
        [
            1f - (2f * x * x), -2f * x * y, -2f * x * z, along * x,
            -2f * y * x, 1f - (2f * y * y), -2f * y * z, along * y,
            -2f * z * x, -2f * z * y, 1f - (2f * z * z), along * z,
        ];
    }
}
