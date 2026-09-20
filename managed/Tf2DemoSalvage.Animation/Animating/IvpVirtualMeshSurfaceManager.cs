using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>The virtual-mesh surface manager, table <c>1800ee220</c>, over a displacement's cache entry and its engine tree (B369).</summary>
/// <param name="Mesh">The cache entry — <see cref="PhysicsVirtualMesh"/>.</param>
/// <param name="Tree">The displacement's collision tree, which the engine's handler answers from.</param>
/// <remarks>
/// **Read from the decompiled `vphysics.dll`** (`docs/findings/51`, *The virtual mesh's cache entry*):
/// <code>
/// slot 4, FUN_1800261a0:  no root → the first MIN(hull count, 2) hull ledges
///     a root → FUN_180025bc0:  c = ((float)(k·x), (float)(k·z), −(float)(k·y)), r' = (float)r·k, k = 39.37008f (DAT_18011f004)
///         the handler's GetTrianglesInSphere(c, r', 0xc00);  none → nothing
///         two hulls or more → the range is [0, n/2) when the root is the first hull, else [n/2, n);  else [0, n)
///         each index in the range, in the handler's order:  FUN_18007bea0(its ledge, the front triangle, the centre) &lt;= r² → kept
/// slot 1, FUN_1800262d0:  the mesh's centre, FUN_180025db0 = (mins + maxs)·0.5 of the handler's bounds, as (x·s, −(z·s), y·s)
/// slot 2, FUN_180026330:  the radius and the deviation alike, FUN_180025e80 = √((y² + x²) + z²) of maxs − (maxs + mins)·0.5, times s
/// </code>
/// *The handler's bounds are INFERRED to be the tree's bloated `m_mins`/`m_maxs`*: its slot 1 reads a 48-byte-stride engine array
/// whose writer is not traced.
/// </remarks>
public sealed record IvpVirtualMeshSurfaceManager(PhysicsVirtualMesh Mesh, DisplacementCollisionTree Tree) : IIvpSurfaceManager
{
    /// <summary><c>DAT_18011f000</c>, inches to metres.</summary>
    private const float MetresPerInch = 0.0254f;

    /// <summary><c>DAT_18011f004</c>, metres to inches.</summary>
    private const float InchesPerMetre = 39.37008f;

    /// <summary>What <c>GetTrianglesInSphere</c> is handed as its cap — <c>0xc00</c>, <c>MAX_VIRTUAL_TRIANGLES·3</c>.</summary>
    private const int IndexCap = 0xc00;

    /// <summary>The mesh's radius in metres, slot 2 — the radius and the deviation both.</summary>
    public float Radius
    {
        get
        {
            Vector3 mins = Tree.Mins;
            Vector3 maxs = Tree.Maxs;
            float x = maxs.X - ((maxs.X + mins.X) * 0.5f);
            float y = maxs.Y - ((mins.Y + maxs.Y) * 0.5f);
            float z = maxs.Z - ((maxs.Z + mins.Z) * 0.5f);

            return MathF.Sqrt((y * y) + (x * x) + (z * z)) * MetresPerInch;
        }
    }

    /// <summary>The mesh's centre in IVP metres and axes, slot 1.</summary>
    public (float X, float Y, float Z) MassCenter
    {
        get
        {
            Vector3 mins = Tree.Mins;
            Vector3 maxs = Tree.Maxs;

            return (
                (mins.X + maxs.X) * 0.5f * MetresPerInch,
                -((mins.Z + maxs.Z) * 0.5f * MetresPerInch),
                (mins.Y + maxs.Y) * 0.5f * MetresPerInch);
        }
    }

    /// <inheritdoc/>
    public void LedgesWithin(
        (double X, double Y, double Z) center, double radius, PhysicsLedgeTreeNode? root, ICollection<PhysicsLedgeTreeNode> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (root is null)
        {
            for (int index = 0; index < Math.Min(Mesh.Hulls.Count, 2); index++)
            {
                into.Add(Mesh.Hulls[index]);
            }

            return;
        }

        double scale = InchesPerMetre;
        Vector3 source = new((float)(scale * center.X), (float)(scale * center.Z), -(float)(scale * center.Y));
        IReadOnlyList<int> found = Tree.TrianglesInSphere(source, (float)radius * InchesPerMetre, IndexCap);

        if (found.Count == 0)
        {
            return;
        }

        int first = 0;
        int last = Mesh.Triangles.Count;

        if (Mesh.Hulls.Count > 1)
        {
            int half = last / 2;

            if (ReferenceEquals(root, Mesh.Hulls[0]))
            {
                last = half;
            }
            else
            {
                first = half;
            }
        }

        foreach (int index in found)
        {
            // Stryker disable once : a mutant that empties the guard body leaves 'ledge'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (index < first || index >= last || Mesh.Triangles[index].Ledge is not { } ledge)
            {
                continue;
            }

            IvpLedgeSide side = IvpLedgeSide.FromLedge(ledge, default, default);

            if (IvpCompactLedgeSolver.TriangleDistanceSquared(side, 0, center) <= radius * radius)
            {
                into.Add(Mesh.Triangles[index]);
            }
        }
    }
}
