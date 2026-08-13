using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// Terrain: the heightfield a displacement face is really made of.
/// </summary>
/// <remarks>
/// **A displacement's entry in FACES is not its shape.** That face is the flat quad the terrain was
/// built on; the real surface is a grid subdividing it, with every vertex pushed along its own
/// direction. Drawing the quad gives a flat slab where a hillside should be, and paints it with the
/// first of the two textures the terrain blends — which is why <c>cp_process_final</c>'s outdoor
/// areas came out as bare dirt with no grass anywhere.
///
/// Two lumps hold it, and their layout was confirmed by arithmetic rather than recalled:
///
/// <code>
///   DISPINFO    (26), 176 bytes each: startPosition at 0, DispVertStart at 12, power at 20
///   DISP_VERTS  (33),  20 bytes each: direction at 0, distance at 12, alpha at 16
/// </code>
///
/// With those, every displacement's vertex range fits inside DISP_VERTS and the ranges together
/// account for exactly 100.0% of it — measured on cp_process_final (578 displacements, 20,306
/// vertices), cp_badlands (1,191 / 42,415) and pl_upward (558 / 14,174). A wrong stride does not
/// divide the lump at all.
///
/// **`power` is 2, 3 or 4**, giving a grid of 5, 9 or 17 vertices a side. The grid is built by
/// interpolating across the base quad and then displacing:
/// <c>position = bilinear(corners) + direction * distance</c>.
///
/// **`alpha` is what makes grass appear.** A blend material carries two textures, and this value
/// per vertex is the mix between them — dirt at zero, grass at one. Without it a blended surface
/// can only show one of its two layers.
/// </remarks>
public static class BspDisplacements
{
    /// <summary>Reads one face's terrain, if it has any.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <param name="surface">A surface whose <c>DisplacementIndex</c> is not -1.</param>
    /// <returns>The subdivided surface, or an empty list if the face is not a displacement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <exception cref="InvalidDataException">The displacement's data is malformed.</exception>
    /// <remarks>
    /// **Reads the map's lumps on every call**, which is fine for one face and quadratic for a
    /// map: see <see cref="BspTerrain"/>, which reads them once and is what the renderer uses.
    /// This overload stays because asking about a single face is a real thing to want - a trace, a
    /// test, a diagnostic - and it should not require the caller to hold a reader.
    /// </remarks>
    public static IReadOnlyList<SurfaceVertex> ReadTriangles(
        ReadOnlyMemory<byte> file, BspSurface surface) =>
        BspTerrain.Create(file).ReadTriangles(surface);
}
