using System;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Where to put the free camera so it sees the whole map from above.
/// </summary>
/// <remarks>
/// **This is what replaces the orthographic camera (D49).** The requirement was always "a top down
/// map view"; an orthographic projection was one reading of it, and it cost a second projection, a
/// decal bias tuned to suit it, a height cut that is not a height, and two reverted attempts to
/// reconcile the two. The other reading — a perspective camera placed high up and pointed down —
/// gives the same view with one projection.
///
/// **So this computes a PLACEMENT rather than a projection.** The output is an origin and a pair of
/// angles that go straight into <see cref="FreeCameraController"/>, and the camera flies away from it normally
/// afterwards. There is no mode to be in and nothing to switch between.
///
/// **Pitch is 89 rather than 90, and that is not a rounding.** Looking exactly along the world's up
/// axis makes the camera basis degenerate — no right vector — which is why the engine clamps a
/// player to the same figure, and why this project has already been bitten by it once: a movement
/// vector that cancelled at pitch 90 left a floating-point residue that normalised up to full speed
/// (D65). One degree off vertical is imperceptible and well-defined.
/// </remarks>
public static class OverheadPlacement
{
    /// <summary>How far above the tallest geometry the camera sits, at minimum.</summary>
    /// <remarks>
    /// **The "not under the map" guarantee.** Framing distance alone is not enough: a wide, flat map
    /// needs little height to frame and could place the camera below a tall skybox brush or inside a
    /// roof. Taking whichever is greater — the framing height or the tallest geometry plus this —
    /// means the camera starts in open air on every map.
    /// </remarks>
    public const float ClearanceAboveGeometry = 512f;

    /// <summary>Looking down, one degree short of vertical.</summary>
    public const float OverheadPitch = 89f;

    /// <summary>Reshapes what the overhead camera frames, for <c>--look</c> and <c>--zoom</c>.</summary>
    /// <param name="bounds">The play area, as the map reports it.</param>
    /// <param name="lookAt">Where to centre, or null to keep the map's own centre.</param>
    /// <param name="zoom">How much to magnify; 1 frames the whole area, 2 frames half of it.</param>
    /// <returns>The rectangle to hand <see cref="For"/>.</returns>
    /// <remarks>
    /// **These two options were parsed and consumed by nothing at all** until 2026-08-29 (B226).
    /// They were written for the orthographic camera D98 removed, and the fields were kept with a
    /// note calling their meaning for a world-placed camera *"a question for whoever reimplements
    /// them"*, adding that inventing an answer *"would be the guess this project keeps paying
    /// for"*. That caution was right about inventing a NEW behaviour and wrong about the outcome:
    /// an option that is accepted and silently dropped is the same class of defect as B223, where
    /// autoplay was accepted and silently switched off.
    ///
    /// **No invention was needed, which is why this is arithmetic rather than a design.** The
    /// overhead placement is the map view's successor — same requirement, one projection instead of
    /// two — so "centre on this point" and "magnify" still mean exactly what they meant, and both
    /// are expressed by reshaping the rectangle <see cref="For"/> already frames. Nothing about the
    /// placement itself changes.
    ///
    /// **Zoom divides the extent**, so a larger number frames less, as zoom does everywhere. A
    /// non-positive zoom is ignored rather than obeyed: zero divides the extent by nothing and a
    /// negative one turns the rectangle inside out, and the honest answer to a typo is the view the
    /// launch would otherwise have had.
    /// </remarks>
    public static MapBounds Framed(MapBounds bounds, (float X, float Y)? lookAt, float zoom)
    {
        float halfWidth = (bounds.MaxX - bounds.MinX) / 2f;
        float halfHeight = (bounds.MaxY - bounds.MinY) / 2f;

        // Guarded rather than clamped to some minimum: an unusable zoom leaves the view alone, so
        // `--zoom 0` shows the map rather than a degenerate rectangle nobody asked for.
        if (zoom > 0f)
        {
            halfWidth /= zoom;
            halfHeight /= zoom;
        }

        (float X, float Y) centre = lookAt ?? (
            (bounds.MinX + bounds.MaxX) / 2f,
            (bounds.MinY + bounds.MaxY) / 2f);

        return new MapBounds(
            centre.X - halfWidth,
            centre.Y - halfHeight,
            centre.X + halfWidth,
            centre.Y + halfHeight);
    }

    /// <summary>Where to place the camera to frame a map from above.</summary>
    /// <param name="minX">Play area's western edge.</param>
    /// <param name="minY">Play area's southern edge.</param>
    /// <param name="maxX">Play area's eastern edge.</param>
    /// <param name="maxY">Play area's northern edge.</param>
    /// <param name="highestGeometry">Z of the tallest thing in the map.</param>
    /// <param name="fieldOfView">The camera's vertical field of view, in degrees.</param>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>An origin and the angles to look down from it.</returns>
    /// <remarks>
    /// **Framed on whichever axis is tighter.** A map is rarely square and a viewport never is, so
    /// fitting the height alone leaves a wide map cropped left and right. The required distance is
    /// computed for both axes and the larger wins, which is the same arithmetic any "zoom to fit"
    /// does.
    /// </remarks>
    public static ((float X, float Y, float Z) Origin, float Pitch, float Yaw) For(
        float minX,
        float minY,
        float maxX,
        float maxY,
        float highestGeometry,
        float fieldOfView = 75f,
        float aspect = 16f / 9f)
    {
        float centreX = (minX + maxX) / 2f;
        float centreY = (minY + maxY) / 2f;

        float width = MathF.Abs(maxX - minX);
        float depth = MathF.Abs(maxY - minY);

        // Half the vertical field of view, which is the angle from the view axis to the top of the
        // screen — so half the extent divided by its tangent is the distance that just fits it.
        float halfVertical = Math.Clamp(fieldOfView, 1f, 179f) / 2f * (MathF.PI / 180f);
        float tangent = MathF.Tan(halfVertical);

        // Guarded because a degenerate field of view would divide by zero and place the camera at
        // infinity, which reads as the map having vanished rather than as a bad argument.
        float safeTangent = tangent > 0.0001f ? tangent : 0.0001f;
        float safeAspect = aspect > 0.0001f ? aspect : 1f;

        // Looking down, the screen's vertical axis spans the map's Y and its horizontal spans X —
        // so X is fitted against the HORIZONTAL half-angle, which is the vertical one widened by
        // the aspect ratio.
        float forDepth = depth / 2f / safeTangent;
        float forWidth = width / 2f / (safeTangent * safeAspect);

        float framing = MathF.Max(forDepth, forWidth);

        // **Whichever is higher.** Framing alone can sit below a tall skybox on a wide flat map;
        // clearance alone can crop a large one. Taking the maximum satisfies both.
        float height = MathF.Max(framing, highestGeometry + ClearanceAboveGeometry);

        return ((centreX, centreY, height), OverheadPitch, 0f);
    }
}
