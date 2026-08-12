using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Core.Tests.Bsp;

/// <summary>
/// Tests reading drawable world geometry out of a BSP.
/// </summary>
/// <remarks>
/// The path is FACES to SURFEDGES to EDGES to VERTEXES, confirmed against a real map in
/// <c>docs/RENDERING_NOTES.md</c> section 2 — brushes are collision volumes, not visible surfaces.
///
/// Built from synthetic files rather than a real BSP. A map is 20-plus MB of Valve content that
/// cannot be committed, and every property here — winding, index bounds, the sign convention on
/// surfedges — is exactly expressible in a handful of hand-built records.
/// </remarks>
public sealed class BspGeometryTests
{
    private const int HeaderSize = BspHeader.SizeBytes;

    private const int LumpPlanes = 1;
    private const int LumpVertexes = 3;
    private const int LumpFaces = 7;
    private const int LumpEdges = 12;
    private const int LumpSurfedges = 13;

    /// <summary>Assembles a BSP from lump payloads, laid out end to end after the header.</summary>
    private static byte[] BuildBsp(Dictionary<int, byte[]> lumps)
    {
        int total = HeaderSize;

        foreach (byte[] payload in lumps.Values)
        {
            total += payload.Length;
        }

        byte[] file = new byte[total];
        Encoding.ASCII.GetBytes("VBSP").CopyTo(file, 0);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4), 21);

        int at = HeaderSize;

        foreach ((int index, byte[] payload) in lumps)
        {
            payload.CopyTo(file, at);
            int entry = 8 + (index * 16);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry), at);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(entry + 4), payload.Length);
            at += payload.Length;
        }

        return file;
    }

    private static byte[] Vertexes(params (float X, float Y, float Z)[] points)
    {
        byte[] data = new byte[points.Length * 12];

        for (int i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 12), points[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((i * 12) + 4), points[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((i * 12) + 8), points[i].Z);
        }

        return data;
    }

    private static byte[] Edges(params (ushort A, ushort B)[] pairs)
    {
        byte[] data = new byte[pairs.Length * 4];

        for (int i = 0; i < pairs.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 4), pairs[i].A);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan((i * 4) + 2), pairs[i].B);
        }

        return data;
    }

    private static byte[] Surfedges(params int[] indices)
    {
        byte[] data = new byte[indices.Length * 4];

        for (int i = 0; i < indices.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), indices[i]);
        }

        return data;
    }

    /// <summary>Planes are a normal and a distance, 20 bytes each.</summary>
    private static byte[] Planes(params (float X, float Y, float Z)[] normals)
    {
        byte[] data = new byte[normals.Length * 20];

        for (int i = 0; i < normals.Length; i++)
        {
            int at = i * 20;
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at), normals[i].X);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 4), normals[i].Y);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(at + 8), normals[i].Z);
        }

        return data;
    }

    /// <summary>Faces in the 56-byte version 20/21 layout; only the fields read are filled.</summary>
    private static byte[] Faces(params (ushort Plane, byte Side, int FirstEdge, short EdgeCount)[] faces)
    {
        byte[] data = new byte[faces.Length * 56];

        for (int i = 0; i < faces.Length; i++)
        {
            int at = i * 56;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), faces[i].Plane);
            data[at + 2] = faces[i].Side;
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at + 4), faces[i].FirstEdge);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(at + 8), faces[i].EdgeCount);
        }

        return data;
    }

    /// <summary>A square: four vertices, four edges, one face on a plane facing as given.</summary>
    private static byte[] Square(float normalZ = 1f) =>
        BuildBsp(new Dictionary<int, byte[]>
        {
            [LumpPlanes] = Planes((0f, 0f, normalZ)),
            [LumpVertexes] = Vertexes((0f, 0f, 0f), (10f, 0f, 0f), (10f, 10f, 0f), (0f, 10f, 0f)),
            [LumpEdges] = Edges((0, 1), (1, 2), (2, 3), (3, 0)),
            [LumpSurfedges] = Surfedges(0, 1, 2, 3),
            [LumpFaces] = Faces((0, 0, 0, 4)),
        });

    [Test]
    public void AFaceBecomesAPolygonOfItsVertices()
    {
        BspGeometry geometry = BspGeometry.Read(Square());

        BspFace face = geometry.Faces.ShouldHaveSingleItem();

        face.Points.Count.ShouldBe(4);
        face.Points[0].ShouldBe((0f, 0f, 0f));
        face.Points[2].ShouldBe((10f, 10f, 0f));
    }

    [Test]
    public void ANegativeSurfedgeReadsItsEdgeBackwards()
    {
        // The sign convention: a positive surfedge reads the edge's first vertex then its second,
        // a negative one reads the reverse. Ignoring the sign gives a polygon whose points jump
        // back and forth and whose winding is meaningless.
        byte[] file = BuildBsp(new Dictionary<int, byte[]>
        {
            [LumpPlanes] = Planes((0f, 0f, 1f)),
            [LumpVertexes] = Vertexes((0f, 0f, 0f), (10f, 0f, 0f)),

            // Edge 0 is a placeholder. The sign carries the direction and the MAGNITUDE is the
            // index, so "edge 0, backwards" is inexpressible - there is no negative zero. Real
            // maps have the same property, which is why edge 0 is conventionally unused.
            [LumpEdges] = Edges((0, 0), (0, 1)),
            [LumpSurfedges] = Surfedges(-1),
            [LumpFaces] = Faces((0, 0, 0, 1)),
        });

        BspGeometry geometry = BspGeometry.Read(file);

        // Edge 1 read backwards yields its SECOND vertex first.
        geometry.Faces[0].Points[0].ShouldBe((10f, 0f, 0f));
    }

    [Test]
    public void AFaceCarriesTheNormalOfItsPlane()
    {
        BspGeometry geometry = BspGeometry.Read(Square(normalZ: 1f));

        geometry.Faces[0].Normal.Z.ShouldBe(1f, tolerance: 0.0001f);
    }

    [Test]
    public void SideFlippedFacesUseTheOppositeNormal()
    {
        // A face on the back side of its plane has side = 1 and faces the other way. Ignoring
        // that makes half the world's ceilings look like floors - which is precisely the
        // distinction the overhead view filters on.
        byte[] file = BuildBsp(new Dictionary<int, byte[]>
        {
            [LumpPlanes] = Planes((0f, 0f, 1f)),
            [LumpVertexes] = Vertexes((0f, 0f, 0f), (10f, 0f, 0f), (10f, 10f, 0f)),
            [LumpEdges] = Edges((0, 1), (1, 2), (2, 0)),
            [LumpSurfedges] = Surfedges(0, 1, 2),
            [LumpFaces] = Faces((0, 1, 0, 3)),
        });

        BspGeometry geometry = BspGeometry.Read(file);

        geometry.Faces[0].Normal.Z.ShouldBe(-1f, tolerance: 0.0001f);
    }

    [Test]
    public void AFaceIndexingPastTheSurfedgeLumpIsRejected()
    {
        // Every cross-lump index is a number from an untrusted file. D32's rule is to bounds
        // check at USE, even though the lump it came from already validated.
        byte[] file = BuildBsp(new Dictionary<int, byte[]>
        {
            [LumpPlanes] = Planes((0f, 0f, 1f)),
            [LumpVertexes] = Vertexes((0f, 0f, 0f)),
            [LumpEdges] = Edges((0, 0)),
            [LumpSurfedges] = Surfedges(0),
            [LumpFaces] = Faces((0, 0, 0, 500)),
        });

        Should.Throw<InvalidDataException>(() => BspGeometry.Read(file));
    }

    [Test]
    public void AnEdgeIndexingPastTheVertexLumpIsRejected()
    {
        byte[] file = BuildBsp(new Dictionary<int, byte[]>
        {
            [LumpPlanes] = Planes((0f, 0f, 1f)),
            [LumpVertexes] = Vertexes((0f, 0f, 0f)),
            // The out-of-range vertex has to be the one this surfedge actually reads: a positive
            // surfedge takes the edge's FIRST vertex, so putting 900 second would never be looked
            // at and the test would pass without exercising the check.
            [LumpEdges] = Edges((900, 0)),
            [LumpSurfedges] = Surfedges(0),
            [LumpFaces] = Faces((0, 0, 0, 1)),
        });

        Should.Throw<InvalidDataException>(() => BspGeometry.Read(file));
    }

    [Test]
    public void AFaceNamingAPlaneThatDoesNotExistIsRejected()
    {
        byte[] file = BuildBsp(new Dictionary<int, byte[]>
        {
            [LumpPlanes] = Planes((0f, 0f, 1f)),
            [LumpVertexes] = Vertexes((0f, 0f, 0f)),
            [LumpEdges] = Edges((0, 0)),
            [LumpSurfedges] = Surfedges(0),
            [LumpFaces] = Faces((77, 0, 0, 1)),
        });

        Should.Throw<InvalidDataException>(() => BspGeometry.Read(file));
    }

    [Test]
    public void CeilingsAreDroppedFromTheOverheadView()
    {
        // What was asked for: freecam looking down, without the ceilings that occlude when zoomed
        // out. A ceiling's normal points down into the room, so it is exactly the downward set.
        BspGeometry floor = BspGeometry.Read(Square(normalZ: 1f));
        BspGeometry ceiling = BspGeometry.Read(Square(normalZ: -1f));

        floor.OverheadFaces.Count.ShouldBe(1);
        ceiling.OverheadFaces.ShouldBeEmpty();
    }

    [Test]
    public void WallsSurviveTheOverheadFilter()
    {
        // Near-vertical faces give a top-down view its room outlines, so the filter must drop
        // only what points downward rather than everything that is not a floor.
        BspGeometry walls = BspGeometry.Read(Square(normalZ: 0f));

        walls.OverheadFaces.Count.ShouldBe(1);
    }

    [Test]
    public void ADegenerateFaceIsSkippedRatherThanFatal()
    {
        // Real maps contain faces with no edges. One should not cost the rest of the map.
        byte[] file = BuildBsp(new Dictionary<int, byte[]>
        {
            [LumpPlanes] = Planes((0f, 0f, 1f)),
            [LumpVertexes] = Vertexes((0f, 0f, 0f), (10f, 0f, 0f), (10f, 10f, 0f)),
            [LumpEdges] = Edges((0, 1), (1, 2), (2, 0)),
            [LumpSurfedges] = Surfedges(0, 1, 2),
            [LumpFaces] = Faces((0, 0, 0, 0), (0, 0, 0, 3)),
        });

        BspGeometry geometry = BspGeometry.Read(file);

        geometry.Faces.Count.ShouldBe(1);
    }
}
