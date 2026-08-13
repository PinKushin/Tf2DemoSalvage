using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Maps Source world coordinates to normalised device coordinates for a top-down view.
/// </summary>
/// <remarks>
/// **Arithmetic, deliberately holding no Direct3D at all.** The renderer's job is to draw points
/// it is handed; deciding where those points go is a separate question that can then be tested
/// exactly and without a graphics adapter. Fitting logic living inside a draw call would be
/// testable only by looking at a screen, which is the least reliable instrument available.
///
/// **The scale is shared between the axes**, which is what stops a map stretching as the window
/// changes shape. Fitting each axis independently is the obvious implementation and produces a
/// view that looks correct at one window size and subtly wrong at every other — players drifting
/// apart horizontally as the window widens.
///
/// **Y is flipped.** Source's Y axis points north and a screen's points down. Getting this wrong
/// mirrors the map, and it is easy to miss because every competitive TF2 map is close to
/// symmetric — the mistake shows up as "spawn is on the wrong side", not as anything obviously
/// broken.
/// </remarks>
internal sealed class TopDownCamera
{
    /// <summary>Used when the bounds have no extent, so a lone entity still renders.</summary>
    private const float MinimumHalfExtent = 1f;

    private readonly float _centreX;
    private readonly float _centreY;
    private readonly float _scaleX;
    private readonly float _scaleY;

    /// <summary>World height mapped to the near plane, and to the far one.</summary>
    /// <remarks>
    /// Equal by default, which means "not told", and <see cref="ToMatrix"/> then passes the third
    /// component through untouched. That keeps a caller that still supplies a precomputed depth
    /// working rather than silently flattening its geometry.
    /// </remarks>
    private readonly float _lowest;
    private readonly float _highest;

    private TopDownCamera(
        float centreX, float centreY, float scaleX, float scaleY,
        float lowest = 0f, float highest = 0f)
    {
        _centreX = centreX;
        _centreY = centreY;
        _scaleX = scaleX;
        _scaleY = scaleY;
        _lowest = lowest;
        _highest = highest;
    }

    /// <summary>Builds a camera showing every point, without distorting them.</summary>
    /// <param name="points">World positions to fit; may be empty.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <returns>A camera whose <see cref="Project"/> puts every point inside [-1, 1].</returns>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is null.</exception>
    public static TopDownCamera Fit(
        IEnumerable<(float X, float Y)> points, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(points);

        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        foreach ((float x, float y) in points)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        // Nothing to fit: before a demo is loaded the render loop still runs, and it needs a
        // camera that projects to finite numbers rather than one that throws or yields NaN.
        if (float.IsInfinity(minX))
        {
            minX = minY = -MinimumHalfExtent;
            maxX = maxY = MinimumHalfExtent;
        }

        float centreX = (minX + maxX) / 2f;
        float centreY = (minY + maxY) / 2f;

        // A single point has zero extent, and dividing by it produces NaN - which does not throw,
        // it silently discards every vertex downstream. The floor is applied per axis because a
        // set of points in a straight line has zero extent on one axis only.
        float halfWidth = Math.Max((maxX - minX) / 2f, MinimumHalfExtent);
        float halfHeight = Math.Max((maxY - minY) / 2f, MinimumHalfExtent);

        // One scale for both axes: the tighter fit wins, so everything stays visible and nothing
        // is stretched. Pixels-per-unit, converted to NDC by the viewport half-extents.
        float pixelsPerUnit = Math.Min(viewportWidth / (2f * halfWidth), viewportHeight / (2f * halfHeight));

        return new TopDownCamera(
            centreX,
            centreY,
            pixelsPerUnit * 2f / viewportWidth,
            pixelsPerUnit * 2f / viewportHeight);
    }

    /// <summary>Returns this camera zoomed about its centre.</summary>
    /// <param name="factor">Multiplier; greater than one magnifies.</param>
    /// <returns>A new camera.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factor"/> is not positive.</exception>
    /// <remarks>
    /// About the centre rather than the origin, so the point being looked at stays put. Zooming
    /// about the world origin instead makes the view lurch, because a Source map's origin is
    /// wherever the level designer left it rather than the middle of anything.
    /// </remarks>
    public TopDownCamera WithZoom(float factor)
    {
        if (factor <= 0f || !float.IsFinite(factor))
        {
            throw new ArgumentOutOfRangeException(
                nameof(factor), factor, "Zoom must be a positive, finite multiplier.");
        }

        return new TopDownCamera(
            _centreX, _centreY, _scaleX * factor, _scaleY * factor, _lowest, _highest);
    }

