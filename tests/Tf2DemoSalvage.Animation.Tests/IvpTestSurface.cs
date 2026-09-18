using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>A compact surface written byte for byte — boxes as ledges under one ledge tree — and read back through the reader.</summary>
/// <remarks>
/// **Written, not built**: the bytes go through <see cref="PhysicsHull.Tree"/>, so a test stands on the tree a map's collide gives,
/// not on a shape this project assembled. Layout: the 0x30 header with the root node's offset at <c>+0x20</c> and <c>IVPS</c> at
/// <c>+0x2C</c>; each ledge a 0x10 header (points offset, node offset, flags, triangle count), its triangles and its points; the tree
/// an inner node over two terminal ones, or one terminal node.
/// </remarks>
internal static class IvpTestSurface
{
    /// <summary>One or two boxes, each at its centre, as one surface's tree.</summary>
    /// <param name="boxes">Each box's centre and half extents.</param>
    /// <returns>The tree.</returns>
    public static PhysicsLedgeTree Boxes(params (Vector3 Centre, Vector3 Half)[] boxes) =>
        PhysicsHull.Tree(Bytes(boxes)) ?? throw new InvalidOperationException("The written surface did not read as a tree.");

    /// <summary>The surface's bytes, as a <c>.phy</c> or a map's collide would carry them untagged.</summary>
    /// <param name="boxes">Each box's centre and half extents.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Bytes(params (Vector3 Centre, Vector3 Half)[] boxes)
    {
        if (boxes.Length is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(boxes), "One or two boxes.");
        }

        List<byte> bytes = [.. new byte[0x30]];
        List<int> ledges = [];

        foreach ((Vector3 centre, Vector3 half) in boxes)
        {
            ledges.Add(WriteLedge(bytes, IvpTestCube.Box(half.X, half.Y, half.Z)[0], centre));
        }

        int root = bytes.Count;

        if (boxes.Length == 1)
        {
            WriteNode(bytes, right: 0, ledge: ledges[0], boxes[0]);
        }
        else
        {
            WriteNode(bytes, right: 0x38, ledge: null, (Vector3.Zero, new Vector3(1e4f)));
            WriteNode(bytes, right: 0, ledge: ledges[0], boxes[0]);
            WriteNode(bytes, right: 0, ledge: ledges[1], boxes[1]);
        }

        byte[] surface = [.. bytes];
        float radius = 0f;

        foreach ((Vector3 centre, Vector3 half) in boxes)
        {
            radius = MathF.Max(radius, centre.Length() + half.Length());
        }

        // The mass centre at the origin, a unit rotation inertia per kilogram; the radius a sphere about it that holds every box.
        BitConverter.TryWriteBytes(surface.AsSpan(0x0C), 1f);
        BitConverter.TryWriteBytes(surface.AsSpan(0x10), 1f);
        BitConverter.TryWriteBytes(surface.AsSpan(0x14), 1f);
        BitConverter.TryWriteBytes(surface.AsSpan(0x18), radius);
        BitConverter.TryWriteBytes(surface.AsSpan(0x20), root);
        "IVPS"u8.CopyTo(surface.AsSpan(0x2C));

        return surface;
    }

    /// <summary>Appends one ledge — header, triangles, points — and answers where it starts.</summary>
    private static int WriteLedge(List<byte> bytes, PhysicsLedge ledge, Vector3 centre)
    {
        int at = bytes.Count;
        int triangles = ledge.Triangles.Count;
        int points = at + 0x10 + (triangles * 0x10);

        Int(bytes, points - at);
        Int(bytes, 0);
        Int(bytes, 0);
        Int(bytes, triangles);

        for (int index = 0; index < triangles; index++)
        {
            (int a, int b, int c) = ledge.Triangles[index];
            (int oa, int ob, int oc) = ledge.EdgeOffsets[index];
            Int(bytes, index | (ledge.PierceTriangles[index] << 12));
            Int(bytes, Edge(a, oa));
            Int(bytes, Edge(b, ob));
            Int(bytes, Edge(c, oc));
        }

        foreach (Vector3 point in ledge.Points)
        {
            Float(bytes, point.X + centre.X);
            Float(bytes, point.Y + centre.Y);
            Float(bytes, point.Z + centre.Z);
            Int(bytes, 0);
        }

        return at;
    }

    private static void WriteNode(List<byte> bytes, int right, int? ledge, (Vector3 Centre, Vector3 Half) sphere)
    {
        int at = bytes.Count;
        Int(bytes, right);
        Int(bytes, ledge is int offset ? offset - at : 0);
        Float(bytes, sphere.Centre.X);
        Float(bytes, sphere.Centre.Y);
        Float(bytes, sphere.Centre.Z);
        Float(bytes, sphere.Half.Length());
        bytes.AddRange([255, 255, 255, 0]);
    }

    private static int Edge(int point, int offset) => point | ((offset & 0x7FFF) << 16);

    private static void Int(List<byte> bytes, int value) => bytes.AddRange(BitConverter.GetBytes(value));

    private static void Float(List<byte> bytes, float value) => bytes.AddRange(BitConverter.GetBytes(value));
}
