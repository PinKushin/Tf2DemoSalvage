using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// Valve's own displacement tesselation, which is what stops two displacements parting company.
/// </summary>
/// <remarks>
/// **This is `TesselateDisplacement` from `public/disp_tesselate.h`, and its own comment says why it
/// belongs in both halves of this project:**
///
/// <code>
/// // This interface is shared betwixt VBSP and the engine. VBSP uses it to build the
/// // physics mesh and the engine uses it to render.
/// </code>
///
/// So the triangles a corpse lands on and the triangles drawn are the SAME triangles, produced by
/// the same walk. That is not an economy; a renderer and a collision mesh that tesselate a
/// displacement differently disagree about where the ground is.
///
/// **A displacement is NOT a uniform grid, and building one as if it were is the defect this
/// replaces.** `ddispinfo_t` ends with
///
/// <code>
/// uint32 m_AllowedVerts[ALLOWEDVERTS_SIZE]; // This is built based on the layout and sizes of our
///                                           // neighbors and tells us which vertices are allowed
///                                           // to be active.
/// </code>
///
/// (`bspfile.h:665`). When a power-3 displacement borders a power-2 one, the fine one's extra edge
/// vertices are turned OFF so its edge collapses onto the coarse neighbour's, and the two meet
/// exactly. The tesselation reads those bits per vertex — `pHelper-&gt;m_pActiveVerts[iVertBit&gt;&gt;5] &amp;
/// (1 &lt;&lt; (iVertBit &amp; 31))` — and skips the ones that are off.
///
/// **A uniform grid keeps them, and the two edges then differ by however far the fine one bulges
/// away from the straight line the coarse one draws.** The map has no hole in it; the collision
/// world grew one, along every seam where the powers differ. Measured before this existed: 80
/// disallowed vertices over 20 displacements on `koth_harvest_final`, 539 over 65 on `ctf_2fort`,
/// 322 over 75 on `cp_dustbowl` — and 26, 107 and 138 columns respectively where a ray fell through
/// ground the map's own brush tree stops it on.
///
/// **The walk is a quadtree, and a node draws only the perimeter its children have not taken.**
/// Each node visits nine perimeter positions clockwise from the lower right, fanning a triangle to
/// its own centre for each adjacent pair; where an active child owns that corner the run is broken
/// instead, because the child will draw that quadrant in finer detail. That is what makes the mesh
/// adaptive without a T-junction anywhere in it.
/// </remarks>
internal static class DisplacementTesselation
{
    /// <summary>Where each perimeter step sits, and which child node owns it.</summary>
    /// <remarks>
    /// **`g_TesselateVerts`, verbatim** (`disp_powerinfo.cpp:241-253`), including the repeated first
    /// entry at the end — the winding closes on itself so the last pair fans back to the start.
    /// `CHILDNODE_UPPER_RIGHT` is 0, `_UPPER_LEFT` 1, `_LOWER_LEFT` 2, `_LOWER_RIGHT` 3
    /// (`bspfile.h:229-232`), and −1 means the position belongs to no child.
    /// </remarks>
    private static readonly (int X, int Y, int Node)[] Winding =
    [
        (1, -1, 3),
        (0, -1, -1),
        (-1, -1, 2),
        (-1, 0, -1),
        (-1, 1, 1),
        (0, 1, -1),
        (1, 1, 0),
        (1, 0, -1),
        (1, -1, 3),
    ];

    /// <summary>Which way each child node lies from its parent — <c>g_ChildNodeIndexMul</c>.</summary>
    private static readonly (int X, int Y)[] Children =
    [
        (1, 1),
        (-1, 1),
        (-1, -1),
        (1, -1),
    ];

    /// <summary>Tesselates one displacement, appending vertex indices three at a time.</summary>
    /// <param name="power">The displacement's power: 2, 3 or 4.</param>
    /// <param name="allowed">Its <c>m_AllowedVerts</c> bits — at least ten little-endian words.</param>
    /// <param name="into">Where the indices are appended, in groups of three.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="power"/> is not 2, 3 or 4.</exception>
    /// <remarks>
    /// **Indices into the caller's own row-major grid**, matching Valve's `InternalVertIndex`:
    /// <c>vert.y * m_SideLength + vert.x</c>. So y is the row and x the column, which is the order
    /// `LUMP_DISP_VERTS` is stored in and the order the grid is built in.
    ///
    /// **The winding is REVERSED against Valve's on the way out.** The engine's helper emits its fan
    /// clockwise and vbsp then reverses the whole list before handing it to vphysics —
    /// <c>V_swap( pIndices[i], pIndices[(indexCount-i)-1] )</c> in `CDispMeshEvent`'s constructor
    /// (`disp_ivp.cpp:210-213`). Reversing each triangle here reaches the same handedness while
    /// leaving the triangles in tesselation order, which keeps a normal computed from the corners
    /// pointing out of the ground rather than into it.
    /// </remarks>
    public static void Build(int power, ReadOnlySpan<byte> allowed, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentOutOfRangeException.ThrowIfLessThan(power, MinimumPower);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(power, MaximumPower);

        int side = (1 << power) + 1;

        Walk(new Walker(power, side, allowed, into), side / 2, side / 2, 0);
    }

