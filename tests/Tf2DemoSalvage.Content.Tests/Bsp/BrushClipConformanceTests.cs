using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// One sweep clipped against brushes as `engine.dll` does it (build 11191711, B681): the plane clips `FUN_1801686b0` (a point) and
/// `FUN_180168a00` (a box), the box-brush slab clip `FUN_18016d670`, and the leaf loops `FUN_1801694a0`/`FUN_1801698a0`.
/// </summary>
/// <remarks>
/// **Evidence class: read in decompile, every value predicted by hand from it.** Per plane, `d1` and `d2` the start's and end's distance
/// outside it:
/// <code>
///   point: a bevel side is skipped; box: no side is
///   d1 &lt;= 0: d2 &lt;= 0 continue, else getout;   d1 &gt; 0: startout, d2 &gt; 0 return
///   leaving:  t = (d1 + ε) · 1/(d1 − d2), the least;  entering: t = max(d1 − ε, 0) · 1/(d1 − d2), the greatest, first side on a tie
///   startout (and, for a point, fractionleftsolid ≤ enter): miss when leave ≤ enter or fraction ≤ enter, else hit at enter
///   otherwise startsolid: no getout is allsolid at fraction 0; else a leave above fractionleftsolid is kept, and a fraction at or
///   below it goes back to 1 with no surface
/// </code>
/// A brush of six sides on axial planes is a box (`FUN_18016e7e0`) and takes the slab clip, which decides crossing and contact without
/// the epsilon. So a cube and the same cube with a seventh, redundant side can answer differently, and several cases here say so.
/// </remarks>
public sealed class BrushClipConformanceTests
{
    /// <summary>Valve's <c>DIST_EPSILON</c>.</summary>
    private const float Epsilon = 0.03125f;

    private const float Tolerance = 1e-6f;

    private static readonly (float X, float Y, float Z) Point = (0f, 0f, 0f);

