using System;
using System.Buffers.Binary;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Which brush contents stop a sweep — <c>MASK_SOLID</c>.
/// </summary>
/// <remarks>
/// **The mask is what a camera collides WITH, and testing only <c>CONTENTS_SOLID</c> was wrong.**
/// <c>CalcChaseCamView</c> traces with <c>MASK_SOLID</c>, which is
/// <c>CONTENTS_SOLID|CONTENTS_MOVEABLE|CONTENTS_WINDOW|CONTENTS_MONSTER|CONTENTS_GRATE</c>
/// (<c>bspflags.h:106</c>). A solid-only test slides the camera through glass, through grates, and
/// through the brushes of doors and lifts.
///
/// **<c>CONTENTS_MONSTER</c> is absent on Valve's own authority**: its declaration says *"should
/// never be on a brush, only in game"* (<c>bspflags.h:71</c>), which is why Valve also ships
/// <c>MASK_SOLID_BRUSHONLY</c> with exactly the four bits a brush can carry (<c>bspflags.h:132</c>).
///
/// **One brush, so the prediction is exact.** A compiled map has thousands and no way to isolate
/// one; this world is a single half-space at z = 0 whose contents each case chooses, so "stopped
/// halfway" and "did not stop" are the only two outcomes and neither can happen by accident.
/// </remarks>
public sealed class BspTraceMaskConformanceTests
{
    [TestCase(0x1, TestName = "Sweep_AgainstASolidBrush_Stops")]
    [TestCase(0x2, TestName = "Sweep_AgainstGlass_Stops")]
    [TestCase(0x8, TestName = "Sweep_AgainstAGrate_Stops")]
    [TestCase(0x4000, TestName = "Sweep_AgainstAMovingBrush_Stops")]
    public void Sweep_AgainstAMaskedBrush_StopsHalfway(int contents)
    {
        // 100 down to -100 crosses z = 0 exactly halfway.
        World(contents).Sweep(0f, 0f, 100f, 0f, 0f, -100f, halfExtent: 0f)
            .ShouldBe(0.5f, 0.01f);
    }

    [TestCase(0x20, TestName = "Sweep_ThroughWater_DoesNotStop")]
    [TestCase(0x10000, TestName = "Sweep_ThroughAPlayerClip_DoesNotStop")]
    [TestCase(0x40000000, TestName = "Sweep_ThroughATrigger_DoesNotStop")]
    public void Sweep_AgainstAnUnmaskedBrush_PassesThrough(int contents)
    {
        // **The controls, and they carry the weight.** Every positive case above is also satisfied
        // by a trace that stops at ANY brush regardless of contents — which is what a mask of
        // "anything non-zero" would be. These say the mask excludes as well as includes: a camera
        // must pass through water, through volumes clipped for players only, and through triggers.
        World(contents).Sweep(0f, 0f, 100f, 0f, 0f, -100f, halfExtent: 0f)
            .ShouldBe(1f, 0.01f);
    }

    /// <summary>A world of one half-space at z = 0, with the contents given.</summary>
    /// <remarks>
    /// A single brush side suffices: the sweep is outside its plane at the start and inside at the
    /// end, which is the only crossing the clip needs to find. Leaf 1 is below the plane and is the
    /// one that lists the brush.
    /// </remarks>
    private static BspLeafTree World(int contents)
    {
        byte[] plane = new byte[20];

        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(8), 1f);
        BinaryPrimitives.WriteSingleLittleEndian(plane.AsSpan(12), 0f);

        byte[] node = new byte[32];

        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(0), 0);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(4), -0 - 1);
        BinaryPrimitives.WriteInt32LittleEndian(node.AsSpan(8), -1 - 1);

        // Two leaves. The lower one owns brush 0: firstleafbrush at +24, numleafbrushes at +26.
        byte[] leaves = new byte[64];

        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 24), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(leaves.AsSpan(32 + 26), 1);

        byte[] leafBrushes = new byte[2];

        BinaryPrimitives.WriteUInt16LittleEndian(leafBrushes, 0);

        // dbrush_t: firstside, numsides, contents.
        byte[] brushes = new byte[12];

        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(0), 0);
        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(brushes.AsSpan(8), contents);

        // dbrushside_t: planenum, texinfo, dispinfo, bevel.
        byte[] brushSides = new byte[8];

        BinaryPrimitives.WriteUInt16LittleEndian(brushSides.AsSpan(0), 0);

        return BspLeafTree.FromCollisionLumps(
            node, plane, leaves, leafBrushes, brushes, brushSides);
    }
}
