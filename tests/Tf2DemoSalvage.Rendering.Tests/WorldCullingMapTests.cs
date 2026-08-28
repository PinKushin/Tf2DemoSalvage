using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// What the world cull does to a real map, and what it must never do.
/// </summary>
/// <remarks>
/// **A cull is only allowed to fail in one direction, and that is what this file is about.** Drawing
/// more than needed is slow; drawing less is a hole in the world. Every assertion here is either
/// "the culled set is contained in the full set" or "the culled set contains what it promised" —
/// the saving itself is measured but not pinned, because it is a property of Valve's map rather
/// than of this code.
///
/// **The hand-built tests cannot reach any of this.** They use four faces and three leaves; a real
/// map has thirteen thousand spans and a compressed PVS, and every off-by-one in a stride or an
/// offset lives in that gap.
/// </remarks>
public sealed class WorldCullingMapTests
{
    /// <summary>The map's level data and its built world, as the viewer would have them.</summary>
    /// <remarks>
    /// **Built through `MapWorldBuilder` directly rather than through `LoadedMap`**, which wants a
    /// `GameContent` and a timeline this has no use for. The arguments below are exactly what
    /// `LoadedMap.BuildWorld` passes, so this is the same world the viewer draws — and
    /// <see cref="MapCache"/> means the assets are decoded once for the whole assembly.
    /// </remarks>
    private static (MapLevel Level, MapWorld World) Built(string mapName = MapCache.DefaultMap)
    {
        MapAssets assets = MapCache.Load(mapName: mapName);
        MapLevel level = MapLevel.Read(MapCache.Bytes(mapName), NullLogger.Instance);

        MapWorld world = MapWorldBuilder.Build(
            level.Terrain,
            level.Surfaces,
            assets.Materials,
            assets.Lightmaps,
            assets.Props,
            area: null,
            level.Overlays,
            level.BrushModels,
            NullLoggerFactory.Instance);

        return (level, world);
    }

    /// <summary>A roomy leaf with a real cluster, and the point at its centre.</summary>
    private static (int Leaf, (float X, float Y, float Z) Middle) SomewhereInside(BspLeafTree tree)
    {
        for (int leaf = 1; leaf < tree.LeafCount; leaf++)
        {
            if (tree.Cluster(leaf) < 0 || tree.Bounds(leaf) is not { } box)
            {
                continue;
            }

            if (box.Max.X - box.Min.X < 128f ||
                box.Max.Y - box.Min.Y < 128f ||
                box.Max.Z - box.Min.Z < 128f)
            {
                continue;
            }

            (float X, float Y, float Z) middle = (
                (box.Min.X + box.Max.X) * 0.5f,
                (box.Min.Y + box.Max.Y) * 0.5f,
                (box.Min.Z + box.Max.Z) * 0.5f);

            if (tree.LeafAt(middle.X, middle.Y, middle.Z) == leaf)
            {
                return (leaf, middle);
            }
        }

        Skip.Because("no leaf on this map is both clustered and roomy enough to stand in");
        return default;
    }

    private static ViewFrustum Looking((float X, float Y, float Z) from) =>
        ViewFrustum.PerspectiveFromAspect(
            from,
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 7f,
            farZ: 28_000f,
            fovX: 90f,
            aspect: 16f / 9f);

