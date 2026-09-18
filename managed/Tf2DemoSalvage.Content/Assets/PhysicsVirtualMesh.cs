using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// A displacement's virtual mesh as vphysics caches it — the entry <c>FUN_180025f10</c> builds on first use (B369).
/// </summary>
/// <remarks>
/// **Read from the decompiled `vphysics.dll`** (`docs/findings/51`, *The virtual mesh's cache entry*):
/// <code>
/// entry bytes:  triangles T × 0x30  |  vertices V × 0x10, (x·0.0254f, −(z·0.0254f), y·0.0254f, 0)  |  the hull ledges, unpacked
/// entry+0x10 = T·0x30 + V·0x10, the hulls' start;  entry+0x12 = the hull blob's count byte
/// FUN_180003f70 (a triangle, 48 bytes):  +0x0 points − ledge;  +0x8 0x304;  +0xc 2
///     front  header pierce 1;          edges (i0, hop 6), (i1, hop 4), (i2, hop 2)
///     back   header index 1, pierce 0; edges (i0, hop −2), (i2, hop −4), (i1, hop −6)
/// FUN_1800048d0 (hull h, over the blob's per-hull bytes T, VT, E, VE, base and its body of T·4 then E·2 bytes):
///     +0x0 points − ledge;  +0x8 (T + 1)·0x100 | 4 | 1;  +0xc T
///     triangle t:  index t | (t &lt; VT) &lt;&lt; 31 | body[4t + 3] &lt;&lt; 12
///         edge k of t, id e = body[4t + k]:  first met → vertex body[4T + 2e] + base, remembered;  met again → body[4T + 2e + 1] +
///         base and the two words hop to each other;  (e &lt; VE) &lt;&lt; 31
/// </code>
/// **The bytes are written as the engine writes them and read back through <see cref="PhysicsHull"/>'s ledge reader**, so a
/// virtual ledge is decoded by the same code as every other ledge. A ledge here has no tree node, so its centre and radius are zero —
/// the ported driver reads neither.
/// </remarks>
public sealed class PhysicsVirtualMesh
{
    /// <summary><c>DAT_18011f000</c>, <c>0x3cd013a9</c>: inches to metres.</summary>
    private const float MetresPerInch = 0.0254f;

    private const int TriangleLedgeSize = 0x30;
    private const int VertexSize = 0x10;

    private PhysicsVirtualMesh(IReadOnlyList<PhysicsLedgeTreeNode> triangles, IReadOnlyList<PhysicsLedgeTreeNode> hulls)
    {
        Triangles = triangles;
        Hulls = hulls;
    }

    /// <summary>One ledge per triangle, in the mesh list's order — what the radius query answers beneath a hull.</summary>
    public IReadOnlyList<PhysicsLedgeTreeNode> Triangles { get; }

    /// <summary>The hull ledges, in the blob's order — what the radius query answers at the root.</summary>
    public IReadOnlyList<PhysicsLedgeTreeNode> Hulls { get; }

