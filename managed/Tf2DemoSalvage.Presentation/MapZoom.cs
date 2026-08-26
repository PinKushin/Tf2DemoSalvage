using System;

namespace Tf2DemoSalvage.Presentation;

/// <summary>How far the overhead camera is zoomed in, where one is the fitted view.</summary>
/// <remarks>
/// **This was a bare `float` field in `MainForm`, plus `Math.Clamp(_zoom * step, 1f, 64f)` and the
/// recentring formula, inside the mouse-wheel handler** (B208).
///
/// **Moving it answers nothing about B205**, which asks whether the overhead camera should survive
/// at all now that it is reachable only as a first-person fallback. That is a behaviour question and
/// remains the owner's; this is only about where the arithmetic lives.
///
/// **The constructor is private and <see cref="Of"/> clamps**, because a launch option feeds this
/// factor straight from the command line. `--zoom 1000` must not put the camera inside a wall and
/// `--zoom -3` must not invert the projection, and a clamp a caller can skip is one that eventually
/// gets skipped.
/// </remarks>
public readonly record struct MapZoom
{
    private MapZoom(float factor) => Factor = factor;

    /// <summary>The whole map in view.</summary>
    public const float Fitted = 1f;

    /// <summary>As close as the camera goes.</summary>
    public const float Closest = 64f;

    /// <summary>What one wheel notch multiplies or divides by.</summary>
    /// <remarks>
    /// **Multiplicative, not additive, and that is what makes a wheel feel right.** A fixed addition
    /// crawls when zoomed out and leaps when zoomed in; a ratio covers the same visual proportion at
    /// every distance. It is also what makes <see cref="In"/> and <see cref="Out"/> exact inverses —
    /// a fixed step drifts, and only after several notches, so it reads as the wheel being imprecise
    /// rather than as an arithmetic choice.
    /// </remarks>
    public const float Step = 1.25f;

    /// <summary>The fitted view: the whole map, no zoom.</summary>
    public static MapZoom None => new(Fitted);

    /// <summary>How far in, where one is the fitted view.</summary>
    public float Factor { get; }

    /// <summary>A zoom at the given factor, clamped to what the camera allows.</summary>
    /// <param name="factor">The wanted factor.</param>
    /// <returns>The zoom.</returns>
    public static MapZoom Of(float factor) => new(Math.Clamp(factor, Fitted, Closest));

    /// <summary>Where the camera must look so a world point stays under the same pixel.</summary>
    /// <param name="centre">Where the camera looks now.</param>
    /// <param name="before">The world point under the cursor before the zoom.</param>
    /// <param name="after">The world point under that same pixel after it.</param>
    /// <returns>The centre that cancels the drift.</returns>
    /// <remarks>
    /// **Zoom-at-cursor, which is the behaviour every map and editor has**: whatever is under the
    /// pointer stays under it. Zooming about the centre instead means aiming at something, zooming,
    /// and finding it has slid off the edge.
    ///
    /// **The subtraction order is the whole of it**, and it is easy to reverse: the point drifted
    /// from `before` to `after`, so the centre moves by `before - after` to cancel that drift. Get it
    /// backwards and the view lurches away from the cursor at double speed.
    /// </remarks>
    public static (float X, float Y) Recentre(
        (float X, float Y) centre, (float X, float Y) before, (float X, float Y) after) =>
        (centre.X + (before.X - after.X), centre.Y + (before.Y - after.Y));

    /// <summary>One notch closer.</summary>
    /// <returns>The new zoom.</returns>
    public MapZoom In() => Of(Factor * Step);

    /// <summary>One notch further out.</summary>
    /// <returns>The new zoom.</returns>
    public MapZoom Out() => Of(Factor / Step);
}