    /// <summary>Carries what every level of the walk needs, so the recursion takes four arguments.</summary>
    /// <remarks>
    /// **The bits are COPIED rather than held as a span**, because a `ReadOnlySpan&lt;byte&gt;` cannot
    /// live in a class field and the walk is recursive. Forty bytes per displacement.
    /// </remarks>
    private sealed class Walker
    {
        public Walker(int power, int side, ReadOnlySpan<byte> allowed, List<int> into)
        {
            Power = power;
            Side = side;
            Into = into;
            _allowed = allowed.ToArray();
        }

        public int Power { get; }

        public int Side { get; }

        public List<int> Into { get; }

        /// <summary>Whether one grid position is allowed to be active.</summary>
        /// <remarks>
        /// **A bit that cannot be read counts as ALLOWED, which is the safe direction.** A truncated
        /// lump would otherwise erase a map's terrain silently; treating the absence as "no
        /// restriction" degrades to the uniform grid this replaces, which is wrong in a way that is
        /// visible rather than wrong in a way that is not.
        /// </remarks>
        public bool Allowed(int x, int y)
        {
            int bit = (y * Side) + x;
            int word = (bit >> 5) * 4;

            return word + 4 > _allowed.Length ||
                (BinaryPrimitives.ReadUInt32LittleEndian(_allowed.AsSpan(word)) &
                    (1u << (bit & 31))) != 0;
        }

        private readonly byte[] _allowed;
    }

    /// <summary>One node of the quadtree: its active children first, then its own perimeter.</summary>
    /// <remarks>
    /// **Children before the node itself, which is the order that makes the breaks mean something.**
    /// A node only learns which of its quadrants to skip by asking whether each child vertex is
    /// allowed, and the child must already have drawn that quadrant when the parent leaves the gap.
    /// </remarks>
    private static void Walk(Walker walker, int x, int y, int level)
    {
        bool[] active = new bool[4];

        if (level < walker.Power - 1)
        {
            int step = 1 << (walker.Power - level - 2);

            for (int child = 0; child < active.Length; child++)
            {
                int childX = x + (Children[child].X * step);
                int childY = y + (Children[child].Y * step);

                active[child] = walker.Allowed(childX, childY);

                if (active[child])
                {
                    Walk(walker, childX, childY, level + 1);
                }
            }
        }

        Node(walker, x, y, level, active);
    }

    /// <summary>Fans this node's own triangles around its centre — <c>TesselateDisplacementNode</c>.</summary>
    private static void Node(Walker walker, int x, int y, int level, bool[] active)
    {
        int step = 1 << (walker.Power - level - 1);
        int centre = (y * walker.Side) + x;

        int held = 0;
        int first = 0;
        int second = 0;

        foreach ((int offsetX, int offsetY, int node) in Winding)
        {
            if (node >= 0 && active[node])
            {
                // An active child owns this corner and will draw it finer. Break the run rather
                // than spanning across the quadrant, which is what would leave a T-junction.
                if (held == 2)
                {
                    Emit(walker, ref first, second, centre);
                }

                held = 0;

                continue;
            }

            int sideX = x + (offsetX * step);
            int sideY = y + (offsetY * step);

            if (!walker.Allowed(sideX, sideY))
            {
                continue;
            }

            if (held == 0)
            {
                first = (sideY * walker.Side) + sideX;
                held = 1;
            }
            else
            {
                second = (sideY * walker.Side) + sideX;

                Emit(walker, ref first, second, centre);

                held = 1;
            }
        }
    }

    /// <summary>Closes one triangle and carries its second corner into the next.</summary>
    private static void Emit(Walker walker, ref int first, int second, int centre)
    {
        // Reversed against Valve's order — see the remarks on `Build`.
        walker.Into.Add(centre);
        walker.Into.Add(second);
        walker.Into.Add(first);

        first = second;
    }

    /// <summary>The smallest displacement the format allows.</summary>
    private const int MinimumPower = 2;

    /// <summary>And the largest — <c>MAX_MAP_DISP_POWER</c>.</summary>
    private const int MaximumPower = 4;
}
