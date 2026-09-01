using System.Collections.Generic;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// Translucent entities draw back to front by camera distance, never in input order.
/// </summary>
/// <remarks>
/// **The outside audit's finding 2, and the engine's rule**: `CClientLeafSystem::SortEntities`
/// (`clientleafsystem.cpp:1758`) sorts translucent entries ascending along the view forward axis,
/// measured at each entity's render-bounds CENTER, and the draw walks the result backwards
/// (`viewrender.cpp:4577`) so the farthest blends first. Alpha blending composes in no other
/// order — a near window drawn before a far hologram erases the hologram from the glass.
/// </remarks>
public sealed class TranslucentOrderTests
{
    private static ModelInstance At(float x, string path) => new(
        path,
        Matrix: new float[16],
        Light: null,
        Sun: null,
        WorldBounds: (x - 8f, -8f, -8f, x + 8f, 8f, 8f));

    private static readonly (float X, float Y, float Z) Eye = (0f, 0f, 0f);

    private static readonly (float X, float Y, float Z) LookingAlongX = (1f, 0f, 0f);

    /// <remarks>
    /// **Invariant to input order, which is the whole claim.** The same two overlapping entities,
    /// handed over both ways round, must come out in one order — ascending along the view — so
    /// the reverse walk draws the farther first either way.
    /// </remarks>
    [Test]
    public void Sort_TwoEntitiesInEitherInputOrder_ComesOutNearToFar()
    {
        ModelInstance near = At(100f, "models/near.mdl");
        ModelInstance far = At(900f, "models/far.mdl");

        List<(float Along, ModelInstance Entry)> forwards =
        [
            (TranslucentOrder.Along(near, Eye, LookingAlongX), near),
            (TranslucentOrder.Along(far, Eye, LookingAlongX), far),
        ];

        List<(float Along, ModelInstance Entry)> backwards =
        [
            (TranslucentOrder.Along(far, Eye, LookingAlongX), far),
            (TranslucentOrder.Along(near, Eye, LookingAlongX), near),
        ];

        TranslucentOrder.Sort(forwards);
        TranslucentOrder.Sort(backwards);

        forwards[0].Entry.ModelPath.ShouldBe("models/near.mdl");
        forwards[1].Entry.ModelPath.ShouldBe("models/far.mdl");

        backwards[0].Entry.ModelPath.ShouldBe(
            "models/near.mdl", "the order must not depend on the input order");
        backwards[1].Entry.ModelPath.ShouldBe("models/far.mdl");
    }

    /// <remarks>
    /// **The center, not the origin — Valve's own comment says why**: *"Compute the center of the
    /// object (needed for translucent brush models)"*. A door's model origin can sit at the map
    /// origin while its faces stand rooms away; measured at the origin this door would sort as
    /// nearer than the pane in front of it.
    /// </remarks>
    [Test]
    public void Along_ABoxFarFromItsOrigin_IsMeasuredAtTheBoxCenter()
    {
        ModelInstance door = new(
            "models/props/door.mdl",
            Matrix: new float[16],
            Light: null,
            Sun: null,
            Origin: (0f, 0f, 0f),
            WorldBounds: (400f, -8f, -8f, 600f, 8f, 8f));

        TranslucentOrder.Along(door, Eye, LookingAlongX)
            .ShouldBe(500f, "the box center is at x=500 however near the origin sits");
    }

    /// <remarks>
    /// The distance is a PROJECTION onto the view axis, not a Euclidean range — the engine dots
    /// the delta with forward, so something far off to the side but level with the camera plane
    /// sorts by how deep it is into the view, not by how far away it is.
    /// </remarks>
    [Test]
    public void Along_AnEntityOffToTheSide_CountsOnlyTheForwardComponent()
    {
        ModelInstance aside = At(100f, "models/aside.mdl") with
        {
            WorldBounds = (92f, 5000f, -8f, 108f, 5016f, 8f),
        };

        TranslucentOrder.Along(aside, Eye, LookingAlongX)
            .ShouldBe(100f, "five thousand units sideways contribute nothing along +X");
    }

    /// <remarks>
    /// An instance with the default zero box falls back to its origin rather than sorting as
    /// though it stood at the map origin — an empty box must never decide a comparison
    /// (`docs/memory/an-empty-box-must-never-cull.md`, same trap one system over).
    /// </remarks>
    [Test]
    public void Along_AnInstanceWithNoBounds_FallsBackToItsOrigin()
    {
        ModelInstance bare = new(
            "models/bare.mdl",
            Matrix: new float[16],
            Light: null,
            Sun: null,
            Origin: (250f, 0f, 0f));

        TranslucentOrder.Along(bare, Eye, LookingAlongX).ShouldBe(250f);
    }
}
