using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// Where the camera stands to draw a map's 3D skybox — Valve's <c>CSkyboxView</c>.
/// </summary>
/// <remarks>
/// **A TF2 map keeps a miniature copy of its surroundings far outside the level**, and the engine
/// draws it as a separate view before the world, from a camera that moves a fraction as far as the
/// player does. That fraction is what makes distant scenery hold still while near scenery slides
/// past — the whole illusion — and drawing the room at its literal size and position instead is
/// B152, which is what this viewer did.
///
/// **<c>CSkyboxView::DrawInternal</c>, <c>viewrender.cpp:4886</c>**, and the transform is three
/// lines:
///
/// <code>
///   zNear = 2.0;
///   zFar = MAX_TRACE_LENGTH;
///
///   // scale origin by sky scale
///   if ( m_pSky3dParams-&gt;scale &gt; 0 )
///   {
///       float scale = 1.0f / m_pSky3dParams-&gt;scale;
///       VectorScale( origin, scale, origin );
///   }
///   Enable3dSkyboxFog();
///   VectorAdd( origin, m_pSky3dParams-&gt;origin, origin );
/// </code>
///
/// **The ANGLES are not touched**, which is the half that is easy to miss and the reason the
/// illusion survives looking around: the sky view faces exactly where the player faces, and only
/// the position is compressed.
///
/// **A non-positive scale means DO NOT DIVIDE**, not "divide by something else". Valve guards the
/// division rather than the whole transform, so a `sky_camera` with `scale 0` still offsets by the
/// camera's origin — which places the sky room around the player rather than leaving it where it
/// was built.
///
/// **Where the near plane comes from, in Valve's own words** (the comment above `zNear = 2.0`):
/// *"if you can get really close to the skybox geometry it's possible that you'll be able to clip
/// into it with this near plane. If so, move it in a bit. It's at 2.0 to give us more precision.
/// That means you need to keep the eye position at least 2 * scale away from the geometry in the
/// skybox"*.
/// </remarks>
public static class SkyboxView
{
    /// <summary>The sky view's near plane — Valve's literal <c>zNear = 2.0</c>.</summary>
    public const float NearPlane = 2f;

    /// <summary>
    /// The sky view's far plane — <c>MAX_TRACE_LENGTH</c>, the diagonal of the coordinate space.
    /// </summary>
    /// <remarks>
    /// <c>#define MAX_TRACE_LENGTH ( 1.732050807569 * COORD_EXTENT )</c> with
    /// <c>COORD_EXTENT = 2*MAX_COORD_INTEGER</c> and <c>MAX_COORD_INTEGER = 16384</c>
    /// (<c>worldsize.h:19</c>), so 1.732050807569 × 32768. Written out rather than left as a round
    /// number, because it is a specific quantity — the longest straight line through a cubic world
    /// — and rounding it would be a departure with no reason behind it.
    /// </remarks>
    public const float FarPlane = 1.732050807569f * (2f * 16384f);

    /// <summary>Where the camera stands to draw the sky room.</summary>
    /// <param name="viewer">The main view's origin.</param>
    /// <param name="skyCamera">The map's <c>sky_camera</c> origin.</param>
    /// <param name="scale">Its <c>scale</c>; a non-positive value skips the division.</param>
    /// <returns>The sky view's origin. The angles are the main view's, unchanged.</returns>
    public static (float X, float Y, float Z) OriginFor(
        (float X, float Y, float Z) viewer,
        (float X, float Y, float Z) skyCamera,
        float scale)
    {
        // `if ( m_pSky3dParams->scale > 0 )` — the guard is on the DIVISION alone, so a zero or
        // negative scale still receives the offset below.
        if (scale > 0f)
        {
            float by = 1f / scale;

            viewer = (viewer.X * by, viewer.Y * by, viewer.Z * by);
        }

        return (viewer.X + skyCamera.X, viewer.Y + skyCamera.Y, viewer.Z + skyCamera.Z);
    }
}
