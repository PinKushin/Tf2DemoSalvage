using System;
using System.Numerics;

namespace Tf2DemoSalvage.Scene;

/// <summary>Whether a glow is in sight — `GlowSightDistance( glowOrigin, true )` (`c_pixel_visibility.cpp:783`).</summary>
/// <remarks>
/// **The engine's own fallback for a glow's occlusion.** `PixelVisibility_FractionVisible` answers from a GPU query when it
/// has one, and otherwise `GlowSightDistance( params.position, true ) &gt; 0.0f ? 1.0f : 0.0f` (`:825`): a world trace from
/// the view origin, all or nothing, ended 4 units short of the glow along the view direction — *"HACKHACK: trace 4" from
/// destination in case the glow is inside some parent object"*. A lamp's halo below a roof is hidden by it; without this
/// it was drawn straight through.
/// </remarks>
public static class GlowSight
{
    /// <summary>How far short of the glow the trace ends.</summary>
    private const float Allowance = 4f;

    /// <summary>Whether the view reaches a glow.</summary>
    /// <param name="eye">`CurrentViewOrigin()`.</param>
    /// <param name="forward">`CurrentViewForward()`, a unit vector.</param>
    /// <param name="glow">The glow's origin.</param>
    /// <param name="reaches">Whether a world trace from the first point reaches the second.</param>
    /// <returns>True when nothing stops the trace.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reaches"/> is null.</exception>
    public static bool Visible(Vector3 eye, Vector3 forward, Vector3 glow, Func<Vector3, Vector3, bool> reaches)
    {
        ArgumentNullException.ThrowIfNull(reaches);

        Vector3 end = Vector3.Distance(glow, eye) > Allowance ? glow - (forward * Allowance) : glow;

        return reaches(eye, end);
    }
}
