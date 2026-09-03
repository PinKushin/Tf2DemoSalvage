using System;

using Tf2DemoSalvage.Content.Bsp;

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
    /// <summary>Whether the 3D sky room is drawn this frame — <c>PreRender3dSkyboxWorld</c>.</summary>
    /// <param name="setting"><c>r_3dsky</c>: 0 off, 1 when visible, 2 always.</param>
    /// <param name="visible">What the eye's leaf can see.</param>
    /// <param name="skyArea">The map's sky area, or −1 when it declares no <c>sky_camera</c>.</param>
    /// <returns>Whether to run the sky pass.</returns>
    /// <remarks>
    /// **<c>viewrender.cpp:4843</c>, in Valve's order, and the order is why this is not a bool:**
    ///
    /// <code>
    ///   if ( ( nSkyboxVisible != SKYBOX_3DSKYBOX_VISIBLE ) &amp;&amp; r_3dsky.GetInt() != 2 )
    ///       return NULL;
    ///
    ///   // render the 3D skybox
    ///   if ( !r_3dsky.GetInt() )
    ///       return NULL;
    ///
    ///   ...
    ///   if ( local-&gt;m_skybox3d.area == 255 )
    ///       return NULL;
    /// </code>
    ///
    /// **`r_3dsky` is an INT with three meanings, not a switch.** Zero is off; one draws the room
    /// only when a `SURF_SKY` face is in view; two draws it regardless, which is how you see it
    /// from somewhere the map never expected — a free camera outside the level, which this viewer
    /// has and TF2 does not. Reading it as a bool would silently delete the third state.
    ///
    /// **`area == 255` is Valve's "no 3D sky"**, a byte sentinel; ours is −1 for the same reason
    /// the rest of this reader uses −1, and it is checked LAST here as it is there.
    /// </remarks>
    public static bool Draws(int setting, SkyboxVisibility visible, int skyArea)
    {
        if (visible != SkyboxVisibility.ThreeDimensional && setting != 2)
        {
            return false;
        }

        return setting != 0 && skyArea >= 0;
    }

    /// <summary><c>r_3dsky</c>'s default — <c>"1"</c>, and NOT cheat-gated.</summary>
    /// <remarks>
    /// <c>static ConVar r_3dsky( "r_3dsky","1", 0, "Enable the rendering of 3d sky boxes" );</c>
    /// (<c>viewrender.cpp:113</c>). The zero is the flags argument: no <c>FCVAR_CHEAT</c>, unlike
    /// <c>r_skybox</c> on the line below it. So turning the 3D sky off is something a player is
    /// allowed to do in a real game, which is exactly the owner's requirement — competitive players
    /// run without it and video makers run with it.
    /// </remarks>
    public const int DrawsByDefault = 1;

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

    /// <summary>The camera the sky room is drawn through.</summary>
    /// <param name="viewer">The main view.</param>
    /// <param name="skyCamera">The map's <c>sky_camera</c> origin.</param>
    /// <param name="scale">Its <c>scale</c>.</param>
    /// <returns>The same view, moved and with the sky's own near and far planes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewer"/> is null.</exception>
    /// <remarks>
    /// **Everything except the position and the planes is COPIED, not recomputed.** The angles,
    /// the field of view and the aspect are the main view's, so the sky cannot disagree with the
    /// world about where the player is looking or how wide the lens is — the same rule as
    /// `docs/memory/one-camera-or-the-cull-lies.md`, applied to a second view rather than to a
    /// second derivation of one.
    /// </remarks>
    public static FreeCamera CameraFor(
        FreeCamera viewer, (float X, float Y, float Z) skyCamera, float scale)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        return new FreeCamera
        {
            Origin = OriginFor(viewer.Origin, skyCamera, scale),
            Angles = viewer.Angles,
            FieldOfView = viewer.FieldOfView,
            Aspect = viewer.Aspect,
            NearZ = NearPlane,
            FarZ = FarPlane,
        };
    }
}