    /// <summary>Builds the cache entry.</summary>
    /// <param name="vertices">The displacement's vertices, in Source units — <c>virtualmeshlist_t::pVerts</c>.</param>
    /// <param name="triangles">Its triangles' vertex indices — <c>virtualmeshlist_t::indices</c>.</param>
    /// <param name="hull">The hull blob from <c>LUMP_PHYSDISP</c>, or empty when the displacement has none.</param>
    /// <returns>The mesh.</returns>
    /// <exception cref="ArgumentNullException">A list is null.</exception>
    public static PhysicsVirtualMesh Build(
        IReadOnlyList<Vector3> vertices, IReadOnlyList<(int A, int B, int C)> triangles, ReadOnlySpan<byte> hull)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);

        int vertexBase = triangles.Count * TriangleLedgeSize;
        int hullBase = vertexBase + (vertices.Count * VertexSize);
        byte[] entry = new byte[hullBase + HullSize(hull)];

        for (int index = 0; index < vertices.Count; index++)
        {
            Span<byte> at = entry.AsSpan(vertexBase + (index * VertexSize));
            Vector3 vertex = vertices[index];

            BinaryPrimitives.WriteSingleLittleEndian(at, MetresPerInch * vertex.X);
            BinaryPrimitives.WriteSingleLittleEndian(at[4..], -(MetresPerInch * vertex.Z));
            BinaryPrimitives.WriteSingleLittleEndian(at[8..], vertex.Y * MetresPerInch);
        }

        for (int index = 0; index < triangles.Count; index++)
        {
            (int a, int b, int c) = triangles[index];
            TriangleLedge(entry, index * TriangleLedgeSize, vertexBase, a, b, c);
        }

        List<int> hullOffsets = [];

        if (!hull.IsEmpty)
        {
            int at = hullBase;

            for (int index = 0; index < hull[0]; index++)
            {
                HullLedge(entry, at, vertexBase, hull, index);
                hullOffsets.Add(at);
                at += (BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(at + 8)) >> 8) * 0x10;
            }
        }

        return new PhysicsVirtualMesh(Nodes(entry, 0, triangles.Count, TriangleLedgeSize, 0), Nodes(entry, hullOffsets, 1));
    }

    /// <summary><c>FUN_1800254e0</c>: <c>16 · count + 16 · ΣT</c>.</summary>
    private static int HullSize(ReadOnlySpan<byte> hull)
    {
        if (hull.IsEmpty)
        {
            return 0;
        }

        int size = hull[0] * 0x10;

        for (int index = 0; index < hull[0]; index++)
        {
            size += hull[4 + (index * 5)] * 0x10;
        }

        return size;
    }

    /// <summary><c>FUN_180003f70</c>: one triangle, front and back, as a 48-byte ledge.</summary>
    private static void TriangleLedge(byte[] entry, int ledge, int points, int i0, int i1, int i2)
    {
        Span<byte> at = entry.AsSpan(ledge);

        Int(at, 0x0, points - ledge);
        Int(at, 0x8, 0x304);
        Int(at, 0xC, 2);

        Int(at, 0x10, 0x1000);
        Int(at, 0x14, Edge(i0, 6));
        Int(at, 0x18, Edge(i1, 4));
        Int(at, 0x1C, Edge(i2, 2));

        Int(at, 0x20, 1);
        Int(at, 0x24, Edge(i0, -2));
        Int(at, 0x28, Edge(i2, -4));
        Int(at, 0x2C, Edge(i1, -6));
    }

    /// <summary><c>FUN_1800048d0</c>: hull <paramref name="index"/> of the blob unpacked into a ledge.</summary>
    private static void HullLedge(byte[] entry, int ledge, int points, ReadOnlySpan<byte> blob, int index)
    {
        int header = 4 + (index * 5);
        int triangleCount = blob[header];
        int virtualTriangles = blob[header + 1];
        int edgeCount = blob[header + 2];
        int virtualEdges = blob[header + 3];
        int vertexBase = blob[header + 4];

        int body = 4 + (blob[0] * 5);

        for (int previous = 0; previous < index; previous++)
        {
            body += (blob[4 + (previous * 5)] * 4) + (blob[4 + (previous * 5) + 2] * 2);
        }

        Span<byte> at = entry.AsSpan(ledge);

        Int(at, 0x0, points - ledge);
        Int(at, 0x8, ((triangleCount + 1) << 8) | 4 | 1);
        Int(at, 0xC, triangleCount);

        int[] firstWord = new int[edgeCount];
        Array.Fill(firstWord, -1);

        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            int word = 4 * (triangle + 1);
            int headerWord = triangle | (blob[body + (4 * triangle) + 3] << 12);

            if (triangle < virtualTriangles)
            {
                headerWord |= int.MinValue;
            }

            Int(at, word * 4, headerWord);

            for (int k = 0; k < 3; k++)
            {
                int edge = blob[body + (4 * triangle) + k];
                int slot = word + 1 + k;
                int flag = edge < virtualEdges ? int.MinValue : 0;
                int vertexBytes = body + (4 * triangleCount) + (2 * edge);

                if (firstWord[edge] < 0)
                {
                    Int(at, slot * 4, flag | (blob[vertexBytes] + vertexBase));
                    firstWord[edge] = slot;
                    continue;
                }

                int other = firstWord[edge];

                Int(at, slot * 4, flag | (blob[vertexBytes + 1] + vertexBase) | (((other - slot) & 0x7FFF) << 16));

                int patched = BinaryPrimitives.ReadInt32LittleEndian(at[(other * 4)..]);
                Int(at, other * 4, (patched & unchecked((int)0x8000FFFF)) | (((slot - other) & 0x7FFF) << 16));
            }
        }
    }

    private static int Edge(int start, int hop) => (start & 0xFFFF) | ((hop & 0x7FFF) << 16);

    private static void Int(Span<byte> at, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(at[offset..], value);

    private static List<PhysicsLedgeTreeNode> Nodes(byte[] entry, int first, int count, int stride, int children)
    {
        List<int> offsets = new(count);

        for (int index = 0; index < count; index++)
        {
            offsets.Add(first + (index * stride));
        }

        return Nodes(entry, offsets, children);
    }

    private static List<PhysicsLedgeTreeNode> Nodes(byte[] entry, List<int> offsets, int children)
    {
        List<PhysicsLedgeTreeNode> nodes = new(offsets.Count);

        foreach (int offset in offsets)
        {
            nodes.Add(new PhysicsLedgeTreeNode(offset, Vector3.Zero, 0f, (255, 255, 255))
            {
                HasLedge = true,
                LedgeOffset = offset,
                Ledge = PhysicsHull.ReadLedge(entry, offset, Vector3.Zero, 0f),
                LedgeChildren = children,
            });
        }

        return nodes;
    }
}