    /// <summary>
    /// +x is 0.01 off and +y 0.02 off. The plane clip clamps both entries to 0 and keeps the first side, +x. The slab clip takes the
    /// latest RAW entry, −0.01125 on y against −0.02125 on x, and reports +y.
    /// </summary>
    [TestCase(false, 1f, 0f, 0, TestName = "Trace_TwoFacesInsideTheEpsilonAtACornerOfAPlaneBrush_KeepsTheFirstSide")]
    [TestCase(true, 0f, 1f, 1, TestName = "Trace_TwoFacesInsideTheEpsilonAtACornerOfABox_TakesTheLatestRawEntry")]
    public void Trace_TwoFacesInsideTheEpsilonAtACorner_ReportsTheEnginesSide(bool box, float normalX, float normalY, int texinfo)
    {
        BspTrace trace = Single(Cube(box)).Trace(4.01f, 4.02f, 0f, 3.01f, 3.02f, 0f, Point);

        trace.Fraction.ShouldBe(0f);
        trace.Normal.ShouldBe((normalX, normalY, 0f));
        trace.Texinfo.ShouldBe(texinfo);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Trace_AStepShorterThanTheGapToTheFace_IsKeptAtZero(bool box)
    {
        // d1 = 0.01, d2 = −0.005: the clamped entry is 0. Both clips stop it where it stands.
        BspTrace trace = Single(Cube(box)).Trace(4.01f, 0f, 0f, 3.995f, 0f, 0f, Point);

        trace.Fraction.ShouldBe(0f);
        trace.Normal.ShouldBe((1f, 0f, 0f));
        trace.StartSolid.ShouldBeFalse();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Trace_EndingExactlyOnAFacePlane_EntersAnEpsilonShort(bool box) =>
        Single(Cube(box)).Trace(5f, 0f, 0f, 4f, 0f, 0f, Point).Fraction.ShouldBe(1f - Epsilon);

    /// <summary>
    /// +x entered at 0, +y left at −0.01125. The plane clip compares the two (leave ≤ enter) and misses. The slab clip finds the ray
    /// touching without the epsilon, 0.01 before 0.02, and with it reports a hit at 0 on +x.
    /// </summary>
    [TestCase(false, 1f, TestName = "Trace_EnteredAtZeroButLeftBeforeZeroOfAPlaneBrush_Misses")]
    [TestCase(true, 0f, TestName = "Trace_EnteredAtZeroButLeftBeforeZeroOfABox_HitsAtZero")]
    public void Trace_EnteredAtZeroButLeftAtOrBeforeZero_IsTheEnginesAnswer(bool box, float fraction) =>
        Single(Cube(box)).Trace(4.01f, 3.98f, 0f, 3.01f, 4.98f, 0f, Point).Fraction.ShouldBe(fraction);

    /// <summary>
    /// A start 0.01 inside +x, moving out: `startsolid`, NOT `allsolid`, and the fraction stays 1. The plane clip's leave, −0.021, is
    /// not above 0 and leaves `fractionleftsolid` at 0; the slab clip's is `(0.01 + ε) / 1.01`.
    /// </summary>
    [TestCase(false, 0f)]
    [TestCase(true, (0.00999975f + Epsilon) * (1f / 1.01f))]
    public void Trace_StartingInsideWithinTheEpsilonOfAFaceAndLeaving_GoesOnFromStartSolid(bool box, float leftSolid)
    {
        BspTrace trace = Single(Cube(box)).Trace(3.99f, 0f, 0f, 5f, 0f, 0f, Point);

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeFalse();
        trace.Fraction.ShouldBe(1f);
        trace.FractionLeftSolid.ShouldBe(leftSolid, 0.0001f);
    }

    /// <summary>A start deep inside, leaving at a third: `startsolid`, the fraction 1, `fractionleftsolid` where it got out, no surface.</summary>
    [TestCase(false, (-3f + Epsilon) * (1f / -9f))]
    [TestCase(true, (3f + Epsilon) * (1f / 9f))]
    public void Trace_StartingDeepInsideAndLeaving_GoesOnAndSaysWhereItLeft(bool box, float leftSolid)
    {
        BspTrace trace = Single(Cube(box)).Trace(1f, 0f, 0f, 10f, 0f, 0f, Point);

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeFalse();
        trace.Fraction.ShouldBe(1f);
        trace.FractionLeftSolid.ShouldBe(leftSolid, Tolerance);
        trace.Texinfo.ShouldBe(-1);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Trace_StartingInsideAndStayingInside_IsAllSolid(bool box)
    {
        BspTrace trace = Single(Cube(box)).Trace(0f, 0f, 0f, 1f, 0f, 0f, Point);

        trace.StartSolid.ShouldBeTrue();
        trace.AllSolid.ShouldBeTrue();
        trace.Fraction.ShouldBe(0f);
    }

    [Test]
    public void Trace_AnEpsilonEntryThenABrushInTheFarLeafItStartsIn_StopsAtZeroWithoutStartSolid()
    {
        // `if ( fraction <= p1f ) return;`: the near leaf's brush is entered at 0, so the far leaf is never clipped.
        BspLeafTree world = Tree(
            (1f, 0f, 0f, 0f),
            front: [[(1f, 0f, 0f, 0.99f), (-1f, 0f, 0f, 10f)]],
            back: [[(1f, 0f, 0f, 5f), (-1f, 0f, 0f, 5f)]]);

        BspTrace trace = world.Trace(1f, 0f, 0f, -1f, 0f, 0f, Point);

        trace.Fraction.ShouldBe(0f);
        trace.Texinfo.ShouldBe(0);
        trace.StartSolid.ShouldBeFalse();
    }

    /// <summary>
    /// **The answer turns on the leaf's brush order, as the engine's does.** A floor entered at 0 (resting feet) and a box the feet start
    /// inside and leave at 0.54125. Floor first: fraction 0, and `if ( fraction == 0 ) return;` never clips the other. The other first:
    /// `startsolid`, `fractionleftsolid` 0.54125, and then the floor's entry at 0 is before it, so the point rule makes the floor a start
    /// inside that never gets out: `allsolid`.
    /// </summary>
    [TestCase(false, false, false, TestName = "Trace_AnEpsilonFloorThenABrushItStartsInInOneLeaf_StopsOnTheFloor")]
    [TestCase(true, true, true, TestName = "Trace_ABrushItStartsInThenAnEpsilonFloorInOneLeaf_IsAllSolid")]
    public void Trace_AnEpsilonFloorAndABrushItStartsInInOneLeaf_DependsOnTheOrder(bool startInsideFirst, bool startSolid, bool allSolid)
    {
        (float X, float Y, float Z, float D)[] floor =
        [
            (0f, 0f, 1f, 0f), (0f, 0f, -1f, 10f), (1f, 0f, 0f, 10f), (-1f, 0f, 0f, 10f), (0f, 1f, 0f, 10f), (0f, -1f, 0f, 10f),
        ];

        (float X, float Y, float Z, float D)[] around = [.. Cube(box: true)[..5], (0f, 0f, -1f, 0.5f)];

        BspLeafTree world = Tree((0f, 0f, 1f, -100000f), front: startInsideFirst ? [around, floor] : [floor, around], back: []);

        BspTrace trace = world.Trace(0f, 0f, 0.01f, 0f, 0f, -0.99f, Point);

        trace.Fraction.ShouldBe(0f);
        trace.StartSolid.ShouldBe(startSolid);
        trace.AllSolid.ShouldBe(allSolid);
    }

    /// <summary>
    /// A point skips a bevel side and a box does not. The bevel cuts the corner the start is in, 4.95 out along its normal: as a side it
    /// rejects the whole sweep; skipped, the point is inside every other side and stays there.
    /// </summary>
    [TestCase(true, true, TestName = "Trace_APointInsideABevelledCorner_SkipsTheBevelAndIsAllSolid")]
    [TestCase(false, false, TestName = "Trace_APointInsideACornerWhoseCutIsNotABevel_IsOutside")]
    public void Trace_APointInsideACutCorner_HonoursTheBevelFlag(bool bevel, bool allSolid)
    {
        (float X, float Y, float Z, float D)[] cut = [.. Cube(box: true), (0.70710677f, 0.70710677f, 0f, 4f)];

        BspTrace trace = Tree((0f, 0f, 1f, -100000f), [cut], [], bevelled: bevel ? [6] : []).Trace(3.5f, 3.5f, 0f, 3.5f, 3.5f, -1f, Point);

        trace.AllSolid.ShouldBe(allSolid);
        trace.Fraction.ShouldBe(allSolid ? 0f : 1f);
    }

    /// <summary>The control: a box with the same bevel is not a point, and clips against it.</summary>
    [Test]
    public void Trace_ABoxInsideABevelledCorner_ClipsTheBevel()
    {
        (float X, float Y, float Z, float D)[] cut = [.. Cube(box: true), (0.70710677f, 0.70710677f, 0f, 4f)];

        BspTrace trace = Tree((0f, 0f, 1f, -100000f), [cut], [], bevelled: [6]).Trace(3.4f, 3.4f, 0f, 3.4f, 3.4f, -1f, (0.01f, 0.01f, 0.01f));

        trace.AllSolid.ShouldBeFalse();
        trace.Fraction.ShouldBe(1f);
    }

    /// <summary>
    /// **Each brush once a trace.** The front leaf lists H then S, the back leaf H again. H is hit at 0.184375; S, which the ray starts
    /// in and leaves at 0.315625, puts the fraction back to 1. The back leaf is then walked, and H, already clipped, is not clipped again.
    /// </summary>
    [Test]
    public void Trace_ABrushInTwoLeavesAfterAStartInsideTookItsHitBack_IsNotClippedAgain()
    {
        (float X, float Y, float Z, float D)[] hit = [(1f, 0f, 0f, 0.6f), (-1f, 0f, 0f, 0.2f), (0f, 1f, 0f, 4f), (0f, -1f, 0f, 4f), (0f, 0f, 1f, 4f), (0f, 0f, -1f, 4f)];
        (float X, float Y, float Z, float D)[] inside = [(1f, 0f, 0f, 4f), (-1f, 0f, 0f, -0.4f), (0f, 1f, 0f, 4f), (0f, -1f, 0f, 4f), (0f, 0f, 1f, 4f), (0f, 0f, -1f, 4f)];

        BspLeafTree world = Tree((1f, 0f, 0f, 0f), [hit, inside], [], backIndices: [0]);

        BspTrace trace = world.Trace(1f, 0f, 0f, -1f, 0f, 0f, Point);

        trace.StartSolid.ShouldBeTrue();
        trace.FractionLeftSolid.ShouldBe(0.315625f, Tolerance);
        trace.Fraction.ShouldBe(1f);
    }

    /// <summary>The cube |x|, |y|, |z| ≤ 4, sides +x, +y, −x, −y, +z, −z; side i's texinfo is i. Not a box: a seventh, redundant side.</summary>
    private static (float X, float Y, float Z, float D)[] Cube(bool box) =>
        box
            ? [(1f, 0f, 0f, 4f), (0f, 1f, 0f, 4f), (-1f, 0f, 0f, 4f), (0f, -1f, 0f, 4f), (0f, 0f, 1f, 4f), (0f, 0f, -1f, 4f)]
            : [(1f, 0f, 0f, 4f), (0f, 1f, 0f, 4f), (-1f, 0f, 0f, 4f), (0f, -1f, 0f, 4f), (0f, 0f, 1f, 4f), (0f, 0f, -1f, 4f), (0.6f, 0.8f, 0f, 100f)];

    /// <summary>One brush in one leaf, behind a node every point is in front of.</summary>
    private static BspLeafTree Single((float X, float Y, float Z, float D)[] brush) =>
        Tree((0f, 0f, 1f, -100000f), front: [brush], back: []);

    /// <summary>`dplane_t.type` as vbsp writes it: 0-2 for an axial normal, else 3 plus the axis of the largest component.</summary>
    private static int PlaneType((float X, float Y, float Z, float D) plane)
    {
        if (MathF.Abs(plane.X) is 1f)
        {
            return 0;
        }

        if (MathF.Abs(plane.Y) is 1f)
        {
            return 1;
        }

        if (MathF.Abs(plane.Z) is 1f)
        {
            return 2;
        }

        float x = MathF.Abs(plane.X);
        float y = MathF.Abs(plane.Y);
        float z = MathF.Abs(plane.Z);

        if (x >= y && x >= z)
        {
            return 3;
        }

        return y >= z ? 4 : 5;
    }

    /// <summary>
    /// One node splitting into a front leaf (1) and a back leaf (2), solid. The front leaf lists the front brushes, the back leaf the back
    /// brushes, or <paramref name="backIndices"/> into all of them. Brush b's side s has texinfo b·10 + s, numbered within its list;
    /// <paramref name="bevelled"/> marks sides of brush 0 bevels.
    /// </summary>
    private static BspLeafTree Tree(
        (float X, float Y, float Z, float D) split,
        IReadOnlyList<(float X, float Y, float Z, float D)[]> front,
        IReadOnlyList<(float X, float Y, float Z, float D)[]> back,
        int[]? bevelled = null,
        int[]? backIndices = null)
    {
        List<(float X, float Y, float Z, float D)> planes = [split];
        List<(int First, int Count)> brushes = [];
        List<(int Plane, int Texinfo, bool Bevel)> sides = [];

        foreach (IReadOnlyList<(float X, float Y, float Z, float D)[]> leaf in new[] { front, back })
        {
            for (int brush = 0; brush < leaf.Count; brush++)
            {
                bool first = brushes.Count == 0;
                brushes.Add((sides.Count, leaf[brush].Length));

                for (int side = 0; side < leaf[brush].Length; side++)
                {
                    sides.Add((planes.Count, (brush * 10) + side, first && (bevelled?.Contains(side) ?? false)));
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
            BinaryPrimitives.WriteInt32LittleEndian(at[16..], PlaneType(planes[each]));
        }

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -2);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -3);

        List<int> leafBrushList = [.. Enumerable.Range(0, front.Count)];
        int[] backList = backIndices ?? [.. Enumerable.Range(front.Count, back.Count)];
        leafBrushList.AddRange(backList);

        byte[] leaves = new byte[96];

        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 24), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 26), (ushort)front.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(64 + 24), (ushort)front.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(64 + 26), (ushort)backList.Length);

        byte[] leafBrushes = new byte[leafBrushList.Count * 2];

        for (int each = 0; each < leafBrushList.Count; each++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(leafBrushes.AsSpan(each * 2), (ushort)leafBrushList[each]);
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
            sideBytes[(each * 8) + 6] = sides[each].Bevel ? (byte)1 : (byte)0;
        }

        return BspLeafTree.FromCollisionLumps(node, planeBytes, leaves, leafBrushes, brushBytes, sideBytes);
    }
}
