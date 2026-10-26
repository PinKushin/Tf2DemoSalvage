using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// One sweep clipped against convex brushes in Source's published form: <c>IntersectRayWithBox</c> and
/// <c>IntersectRayWithOBB</c> (<c>public/collisionutils.cpp:1130-1202</c>, <c>:1465-1685</c>), both called with
/// <c>flTolerance = DIST_EPSILON</c>.
/// </summary>
/// <remarks>
/// **Evidence class: published source of a sibling, interpolated to the brush clip.** The engine's own
/// <c>CM_ClipBoxToBrush</c> is in <c>engine/cmodel.cpp</c>, which the SDK does not ship; the two published clips are the
/// SOLID_BBOX and OBB entity clips and carry the same tolerance idiom. Per plane, <c>d1</c> and <c>d2</c> the start's and
/// end's distance outside it:
/// <code>
///   if (d1 &gt; 0 &amp;&amp; d2 &gt; 0) return false;           // :1158, :1610
///   if (d1 &lt;= 0 &amp;&amp; d2 &lt;= 0) continue;
///   entering: f = d1 - tol; if (f &lt; 0) f = 0; f /= (d1-d2); if (f &gt; t1) { t1 = f; side = i; }   // :1176-1189, :1624-1637
///   leaving:  f = (d1 + tol) / (d1-d2); if (f &lt; t2) t2 = f;
///   hit when t1 &lt; t2 &amp;&amp; t1 &gt;= 0                       // :1201, :1651
///   start inside: fraction 0, allsolid = t2 &lt;= 0 || t2 &gt;= 1, normal (1, 0, 0)   // :1240-1250, :1672-1678
/// </code>
/// The multi-brush walk (<c>CM_RecursiveHullCheck</c>, <c>CM_TraceToLeaf</c>) is closed too; it is read from Quake 2's
/// <c>qcommon/cmodel.c</c> (id Software's GPL release), the ancestor, *interpolated to Source*.
/// </remarks>
public sealed class BrushClipConformanceTests
{
    /// <summary>Valve's <c>DIST_EPSILON</c>.</summary>
    private const float Epsilon = 0.03125f;

    private static readonly (float X, float Y, float Z) Point = (0f, 0f, 0f);

    [Test]
    public void Trace_TwoFacesInsideTheEpsilonAtACorner_KeepsTheFirstSide()
    {
        // +x is 0.01 off and +y 0.02 off, both inside the epsilon: each entry clamps to 0, and `f > t1` keeps the first
        // side to reach 0. The raw entries are −0.02125 and −0.01125, so keeping the greater raw value would answer +y.
        BspTrace trace = Single(Cube()).Trace(4.01f, 4.02f, 0f, 3.01f, 3.02f, 0f, Point);

        trace.Fraction.ShouldBe(0f);
        trace.Normal.ShouldBe((1f, 0f, 0f));
        trace.Texinfo.ShouldBe(0);
    }

    [Test]
    public void Trace_AStepShorterThanTheGapToTheFace_IsKeptAtZero()
    {
        // d1 = 0.01, d2 = −0.005: the clamped entry is 0 / 0.015 = 0. There is no lower bound on the entry, so a step
        // that would cross the face is stopped where it stands rather than let into the brush.
        BspTrace trace = Single(Cube()).Trace(4.01f, 0f, 0f, 3.995f, 0f, 0f, Point);

        trace.Fraction.ShouldBe(0f);
        trace.Normal.ShouldBe((1f, 0f, 0f));
        trace.StartSolid.ShouldBeFalse();
    }

    [Test]
    public void Trace_EndingExactlyOnAFacePlane_EntersAnEpsilonShort()
    {
        // d1 = 1, d2 = 0: not `d1 > 0 && d2 > 0`, so it crosses, at (1 − ε) / 1.
        Single(Cube()).Trace(5f, 0f, 0f, 4f, 0f, 0f, Point).Fraction.ShouldBe(1f - Epsilon);
    }

    [Test]
    public void Trace_EnteredAtZeroButLeftAtOrBeforeZero_Misses()
    {
        // +x: d1 = 0.01, entry clamped to 0. +y: d1 = −0.02, d2 = 0.98, leave (−0.02 + ε) / −1 = −0.01125. The CLAMPED
        // entry is compared, 0 < −0.01125 is false, and the corner is passed; the raw −0.02125 would have hit.
        Single(Cube()).Trace(4.01f, 3.98f, 0f, 3.01f, 4.98f, 0f, Point).Fraction.ShouldBe(1f);
    }

    [Test]
    public void Trace_StartingInsideWithinTheEpsilonOfAFaceAndLeaving_IsAllSolid()
    {
        // d1 = −0.01, d2 = 1: leave (−0.01 + ε) / −1.01 is below 0, and `t2 <= 0` is allsolid although the end is out.
        BspTrace trace = Single(Cube()).Trace(3.99f, 0f, 0f, 5f, 0f, 0f, Point);

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeTrue();
        trace.Fraction.ShouldBe(0f);
    }

    [Test]
    public void Trace_StartingDeepInsideAndLeaving_IsStartSolidOnlyWithTheConventionalNormal()
    {
        // The control: leave (−3 + ε) / −9 = 0.330 is between 0 and 1. Fraction 0, plane normal (1, 0, 0) and plane.dist
        // startpos[0] — the start's x, which a particle's TestForPlane turns into its collision plane.
        BspTrace trace = Single(Cube()).Trace(1f, 0f, 0f, 10f, 0f, 0f, Point);

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeFalse();
        trace.Fraction.ShouldBe(0f);
        trace.Normal.ShouldBe((1f, 0f, 0f));
        trace.Distance.ShouldBe(1f);
        trace.Texinfo.ShouldBe(-1);
    }

