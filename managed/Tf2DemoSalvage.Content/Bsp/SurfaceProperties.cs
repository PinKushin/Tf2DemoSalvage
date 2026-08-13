using System;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// Surface flags carried by a texinfo record, from Valve's <c>bspflags.h</c>.
/// </summary>
/// <remarks>
/// **These are why the tool-texture problem mostly goes away.** A map is full of surfaces that
/// exist for the compiler rather than for the player — skybox, nodraw, triggers, hints, skips —
/// and the flags identify all of them without reading a single texture name.
///
/// That matters more than convenience: the alternative is TEXINFO to TEXDATA to a string table to
/// a string blob, four lumps and three more indices to bounds-check, to end up comparing against
/// names like <c>tools/toolsskybox</c>. The flags are one lookup and one bit test, and they are
/// what the engine itself branches on.
/// </remarks>
[Flags]
public enum SurfaceProperties
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>Value will hold the light strength.</summary>
    Light = 0x0001,

    /// <summary>Don't draw, indicates we should skylight and light this surface as sky.</summary>
    Sky2D = 0x0002,

    /// <summary>Don't draw, but add to skybox.</summary>
    Sky = 0x0004,

    /// <summary>Turbulent water warp.</summary>
    Warp = 0x0008,

    /// <summary>Translucent.</summary>
    Translucent = 0x0010,

    /// <summary>The surface can not have a portal placed on it.</summary>
    NoPortal = 0x0020,

    /// <summary>This is a trigger volume's surface.</summary>
    Trigger = 0x0040,

    /// <summary>Don't bother referencing the texture.</summary>
    NoDraw = 0x0080,

    /// <summary>Make a primary bsp splitter.</summary>
    Hint = 0x0100,

    /// <summary>Completely ignore, allowing non-closed brushes.</summary>
    Skip = 0x0200,

    /// <summary>Don't calculate light.</summary>
    NoLight = 0x0400,

    /// <summary>Calculate three lightmaps for the surface for bumpmapping.</summary>
    BumpLight = 0x0800,
}
