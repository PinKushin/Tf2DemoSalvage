using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The overlay lump's packed field, with Valve's constants on one side and this reader's on the other.
/// </summary>
/// <remarks>
/// **`doverlay_t` packs two fields into one sixteen-bit word**, and getting the split wrong does not
/// throw — it produces a face count in the tens of thousands for any overlay whose render order is
/// not zero, and the face loop then walks off the end of the record. The reader carries three
/// constants for it, each with a doc comment citing an engine identifier, and until now nothing
/// compared any of them to the identifier they cite.
///
/// **That is the whole point of the conformance sweep**, in the owner's words:
///
/// > "the conf tests have to test our code against valves or its really not testing anything because
/// > im pretty sure valve tested their code themselves, a lot, so us retesting the unchanging sdk is
/// > worthless."
///
/// So every value below is parsed out of <c>bspfile.h</c> and asserted against
/// <see cref="BspOverlays"/>'s own constant. None of it is restated in this file.
/// </remarks>
public sealed class OverlayLumpConformanceTests
{
    private const string BspFile = "src/public/bspfile.h";

    [SetUp]
    public void RequireTheSdk()
    {
        if (!SourceSdk.Available)
        {
            Assert.Ignore(SourceSdk.Missing);
        }
    }

    [Test]
    public void RenderOrderMask_OurConstant_IsValvesOverlayRenderOrderMask()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(BspFile);

        constants.TryGetValue("OVERLAY_RENDER_ORDER_MASK", out int valves).ShouldBeTrue(
            "OVERLAY_RENDER_ORDER_MASK was not found in bspfile.h");

        BspOverlays.RenderOrderMask.ShouldBe(valves);

