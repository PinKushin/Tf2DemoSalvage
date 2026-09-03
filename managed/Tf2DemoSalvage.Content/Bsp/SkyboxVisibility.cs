namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// Which kind of sky a point can see — Valve's <c>SkyboxVisibility_t</c>.
/// </summary>
/// <remarks>
/// <c>cdll_int.h:113</c>, and the order is the engine's because the values are compared rather than
/// merely named:
///
/// <code>
///   enum SkyboxVisibility_t
///   {
///       SKYBOX_NOT_VISIBLE = 0,
///       SKYBOX_3DSKYBOX_VISIBLE,
///       SKYBOX_2DSKYBOX_VISIBLE,
///   };
/// </code>
///
/// **A leaf carries at most one**, because vrad stops at the first sky face it finds in the leaf
/// and decides on that face alone (<c>lightmap.cpp:1355</c>). So this is a choice, not a set.
/// </remarks>
public enum SkyboxVisibility
{
    /// <summary><c>SKYBOX_NOT_VISIBLE</c> — no sky face in this leaf.</summary>
    None = 0,

    /// <summary><c>SKYBOX_3DSKYBOX_VISIBLE</c> — a <c>SURF_SKY</c> face, so the 3D room shows.</summary>
    ThreeDimensional = 1,

    /// <summary>
    /// <c>SKYBOX_2DSKYBOX_VISIBLE</c> — a <c>SURF_SKY2D</c> face, which skylights and draws the flat
    /// sky but explicitly does NOT draw the 3D room.
    /// </summary>
    /// <remarks>
    /// <c>SURF_SKY2D</c>'s own definition says it: *"don't draw, indicates we should skylight + draw
    /// 2d sky but not draw the 3D skybox"* (<c>bspflags.h:81</c>). So this value is the reason the
    /// 3D pass is gated rather than run whenever any sky is in view.
    /// </remarks>
    TwoDimensional = 2,
}