    /// <summary>Returns this camera moved to look at a different point.</summary>
    /// <param name="worldX">Where to centre the view.</param>
    /// <param name="worldY">Where to centre the view.</param>
    /// <returns>A new camera.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    public TopDownCamera LookingAt(float worldX, float worldY)
    {
        if (!float.IsFinite(worldX) || !float.IsFinite(worldY))
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldX), "A camera centre must be finite.");
        }

        return new TopDownCamera(worldX, worldY, _scaleX, _scaleY, _lowest, _highest);
    }

    /// <summary>Where this camera is looking, in world units.</summary>
    public (float X, float Y) Centre => (_centreX, _centreY);

    /// <summary>How many world units one screen pixel covers, horizontally.</summary>
    /// <remarks>
    /// **What a drag has to be converted through.** A mouse moves in pixels and the camera lives in
    /// world units; panning by the pixel count makes the map fly off at any zoom but one, which
    /// reads as the drag being wildly oversensitive rather than as a missing conversion.
    /// </remarks>
    public float WorldUnitsPerPixel(int viewportWidth) =>
        _scaleX == 0f ? 1f : 2f / (_scaleX * Math.Max(1, viewportWidth));

    /// <summary>Projects a world position to normalised device coordinates.</summary>
    /// <param name="worldX">World X.</param>
    /// <param name="worldY">World Y.</param>
    /// <returns>Coordinates in [-1, 1] for anything inside the fitted bounds.</returns>
    public (float X, float Y) Project(float worldX, float worldY) =>
        ((worldX - _centreX) * _scaleX, (worldY - _centreY) * _scaleY);

    /// <summary>Returns this camera told how high and how low the world goes.</summary>
    /// <param name="lowest">World height that should land furthest away.</param>
    /// <param name="highest">World height that should land nearest.</param>
    /// <returns>A camera whose matrix projects world height into the depth range.</returns>
    /// <remarks>
    /// **D21: the camera owns the projection, and depth is part of it.** Height used to be
    /// flattened into every vertex before the matrix ran, which is a top-down projection baked
    /// into the geometry — fine while the overhead view was the only camera, and exactly what a
    /// free camera would have to undo.
    ///
    /// The convention is unchanged, deliberately: the highest point in the map is nearest at 0 and
    /// the lowest is furthest at 1, which is what the per-vertex arithmetic produced. Only the
    /// place it happens has moved.
    ///
    /// An empty or inverted range is ignored rather than throwing: a map with no height at all is
    /// a legitimate degenerate case, and dividing by its range would put every surface at
    /// infinity.
    /// </remarks>
    public TopDownCamera WithHeights(float lowest, float highest) =>
        highest > lowest
            ? new TopDownCamera(_centreX, _centreY, _scaleX, _scaleY, lowest, highest)
            : this;

    /// <summary>The same projection as a matrix, for geometry the GPU transforms.</summary>
    /// <returns>Sixteen floats, row major, for <c>mul(float4(position, 1), matrix)</c>.</returns>
    /// <remarks>
    /// **The same arithmetic as <see cref="Project"/>, and that is the point.** A vertex projected
    /// on the processor and one projected by this matrix must land on the same pixel, or the map's
    /// outline and its textured surfaces drift apart — they are drawn by different paths and only
    /// one of them moved to the GPU.
    ///
    /// So this is deliberately a restatement rather than a reimplementation: scale, then translate
    /// by the centre already scaled. A test asserts the two agree, because the failure is a
    /// half-pixel disagreement that looks like a rounding artifact rather than a wrong formula.
    ///
    /// **The third row projects world height into depth** once <see cref="WithHeights"/> has said
    /// what the range is: nearest at the top of the map, furthest at the bottom. Until it has, it
    /// passes the component through, so geometry still carrying a precomputed depth is unharmed.
    ///
    /// Depth does not depend on the zoom, which is what keeps the map sorting the same however far
    /// the view is pulled back.
    /// </remarks>
    public float[] ToMatrix()
    {
        bool projectsHeight = _highest > _lowest;

        // z' = (highest - z) / (highest - lowest), written as a scale and a translate.
        float depthScale = projectsHeight ? -1f / (_highest - _lowest) : 1f;
        float depthOffset = projectsHeight ? _highest / (_highest - _lowest) : 0f;

        return
        [
            _scaleX, 0f, 0f, 0f,
            0f, _scaleY, 0f, 0f,
            0f, 0f, depthScale, 0f,
            -_centreX * _scaleX, -_centreY * _scaleY, depthOffset, 1f,
        ];
    }
}