    /// <summary>That badlands' runs never draw one material's triangles with another's texture.</summary>
    /// <remarks>
    /// **A second map, because the owner was looking at badlands and cp_process is not it.** He saw
    /// roller-door grates rendering as concrete, and there were two candidates: a model culled by a
    /// degenerate box (which it was), or a run merged across a material boundary, which would paint
    /// one surface's triangles with a neighbour's texture. This is the second candidate, tested on
    /// the map where it was seen.
    ///
    /// Badlands is far more displacement-heavy than cp_process — 1,191 terrain spans against 60 —
    /// so it also exercises the leaf-orphan path at a scale the default map does not.
    /// </remarks>
    [Test]
    public void Batches_ForBadlands_NeverMergeAcrossAMaterialBoundary()
    {
        const string Badlands = "cp_badlands";

        if (!MapCache.Exists(Badlands))
        {
            Assert.Ignore("cp_badlands is not installed on this machine.");
            return;
        }

        (MapLevel level, MapWorld world) = Built(Badlands);

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("badlands has a tree");
        (_, (float X, float Y, float Z) middle) = SomewhereInside(tree);

        IReadOnlyList<WorldBatch> runs =
            level.Culling(world.FaceSpans)!.Batches(middle.X, middle.Y, middle.Z, Looking(middle))
                .ShouldNotBeNull("culling was available");

        Dictionary<int, int> materialOf = [];

        foreach (WorldBatch batch in world.Batches)
        {
            for (int at = batch.FirstVertex; at < batch.FirstVertex + batch.VertexCount; at++)
            {
                materialOf[at] = batch.MaterialIndex;
            }
        }

        foreach (WorldBatch run in runs)
        {
            for (int at = run.FirstVertex; at < run.FirstVertex + run.VertexCount; at++)
            {
                materialOf.TryGetValue(at, out int material)
                    .ShouldBeTrue($"run at {run.FirstVertex} covers vertex {at}, which no batch owns");

                material.ShouldBe(
                    run.MaterialIndex,
                    $"vertex {at} belongs to material {material}, drawn here as {run.MaterialIndex}");
            }
        }
    }

    /// <summary>That every corner the cull keeps is a corner the full world also draws.</summary>
    /// <remarks>
    /// **The containment property, checked corner by corner rather than run by run.** A merge that
    /// joined two spans across a gap would produce a run covering vertices no batch owns — geometry
    /// drawn with the wrong material, or past the end of a material's region. Runs are compared as
    /// SETS OF VERTEX INDICES against the uploaded batches, so a run that strayed a single corner
    /// outside its batch is caught.
    ///
    /// This is the assertion that would have caught a rebase using the wrong group's base offset,
    /// which is the mistake the span recording is most exposed to.
    /// </remarks>
    [Test]
    public void Batches_ForACameraInsideTheMap_CoverOnlyVerticesTheFullWorldDraws()
    {
        (MapLevel level, MapWorld world) = Built();

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        (_, (float X, float Y, float Z) middle) = SomewhereInside(tree);

        WorldCulling culling =
            level.Culling(world.FaceSpans).ShouldNotBeNull("this map can be culled");

        IReadOnlyList<WorldBatch> runs =
            culling.Batches(middle.X, middle.Y, middle.Z, Looking(middle))
                .ShouldNotBeNull("culling was available");

        // Every vertex the uploaded batches cover, keyed by the material that covers it.
        Dictionary<int, int> materialOf = [];

        foreach (WorldBatch batch in world.Batches)
        {
            for (int at = batch.FirstVertex; at < batch.FirstVertex + batch.VertexCount; at++)
            {
                materialOf[at] = batch.MaterialIndex;
            }
        }

        foreach (WorldBatch run in runs)
        {
            for (int at = run.FirstVertex; at < run.FirstVertex + run.VertexCount; at++)
            {
                materialOf.TryGetValue(at, out int material)
                    .ShouldBeTrue($"run at {run.FirstVertex} covers vertex {at}, which no batch owns");

                material.ShouldBe(
                    run.MaterialIndex,
                    $"vertex {at} belongs to material {material}, drawn here as {run.MaterialIndex}");
            }
        }
    }