        // **The control, because 0xC000 has to be the TOP two bits of a sixteen-bit field and not
        // merely two bits.** A mask of 0x0003 would pass a bare equality against a mistyped
        // constant; this states the shape independently.
        valves.ShouldBe(0xC000);
        System.Numerics.BitOperations.PopCount((uint)valves).ShouldBe(2);
    }

    [Test]
    public void RenderOrderShift_OurConstant_IsSixteenMinusValvesBitCount()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(BspFile);

        constants.TryGetValue("OVERLAY_RENDER_ORDER_NUM_BITS", out int bits).ShouldBeTrue(
            "OVERLAY_RENDER_ORDER_NUM_BITS was not found in bspfile.h");

        // How Valve writes it, at bspfile.h:1052:
        //
        //     return ( m_nFaceCountAndRenderOrder >> ( 16 - OVERLAY_RENDER_ORDER_NUM_BITS ) );
        //
        // Derived rather than transcribed: writing 14 into this test would pin what I believe the
        // shift is, and the arithmetic is the thing worth checking.
        BspOverlays.RenderOrderShift.ShouldBe(16 - bits);

        // And the two constants have to agree with each other, which is the check neither of them
        // can do alone: shifting the mask down by the shift must leave exactly the orders.
        (BspOverlays.RenderOrderMask >> BspOverlays.RenderOrderShift).ShouldBe((1 << bits) - 1);
    }

    [Test]
    public void MaximumFaces_OurGuard_IsValvesOverlayBspFaceCount()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(BspFile);

        constants.TryGetValue("OVERLAY_BSP_FACE_COUNT", out int valves).ShouldBeTrue(
            "OVERLAY_BSP_FACE_COUNT was not found in bspfile.h");

        // **A guard stricter than the engine rejects maps the engine loads** — the hazard
        // CapacityGuardTests exists for — and one looser than the engine reads past the record.
        // Equality is the only safe relation here, so it is asserted as equality.
        BspOverlays.MaximumFaces.ShouldBe(valves);

        // The control: the WATER overlay's face count is a different number in the same header, so a
        // loose parse that picked up the neighbouring define would be caught rather than confirmed.
        constants.TryGetValue("WATEROVERLAY_BSP_FACE_COUNT", out int water).ShouldBeTrue();
        water.ShouldNotBe(valves);
    }

    [Test]
    public void FaceCountAndRenderOrder_OurSplit_AgreesWithValvesAccessorsAcrossEveryOrder()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(BspFile);

        int mask = constants["OVERLAY_RENDER_ORDER_MASK"];
        int bits = constants["OVERLAY_RENDER_ORDER_NUM_BITS"];
        int orders = constants["OVERLAY_NUM_RENDER_ORDERS"];

        orders.ShouldBe(1 << bits, "OVERLAY_NUM_RENDER_ORDERS is (1<<OVERLAY_RENDER_ORDER_NUM_BITS)");

        // **Valve's two accessors, transcribed from bspfile.h:1041 and :1052 and applied to every
        // combination this project can meet.** The reader's split is the same arithmetic written
        // differently, and "written differently" is exactly the case where a transcription can be
        // wrong for one input and right for the rest.
        //
        // Every render order against face counts either side of each interesting boundary: zero,
        // one, the maximum, and the value that would be produced by forgetting to mask.
        foreach (int order in new[] { 0, 1, 2, 3 })
        {
            foreach (int faces in new[] { 0, 1, 63, BspOverlays.MaximumFaces })
            {
                ushort packed = (ushort)((faces & ~mask) | (order << (16 - bits)));

                int ourFaces = packed & ~BspOverlays.RenderOrderMask;
                int ourOrder = (packed & BspOverlays.RenderOrderMask) >> BspOverlays.RenderOrderShift;

                ourFaces.ShouldBe(faces, $"face count survives order {order}");
                ourOrder.ShouldBe(order, $"render order {order} survives a face count of {faces}");
            }
        }

        // **The condition that separates a correct reader from one that ignores the packing**, which
        // the loop above would pass even if the mask were zero for order 0. An overlay with 8 faces
        // at render order 3 packs to 0xC008; read whole, that is 49,160 faces.
        ushort real = (ushort)(8 | (3 << 14));

        ((int)real).ShouldBe(49160, "the value a reader that ignores the split would see");
        (real & ~BspOverlays.RenderOrderMask).ShouldBe(8);
        ((real & BspOverlays.RenderOrderMask) >> BspOverlays.RenderOrderShift).ShouldBe(3);
    }

    [Test]
    public void OverlayFades_ValvesLumpSixty_IsNotAmongTheLumpsThisReaderNames()
    {
        IReadOnlyDictionary<string, int> constants = SourceSdk.Constants(BspFile);

        constants.TryGetValue("LUMP_OVERLAY_FADES", out int fades).ShouldBeTrue(
            "LUMP_OVERLAY_FADES was not found in bspfile.h");

        fades.ShouldBe(60);

        // The record is fixed-size and one per overlay, so the data is present in every map.
        string text = SourceSdk.Text(BspFile)
            ?? throw new InvalidOperationException("bspfile.h is missing");

        Match fade = new Regex(
            @"struct doverlayfade_t(?s).{0,400}?\n\};",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(10)).Match(text);

        fade.Success.ShouldBeTrue("doverlayfade_t was not found");
        fade.Value.ShouldContain("flFadeDistMinSq");
        fade.Value.ShouldContain("flFadeDistMaxSq");

        // **Ours: the gap, measured against the reader rather than asserted in prose.**
        // BspLumpIndex names every lump this project reads, and 60 is not among them — so every
        // overlay draws at every distance where the engine fades it out. Lump 45 beside it IS read,
        // which is the control: without that, "60 is absent" would be consistent with the whole
        // enum being empty.
        IReadOnlyList<int> lumps = Lumps();

        lumps.ShouldContain(BspLumpIndex.Overlays, "the overlay lump itself is read");

        lumps.ShouldNotContain(
            fades,
            "lump 60 is not read; when it is, delete this assertion and test the fade distances "
            + "against r_overlayfademin / r_overlayfademax instead (D45)");

        // Read 2026-08-21 from the live client's engine.dll, beside COverlayMgr::RenderOverlays:
        // r_renderoverlayfragment, r_overlaywireframe, r_overlayfadeenable, r_overlayfademin,
        // r_overlayfademax. Recorded as the names an implementation should be checked against; not
        // asserted, because the binary is not in the repository and must never be — see
        // docs/memory/where-the-game-and-clients-live.md.
    }

    /// <summary>Every lump index this project names.</summary>
    private static List<int> Lumps()
    {
        List<int> indices = [];

        foreach (FieldInfo field in typeof(BspLumpIndex).GetFields(
            BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && field.GetRawConstantValue() is int value)
            {
                indices.Add(value);
            }
        }

        indices.ShouldNotBeEmpty("BspLumpIndex read as empty would make the assertions above vacuous");

        return indices;
    }
}
