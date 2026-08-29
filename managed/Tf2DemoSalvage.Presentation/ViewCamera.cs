using System;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Which camera the world is drawn through.</summary>
/// <remarks>
/// **This was <c>MainForm.ViewMatrix</c> and <c>MainForm.MapCamera</c>** (B188, D90). Choosing a
/// camera does not need a window.
///
/// **The choice is Valve's `CalcView` dispatch.** `C_BasePlayer::CalcView` (`c_baseplayer.h:112`)
/// branches to `CalcObserverView`, which branches again to `CalcInEyeCamView`, `CalcChaseCamView` or
/// `CalcRoamingView` (`:455`, `:463`). One chooser, called from one place — which is the shape this
/// takes, and the reason it is one method rather than a conditional at each draw site.
///
/// **One chooser rather than a ternary per call site, and that is not tidiness.** There were two
/// places that set the camera — the world draw and the resize path — and they were a copied ternary
/// apart. Adding a third mode to a copied ternary is how this viewer's two drawing paths drifted
/// until one of them stopped showing decals.
///
/// **`Overhead` and `EmptyMapExtent` were here until 2026-08-26** (D98). They built the orthographic
/// top-down projection, which D49 had already removed as a *mode* while leaving the projection
/// reachable by one path — first person with no eye. That path now falls back to the free camera,
/// and with it the last caller of `TopDownCamera` from the view is gone.
/// </remarks>
public static class ViewCamera
{
    /// <summary>The view matrix for whichever camera is active.</summary>
    /// <param name="firstPerson">Whether the view is through a player's eyes.</param>
    /// <param name="eye">That player's camera, or null when nobody's eyes are available.</param>
    /// <param name="free">The free camera, which is what everything else falls back to.</param>
    /// <returns>Sixteen floats for the camera constant buffer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="free"/> is null.</exception>
    /// <remarks>
    /// **First person FALLS BACK rather than failing.** A demo can lose its subject mid-playback —
    /// the recorded view runs out before the first packet, and a spectated player can leave — and a
    /// black screen would read as a rendering fault rather than as the end of the material. That is
    /// why the eye is passed as a nullable and not asserted.
    ///
    /// **It falls back to the FREE camera as of D98**, not to an overhead placement of it. A viewer
    /// that loses its subject drops into the view it can always offer, which is also the view the
    /// user was given at launch.
    ///
    /// **Two arguments where there were five**, because `_freeLook` and `_firstPerson` were exact
    /// complements — `CameraMode` has had two members since D49 — so "which mode" and "is there an
    /// eye" were the only questions ever being asked.
    /// </remarks>
    public static float[] Matrix(bool firstPerson, FreeCamera? eye, FreeCamera free) =>
        Active(firstPerson, eye, free).ToMatrix();

    /// <summary>Which camera the frame is seen through, by mode.</summary>
    /// <param name="mode">Which view the user asked for.</param>
    /// <param name="eye">The in-eye camera, or null when nobody's eyes are available.</param>
    /// <param name="chase">The chase camera, or null when there is no target to chase.</param>
    /// <param name="free">The free camera, which everything falls back to.</param>
    /// <returns>The camera to project, cull and measure with.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="free"/> is null.</exception>
    /// <remarks>
    /// **The two-argument form below could not express three modes**, and the comment on it says why
    /// it only ever needed two: `CameraMode` had two members. It has three now
    /// (<see cref="CameraMode.ThirdPerson"/>), so "which mode" is a mode again rather than a flag.
    ///
    /// **Every mode still falls back to the free camera**, per D98 — a viewer that loses its subject
    /// drops into the view it can always offer. That is why both nullable cameras are asked for
    /// rather than assumed: first person on a demo with no eye, and chase with nobody to chase, are
    /// ordinary states rather than faults.
    /// </remarks>
    public static FreeCamera Active(
        CameraMode mode, FreeCamera? eye, FreeCamera? chase, FreeCamera free)
    {
        ArgumentNullException.ThrowIfNull(free);

        return mode switch
        {
            CameraMode.FirstPerson when eye is { } through => through,
            CameraMode.ThirdPerson when chase is { } behind => behind,
            _ => free,
        };
    }

    /// <summary>Which camera the frame is actually seen through.</summary>
    /// <param name="firstPerson">Whether the view is through a player's eyes.</param>
    /// <param name="eye">That player's camera, or null when nobody's eyes are available.</param>
    /// <param name="free">The free camera, which is what everything else falls back to.</param>
    /// <returns>The camera to project, cull and measure with.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="free"/> is null.</exception>
    /// <remarks>
    /// **Extracted so the projection and the view frustum cannot come from different cameras.**
    /// <see cref="Matrix"/> used to make this choice inline, which was fine while a matrix was the
    /// only thing derived from it. Now that a cull is derived too, "which camera" has to be one
    /// answer given once — a frustum built from the free camera while the picture is drawn through
    /// a player's eyes culls the geometry the viewer is looking at, and the symptom is the world
    /// disappearing in first person only.
    ///
    /// **Kept as the two-mode form while callers migrate to the mode-taking overload above.** It is
    /// that overload with `mode` collapsed to a boolean and no chase camera, which is exactly what
    /// it meant before `CameraMode` had a third member.
    /// </remarks>
    public static FreeCamera Active(bool firstPerson, FreeCamera? eye, FreeCamera free)
    {
        ArgumentNullException.ThrowIfNull(free);

        return firstPerson && eye is { } through ? through : free;
    }
}
