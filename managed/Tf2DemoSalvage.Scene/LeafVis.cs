using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The BSP leaf the camera stands in, as projected line segments — <c>mat_leafvis</c>.</summary>
/// <remarks>
/// **This was <c>MainForm.LeafBoxLines</c>** (B188, D90). Walking a BSP tree to a leaf and
/// projecting a box through a view matrix are both things a second frontend would have to
/// reimplement, so neither is view work however few lines it takes.
///
/// **The leaf the CAMERA is in, which is what Valve draws.** A BSP leaf is the unit the engine culls
/// and traces against, so "which one am I in and how big is it" is the question behind every
/// visibility oddity — a prop that vanishes at a doorway, a sound that carries too far. Drawing all
/// of them would be a wireframe of the whole tree and would answer nothing.
///
/// **The box is the leaf's own culling bound rather than a tight fit**, which is Valve's own
/// framing: `dleaf_t`'s mins/maxs are "for frustum culling". A picture of a tighter shape would
/// answer a question nobody asks, because the loose box is what the engine actually tests.
///
/// **World space, because that is what the engine's debug lines take** (D95). Every overlay in the
/// SDK is given absolute coordinates and a depth flag —
/// <c>DebugDrawLine( const Vector&amp; vecAbsStart, const Vector&amp; vecAbsEnd, int r, int g, int b,
/// bool test, float duration )</c>, and <c>bool noDepthTest</c> on the overlay record itself
/// (`game/server/ndebugoverlay.h:24`, <c>:28</c>). The transform happens on the GPU like every other
/// primitive.
///
/// **This projected on the CPU until 2026-08-25, and that was our invention rather than Valve's.**
/// It multiplied eight corners through the view matrix here and handed the renderer flat clip-space
/// pairs, which could not be occluded by anything. Two things were wrong with it: the leaf box
/// describes GEOMETRY and so should be hidden by geometry, and a hand-written transform is a second
/// implementation of the camera — one this project had already got wrong once, indexing the matrix
/// as a column-vector transform and collapsing a room-sized box into "a dot that gets kinda
/// triangular".
///
/// **`mat_leafvis` itself is engine-side and not in `source-sdk-2013`**; the published analogue is
/// `cl_drawleaf` (`clientleafsystem.cpp:32`), a `FCVAR_CHEAT` convar that filters the renderables
/// list to one leaf rather than outlining it. So the intent is Valve's and the outline is ours —
/// but the way it reaches the screen is now the engine's.
/// </remarks>
public static class LeafVis
{
    // A `/// <summary>How large w must be before a corner is considered to be in front of the
    // eye.</summary>` sat here until 2026-08-26, describing a constant that no longer exists. An
    // orphaned doc comment does not warn — it silently reattaches to the next member, so `Lines`
    // carried a summary about clip-space W for however long the constant had been gone.

    /// <summary>Why <see cref="Lines"/> drew nothing, in the words a user should see.</summary>
    /// <param name="mapLoaded">Whether a map is open at all.</param>
    /// <param name="tree">That map's BSP tree, or null when it carried none.</param>
    /// <returns>The reason, ready to log.</returns>
    /// <remarks>
    /// **This was `MainForm.WhyNoLeafBox`** (B208). Deciding what an absent BSP tree means, and
    /// telling it apart from an absent map and from a leaf with no bounds, is knowledge about the
    /// format — a window has no business holding it.
    ///
    /// **Three cases and not two, deliberately.** "No map" and "a map with no tree" are different
    /// problems with different fixes: one is a demo whose map could not be found, the other is a map
    /// whose tree we failed to read. Collapsing them would send the reader to the wrong half.
    ///
    /// **`mapLoaded` is a bool rather than the map**, so this stays a question about the BSP rather
    /// than acquiring a dependency on `LoadedMap` — which would point `LeafVis` upward at the thing
    /// that composes it (D92).
    /// </remarks>
    public static string WhyNothing(bool mapLoaded, BspLeafTree? tree)
    {
        if (!mapLoaded)
        {
            return "mat_leafvis is on with no map loaded";
        }

        return tree is null or { IsEmpty: true }
            ? "mat_leafvis is on but the map carried no BSP tree"
            : "mat_leafvis is on but the leaf under the camera has no bounds";
    }

