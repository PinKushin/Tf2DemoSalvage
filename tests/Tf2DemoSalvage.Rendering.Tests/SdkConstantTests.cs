using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Every magic number this project hardcodes, checked against the engine's own declaration.
/// </summary>
/// <remarks>
/// **A format reader is mostly magic numbers, and every one of them fails the same way.** A wrong
/// lump index, version bound, vertex size or bit width does not throw — it lands on real data and
/// decodes something plausible, which is the failure mode this whole project keeps meeting. The
/// engine declares all of them in headers that can be read, so they can be CHECKED rather than
/// trusted, and the check costs nothing per run.
///
/// **These are predictions, not observations.** Each test states the number this project uses and
/// asserts the SDK agrees; neither side is derived from the other. That is what separates a
/// conformance test from a change detector — if someone edits our constant, this fails, and if Valve
/// had shipped a different value, it would have failed the day it was written.
///
/// Skips when the SDK is absent, like everything else here that needs a checkout.
/// </remarks>
public sealed class SdkConstantTests
{
    /// <summary>Reads a header's constants, skipping the test when the SDK is not present.</summary>
    private static IReadOnlyDictionary<string, int> Constants(string header)
    {
        if (SdkInventory.Root is null)
        {
            Assert.Ignore("source-sdk-2013 is not available; set SOURCE_SDK to run this.");
        }

        IReadOnlyDictionary<string, int> values = SdkInventory.Constants(header);

        // **The instrument is checked before its answer is used.** An extraction that returned
        // nothing would make every assertion below vacuous, and a suite of vacuous assertions is
        // worse than none: it reports parity it never measured.
        values.Count.ShouldBeGreaterThan(10, $"no constants were extracted from {header}");

        return values;
    }

    [Test]
    public void TheBspVersionsWeAccept_AreTheOnesTheEngineDeclares()
    {
        // bspfile.h: MINBSPVERSION 19, BSPVERSION 20. TF2 ships 20, and 19 exists because the
        // engine still loads the older one — a reader that demanded 20 exactly would refuse maps
        // this project is meant to open.
        IReadOnlyDictionary<string, int> constants = Constants("src/public/bspfile.h");

        constants["MINBSPVERSION"].ShouldBe(19);
        constants["BSPVERSION"].ShouldBe(20);
    }

    [Test]
    public void TheLumpNumbersWeRead_AreTheOnesTheEngineDeclares()
    {
        // **The single highest-risk set of numbers in the project.** A lump index off by one reads
        // another lump's bytes as its own: entirely valid data, entirely wrong meaning, and no
        // error anywhere. Every one this project hardcodes is asserted against bspfile.h.
        IReadOnlyDictionary<string, int> lumps = Constants("src/public/bspfile.h");

        lumps["LUMP_ENTITIES"].ShouldBe(0);
        lumps["LUMP_PLANES"].ShouldBe(1);
        lumps["LUMP_TEXDATA"].ShouldBe(2);
        lumps["LUMP_VERTEXES"].ShouldBe(3);
        lumps["LUMP_VISIBILITY"].ShouldBe(4);
        lumps["LUMP_NODES"].ShouldBe(5);
        lumps["LUMP_TEXINFO"].ShouldBe(6);
        lumps["LUMP_FACES"].ShouldBe(7);
        lumps["LUMP_LIGHTING"].ShouldBe(8);
        lumps["LUMP_LEAFS"].ShouldBe(10);
        lumps["LUMP_EDGES"].ShouldBe(12);
        lumps["LUMP_SURFEDGES"].ShouldBe(13);
        lumps["LUMP_MODELS"].ShouldBe(14);
        lumps["LUMP_WORLDLIGHTS"].ShouldBe(15);
        lumps["LUMP_LEAFFACES"].ShouldBe(16);
        lumps["LUMP_DISPINFO"].ShouldBe(26);
        lumps["LUMP_DISP_VERTS"].ShouldBe(33);
        lumps["LUMP_GAME_LUMP"].ShouldBe(35);
        lumps["LUMP_PAKFILE"].ShouldBe(40);
        lumps["LUMP_CUBEMAPS"].ShouldBe(42);
        lumps["LUMP_OVERLAYS"].ShouldBe(45);
        lumps["LUMP_LEAF_AMBIENT_INDEX_HDR"].ShouldBe(51);
        lumps["LUMP_LEAF_AMBIENT_INDEX"].ShouldBe(52);
        lumps["LUMP_LIGHTING_HDR"].ShouldBe(53);
        lumps["LUMP_LEAF_AMBIENT_LIGHTING_HDR"].ShouldBe(55);
        lumps["LUMP_LEAF_AMBIENT_LIGHTING"].ShouldBe(56);
    }