    [Test]
    public void Trace_StartingInsideAndStayingInside_IsAllSolid()
    {
        // No plane is left, t2 stays 1: `t2 >= 1`.
        BspTrace trace = Single(Cube()).Trace(0f, 0f, 0f, 1f, 0f, 0f, Point);

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeTrue();
    }

    [Test]
    public void Trace_AnEpsilonEntryThenABrushInTheFarLeafItStartsIn_StopsAtZeroWithoutStartSolid()
    {
        // Quake 2's `CM_RecursiveHullCheck`: `if (trace_trace.fraction <= p1f) return;` (cmodel.c:1239). The near leaf's
        // brush is entered at 0, so the far leaf, which starts at 0.5, is never clipped and its brush's startsolid is
        // never seen. Interpolated to Source.
        BspLeafTree world = Tree(
            (1f, 0f, 0f, 0f),
            front: [[(1f, 0f, 0f, 0.99f), (-1f, 0f, 0f, 10f)]],
            back: [[(1f, 0f, 0f, 5f), (-1f, 0f, 0f, 5f)]]);

        BspTrace trace = world.Trace(1f, 0f, 0f, -1f, 0f, 0f, Point);

        trace.Fraction.ShouldBe(0f);
        trace.Texinfo.ShouldBe(0);
        trace.StartSolid.ShouldBeFalse();
    }

    /// <summary>The cube |x|, |y|, |z| ≤ 4, sides +x, +y, −x, −y, +z, −z; side i's texinfo is i.</summary>
    private static (float X, float Y, float Z, float D)[] Cube() =>
    [
        (1f, 0f, 0f, 4f), (0f, 1f, 0f, 4f), (-1f, 0f, 0f, 4f), (0f, -1f, 0f, 4f), (0f, 0f, 1f, 4f), (0f, 0f, -1f, 4f),
    ];

    /// <summary>One brush in one leaf, behind a node every point is in front of.</summary>
    private static BspLeafTree Single((float X, float Y, float Z, float D)[] brush) =>
        Tree((0f, 0f, 1f, -100000f), front: [brush], back: []);

    /// <summary>
    /// One node splitting into a front leaf (1) and a back leaf (2), each holding the brushes given, solid. Brush b's side
    /// s has texinfo b·10 + s, numbered within its leaf.
    /// </summary>
    private static BspLeafTree Tree(
        (float X, float Y, float Z, float D) split,
        IReadOnlyList<(float X, float Y, float Z, float D)[]> front,
        IReadOnlyList<(float X, float Y, float Z, float D)[]> back)
    {
        List<(float X, float Y, float Z, float D)> planes = [split];
        List<(int First, int Count)> brushes = [];
        List<(int Plane, int Texinfo)> sides = [];

        foreach (IReadOnlyList<(float X, float Y, float Z, float D)[]> leaf in new[] { front, back })
        {
            for (int brush = 0; brush < leaf.Count; brush++)
            {
                brushes.Add((sides.Count, leaf[brush].Length));

                for (int side = 0; side < leaf[brush].Length; side++)
                {
                    sides.Add((planes.Count, (brush * 10) + side));
                    planes.Add(leaf[brush][side]);
                }
            }
        }

        byte[] planeBytes = new byte[planes.Count * 20];

        for (int each = 0; each < planes.Count; each++)
        {
            Span<byte> at = planeBytes.AsSpan(each * 20);

            BinaryPrimitives.WriteSingleLittleEndian(at, planes[each].X);
            BinaryPrimitives.WriteSingleLittleEndian(at[4..], planes[each].Y);
            BinaryPrimitives.WriteSingleLittleEndian(at[8..], planes[each].Z);
            BinaryPrimitives.WriteSingleLittleEndian(at[12..], planes[each].D);
        }

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -2);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -3);

        byte[] leaves = new byte[96];

        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 24), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 26), (ushort)front.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(64 + 24), (ushort)front.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(64 + 26), (ushort)back.Count);

        byte[] leafBrushes = new byte[brushes.Count * 2];

        for (int each = 0; each < brushes.Count; each++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(leafBrushes.AsSpan(each * 2), (ushort)each);
        }

        byte[] brushBytes = new byte[brushes.Count * 12];

        for (int each = 0; each < brushes.Count; each++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(brushBytes.AsSpan(each * 12), brushes[each].First);
            BinaryPrimitives.WriteInt32LittleEndian(brushBytes.AsSpan((each * 12) + 4), brushes[each].Count);
            BinaryPrimitives.WriteInt32LittleEndian(brushBytes.AsSpan((each * 12) + 8), 0x1);
        }

        byte[] sideBytes = new byte[sides.Count * 8];

        for (int each = 0; each < sides.Count; each++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(sideBytes.AsSpan(each * 8), (ushort)sides[each].Plane);
            BinaryPrimitives.WriteInt16LittleEndian(sideBytes.AsSpan((each * 8) + 2), (short)sides[each].Texinfo);
        }

        return BspLeafTree.FromCollisionLumps(node, planeBytes, leaves, leafBrushes, brushBytes, sideBytes);
    }
}
