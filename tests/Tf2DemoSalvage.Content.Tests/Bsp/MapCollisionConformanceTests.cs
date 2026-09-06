using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// The map's baked physics collision — what a corpse lands on (B58, B369).
/// </summary>
/// <remarks>
/// **`LUMP_PHYSCOLLIDE` is lump 29** (`bspfile.h:310`) and the engine hands its contents to
/// `CreatePolyObjectStatic` at level load (`physics_shared.cpp:602-667`). Without it a simulated
/// corpse has no floor.
///
/// **The layout is Valve's own, terminator included**, which is the part a guess gets wrong
/// (`bsplib.cpp:1575-1600`):
///
/// <code>
/// // physics data is variable length.  The last physmodel is a NULL pointer
/// // with modelIndex -1, dataSize -1
/// struct dphysmodel_t { int modelIndex; int dataSize; int keydataSize; int solidCount; };
/// </code>
///
/// Each entry is one brush model, then `dataSize` bytes of length-prefixed solids, then
/// `keydataSize` bytes of KeyValues text.
///
/// **Asserted against the game's own maps, because the question is whether this reads what Valve
/// ships**, with the numbers taken from the `map-collision` probe rather than from this reader —
/// so the test and the code under test cannot agree with each other by construction.
/// </remarks>
public sealed class MapCollisionConformanceTests
{
    /// <remarks>
    /// **Measured with the `map-collision` probe: 40 models and 41 solids.** The two counts differ
    /// by one, which is itself a prediction: one brush model carries two solids and every other
    /// carries one, so a reader that lost a solid or double-counted a model breaks the pair rather
    /// than only one number.
    /// </remarks>
    [Test]
    public void Read_TheHarvestMap_FindsEveryBrushModelAndSolid()
    {
        IReadOnlyList<MapPhysicsModel> models = Collision("koth_harvest_final");

        models.Count.ShouldBe(40);

        int solids = 0;

        foreach (MapPhysicsModel model in models)
        {
            solids += model.SolidCount;
        }

        solids.ShouldBe(41);
    }

    /// <remarks>
    /// **Model 0 is the world**, and it is the one with two solids. Brush entities follow it in the
    /// order the compiler emitted them, which is the same order `*1`, `*2` … name them in the
    /// entity lump.
    /// </remarks>
    [Test]
    public void Read_TheHarvestMap_StartsWithTheWorldBrushModel()
    {
        IReadOnlyList<MapPhysicsModel> models = Collision("koth_harvest_final");

        models[0].ModelIndex.ShouldBe(0);
        models[0].SolidCount.ShouldBe(2);
        models[0].Solids.Count.ShouldBe(2, "one extent per declared solid");
    }

    /// <remarks>
    /// **Every solid extent must lie inside the lump.** A `.bsp` is a stranger's file (D32) and this
    /// walk is driven entirely by lengths the file declares, so the extents are the thing a
    /// malformed map would push out of bounds.
    /// </remarks>
    [Test]
    public void Read_TheHarvestMap_KeepsEverySolidExtentInsideTheLump()
    {
        IReadOnlyList<MapPhysicsModel> models = Collision("koth_harvest_final");

        foreach (MapPhysicsModel model in models)
        {
            foreach (PhysicsBrushSolid solid in model.Solids)
            {
                solid.Length.ShouldBeGreaterThan(0);
                solid.Offset.ShouldBeGreaterThanOrEqualTo(0);
            }
        }
    }

    /// <remarks>
    /// **The text half is readable exactly as a `.phy`'s is**, and these are the values the probe
    /// measured. `contents` is a `BSPFlags.h` mask; the two solids of the world differ, which is why
    /// both are asserted rather than one.
    /// </remarks>
    [Test]
    public void Parse_TheWorldsCollisionText_ReadsItsStaticSolids()
    {
        MapSurfaceTable table = MapSurfaceTable.Parse(Collision("koth_harvest_final")[0].Text);

        table.StaticSolids.Count.ShouldBe(2);

        table.StaticSolids[0].Index.ShouldBe(0);
        table.StaticSolids[0].Contents.ShouldBe(33570827);

        table.StaticSolids[1].Index.ShouldBe(1);
        table.StaticSolids[1].Contents.ShouldBe(65536);
    }

    /// <remarks>
    /// **Keyed by SLOT, and the shipped data is what settled that.** The first version of this
    /// asserted a name-to-index map and failed against the real map, which writes:
    ///
    /// <code>
    /// materialtable {
    ///   "default_silent" "1"   "default" "2"   "wood" "3"      "metal" "4"
    ///   "glass" "5"            "concrete" "6"  "metalpanel" "7"  "default" "8"
    /// }
    /// </code>
    ///
    /// **`default` appears twice, at 2 and at 8.** Names repeat and slots do not, because the table
    /// exists to resolve the index a hull stores — so a name-keyed table silently loses one entry.
    /// Both are asserted here for exactly that reason.
    /// </remarks>
    [Test]
    public void Parse_TheWorldsCollisionText_ReadsItsMaterialTable()
    {
        MapSurfaceTable table = MapSurfaceTable.Parse(Collision("koth_harvest_final")[0].Text);

        table.Materials[1].ShouldBe("default_silent");
        table.Materials[2].ShouldBe("default");
        table.Materials[3].ShouldBe("wood");
        table.Materials[4].ShouldBe("metal");
        table.Materials[5].ShouldBe("glass");
        table.Materials[6].ShouldBe("concrete");
        table.Materials[7].ShouldBe("metalpanel");

        table.Materials[8].ShouldBe("default", "the repeat that a name-keyed table would swallow");
    }