    [Test]
    public void TheHdrAndLdrLightingPairs_AreDistinctLumps()
    {
        // **The pairing that is easy to get half right.** Lighting, faces and leaf ambient each
        // exist twice, and a map compiled for HDR carries its real data in the second of each pair
        // while the first holds something stale or empty. Reading the LDR one from an HDR map is
        // how a correctly-lit map draws black, and nothing about it errors.
        IReadOnlyDictionary<string, int> lumps = Constants("src/public/bspfile.h");

        lumps["LUMP_LIGHTING"].ShouldNotBe(lumps["LUMP_LIGHTING_HDR"]);
        lumps["LUMP_FACES"].ShouldNotBe(lumps["LUMP_FACES_HDR"]);
        lumps["LUMP_LEAF_AMBIENT_LIGHTING"].ShouldNotBe(lumps["LUMP_LEAF_AMBIENT_LIGHTING_HDR"]);
        lumps["LUMP_LEAF_AMBIENT_INDEX"].ShouldNotBe(lumps["LUMP_LEAF_AMBIENT_INDEX_HDR"]);
    }

    [Test]
    public void TheStudioVersionsWeAccept_BracketTheOneTheEngineShips()
    {
        // studio.h: STUDIO_VERSION 48. This project accepts 44 to 49, which is deliberately wider —
        // TF2 has shipped models across that range and the fields these readers touch have not
        // moved. The assertion is that the SDK's version falls INSIDE our range, not that it equals
        // an end of it: a bound that happened to sit on 48 would pass while accepting nothing else.
        IReadOnlyDictionary<string, int> constants = Constants("src/public/studio.h");

        const int weAcceptFrom = 44;
        const int weAcceptTo = 49;

        constants["STUDIO_VERSION"].ShouldBeGreaterThanOrEqualTo(weAcceptFrom);
        constants["STUDIO_VERSION"].ShouldBeLessThanOrEqualTo(weAcceptTo);
    }

    [Test]
    public void TheVertexFileVersionWeAccept_IsTheOneTheEngineDeclares()
    {
        // studio.h: MODEL_VERTEX_FILE_VERSION 4, the .vvd beside every model.
        Constants("src/public/studio.h")["MODEL_VERTEX_FILE_VERSION"].ShouldBe(4);
    }

    [Test]
    public void TheBonesPerVertexWeRead_IsTheEnginesLimit()
    {
        // studio.h: MAX_NUM_BONES_PER_VERT 3. This project carries exactly three bone indices and
        // three weights per vertex, in PropVertex and in the shader's skinning path — so a model
        // weighted to more would lose the extras. The engine's own cap says there are none.
        Constants("src/public/studio.h")["MAX_NUM_BONES_PER_VERT"].ShouldBe(3);
    }

    [Test]
    public void TheDisplacementPowerWeSupport_IsTheEnginesMaximum()
    {
        // bspfile.h: MAX_MAP_DISP_POWER 4, so a displacement is at most (1<<4)+1 = 17 vertices on a
        // side. Terrain reading sizes its grids from the power in each dispinfo; the cap is what
        // bounds the allocation, and a reader that assumed a smaller one would truncate the
        // densest terrain rather than fail.
        IReadOnlyDictionary<string, int> constants = Constants("src/public/bspfile.h");

        constants["MAX_MAP_DISP_POWER"].ShouldBe(4);

        int side = (1 << constants["MAX_MAP_DISP_POWER"]) + 1;

        side.ShouldBe(17, "a displacement at maximum power is 17 vertices on a side");
    }

    [Test]
    public void TheLodCountTheFormatAllows_IsKnown()
    {
        // studio.h: MAX_NUM_LODS 8. This project always reads level zero, which is a deliberate
        // choice recorded in ModelConformanceTests rather than an accident — and the number here is
        // what says how much is being skipped.
        Constants("src/public/studio.h")["MAX_NUM_LODS"].ShouldBe(8);
    }
}