    /// <summary>That every face of every visible leaf reaches a run.</summary>
    /// <remarks>
    /// **The other direction, and the one that catches a hole in the world.** Containment alone is
    /// satisfied by a cull that draws nothing. This walks the visible leaves independently, collects
    /// the faces they name, and requires that each one the world build actually drew is covered by
    /// some run.
    ///
    /// **Faces the BUILD dropped are excluded**, because they are not missing from the picture — a
    /// tool surface, a brush-entity face, and anything outside the play area were never drawn by the
    /// full world either. The span list is what says which faces exist, and that is the right
    /// denominator.
    /// </remarks>
    [Test]
    public void Batches_ForACameraInsideTheMap_CoverEveryDrawnFaceOfEveryVisibleLeaf()
    {
        (MapLevel level, MapWorld world) = Built();

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        BspLeafFaces leafFaces = level.LeafFaces.ShouldNotBeNull("and a leaf-face lump");
        (_, (float X, float Y, float Z) middle) = SomewhereInside(tree);

        ViewFrustum frustum = Looking(middle);

        IReadOnlyList<WorldBatch> runs =
            level.Culling(world.FaceSpans)!.Batches(middle.X, middle.Y, middle.Z, frustum)
                .ShouldNotBeNull("culling was available");

        HashSet<int> covered = [];

        foreach (WorldBatch run in runs)
        {
            for (int at = run.FirstVertex; at < run.FirstVertex + run.VertexCount; at++)
            {
                covered.Add(at);
            }
        }

        Dictionary<int, WorldFaceSpan> spanOf =
            world.FaceSpans.ToDictionary(span => span.Face);

        IReadOnlyList<int> leaves =
            new WorldVisibility(tree, level.Visibility ?? BspVisibility.None)
                .Leaves(middle.X, middle.Y, middle.Z, frustum);

        int checkedFaces = 0;

        foreach (int leaf in leaves)
        {
            (int first, int count) = tree.LeafFaces(leaf);

            for (int entry = 0; entry < count; entry++)
            {
                int face = leafFaces.Face(first + entry);

                if (face < 0 || !spanOf.TryGetValue(face, out WorldFaceSpan span))
                {
                    continue;
                }

                checkedFaces++;

                covered.Contains(span.FirstVertex).ShouldBeTrue(
                    $"face {face} is in visible leaf {leaf} and no run covers it");
            }
        }

        checkedFaces.ShouldBeGreaterThan(
            0, "no visible leaf named a drawn face, so this test measured nothing");
    }

    /// <summary>That nothing is dropped unless its own box is off screen.</summary>
    /// <remarks>
    /// **This is the test that should have existed first, and its absence let the ground go
    /// missing.** Its neighbour above checks that every face of every VISIBLE LEAF is drawn — which
    /// is vacuously satisfied for a face no leaf ever names, and displacements are exactly that.
    /// The owner found it by looking at the screen; every automated check was green.
    ///
    /// **The denominator here is the SPANS — everything the full world draws.** For each one that
    /// the cull dropped, its own world-space box must be outside the frustum. That is the actual
    /// contract of a cull, it needs no knowledge of leaves or clusters, and it cannot be satisfied
    /// vacuously: a surface in front of the camera that is not drawn fails it.
    ///
    /// **The PVS makes this weaker than it looks and it is still worth having.** A surface inside
    /// the frustum but behind a wall is legitimately dropped by the PVS, so this cannot demand that
    /// every in-frustum span survives. What it demands is that every dropped span is either
    /// out of frustum OR was in a leaf the PVS excluded — and the second is checked by looking the
    /// face up in the leaves rather than by trusting it.
    /// </remarks>
    [Test]
    public void Batches_ForACameraInsideTheMap_DropNothingThatIsOnScreenAndUnoccluded()
    {
        (MapLevel level, MapWorld world) = Built();

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        BspLeafFaces leafFaces = level.LeafFaces.ShouldNotBeNull("and a leaf-face lump");

        // **The camera is placed to LOOK AT a displacement, and the first version was not.** It
        // stood in the first roomy leaf the tree offered, which on cp_process is indoors: all sixty
        // orphaned spans were legitimately off screen, so the box path never ran and the control
        // could not tell that from the path being broken. Standing two hundred units back from a
        // real terrain span, facing it, is a condition where the difference shows.
        WorldFaceSpan terrain =
            world.FaceSpans.First(span => span.Category == SurfaceCategory.Terrain);

        (float X, float Y, float Z) eye = (
            terrain.Min.X - 200f,
            (terrain.Min.Y + terrain.Max.Y) * 0.5f,
            ((terrain.Min.Z + terrain.Max.Z) * 0.5f) + 64f);

        ViewFrustum frustum = Looking(eye);

        IReadOnlyList<WorldBatch> runs =
            level.Culling(world.FaceSpans)!.Batches(eye.X, eye.Y, eye.Z, frustum)
                .ShouldNotBeNull("culling was available");

        HashSet<int> covered = [];

        foreach (WorldBatch run in runs)
        {
            for (int at = run.FirstVertex; at < run.FirstVertex + run.VertexCount; at++)
            {
                covered.Add(at);
            }
        }

        // Which faces any leaf at all can reach. A dropped face outside this set has no leaf and no
        // PVS to excuse it, so being on screen is enough to condemn the cull.
        HashSet<int> named = [];

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            (int first, int count) = tree.LeafFaces(leaf);

            for (int entry = 0; entry < count; entry++)
            {
                int face = leafFaces.Face(first + entry);

                if (face >= 0)
                {
                    named.Add(face);
                }
            }
        }