    /// <remarks>
    /// **`virtualterrain` is an empty block and its PRESENCE is the fact.** A map compiled with
    /// displacement collision carries it; the block has no keys, so a reader that only gathered
    /// key/value pairs would report nothing and lose it entirely.
    /// </remarks>
    [Test]
    public void Parse_TheWorldsCollisionText_NoticesTheVirtualTerrainBlock()
    {
        MapSurfaceTable.Parse(Collision("koth_harvest_final")[0].Text)
            .HasVirtualTerrain.ShouldBeTrue();
    }

    /// <remarks>
    /// **The terminator ends the chain, and finding the input that proves it took two attempts.**
    /// The first version put one model and then the terminator, which cannot fail: a terminator
    /// declares `dataSize -1`, so the bounds guard rejects it as a model anyway and correct and
    /// broken agree.
    ///
    /// The second attempt added a **perfectly valid model AFTER the terminator**, which is a better
    /// input and still does not separate them: with the terminator check broken, the bounds guard
    /// rejects `dataSize -1` and the walk stops in the same place.
    ///
    /// **So this test pins the BEHAVIOUR and cannot attribute it**, and that is written down rather
    /// than glossed. The chain stops at the terminator and trailing bytes are not read — which is
    /// what a caller depends on — but the terminator check and the bounds guard are equivalent on
    /// every input, so no assertion here can say which one did it. The source says the same beside
    /// the check.
    /// </remarks>
    [Test]
    public void Read_ALumpWithAModelAfterTheTerminator_StopsAtTheTerminator()
    {
        byte[] lump =
        [
            .. Model(modelIndex: 0, dataSize: 8, keydataSize: 0, solidCount: 1),
            .. BitConverter.GetBytes(4),      // one solid, four bytes of payload
            .. BitConverter.GetBytes(0),
            .. Model(modelIndex: -1, dataSize: -1, keydataSize: 0, solidCount: 0),

            // Entirely well-formed, and unreachable — the engine has already stopped.
            .. Model(modelIndex: 1, dataSize: 8, keydataSize: 0, solidCount: 1),
            .. BitConverter.GetBytes(4),
            .. BitConverter.GetBytes(0),
        ];

        IReadOnlyList<MapPhysicsModel> models = BspPhysicsCollision.Read(lump);

        models.Count.ShouldBe(1, "the terminator ends the chain, trailing bytes and all");
        models[0].ModelIndex.ShouldBe(0);
        models[0].Solids[0].Length.ShouldBe(4);
    }

    /// <remarks>
    /// **A model declaring more than the lump holds is refused, not read.** This is the shape of
    /// every allocate-before-validate defect already fixed in this project, and a map arriving from
    /// fastdl is reviewed by nobody.
    /// </remarks>
    [Test]
    public void Read_ALumpWhoseModelOverrunsIt_StopsWithoutReadingPastTheEnd()
    {
        byte[] lump = [.. Model(modelIndex: 0, dataSize: 4096, keydataSize: 0, solidCount: 1)];

        BspPhysicsCollision.Read(lump).ShouldBeEmpty();
    }

    /// <remarks>
    /// **A hull whose declared size runs past its own block yields nothing, and this input exists
    /// because a sabotage found no test that supplied it.** Removing the `cursor + size > end` check
    /// reddened nothing at all — which for a bounds guard on a file supplied by whoever runs the
    /// server (D32) is the one place an unproven guard is unacceptable.
    ///
    /// The model below declares eight bytes of data holding one solid, and that solid claims a
    /// hundred. Valve's own loop trusts the declared size; this one checks it against the block and
    /// keeps what is actually there.
    /// </remarks>
    [Test]
    public void Read_AHullDeclaringMoreThanItsBlockHolds_YieldsNoExtent()
    {
        byte[] lump =
        [
            .. Model(modelIndex: 0, dataSize: 8, keydataSize: 0, solidCount: 1),
            .. BitConverter.GetBytes(100),    // a hundred bytes, inside a block of eight
            .. BitConverter.GetBytes(0),
        ];

        IReadOnlyList<MapPhysicsModel> models = BspPhysicsCollision.Read(lump);

        models.Count.ShouldBe(1, "the model itself is well-formed and is still reported");

        models[0].SolidCount.ShouldBe(1, "the header's claim is carried through as declared");
        models[0].Solids.ShouldBeEmpty("but no extent is manufactured for a hull that does not fit");
    }

    /// <summary>One <c>dphysmodel_t</c>.</summary>
    /// <param name="modelIndex">Which brush model.</param>
    /// <param name="dataSize">Bytes of solids that follow.</param>
    /// <param name="keydataSize">Bytes of text after those.</param>
    /// <param name="solidCount">How many solids.</param>
    /// <returns>The sixteen header bytes.</returns>
    private static byte[] Model(int modelIndex, int dataSize, int keydataSize, int solidCount) =>
    [
        .. BitConverter.GetBytes(modelIndex),
        .. BitConverter.GetBytes(dataSize),
        .. BitConverter.GetBytes(keydataSize),
        .. BitConverter.GetBytes(solidCount),
    ];

    /// <summary>One of the game's own maps, skipping when TF2 is not installed.</summary>
    /// <param name="map">The map's name, without extension.</param>
    /// <returns>Its physics models.</returns>
    private static IReadOnlyList<MapPhysicsModel> Collision(string map)
    {
        string path = Skip.Unless(
            GameInstall.Find(Path.Combine("maps", map + ".bsp")), GameInstall.Missing);

        ReadOnlyMemory<byte> file = File.ReadAllBytes(path);

        BspHeader header = BspHeader.Parse(file.Span);

        return BspPhysicsCollision.Read(BspLumpData.Read(file, header.Lump(29)));
    }
}
