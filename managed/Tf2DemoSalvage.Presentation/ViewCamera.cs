using System;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Which camera the world is drawn through, and where the overhead one sits.</summary>
/// <remarks>
/// **This was <c>MainForm.ViewMatrix</c> and <c>MainForm.MapCamera</c>** (B188, D90). Neither needs
/// a window: one is a choice between three cameras and the other is arithmetic over a map's bounds.
/// What the form contributed was the viewport's size, which is two integers.
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
/// </remarks>
public static class ViewCamera
{
    /// <summary>How far an empty map's overhead camera reaches, in world units.</summary>
    /// <remarks>
    /// **A demo whose map failed to load still has to draw something.** Fitting to bounds that do
    /// not exist divides by zero; 1024 units is a room-sized view, which reads as "nothing here"
    /// rather than as a broken projection.
    /// </remarks>
    public const float EmptyMapExtent = 1024f;

    /// <summary>The overhead camera, fitted to the map and then zoomed and panned.</summary>
    /// <param name="map">The loaded level, or null when none is open.</param>
    /// <param name="width">The viewport's width in pixels.</param>
    /// <param name="height">The viewport's height in pixels.</param>
    /// <param name="zoom">How far in, where one is the fitted view.</param>
    /// <param name="lookingAt">Where the view is centred, or null to keep the map's centre.</param>
    /// <returns>The camera.</returns>
    /// <remarks>
    /// **Fitted to the MAP rather than to the players.** A camera that reframes itself around
    /// wherever the players happen to be turns every scrub into a jump — the world should sit still
    /// while the players move within it.
    ///
    /// **`MainBounds` rather than `Bounds`**, so a 3D skybox room sitting thousands of units outside
    /// the level does not shrink the play area to a dot.
    ///
    /// **D35: the camera projects height, so it has to know the range.** The geometry carries world
    /// Z; without the range the third row is a pass-through and every surface lands at a depth of
    /// its own world height in units, far outside the clip range, drawing nothing at all.
    /// </remarks>
    public static TopDownCamera Overhead(
        LoadedMap? map, int width, int height, float zoom, (float X, float Y)? lookingAt)
    {
        (float MinX, float MinY, float MaxX, float MaxY) bounds = map?.Outline is { } loaded
            ? (loaded.MainBounds.MinX, loaded.MainBounds.MinY,
               loaded.MainBounds.MaxX, loaded.MainBounds.MaxY)
            : (-EmptyMapExtent, -EmptyMapExtent, EmptyMapExtent, EmptyMapExtent);

        TopDownCamera fitted = TopDownCamera.Fit(
            [
                (bounds.MinX, bounds.MinY),
                (bounds.MaxX, bounds.MaxY),
            ],
            Math.Max(1, width),
            Math.Max(1, height));

        TopDownCamera zoomed = zoom > 1f ? fitted.WithZoom(zoom) : fitted;

        TopDownCamera placed = lookingAt is { } centre
            ? zoomed.LookingAt(centre.X, centre.Y)
            : zoomed;

        return map?.HeightRange is { } range
            ? placed.WithHeights(range.Lowest, range.Highest)
            : placed;
    }

    /// <summary>The view matrix for whichever camera mode is active.</summary>
    /// <param name="firstPerson">Whether the view is through a player's eyes.</param>
    /// <param name="eye">That player's camera, or null when nobody's eyes are available.</param>
    /// <param name="freeLook">Whether the free camera is active.</param>
    /// <param name="free">The free camera.</param>
    /// <param name="overhead">The overhead camera, used when neither of the above applies.</param>
    /// <returns>Sixteen floats for the camera constant buffer.</returns>
    /// <exception cref="ArgumentNullException">A camera is null.</exception>
    /// <remarks>
    /// **First person FALLS BACK rather than failing.** A demo can lose its subject mid-playback —
    /// the recorded view runs out before the first packet, and a spectated player can leave — and a
    /// black screen would read as a rendering fault rather than as the end of the material. That is
    /// why the eye is passed as a nullable and not asserted.
    /// </remarks>
    public static float[] Matrix(
        bool firstPerson, FreeCamera? eye, bool freeLook, FreeCamera free, TopDownCamera overhead)
    {
        ArgumentNullException.ThrowIfNull(free);
        ArgumentNullException.ThrowIfNull(overhead);

        if (firstPerson && eye is { } through)
        {
            return through.ToMatrix();
        }

        return freeLook ? free.ToMatrix() : overhead.ToMatrix();
    }
}