        int orphans = 0;
        int drawnOrphans = 0;

        foreach (WorldFaceSpan span in world.FaceSpans)
        {
            if (named.Contains(span.Face))
            {
                continue;
            }

            orphans++;

            if (covered.Contains(span.FirstVertex))
            {
                drawnOrphans++;

                continue;
            }

            // Not drawn, and no leaf and no PVS can excuse it — so it had better be off screen.
            frustum.Cull(span.Min.X, span.Min.Y, span.Min.Z, span.Max.X, span.Max.Y, span.Max.Z)
                .ShouldBeTrue(
                    $"face {span.Face} ({span.Category}) is on screen, belongs to no leaf, and was "
                    + "not drawn");
        }

        // **Two controls, because the loop above passes vacuously in two different ways.** With no
        // orphaned surfaces at all there is nothing to check — that is the state the old coverage
        // test silently assumed. With orphans that are all off screen the box path never fires, and
        // an implementation that simply dropped them would look identical.
        orphans.ShouldBeGreaterThan(
            0, "this map has no leaf-orphaned surfaces, so this test proves nothing here");

        drawnOrphans.ShouldBeGreaterThan(
            0, "no leaf-orphaned surface was drawn, so the box path never ran");
    }

    /// <summary>That standing inside the map draws materially less than the whole world.</summary>
    /// <remarks>
    /// **The saving, measured rather than pinned.** The claim is that a camera in a room does not
    /// draw the whole map — a weak-looking bound deliberately, because the real figure depends on
    /// which room the leaf search happens to pick and on Valve's map. What it catches is the cull
    /// silently becoming a no-op, which is the failure that costs nothing visible and everything in
    /// frame rate.
    /// </remarks>
    [Test]
    public void Corners_ForACameraInsideTheMap_AreFewerThanTheWholeWorld()
    {
        (MapLevel level, MapWorld world) = Built();

        BspLeafTree tree = level.Leaves.ShouldNotBeNull("cp_process has a tree");
        (_, (float X, float Y, float Z) middle) = SomewhereInside(tree);

        WorldCulling culling = level.Culling(world.FaceSpans)!;

        culling.Batches(middle.X, middle.Y, middle.Z, Looking(middle));

        (int drawn, int total) = culling.Corners;

        total.ShouldBeGreaterThan(0, "the world has geometry");

        drawn.ShouldBeLessThan(
            total,
            $"the cull kept every corner: {drawn} of {total}");
    }
}
