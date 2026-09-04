using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// What <c>FormatViewModelAttachment</c> reads out of the view — <c>CViewSetup</c> plus the main
/// view vectors.
/// </summary>
/// <param name="Eye"><c>pViewSetup-&gt;origin</c>.</param>
/// <param name="Right"><c>MainViewRight()</c>.</param>
/// <param name="Up"><c>MainViewUp()</c>.</param>
/// <param name="Forward"><c>MainViewForward()</c>.</param>
/// <param name="WorldFieldOfView"><c>pViewSetup-&gt;fov</c>, in degrees.</param>
/// <param name="ViewmodelFieldOfView"><c>pViewSetup-&gt;fovViewmodel</c>, in degrees.</param>
/// <remarks>
/// **One record rather than six arguments threaded through the pose path**, so the correction and
/// the camera that drew the frame cannot come apart — the rule in
/// <c>docs/memory/one-camera-or-the-cull-lies.md</c> applied to a third consumer of the view.
/// </remarks>
public readonly record struct ViewmodelProjection(
    (float X, float Y, float Z) Eye,
    (float X, float Y, float Z) Right,
    (float X, float Y, float Z) Up,
    (float X, float Y, float Z) Forward,
    float WorldFieldOfView,
    float ViewmodelFieldOfView);

/// <summary>
/// Corrects an attachment point on a VIEWMODEL for the projection it is drawn through.
/// </summary>
/// <remarks>
/// **A viewmodel renders with its own field of view, so a point taken from its bones is in the
/// wrong place in the world.** `SetupBones_AttachmentHelper` calls this on every attachment it
/// resolves (`c_baseanimating.cpp:2081`) — a virtual whose base body is EMPTY, so it does nothing
/// for a world model and everything for a viewmodel.
///
/// **`FormatViewModelAttachment`, `c_baseviewmodel.cpp:49`, and the whole of it:**
///
/// <code>
///   float worldx = tan( pViewSetup-&gt;fov * M_PI/360.0 );
///   float viewx  = tan( pViewSetup-&gt;fovViewmodel * M_PI/360.0 );
///   float factorX = viewx ? ( worldx / viewx ) : 0.0f;
///   float factorY = factorX;
///
///   Vector tmp = vOrigin - pViewSetup-&gt;origin;
///   Vector vTransformed( MainViewRight().Dot( tmp ), MainViewUp().Dot( tmp ),
///                        MainViewForward().Dot( tmp ) );
///   // ...squash x and y by the factors...
///   Vector vOut = (MainViewRight() * vTransformed.x) + (MainViewUp() * vTransformed.y)
///               + (MainViewForward() * vTransformed.z);
///   vOrigin = pViewSetup-&gt;origin + vOut;
/// </code>
///
/// **`M_PI/360` is a half-angle in radians, not a typo for `/180`.** `fov` is the full angle, so
/// halving it and converting is one division; writing `/180` would take the tangent of the whole
/// field of view and give a factor roughly squared.
///
/// **Only the POSITION moves.** `C_BaseViewModel::FormatViewModelAttachment` reads the matrix's
/// translation, corrects it and writes it back with `PositionMatrix` — the rotation is untouched,
/// so an attached model keeps the angle its bone gave it.
///
/// **The factor is the same for x and y**, and Valve says why in a comment: *"aspect ratio cancels
/// out, so only need one factor"*.
///
/// **A viewmodel FOV of zero gives a factor of ZERO, not one.** Valve's `viewx ? … : 0.0f` is
/// deliberate and its comment names the case: *"NOTE: viewx was coming in as 0 when folks set their
/// viewmodel_fov to 0 and show their weapon."* That collapses the attachment onto the view axis
/// rather than leaving it where it was, and reproducing it matters because `viewmodel_fov 0` is a
/// thing people type.
/// </remarks>
public static class ViewmodelAttachment
{
    /// <summary>Corrects an attachment position for the viewmodel's projection.</summary>
    /// <param name="point">The attachment's world position, from the viewmodel's bones.</param>
    /// <param name="eye">The view's origin — <c>pViewSetup-&gt;origin</c>.</param>
    /// <param name="right">The view's right vector — <c>MainViewRight()</c>.</param>
    /// <param name="up">The view's up vector — <c>MainViewUp()</c>.</param>
    /// <param name="forward">The view's forward vector — <c>MainViewForward()</c>.</param>
    /// <param name="worldFieldOfView">The world's field of view, in degrees.</param>
    /// <param name="viewmodelFieldOfView">The viewmodel's field of view, in degrees.</param>
    /// <param name="inverse">
    /// Whether to undo the correction rather than apply it — <c>UncorrectViewModelAttachment</c>,
    /// which the engine uses to turn a corrected point back into one the viewmodel's own space
    /// understands.
    /// </param>
    /// <returns>The corrected position.</returns>
    public static (float X, float Y, float Z) Correct(
        (float X, float Y, float Z) point,
        (float X, float Y, float Z) eye,
        (float X, float Y, float Z) right,
        (float X, float Y, float Z) up,
        (float X, float Y, float Z) forward,
        float worldFieldOfView,
        float viewmodelFieldOfView,
        bool inverse = false)
    {
        // `tan( fov * M_PI/360.0 )` — the HALF angle, in radians.
        float worldX = MathF.Tan(worldFieldOfView * (MathF.PI / 360f));
        float viewX = MathF.Tan(viewmodelFieldOfView * (MathF.PI / 360f));

        float factorX = viewX != 0f ? worldX / viewX : 0f;
        float factorY = factorX;

        (float X, float Y, float Z) offset =
            (point.X - eye.X, point.Y - eye.Y, point.Z - eye.Z);

        float x = Dot(right, offset);
        float y = Dot(up, offset);
        float z = Dot(forward, offset);

        if (inverse)
        {
            // Valve zeroes rather than dividing when a factor is zero, which is the same refusal
            // as the forward direction's: an undefined scale collapses the axis.
            if (factorX != 0f && factorY != 0f)
            {
                x /= factorX;
                y /= factorY;
            }
            else
            {
                x = 0f;
                y = 0f;
            }
        }
        else
        {
            x *= factorX;
            y *= factorY;
        }

        return (
            eye.X + (right.X * x) + (up.X * y) + (forward.X * z),
            eye.Y + (right.Y * x) + (up.Y * y) + (forward.Y * z),
            eye.Z + (right.Z * x) + (up.Z * y) + (forward.Z * z));
    }

    private static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