    /// <summary>The twelve edges of the leaf containing a point, in world units.</summary>
    /// <param name="tree">The map's BSP tree, or null when no map is loaded.</param>
    /// <param name="eye">Where the viewer is standing, in world units.</param>
    /// <returns>World-space segments, or nothing when there is no tree or the leaf has no box.</returns>
    /// <remarks>
    /// **Nothing rather than a box at the origin when the leaf has no bounds.** A tree built without
    /// the leaf lump can still say WHICH leaf a point is in — the walk needs only nodes and planes —
    /// but it cannot say how big that leaf is, and drawing a guess would be drawing a lie.
    ///
    /// When this returns nothing, <see cref="WhyNothing"/> says which of the three reasons it was.
    /// </remarks>
    public static IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> Lines(
        BspLeafTree? tree,
        (float X, float Y, float Z) eye) =>
        tree is not null && tree.Bounds(tree.LeafAt(eye.X, eye.Y, eye.Z)) is { } box
            ? Edges(box)
            : [];

    /// <summary>The twelve edges of a box, in world units.</summary>
    /// <param name="box">Its minimum and maximum corner, in world units.</param>
    /// <returns>World-space segments, one per edge.</returns>
    /// <remarks>
    /// **All twelve, always.** The old version dropped an edge whose end was behind the eye, because
    /// it divided by w on the CPU and a w at or below zero mirrors the point through the camera. The
    /// GPU clips properly, so there is nothing to guard against and nothing to lose: an edge that
    /// crosses the near plane is now drawn up to it rather than discarded whole.
    /// </remarks>
    public static IReadOnlyList<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> Edges(
        ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max) box)
    {
        List<((float X, float Y, float Z) From, (float X, float Y, float Z) To)> lines = [];

        // The twelve edges of a box: every pair of corners differing in exactly one axis bit, taken
        // from the end where that bit is clear so each edge is emitted once rather than twice.
        for (int from = 0; from < 8; from++)
        {
            foreach (int axis in Axes)
            {
                int to = from | axis;

                if (to != from)
                {
                    lines.Add((Corner(box, from), Corner(box, to)));
                }
            }
        }

        return lines;
    }

    /// <summary>The three axis bits of a corner index.</summary>
    /// <remarks>A field rather than a literal in the loop, so the allocation happens once.</remarks>
    private static ReadOnlySpan<int> Axes => [1, 2, 4];

    /// <summary>One of the eight corners of a box, chosen by its three axis bits.</summary>
    private static (float X, float Y, float Z) Corner(
        ((float X, float Y, float Z) Min, (float X, float Y, float Z) Max) box, int which) => (
            (which & 1) == 0 ? box.Min.X : box.Max.X,
            (which & 2) == 0 ? box.Min.Y : box.Max.Y,
            (which & 4) == 0 ? box.Min.Z : box.Max.Z);

    // **`Project` and `CheckMatrix` were here until 2026-08-25** (D95). They multiplied a world
    // point through the view matrix by hand and dropped anything behind the eye, so this type had
    // to be handed a camera in order to describe a box.
    //
    // That was a second implementation of the camera, and it had already been wrong once: the first
    // version indexed the matrix as a column-vector transform, taking w from elements 12-15 instead
    // of 11. It did not fail — it produced A projection, and the owner saw a room-sized box as "a
    // dot that gets kinda triangular".
    //
    // The transform is the GPU's now, which is where the engine has always done it. What is left
    // here is arithmetic about a box, and there is no matrix in it.
}
